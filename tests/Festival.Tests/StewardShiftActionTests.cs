using System.Collections;
using System.Reflection;
using Festival.Persistence;
using Festival.Simulation;

namespace Festival.Tests;

/// <summary>The steward shift's mechanics: hold-to-act actions, radio calls, the player's needs, failure and the report.</summary>
[TestClass]
public sealed partial class StewardShiftActionTests
{
    private const ulong Seed = 20260922;
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static ulong Player(GameSession s) => s.PlayerStewardId!.Value;

    private static (int X, int Z) At(GameSession s, ulong id)
    {
        var agent = s.CaptureObservation().NavigationAgents.Single(item => item.Id.Value == id);
        return (agent.XMillimetres, agent.ZMillimetres);
    }

    private static object Call(GameSession s, string method, params object[] args) => typeof(GameSession).GetMethod(method, Private)!.Invoke(s, args)!;
    private static void Forge(GameSession s, ulong id, Func<Person, Person> change) => Call(s, "UpdatePerson", id, change);
    private static T Field<T>(GameSession s, string name) => (T)typeof(GameSession).GetField(name, Private)!.GetValue(s)!;
    private static void SetField(GameSession s, string name, object? value) => typeof(GameSession).GetField(name, Private)!.SetValue(s, value);

    /// <summary>Puts the player's steward down on a cell centre, standing, facing nothing in particular.</summary>
    private static void Teleport(GameSession s, int x, int z)
    {
        var agents = (IDictionary)typeof(GameSession).GetField("_navigationAgents", Private)!.GetValue(s)!;
        var nav = agents[new EntityId(Player(s))]!;
        void Set(string name, object value) => nav.GetType().GetProperty(name)!.SetValue(nav, value);
        Set("XMillimetres", x); Set("ZMillimetres", z); Set("SegmentOriginXMillimetres", x); Set("SegmentOriginZMillimetres", z);
    }

    /// <summary>The player a metre or so from a point, on open ground in the field, nearest the side they're on; returns the facing at it.</summary>
    private static int StandBeside(GameSession s, int x, int z)
    {
        var grid = s.TraversalGrid!;
        var target = TraversalGrid.WorldToCell(x, z);
        var me = At(s, Player(s));
        var spot = Enumerable.Range(-3, 7).SelectMany(dx => Enumerable.Range(-3, 7).Select(dz => new GridCell(target.X + dx, target.Z + dz)))
            .Where(cell => Math.Max(Math.Abs(cell.X - target.X), Math.Abs(cell.Z - target.Z)) >= 2 && grid.Get(cell).IsWalkable && StewardShiftRules.InField(cell))
            .OrderBy(cell => { var c = TraversalGrid.CellCentre(cell); return Math.Abs((long)(c.XMillimetres - x) * (c.XMillimetres - x) + (long)(c.ZMillimetres - z) * (c.ZMillimetres - z) - 1_200_000L); })
            .ThenBy(cell => cell.X).ThenBy(cell => cell.Z).First();
        var (sx, sz) = TraversalGrid.CellCentre(spot);
        Teleport(s, sx, sz);
        _ = me;
        return StewardShiftRules.FacingTowards(x - sx, z - sz);
    }

    private static int FacingAt(GameSession s, int x, int z)
    {
        var me = At(s, Player(s));
        return StewardShiftRules.FacingTowards(x - me.X, z - me.Z);
    }

    /// <summary>Starts holding the action on a target, from where the player stands, facing it.</summary>
    private static CommandResult Press(GameSession s, StewardActionKind kind, string target)
    {
        var point = s.StewardTargetPoint(target)!.Value;
        return BuildSession.Send(s, new StewardActionCommand(kind, target, FacingAt(s, point.X, point.Z)));
    }

    /// <summary>Holds until it's done or dropped, keeping beside a moving target when asked; true when it completed.</summary>
    private static bool Hold(GameSession s, StewardActionKind kind, string target, bool follow = false)
    {
        var accepted = Press(s, kind, target);
        Assert.IsTrue(accepted.IsAccepted, $"{kind} on {target}: {accepted.Message}");
        for (var tick = 0; tick < StewardShiftRules.DurationTicks(kind) + 2 && s.CaptureStewardShift()!.Action is not null; tick++)
        {
            if (follow && s.StewardTargetPoint(target) is { } point)
            {
                var me = At(s, Player(s));
                long dx = point.X - me.X, dz = point.Z - me.Z;
                if (dx * dx + dz * dz > 1_800L * 1_800) StandBeside(s, point.X, point.Z);
            }
            s.AdvanceWithoutSnapshot(1);
        }
        return s.CaptureStewardShift()!.LastOutcome is { Completed: true } done && done.Kind == kind && done.TargetId == target;
    }

    /// <summary>An admitted guest standing still in the field, with another guest within a metre and a half.</summary>
    private static ulong StandingGuest(GameSession s, Func<ulong, bool>? also = null)
    {
        var guests = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed).Select(p => p.AgentId).ToArray();
        var agents = s.CaptureObservation().NavigationAgents.ToDictionary(a => a.Id.Value);
        bool Near(ulong a, ulong b) { var x = agents[a]; var y = agents[b]; long dx = x.XMillimetres - y.XMillimetres, dz = x.ZMillimetres - y.ZMillimetres; return dx * dx + dz * dz <= 1_500L * 1_500; }
        var picked = guests.Where(id => agents[id].Action != AgentNavigationAction.Travelling && StewardShiftRules.InField(TraversalGrid.WorldToCell(agents[id].XMillimetres, agents[id].ZMillimetres)) &&
                s.CapturePerson(id) is { ConductStage: DisorderStage.Calm, Intent: MedicalIntent.WatchShow, VendorId: null, ToiletStage: ToiletVisitStage.None, HealthStage: MedicalStage.Clear or MedicalStage.Treated } &&
                guests.Any(other => other != id && Near(id, other)) && (also?.Invoke(id) ?? true))
            .OrderBy(id => id).ToArray();
        if (picked.Length == 0)
            Assert.Fail($"No standing guest of {guests.Length}: still {guests.Count(id => agents[id].Action != AgentNavigationAction.Travelling)}, " +
                $"calm {guests.Count(id => s.CapturePerson(id)!.ConductStage == DisorderStage.Calm)} ({string.Join(",", guests.Select(id => s.CapturePerson(id)!.ConductStage).Distinct())}), " +
                $"watching {guests.Count(id => s.CapturePerson(id)!.Intent == MedicalIntent.WatchShow)}, near {guests.Count(id => guests.Any(o => o != id && Near(id, o)))}");
        return picked[0];
    }

    private static string Who(ulong id) => $"person:{id}";

    private static void Restores(GameSession s, string when)
    {
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, $"{when}: {restored.Error}");
    }

}
