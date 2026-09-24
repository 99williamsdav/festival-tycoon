using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Festival.Simulation.TrafficProof;

public enum TrafficDirection { East, West }
public enum TrafficRunOutcome { Completed, PendingWithinBound, UnsupportedLayout, EstablishedInfeasible }
public enum TrafficRegionPhase { None, Approaching, Waiting, Inside, Cleared }

public readonly record struct TrafficPoint(int XMillimetres, int ZMillimetres);
public readonly record struct TrafficRectangle(int MinimumX, int MinimumZ, int MaximumX, int MaximumZ)
{
    public bool Contains(TrafficPoint point) => point.XMillimetres >= MinimumX && point.XMillimetres <= MaximumX &&
        point.ZMillimetres >= MinimumZ && point.ZMillimetres <= MaximumZ;
}

public sealed record TrafficPersonDefinition(ulong Id, TrafficPoint Start, int SpeedMillimetresPerTick,
    IReadOnlyList<TrafficPoint> Route, string? RegionId = null, TrafficDirection? RegionDirection = null,
    string? ServiceId = null);
public sealed record TrafficRegionDefinition(string Id, TrafficRectangle Core, TrafficPoint EastEntrance,
    TrafficPoint WestEntrance, TrafficPoint EastExitBerth, TrafficPoint WestExitBerth, int BatchLimit = 2);
public sealed record TrafficServiceDefinition(string Id, TrafficPoint Anchor, int DurationTicks);
public sealed record TrafficPersonSnapshot(ulong Id, int IntentGeneration, TrafficPoint Position,
    int SpeedMillimetresPerTick, TrafficPoint[] Route, int RouteIndex, int WaitTicks, long LocalTicket,
    string? RegionId, TrafficDirection? RegionDirection, TrafficRegionPhase RegionPhase, long? RegionTicket,
    string? ServiceId, int UsefulProgressTicks, bool IsInService, bool ServiceCompleted);
public sealed record TrafficRegionSnapshot(string Id, TrafficRectangle Core, TrafficPoint EastEntrance,
    TrafficPoint WestEntrance, TrafficPoint EastExitBerth, TrafficPoint WestExitBerth, int BatchLimit,
    TrafficDirection? ActiveDirection, TrafficDirection NextDirection, int BatchAdmissions, ulong[] Occupants);
public sealed record TrafficServiceSnapshot(string Id, TrafficPoint Anchor, int DurationTicks,
    ulong? ActivePersonId, int RemainingTicks, int CompletedCount);
public sealed record TrafficKernelSnapshot(int SchemaVersion, long Tick, long NextTicket,
    TrafficPersonSnapshot[] People, TrafficRegionSnapshot[] Regions, TrafficServiceSnapshot[] Services,
    TrafficRectangle[] Obstacles, int RegionDischarges, int ServiceCompletions, bool UnsupportedLayout);
public sealed record TrafficTickResult(long Tick, IReadOnlyList<ulong> MovedPeople,
    IReadOnlyList<ulong> DeniedPeople, int UsefulProgress, int RegionDischarges, int ServiceCompletions);

/// <summary>Isolated deterministic proof kernel. It has no production GameSession caller.</summary>
public sealed class TrafficKernel
{
    public const int MinimumCentreSeparationMillimetres = 300;
    public const int SnapshotSchemaVersion = 1;
    private const long SeparationSquared = MinimumCentreSeparationMillimetres * MinimumCentreSeparationMillimetres;
    private readonly SortedDictionary<ulong, PersonState> _people = [];
    private readonly SortedDictionary<string, RegionState> _regions = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, ServiceState> _services = new(StringComparer.Ordinal);
    private readonly List<TrafficRectangle> _obstacles = [];
    private long _nextTicket = 1;
    private int _regionDischarges;
    private int _serviceCompletions;

    public long Tick { get; private set; }
    public bool UnsupportedLayout { get; private set; }
    public IReadOnlyCollection<ulong> PersonIds => _people.Keys;

    public void AddObstacle(TrafficRectangle obstacle)
    {
        if (obstacle.MinimumX > obstacle.MaximumX || obstacle.MinimumZ > obstacle.MaximumZ)
            throw new ArgumentException("Obstacle bounds are inverted.", nameof(obstacle));
        _obstacles.Add(obstacle);
    }

    public void AddRegion(TrafficRegionDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Id);
        if (definition.BatchLimit <= 0 || !_regions.TryAdd(definition.Id, new RegionState(definition)))
            throw new ArgumentException("Region id must be unique and batch limit positive.", nameof(definition));
    }

    public void AddService(TrafficServiceDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Id);
        if (definition.DurationTicks <= 0 || !_services.TryAdd(definition.Id, new ServiceState(definition)))
            throw new ArgumentException("Service id must be unique and duration positive.", nameof(definition));
    }

    public void AddPerson(TrafficPersonDefinition definition)
    {
        if (definition.Id == 0 || definition.SpeedMillimetresPerTick <= 0 || definition.Route.Count == 0 ||
            !_people.TryAdd(definition.Id, new PersonState(definition, _nextTicket++)))
            throw new ArgumentException("Person id must be unique/non-zero with positive speed and a route.", nameof(definition));
        if (definition.RegionId is not null && (!_regions.ContainsKey(definition.RegionId) || definition.RegionDirection is null))
            throw new ArgumentException("A region route requires a known region and direction.", nameof(definition));
        if (definition.ServiceId is not null && !_services.ContainsKey(definition.ServiceId))
            throw new ArgumentException("A service route requires a known service.", nameof(definition));
        ValidatePhysicalState();
    }

    public void MarkUnsupportedLayout() => UnsupportedLayout = true;

    public void Retarget(ulong personId, IReadOnlyList<TrafficPoint> route, string? regionId = null,
        TrafficDirection? direction = null, string? serviceId = null)
    {
        if (route.Count == 0) throw new ArgumentException("A retarget route cannot be empty.", nameof(route));
        var person = _people[personId];
        person.IntentGeneration++;
        person.Route = route.ToArray();
        person.RouteIndex = 0;
        person.ServiceId = serviceId;
        if (person.RegionPhase != TrafficRegionPhase.Inside)
        {
            person.RegionId = regionId;
            person.RegionDirection = direction;
            person.RegionPhase = regionId is null ? TrafficRegionPhase.None : TrafficRegionPhase.Approaching;
            person.RegionTicket = null;
        }
    }

    public TrafficTickResult Advance()
    {
        if (UnsupportedLayout) return new(++Tick, [], _people.Keys.ToArray(), 0, 0, 0);
        AdmitOrProgressServices();
        EstablishRegionRequests();
        PrepareRegions();
        var proposals = _people.Values.ToDictionary(person => person.Id, CreateProposal);
        ApplyRegionGates(proposals);
        ApplyTerrainGates(proposals);
        ArbitrateSweptConflicts(proposals);
        var moved = new List<ulong>(); var denied = new List<ulong>(); var useful = 0; var discharged = 0;
        foreach (var person in _people.Values)
        {
            var proposal = proposals[person.Id];
            if (proposal.Accepted && proposal.Target == person.Position && proposal.ReachesWaypoint)
            {
                person.RouteIndex++;
                person.WaitTicks = 0;
                continue;
            }
            if (!proposal.Accepted || proposal.Target == person.Position)
            {
                if (!person.IsComplete && !person.IsInService) { person.WaitTicks++; denied.Add(person.Id); }
                continue;
            }
            var oldGoalDistance = Manhattan(person.Position, person.Route[^1]);
            person.Position = proposal.Target;
            if (proposal.ReachesWaypoint) person.RouteIndex++;
            person.WaitTicks = 0; moved.Add(person.Id);
            if (Manhattan(person.Position, person.Route[^1]) < oldGoalDistance)
            { person.UsefulProgressTicks++; useful++; }
            if (proposal.EnterRegionId is { } entered)
            {
                var region = _regions[entered]; person.RegionPhase = TrafficRegionPhase.Inside;
                region.Occupants.Add(person.Id); region.BatchAdmissions++;
            }
            if (person.RegionPhase == TrafficRegionPhase.Inside && AtExitBerth(person))
            {
                _regions[person.RegionId!].Occupants.Remove(person.Id); person.RegionPhase = TrafficRegionPhase.Cleared;
                person.RegionTicket = null; _regionDischarges++; discharged++;
            }
        }
        Tick++;
        ValidatePhysicalState();
        return new(Tick, moved, denied, useful, discharged, _serviceCompletions);
    }

    public TrafficRunOutcome RunBounded(int maximumTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumTicks);
        if (UnsupportedLayout) return TrafficRunOutcome.UnsupportedLayout;
        for (var index = 0; index < maximumTicks && !_people.Values.All(person => person.IsComplete); index++) Advance();
        return _people.Values.All(person => person.IsComplete) ? TrafficRunOutcome.Completed : TrafficRunOutcome.PendingWithinBound;
    }

    public TrafficKernelSnapshot CaptureSnapshot() => new(SnapshotSchemaVersion, Tick, _nextTicket,
        _people.Values.Select(person => person.Capture()).ToArray(), _regions.Values.Select(region => region.Capture()).ToArray(),
        _services.Values.Select(service => service.Capture()).ToArray(), _obstacles.ToArray(),
        _regionDischarges, _serviceCompletions, UnsupportedLayout);
    public string ComputeHash() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(CaptureSnapshot())))).ToLowerInvariant();
    public string Serialize() => JsonSerializer.Serialize(CaptureSnapshot());

    public static TrafficKernel Restore(string json)
    {
        TrafficKernelSnapshot snapshot;
        try { snapshot = JsonSerializer.Deserialize<TrafficKernelSnapshot>(json) ?? throw new InvalidOperationException("Traffic snapshot is empty."); }
        catch (JsonException exception) { throw new InvalidOperationException("Traffic snapshot is malformed.", exception); }
        if (snapshot.SchemaVersion != SnapshotSchemaVersion || snapshot.Tick < 0 || snapshot.NextTicket <= 0)
            throw new InvalidOperationException("Traffic snapshot header is invalid.");
        var kernel = new TrafficKernel { Tick = snapshot.Tick, _nextTicket = snapshot.NextTicket,
            _regionDischarges = snapshot.RegionDischarges, _serviceCompletions = snapshot.ServiceCompletions,
            UnsupportedLayout = snapshot.UnsupportedLayout };
        foreach (var obstacle in snapshot.Obstacles) kernel.AddObstacle(obstacle);
        foreach (var item in snapshot.Regions)
        {
            var region = new RegionState(new(item.Id, item.Core, item.EastEntrance, item.WestEntrance,
                item.EastExitBerth, item.WestExitBerth, item.BatchLimit))
            { ActiveDirection = item.ActiveDirection, NextDirection = item.NextDirection, BatchAdmissions = item.BatchAdmissions };
            foreach (var id in item.Occupants)
                if (!region.Occupants.Add(id)) throw new InvalidOperationException("Duplicate region occupant.");
            if (!kernel._regions.TryAdd(item.Id, region)) throw new InvalidOperationException("Duplicate region id.");
        }
        foreach (var item in snapshot.Services)
        {
            var service = new ServiceState(new(item.Id, item.Anchor, item.DurationTicks))
            { ActivePersonId = item.ActivePersonId, RemainingTicks = item.RemainingTicks, CompletedCount = item.CompletedCount };
            if (!kernel._services.TryAdd(item.Id, service)) throw new InvalidOperationException("Duplicate service id.");
        }
        foreach (var item in snapshot.People)
        {
            var person = PersonState.Restore(item);
            if (!kernel._people.TryAdd(item.Id, person)) throw new InvalidOperationException("Duplicate person id.");
        }
        kernel.ValidateCanonicalState(); kernel.ValidatePhysicalState(); return kernel;
    }

    private void AdmitOrProgressServices()
    {
        foreach (var service in _services.Values)
        {
            if (service.ActivePersonId is { } active)
            {
                var person = _people[active]; person.IsInService = true;
                if (--service.RemainingTicks <= 0)
                {
                    person.IsInService = false; person.ServiceCompleted = true; service.ActivePersonId = null;
                    service.CompletedCount++; _serviceCompletions++;
                }
                continue;
            }
            var candidate = _people.Values.Where(person => !person.ServiceCompleted && person.ServiceId == service.Id &&
                    person.Position == service.Anchor).OrderBy(person => person.LocalTicket).ThenBy(person => person.Id).FirstOrDefault();
            if (candidate is null) continue;
            service.ActivePersonId = candidate.Id; service.RemainingTicks = service.DurationTicks; candidate.IsInService = true;
        }
    }

    private void EstablishRegionRequests()
    {
        foreach (var person in _people.Values.Where(person => person.RegionPhase == TrafficRegionPhase.Approaching))
        {
            var region = _regions[person.RegionId!];
            var entrance = person.RegionDirection == TrafficDirection.East ? region.EastEntrance : region.WestEntrance;
            if (person.Position == entrance)
            { person.RegionPhase = TrafficRegionPhase.Waiting; person.RegionTicket = _nextTicket++; }
        }
    }

    private void PrepareRegions()
    {
        foreach (var region in _regions.Values)
        {
            if (region.Occupants.Count > 0) continue;
            var waiting = _people.Values.Where(person => person.RegionId == region.Id && person.RegionPhase == TrafficRegionPhase.Waiting).ToArray();
            var east = waiting.Any(person => person.RegionDirection == TrafficDirection.East);
            var west = waiting.Any(person => person.RegionDirection == TrafficDirection.West);
            if (!east && !west) { region.ActiveDirection = null; region.BatchAdmissions = 0; continue; }
            if (region.ActiveDirection is { } active && region.BatchAdmissions < region.BatchLimit &&
                waiting.Any(person => person.RegionDirection == active)) continue;
            var selected = east && west ? region.NextDirection : east ? TrafficDirection.East : TrafficDirection.West;
            region.ActiveDirection = selected; region.NextDirection = selected == TrafficDirection.East ? TrafficDirection.West : TrafficDirection.East;
            region.BatchAdmissions = 0;
        }
    }

    private Proposal CreateProposal(PersonState person)
    {
        if (person.IsComplete || person.IsInService) return new(person.Position, false);
        var waypoint = person.Route[person.RouteIndex]; var dx = waypoint.XMillimetres - person.Position.XMillimetres;
        var dz = waypoint.ZMillimetres - person.Position.ZMillimetres; var distanceSquared = (long)dx * dx + (long)dz * dz;
        if (distanceSquared <= (long)person.Speed * person.Speed) return new(waypoint, true);
        var distance = CeilingSquareRoot(distanceSquared);
        return new(new(person.Position.XMillimetres + (int)((long)dx * person.Speed / distance),
            person.Position.ZMillimetres + (int)((long)dz * person.Speed / distance)), false);
    }

    private void ApplyRegionGates(Dictionary<ulong, Proposal> proposals)
    {
        foreach (var region in _regions.Values)
        {
            var berth = region.ActiveDirection == TrafficDirection.East ? region.EastExitBerth : region.WestExitBerth;
            var blocked = region.ActiveDirection is not null && _people.Values.Any(person =>
                !region.Occupants.Contains(person.Id) && DistanceSquared(person.Position, berth) < SeparationSquared);
            var crossing = _people.Values.Where(person => !region.Occupants.Contains(person.Id) &&
                person.Position != proposals[person.Id].Target &&
                SegmentIntersectsRectangle(person.Position, proposals[person.Id].Target, region.Core)).ToArray();
            var candidates = crossing.Where(person => person.RegionId == region.Id &&
                    person.RegionPhase == TrafficRegionPhase.Waiting && person.RegionDirection == region.ActiveDirection &&
                    proposals[person.Id].Accepted).OrderBy(person => person.RegionTicket).ThenBy(person => person.Id).ToArray();
            // This explicit region has one downstream berth: reserve it for one occupant at a time.
            var allow = !blocked && region.Occupants.Count == 0 && region.BatchAdmissions < region.BatchLimit
                ? candidates.FirstOrDefault() : null;
            foreach (var person in crossing)
                if (person != allow) proposals[person.Id].Accepted = false; else proposals[person.Id].EnterRegionId = region.Id;
            if (region.Occupants.Count > 0 || allow is not null)
                foreach (var person in _people.Values.Where(person => !region.Occupants.Contains(person.Id) && person != allow))
                    if (!SweptSafe(person.Position, proposals[person.Id].Target, berth, berth))
                        proposals[person.Id].Accepted = false;
            foreach (var person in _people.Values.Where(person => person.RegionId == region.Id &&
                         person.RegionPhase == TrafficRegionPhase.Waiting && person.RegionDirection != region.ActiveDirection))
                proposals[person.Id].Accepted = false;
        }
    }

    private void ApplyTerrainGates(Dictionary<ulong, Proposal> proposals)
    {
        foreach (var person in _people.Values)
            if (_obstacles.Any(obstacle => SegmentIntersectsRectangle(person.Position, proposals[person.Id].Target, obstacle)))
                proposals[person.Id].Accepted = false;
    }

    private void ArbitrateSweptConflicts(Dictionary<ulong, Proposal> proposals)
    {
        var people = _people.Values.ToArray();
        for (var left = 0; left < people.Length; left++) for (var right = left + 1; right < people.Length; right++)
        {
            var a = people[left]; var b = people[right]; var pa = proposals[a.Id]; var pb = proposals[b.Id];
            if (!pa.Accepted || !pb.Accepted || SweptSafe(a.Position, pa.Target, b.Position, pb.Target)) continue;
            var aAloneSafe = SweptSafe(a.Position, pa.Target, b.Position, b.Position);
            var bAloneSafe = SweptSafe(a.Position, a.Position, b.Position, pb.Target);
            if (aAloneSafe && (!bAloneSafe || PriorityCompare(a, b) <= 0)) pb.Accepted = false;
            else if (bAloneSafe) pa.Accepted = false;
            else { pa.Accepted = false; pb.Accepted = false; }
        }
        bool changed;
        do
        {
            changed = false;
            foreach (var moving in people.Where(person => proposals[person.Id].Accepted && proposals[person.Id].Target != person.Position))
            foreach (var stationary in people.Where(person => !proposals[person.Id].Accepted || proposals[person.Id].Target == person.Position))
            {
                if (moving.Id == stationary.Id || SweptSafe(moving.Position, proposals[moving.Id].Target,
                        stationary.Position, stationary.Position)) continue;
                proposals[moving.Id].Accepted = false; changed = true; break;
            }
        } while (changed);
    }

    private int PriorityCompare(PersonState left, PersonState right)
    {
        var li = left.RegionPhase == TrafficRegionPhase.Inside; var ri = right.RegionPhase == TrafficRegionPhase.Inside;
        if (li != ri) return li ? -1 : 1;
        var wait = right.WaitTicks.CompareTo(left.WaitTicks); if (wait != 0) return wait;
        var ticket = left.LocalTicket.CompareTo(right.LocalTicket); return ticket != 0 ? ticket : left.Id.CompareTo(right.Id);
    }

    private bool AtExitBerth(PersonState person)
    {
        var region = _regions[person.RegionId!];
        return person.Position == (person.RegionDirection == TrafficDirection.East ? region.EastExitBerth : region.WestExitBerth);
    }

    private void ValidateCanonicalState()
    {
        if (_people.Values.Any(person => person.Id == 0 || person.Speed <= 0 || person.Route.Length == 0 ||
            person.RouteIndex < 0 || person.RouteIndex > person.Route.Length || person.IntentGeneration < 0 ||
            person.WaitTicks < 0 || person.LocalTicket <= 0 || person.RegionTicket <= 0))
            throw new InvalidOperationException("Traffic person state is invalid.");
        foreach (var region in _regions.Values)
            if (region.BatchLimit <= 0 || region.BatchAdmissions < 0 || region.BatchAdmissions > region.BatchLimit ||
                region.Occupants.Any(id => !_people.TryGetValue(id, out var person) || person.RegionId != region.Id ||
                    person.RegionPhase != TrafficRegionPhase.Inside))
                throw new InvalidOperationException("Traffic region state is invalid.");
        foreach (var person in _people.Values)
        {
            if (!Enum.IsDefined(person.RegionPhase) ||
                person.RegionDirection is { } direction && !Enum.IsDefined(direction))
                throw new InvalidOperationException("Traffic person region enum is invalid.");
            if (person.RegionId is null)
            {
                if (person.RegionPhase != TrafficRegionPhase.None || person.RegionDirection is not null || person.RegionTicket is not null)
                    throw new InvalidOperationException("Traffic person has an orphan region claim.");
                continue;
            }
            if (!_regions.TryGetValue(person.RegionId, out var region) || person.RegionDirection is null ||
                person.RegionPhase == TrafficRegionPhase.None ||
                (person.RegionPhase == TrafficRegionPhase.Inside) != region.Occupants.Contains(person.Id) ||
                ((person.RegionPhase is TrafficRegionPhase.Waiting or TrafficRegionPhase.Inside) != person.RegionTicket.HasValue) ||
                person.RegionPhase == TrafficRegionPhase.Inside && region.ActiveDirection != person.RegionDirection)
                throw new InvalidOperationException("Traffic person region claim is inconsistent.");
        }
        foreach (var service in _services.Values)
            if (service.DurationTicks <= 0 || service.RemainingTicks < 0 ||
                service.ActivePersonId is { } id && (!_people.ContainsKey(id) || service.RemainingTicks == 0))
                throw new InvalidOperationException("Traffic service state is invalid.");
    }

    private void ValidatePhysicalState()
    {
        var people = _people.Values.ToArray();
        for (var left = 0; left < people.Length; left++)
        {
            if (_obstacles.Any(obstacle => obstacle.Contains(people[left].Position)))
                throw new InvalidOperationException("A person occupies blocked terrain.");
            for (var right = left + 1; right < people.Length; right++)
                if (DistanceSquared(people[left].Position, people[right].Position) < SeparationSquared)
                    throw new InvalidOperationException("Minimum centre separation is violated.");
        }
    }

    public static bool SweptPathsAreSafe(TrafficPoint a0, TrafficPoint a1, TrafficPoint b0, TrafficPoint b1) => SweptSafe(a0, a1, b0, b1);
    private static bool SweptSafe(TrafficPoint a0, TrafficPoint a1, TrafficPoint b0, TrafficPoint b1)
    {
        var rx = (long)a0.XMillimetres - b0.XMillimetres; var rz = (long)a0.ZMillimetres - b0.ZMillimetres;
        var vx = (long)(a1.XMillimetres - a0.XMillimetres) - (b1.XMillimetres - b0.XMillimetres);
        var vz = (long)(a1.ZMillimetres - a0.ZMillimetres) - (b1.ZMillimetres - b0.ZMillimetres);
        var denominator = vx * vx + vz * vz; var dot = rx * vx + rz * vz;
        if (denominator == 0 || dot >= 0) return rx * rx + rz * rz >= SeparationSquared;
        var ex = rx + vx; var ez = rz + vz;
        if (-dot >= denominator) return ex * ex + ez * ez >= SeparationSquared;
        var cross = rx * vz - rz * vx; return cross * cross >= SeparationSquared * denominator;
    }

    private static bool SegmentIntersectsRectangle(TrafficPoint start, TrafficPoint end, TrafficRectangle rectangle)
    {
        if (rectangle.Contains(start) || rectangle.Contains(end)) return true;
        var a = new TrafficPoint(rectangle.MinimumX, rectangle.MinimumZ); var b = new TrafficPoint(rectangle.MaximumX, rectangle.MinimumZ);
        var c = new TrafficPoint(rectangle.MaximumX, rectangle.MaximumZ); var d = new TrafficPoint(rectangle.MinimumX, rectangle.MaximumZ);
        return SegmentsIntersect(start, end, a, b) || SegmentsIntersect(start, end, b, c) ||
            SegmentsIntersect(start, end, c, d) || SegmentsIntersect(start, end, d, a);
    }

    private static bool SegmentsIntersect(TrafficPoint a, TrafficPoint b, TrafficPoint c, TrafficPoint d)
    {
        if (Math.Max(a.XMillimetres, b.XMillimetres) < Math.Min(c.XMillimetres, d.XMillimetres) ||
            Math.Max(c.XMillimetres, d.XMillimetres) < Math.Min(a.XMillimetres, b.XMillimetres) ||
            Math.Max(a.ZMillimetres, b.ZMillimetres) < Math.Min(c.ZMillimetres, d.ZMillimetres) ||
            Math.Max(c.ZMillimetres, d.ZMillimetres) < Math.Min(a.ZMillimetres, b.ZMillimetres)) return false;
        static long Cross(TrafficPoint p, TrafficPoint q, TrafficPoint r) =>
            (long)(q.XMillimetres - p.XMillimetres) * (r.ZMillimetres - p.ZMillimetres) -
            (long)(q.ZMillimetres - p.ZMillimetres) * (r.XMillimetres - p.XMillimetres);
        var ac = Cross(a, b, c); var ad = Cross(a, b, d); var ca = Cross(c, d, a); var cb = Cross(c, d, b);
        return ((ac <= 0 && ad >= 0) || (ac >= 0 && ad <= 0)) && ((ca <= 0 && cb >= 0) || (ca >= 0 && cb <= 0));
    }

    private static long DistanceSquared(TrafficPoint left, TrafficPoint right)
    { var dx = (long)left.XMillimetres - right.XMillimetres; var dz = (long)left.ZMillimetres - right.ZMillimetres; return dx * dx + dz * dz; }
    private static long Manhattan(TrafficPoint left, TrafficPoint right) =>
        Math.Abs((long)left.XMillimetres - right.XMillimetres) + Math.Abs((long)left.ZMillimetres - right.ZMillimetres);
    private static long CeilingSquareRoot(long value)
    { var root = (long)Math.Sqrt(value); while (root * root < value) root++; while ((root - 1) * (root - 1) >= value) root--; return root; }

    private sealed class Proposal(TrafficPoint target, bool reachesWaypoint)
    { public TrafficPoint Target { get; } = target; public bool ReachesWaypoint { get; } = reachesWaypoint;
        public bool Accepted { get; set; } = true; public string? EnterRegionId { get; set; } }

    private sealed class PersonState
    {
        public PersonState(TrafficPersonDefinition definition, long ticket)
        { Id = definition.Id; Position = definition.Start; Speed = definition.SpeedMillimetresPerTick; Route = definition.Route.ToArray();
            LocalTicket = ticket; RegionId = definition.RegionId; RegionDirection = definition.RegionDirection; ServiceId = definition.ServiceId;
            RegionPhase = RegionId is null ? TrafficRegionPhase.None : TrafficRegionPhase.Approaching; }
        public ulong Id; public int IntentGeneration; public TrafficPoint Position; public int Speed; public TrafficPoint[] Route;
        public int RouteIndex; public int WaitTicks; public long LocalTicket; public string? RegionId; public TrafficDirection? RegionDirection;
        public TrafficRegionPhase RegionPhase; public long? RegionTicket; public string? ServiceId; public int UsefulProgressTicks;
        public bool IsInService; public bool ServiceCompleted; public bool IsComplete => RouteIndex >= Route.Length;
        public TrafficPersonSnapshot Capture() => new(Id, IntentGeneration, Position, Speed, Route.ToArray(), RouteIndex, WaitTicks,
            LocalTicket, RegionId, RegionDirection, RegionPhase, RegionTicket, ServiceId, UsefulProgressTicks, IsInService, ServiceCompleted);
        public static PersonState Restore(TrafficPersonSnapshot item) => new(new(item.Id, item.Position, item.SpeedMillimetresPerTick,
            item.Route, item.RegionId, item.RegionDirection, item.ServiceId), item.LocalTicket)
        { IntentGeneration = item.IntentGeneration, RouteIndex = item.RouteIndex, WaitTicks = item.WaitTicks,
            RegionPhase = item.RegionPhase, RegionTicket = item.RegionTicket, UsefulProgressTicks = item.UsefulProgressTicks,
            IsInService = item.IsInService, ServiceCompleted = item.ServiceCompleted };
    }

    private sealed class RegionState(TrafficRegionDefinition definition)
    {
        public string Id { get; } = definition.Id; public TrafficRectangle Core { get; } = definition.Core;
        public TrafficPoint EastEntrance { get; } = definition.EastEntrance; public TrafficPoint WestEntrance { get; } = definition.WestEntrance;
        public TrafficPoint EastExitBerth { get; } = definition.EastExitBerth; public TrafficPoint WestExitBerth { get; } = definition.WestExitBerth;
        public int BatchLimit { get; } = definition.BatchLimit; public TrafficDirection? ActiveDirection;
        public TrafficDirection NextDirection = TrafficDirection.East; public int BatchAdmissions; public SortedSet<ulong> Occupants { get; } = [];
        public TrafficRegionSnapshot Capture() => new(Id, Core, EastEntrance, WestEntrance, EastExitBerth,
            WestExitBerth, BatchLimit, ActiveDirection, NextDirection, BatchAdmissions, Occupants.ToArray());
    }

    private sealed class ServiceState(TrafficServiceDefinition definition)
    {
        public string Id { get; } = definition.Id; public TrafficPoint Anchor { get; } = definition.Anchor;
        public int DurationTicks { get; } = definition.DurationTicks; public ulong? ActivePersonId; public int RemainingTicks; public int CompletedCount;
        public TrafficServiceSnapshot Capture() => new(Id, Anchor, DurationTicks, ActivePersonId, RemainingTicks, CompletedCount);
    }
}
