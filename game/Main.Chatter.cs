using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// What the crowd says about the day: loving or sitting through a set, cheering an encore, grumbling in a queue,
/// the heat, the smell from the loos, wasps at a full bin, a fight, someone collapsing, and how they felt on the way
/// home. Up to three remarks show at once across every kind of speech, spread apart, with complaints about a problem
/// before happy chatter, and each tinted by mood. Read from the simulation twice a second; presentation only.
/// </summary>
public partial class Main
{
    internal enum Mood { Happy, Neutral, Grumble, Angry }

    /// <summary>One thing someone could say now. Higher priority wins; a topic rests a while after it's heard.</summary>
    private sealed record Remark(ulong Speaker, string Text, Mood Mood, int Priority, string Topic, double TopicRest, string? Once = null);

    private const int SpeechMaxVisible = 3;
    private const double ChatterSeconds = 3.2, ChatterSpacingSeconds = 1.4, ChatterPersonRest = 25;
    private const float ChatterSpreadMetres = 7, ChatterHeight = 2.35f;
    private readonly List<(Label3D Label, ulong Speaker, double Age)> _chatter = [];
    private readonly Dictionary<ulong, double> _chatterPersonRest = [];
    private readonly Dictionary<string, double> _chatterTopicRest = [];
    private readonly HashSet<string> _chatterSaid = [];
    private double _chatterPoll, _chatterSince = ChatterSpacingSeconds;
    private (GameSession? Session, int Attempt) _chatterDay;
    // What changed since the last look: arrivals, queue joins, purchases, crowd reactions, rescues and recoveries.
    private readonly HashSet<ulong> _chatterAdmitted = [];
    private readonly Dictionary<ulong, long> _chatterQueuedSince = [];
    private readonly Dictionary<ulong, long> _chatterArrived = [];
    private int _chatterPurchases, _chatterReactionSequence;
    private long _chatterReactionTick = long.MinValue;
    private readonly HashSet<string> _chatterStuck = [];
    private readonly Dictionary<string, long> _chatterFreed = [];
    private readonly Dictionary<ulong, MedicalStage> _chatterStages = [];
    private readonly Dictionary<ulong, long> _chatterRecovered = [];
    private List<(ulong Id, ImmersionProduct Product, long Tick, int Index)> _chatterBought = [];

    internal static Color MoodColour(Mood mood) => mood switch
    {
        Mood.Happy => new Color("a4ecaa"),
        Mood.Grumble => new Color("ffd27a"),
        Mood.Angry => new Color("ff7d6e"),
        _ => new Color("fff7e1"),
    };

    /// <summary>Every speech bubble showing now, from any system, so the budget and spacing apply across them all.</summary>
    private List<Vector3> VisibleSpeech() => SpeechLabels().Select(label => label.Position).ToList();

    private List<Label3D> SpeechLabels()
    {
        var shown = new List<Label3D>();
        void Add(Label3D? label) { if (label is not null && IsInstanceValid(label) && label.Visible) shown.Add(label); }
        foreach (var label in _medicalCueLabels.Values) Add(label);
        foreach (var label in _disorderCueLabels.Values) Add(label);
        foreach (var label in _immersionWarningLabels.Values) Add(label);
        foreach (var label in _faultRemarkLabels.Values) Add(label);
        Add(_immersionRemark); Add(_litterRemarkLabel);
        foreach (var (label, _, _) in _chatter) Add(label);
        return shown;
    }

    /// <summary>
    /// Lifts any bubble that would overlap another on screen by a line at a time, lowest first, so an argument's two
    /// shouts or two passers-by never print over each other. Only the labels' pixel offsets move, reset every frame.
    /// </summary>
    private void SeparateSpeech()
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return;
        var placed = new List<Rect2>();
        var labels = SpeechLabels();
        foreach (var label in labels) label.Offset = Vector2.Zero;
        var screen = GetViewport().GetVisibleRect().Size.Y;
        var items = labels.Where(label => !camera.IsPositionBehind(label.GlobalPosition)).Select(label =>
        {
            // Speech is drawn at a fixed screen size: a label pixel is PixelSize × half the viewport's height on screen.
            var scale = label.PixelSize * screen / 2;
            // Some remarks run to two lines: measure the longest line's width and every line's height.
            var lines = label.Text.Split('\n');
            var lineHeight = label.FontSize * 1.25f * scale;
            var height = lineHeight * lines.Length;
            var half = lines.Max(line => line.Length) * label.FontSize * 0.5f * scale / 2;
            var centre = camera.UnprojectPosition(label.GlobalPosition);
            return (label, rect: new Rect2(centre.X - half, centre.Y - height / 2, half * 2, height), lineHeight);
        }).OrderByDescending(item => item.rect.Position.Y).ToList();
        foreach (var (label, start, lineHeight) in items)
        {
            var rect = start; var lifts = 0;
            while (lifts < 4 && placed.Any(other => other.Intersects(rect))) { rect.Position -= new Vector2(0, lineHeight); lifts++; }
            if (lifts > 0) label.Offset = new Vector2(0, lifts * label.FontSize * 1.25f);
            placed.Add(rect);
        }
    }

    private bool SpeechCrowded() => VisibleSpeech().Count >= SpeechMaxVisible;

    private void ProcessChatter(double delta)
    {
        var step = _session.IsPaused ? 0 : delta;
        for (var i = _chatter.Count - 1; i >= 0; i--)
        {
            var (label, speaker, age) = _chatter[i];
            age += step;
            if (age >= ChatterSeconds || !_attendeeVisuals.TryGetValue(new EntityId(speaker), out var body) || !body.Visible || EyeViewActive)
            { label.QueueFree(); _chatter.RemoveAt(i); continue; }
            _chatter[i] = (label, speaker, age);
            label.Position = body.Position + new Vector3(0, ChatterHeight, 0);
        }
        foreach (var id in _chatterPersonRest.Keys.ToArray()) _chatterPersonRest[id] -= step;
        foreach (var topic in _chatterTopicRest.Keys.ToArray()) _chatterTopicRest[topic] -= step;
        _chatterSince += step;
        var day = (_session, _session.CapturePreparation()?.Attempt ?? 0);
        if (_chatterDay != day) { ResetChatter(); _chatterDay = day; }
        if (_session.PreparedStatus is not (PreparationStatus.Running or PreparationStatus.Departing) || _session.IsPaused || EyeViewActive) return;
        _chatterPoll -= delta;
        if (_chatterPoll > 0) return;
        _chatterPoll = .5;
        var remarks = ObserveChatter();
        var visible = VisibleSpeech();
        if (visible.Count >= SpeechMaxVisible || _chatterSince < ChatterSpacingSeconds) return;
        var speaking = _chatter.Select(c => c.Speaker).ToHashSet();
        foreach (var remark in remarks.OrderByDescending(r => r.Priority).ThenBy(r => Hash(r.Speaker, _session.CurrentTick / 40)))
        {
            if (speaking.Contains(remark.Speaker) || _chatterPersonRest.GetValueOrDefault(remark.Speaker) > 0 ||
                _chatterTopicRest.GetValueOrDefault(remark.Topic) > 0 || remark.Once is { } once && _chatterSaid.Contains(once) ||
                !_attendeeVisuals.TryGetValue(new EntityId(remark.Speaker), out var body) || !body.Visible) continue;
            var at = body.Position + new Vector3(0, ChatterHeight, 0);
            if (visible.Any(p => p.DistanceTo(at) < ChatterSpreadMetres)) continue;
            Say(remark, at);
            return;
        }
    }

    private void Say(Remark remark, Vector3 at)
    {
        var label = WorldText.Speech(new Label3D { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Text = remark.Text,
            Modulate = MoodColour(remark.Mood), Position = at }, 36);
        label.Layers = EyeHiddenLayer;
        AddChild(label);
        _chatter.Add((label, remark.Speaker, 0));
        _chatterPersonRest[remark.Speaker] = ChatterPersonRest;
        _chatterTopicRest[remark.Topic] = remark.TopicRest;
        if (remark.Once is { } once) _chatterSaid.Add(once);
        _chatterSince = 0;
    }

    private void ResetChatter()
    {
        foreach (var (label, _, _) in _chatter) label.QueueFree();
        _chatter.Clear(); _chatterPersonRest.Clear(); _chatterTopicRest.Clear(); _chatterSaid.Clear();
        _chatterQueuedSince.Clear(); _chatterArrived.Clear(); _chatterStuck.Clear(); _chatterFreed.Clear(); _chatterRecovered.Clear();
        _chatterBought = [];
        // Nothing that already happened is news: prime what's been seen without anyone speaking.
        _chatterAdmitted.Clear();
        foreach (var person in _session.CapturePreparation()?.People ?? []) if (person.Admitted) _chatterAdmitted.Add(person.AgentId);
        _chatterPurchases = _session.CaptureImmersion()?.Purchases.Length ?? 0;
        _chatterReactionSequence = _session.CaptureLivePerformance()?.ReactionSequence ?? 0;
        _chatterReactionTick = long.MinValue;
        _chatterStages.Clear();
        foreach (var need in _session.CaptureMedical()?.Needs ?? []) _chatterStages[need.AgentId] = need.Stage;
        foreach (var fault in _session.CaptureFaults()?.Faults ?? [])
            if (fault.Kind == FacilityFaultKind.StuckInToilet && fault.Stage == FacilityFaultStage.Active) _chatterStuck.Add(fault.Id);
    }

    private static ulong Hash(ulong id, long salt) => (id * 2654435761UL) ^ ((ulong)salt * 40503UL);

    /// <summary>One of several lines, steady for a few seconds for the same speaker so a remark doesn't flicker.</summary>
    private string Pick(ulong id, params string[] lines) => lines[(int)(Hash(id, _session.CurrentTick / 400) % (ulong)lines.Length)];

    private List<Remark> ObserveChatter()
    {
        var tick = _session.CurrentTick;
        var remarks = new List<Remark>();
        var prep = _session.CapturePreparation();
        if (prep is null) return remarks;
        var guests = prep.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed).ToDictionary(p => p.AgentId);
        var medical = _session.CaptureMedical();
        var needs = medical?.Needs.ToDictionary(n => n.AgentId) ?? [];
        var disorder = _session.CaptureDisorder();
        var busy = new HashSet<ulong>(); // People in a fight or an emergency say only what that calls for.
        foreach (var need in needs.Values) if (need.Stage is MedicalStage.Distress or MedicalStage.Collapsed or MedicalStage.Critical) busy.Add(need.AgentId);
        foreach (var person in disorder?.People ?? []) if (person.Stage is DisorderStage.Argument or DisorderStage.Fight or DisorderStage.Injured) busy.Add(person.AgentId);
        bool Free(ulong id) => guests.ContainsKey(id) && !busy.Contains(id) && _attendeeVisuals.ContainsKey(new EntityId(id));
        Vector3 At(ulong id) => _attendeeVisuals.TryGetValue(new EntityId(id), out var body) ? body.Position : new Vector3(9999, 0, 9999);
        IEnumerable<ulong> Near(Vector3 point, float metres) => guests.Keys.Where(id => Free(id) && At(id).DistanceTo(point) <= metres);
        var immersion = _session.CaptureImmersion();
        var consumers = immersion?.People.ToDictionary(p => p.AgentId) ?? [];
        var perks = _session.CapturePerks() is { } p ? (p.Ended ? p.FrozenEffects : p.Equipped) : [];
        var beerFestival = perks.Contains(PerkCatalogue.BeerFestival);
        var live = _session.CaptureLivePerformance();
        var act = _session.CurrentFestivalAct;

        // Arrivals: a few seconds to say something as they come through the gate.
        foreach (var id in guests.Keys) if (_chatterAdmitted.Add(id)) _chatterArrived[id] = tick;
        foreach (var (id, since) in _chatterArrived.ToArray())
        {
            if (tick - since > 800) { _chatterArrived.Remove(id); continue; }
            if (!Free(id)) continue;
            var programme = _session.CaptureProgramme();
            // Only an act still to come, for someone arriving late.
            var wanted = programme?.ActIds.Skip(Math.Max(0, programme.CurrentSlot)).Select(ActCatalogue.Find).FirstOrDefault(a => a?.Genre == guests[id].ExpectedGenre);
            var line = wanted is not null && Hash(id, 7) % 3 == 0 ? $"Can't wait for {wanted.Name}!"
                : Pick(id, "We made it!", "Right, where's the bar?", "This better be worth the journey");
            remarks.Add(new(id, line, Mood.Happy, 2, "arrive", 4, $"arrive:{id}"));
        }

        // The music: loving it, sitting through it, booing, an encore, or relief when it comes back.
        if (live is not null)
        {
            if (live.ReactionSequence != _chatterReactionSequence) { _chatterReactionSequence = live.ReactionSequence; _chatterReactionTick = tick; }
            var fresh = tick - _chatterReactionTick <= 400;
            foreach (var listener in live.Listeners.Where(l => l.AtPlace && Free(l.AgentId)))
            {
                var id = listener.AgentId;
                if (live.Stage == LiveSetStage.Live && listener.Enthusiasm >= 80 && act is not null)
                    remarks.Add(new(id, Hash(id, tick / 400) % 3 == 0 ? GenreCheer(act.Genre) : Pick(id, "TUNE!", "I love this band!", "Best festival ever!"),
                        Mood.Happy, 1, "set-love", 7));
                else if (live.Stage == LiveSetStage.Live && listener.Enthusiasm < 35)
                    remarks.Add(new(id, Pick(id, "Not really my thing", "When's the next band?", "Bit boring, this", "Bit of a weird booking",
                        "I wouldn't have booked these"), Mood.Grumble, 2, "set-meh", 12));
                if (live.LastReaction == "sustained-boo")
                    remarks.Add(new(id, Pick(id, "Boo! Get off!", "Rubbish!"), Mood.Angry, 3, "boo", 4));
                if (fresh && live.LastReaction == "set-finished-applause")
                    remarks.Add(new(id, Pick(id, "Encore!", "One more song!"), Mood.Happy, 3, "encore", 1.5, $"encore:{id}:{_chatterReactionSequence}"));
                if (fresh && live.LastReaction == "set-finished-interrupted")
                    remarks.Add(new(id, "That's it?!", Mood.Grumble, 3, "cut-short", 2, $"cut:{id}:{_chatterReactionSequence}"));
                if (fresh && live.LastReaction == "resumed")
                    remarks.Add(new(id, Pick(id, "YES! It's back!", "Finally!"), Mood.Happy, 3, "resumed", 1.5, $"resumed:{id}:{_chatterReactionSequence}"));
                if (_session.CaptureEquipment()?.Stage is EquipmentStage.Warning or EquipmentStage.DangerousFault)
                    remarks.Add(new(id, Pick(id, "What's that burning smell?", "Is the generator meant to make that noise?"), Mood.Grumble, 3, "generator", 10));
            }
        }

        // Queues: at the bar, the food van and the loos, once someone's been waiting a while; and chat with Friendly Queues.
        var queued = new Dictionary<ulong, string>();
        foreach (var vendor in _session.CaptureVendors())
            foreach (var id in vendor.Queue) if (id != vendor.OwnerId) queued[id] = vendor.Id;
        foreach (var (id, person) in consumers) if (person.ToiletStage == ToiletVisitStage.Queued) queued[id] = "toilet";
        foreach (var point in _session.CaptureWaterPoints())
            foreach (var id in point.Queue.Concat(point.Overflow)) if (id != point.OwnerId) queued.TryAdd(id, "water");
        foreach (var id in _chatterQueuedSince.Keys.Where(id => !queued.ContainsKey(id)).ToArray()) _chatterQueuedSince.Remove(id);
        foreach (var (id, where) in queued)
        {
            if (!_chatterQueuedSince.TryGetValue(id, out var since)) _chatterQueuedSince[id] = since = tick;
            if (!Free(id)) continue;
            if (perks.Contains(PerkCatalogue.FriendlyQueues))
                remarks.Add(new(id, Pick(id, "Who are you here for?", "Love your hat!", "Seen them live before?"), Mood.Happy, 1, "queue-chat", 6));
            if (tick - since < 1_200) continue;
            if (where == "drinks")
                remarks.Add(new(id, Pick(id, "Is the barman on a break?", "I'll be sober by the time I'm served"), Mood.Grumble, 2, "queue-bar", 8));
            else if (where == "food")
                remarks.Add(new(id, Pick(id, "I need food!", "How long can it take?!"), Mood.Grumble, 2, "queue-food", 8));
            else if (where == "toilet")
            {
                var bursting = consumers.TryGetValue(id, out var c) && c.ToiletNeed >= 8_000;
                if (_session.GuestCharacterOf(id).Ibs)
                    remarks.Add(new(id, "Emergency! Let me through!", Mood.Angry, 3, "queue-toilet", 8));
                else
                    remarks.Add(new(id, bursting ? Pick(id, "I'm bursting!", "I don't think I can wait much longer")
                        : Pick(id, "Hurry up in there!", "Do they have any other toilets?"), Mood.Grumble, 2, "queue-toilet", 8));
            }
        }

        // The bar and the van: sold out, too dear, or a happy purchase.
        if (immersion is not null)
        {
            foreach (var vendor in _session.CaptureVendors())
            {
                var at = ImmersionPosition(vendor.Cell);
                var out_ = !_session.StallPowered(vendor.Id) ? vendor.Id == "food" ? "The food van's shut?!" : "The bar's closed?!"
                    // The food trader never runs out; only the bar's stock can.
                    : vendor.Id == "food" ? null
                    : immersion.StockPurchased && immersion.BeerStock == 0 ? "Out of beer?! At a festival?!"
                    : immersion.StockPurchased && immersion.SoftStock == 0 ? "No cola?!" : null;
                foreach (var id in Near(at, 10))
                {
                    if (out_ is not null) remarks.Add(new(id, out_, Mood.Grumble, 2, $"soldout:{vendor.Id}", 15));
                    if (vendor.Id == "drinks" && consumers.TryGetValue(id, out var c) && !_session.Teetotal(id) && c.BeerTaste >= 50 &&
                        immersion.BeerStock > 0 && !_session.CanAffordImmersion(id, ImmersionProduct.Beer))
                        remarks.Add(new(id, beerFestival && Hash(id, 3) % 2 == 0 ? $"{FestivalCurrency.Format(_session.ImmersionListPrice(ImmersionProduct.Beer))} a pint?!"
                            : Pick(id, "How much?!", "Skint already…"), Mood.Grumble, 2, "skint", 10, $"skint:{id}"));
                }
            }
            for (var index = _chatterPurchases; index < immersion.Purchases.Length; index++)
                if (immersion.Purchases[index] is { Product: not ImmersionProduct.Water } purchase) _chatterBought.Add((purchase.AgentId, purchase.Product, tick, index));
            _chatterPurchases = immersion.Purchases.Length;
            _chatterBought.RemoveAll(b => tick - b.Tick > 400);
            foreach (var (id, product, _, index) in _chatterBought.Where(b => Free(b.Id)))
                remarks.Add(new(id, product == ImmersionProduct.Beer
                    ? beerFestival && Hash(id, 5) % 2 == 0 ? "Now THAT'S a pint!" : Pick(id, "Cheers!", "Ahh, lovely")
                    : "Ahh, lovely", Mood.Happy, 1, "bought", 5, $"bought:{index}"));
        }

        // Dav's tanker, which everyone calls Dirty Henry: cheered in, then cursed while it pumps.
        foreach (var call in _session.CaptureLavSucker()?.Calls ?? [])
        {
            if (call.Stage == LavSuckerStage.Gone) continue;
            var truck = new Vector3(call.XMillimetres / 1000f, 0, call.ZMillimetres / 1000f);
            foreach (var id in Near(truck, 8))
                remarks.Add(call.Stage == LavSuckerStage.Pumping
                    ? new(id, Pick(id, "Oh, that's rank!", "Who ordered the smell?", "Dirty Henry's at it again", "I'm never unseeing that", "Eurgh, not near the food!"), Mood.Grumble, 2, "henry:pump", 12)
                    : new(id, Pick(id, "Here comes Dirty Henry!", "It's Dirty Henry!", "Make way for Dirty Henry", "Dirty Henry's here!"), Mood.Neutral, 1, "henry:drive", 20));
        }

        // The heat, the loos' smell and wasps at a full bin.
        foreach (var id in guests.Keys.Where(Free))
        {
            if (medical?.IsHot == true && needs.TryGetValue(id, out var need) && need.HeatExposure >= 6_000)
                remarks.Add(new(id, Pick(id, "It's boiling!", "Need some shade", "I'm melting"), Mood.Grumble, 2, "heat", 10));
            if (_session.ToiletSmellPenaltyPerSecond(id) > 0)
                remarks.Add(new(id, _session.GuestLabels(id).Contains("Princess") ? "I think I'm going to be sick"
                    : Pick(id, "What is that smell?!", "Ugh, the loos…"), Mood.Grumble, 2, "smell", 10));
        }
        foreach (var bin in _session.CaptureBins().Where(b => b.Wasps))
            foreach (var id in Near(ImmersionPosition(bin.Cell), LitterRules.WaspRadiusCells * TraversalGrid.CellSizeMillimetres / 1000f))
                remarks.Add(_session.GuestCharacterOf(id).WaspAllergy
                    ? new(id, "I'm allergic! Get it away!", Mood.Angry, 3, "wasps", 6)
                    : new(id, Pick(id, "Wasps! Get away!", "Watch out! Wasps!"), Mood.Grumble, 2, "wasps", 8));

        // Fights and collapses draw a crowd; a rescue and a recovery are a relief.
        foreach (var fighter in disorder?.People.Where(f => f.Stage == DisorderStage.Fight) ?? [])
            foreach (var id in Near(At(fighter.AgentId), 10))
                remarks.Add(new(id, Pick(id, "Fight! Fight!", "Leave it, mate!", "Someone get security!"), Mood.Angry, 3, "fight-watch", 3));
        foreach (var need in needs.Values)
        {
            var before = _chatterStages.GetValueOrDefault(need.AgentId, need.Stage);
            if (need.Stage == MedicalStage.Treated && before is MedicalStage.Collapsed or MedicalStage.Critical) _chatterRecovered[need.AgentId] = tick;
            _chatterStages[need.AgentId] = need.Stage;
            if (need.Stage is MedicalStage.Collapsed or MedicalStage.Critical)
                foreach (var id in Near(At(need.AgentId), 8))
                    remarks.Add(new(id, Pick(id, "Someone get a medic!", "Are they OK?!"), Mood.Angry, 4, "collapse-watch", 3));
        }
        foreach (var (id, since) in _chatterRecovered.ToArray())
        {
            if (tick - since > 800) { _chatterRecovered.Remove(id); continue; }
            if (Free(id)) remarks.Add(new(id, "Thank you so much", Mood.Happy, 3, "thanks", 2, $"thanks:{id}:{since}"));
        }
        foreach (var steward in _session.GetStewardResponses().Where(s => s.Stage is SecurityResponseStage.Calming or SecurityResponseStage.Confronting))
            if (steward.TargetId is { } target && guests.ContainsKey(target))
                remarks.Add(new(target, Pick(target, "Alright, alright, I'm going", "Sorry, sorry"), Mood.Grumble, 3, "steward", 3));
        foreach (var job in medical?.StaffInterventions ?? [])
            if (job.Action == StaffInterventionAction.EscortOut && job.Stage == StaffInterventionStage.Escorting && Free(job.GuestId))
                remarks.Add(new(job.GuestId, Pick(job.GuestId, "Alright, alright, I'm going", "Sorry, sorry"), Mood.Grumble, 3, "steward", 3));
        foreach (var fault in _session.CaptureFaults()?.Faults ?? [])
        {
            if (fault.Kind != FacilityFaultKind.StuckInToilet) continue;
            if (fault.Stage == FacilityFaultStage.Active) _chatterStuck.Add(fault.Id);
            else if (_chatterStuck.Remove(fault.Id)) _chatterFreed[fault.Id] = tick;
            if (_chatterFreed.TryGetValue(fault.Id, out var freed) && tick - freed <= 800 && Free(fault.VictimId))
                remarks.Add(new(fault.VictimId, Pick(fault.VictimId, "FREEDOM!", "Never again."), Mood.Happy, 3, "freed", 2, $"freed:{fault.Id}"));
        }

        // A goody two-shoes tidying up, and how everyone felt on the way home.
        foreach (var piece in _session.CaptureLitter()?.Pieces ?? [])
            if (piece.CarrierId is { } carrier && carrier != piece.ProducerId && Free(carrier) && _session.GuestLabels(carrier).Contains("Goody two-shoes"))
                remarks.Add(new(carrier, Pick(carrier, "I'll get that", "Leave no trace!"), Mood.Happy, 1, "goody", 8, $"goody:{carrier}"));
        // Only at the end of the day: someone escorted out or sent home after care earlier isn't reviewing the day.
        foreach (var need in needs.Values.Where(n => _session.PreparedStatus == PreparationStatus.Departing && n.Intent == MedicalIntent.Leaving && Free(n.AgentId)))
        {
            var id = need.AgentId; var satisfaction = guests[id].Satisfaction;
            remarks.Add(satisfaction >= 6_500
                ? new(id, Pick(id, "Best day ever!", "Same time next year!", "What a day!", "Loved every minute"), Mood.Happy, 2, "home", 2, $"home:{id}")
                : satisfaction >= 4_000
                ? new(id, Pick(id, "Not bad, I suppose", "It was alright", "Decent enough", "Can't complain"), Mood.Neutral, 2, "home", 2, $"home:{id}")
                : new(id, Pick(id, "Never coming back", "What a waste of money", "Worst festival ever", "I want a refund"), Mood.Angry, 2, "home", 2, $"home:{id}"));
        }
        // Mud underfoot. Only rain mud would cheer a hippie, and there's no rain yet.
        foreach (var id in guests.Keys.Where(Free))
        {
            var at = At(id);
            var ground = _session.GroundStateAt(TraversalGrid.WorldToCell((int)(at.X * 1000), (int)(at.Z * 1000)));
            if (ground is GroundState.Mud or GroundState.Swamp)
                remarks.Add(new(id, ground == GroundState.Swamp ? Pick(id, "It's like a swamp!", "Ugh, my shoes!") : Pick(id, "Ugh, my shoes!", "Squelch…"),
                    Mood.Grumble, 2, "mud", 10));
        }

        // Personalities: the twat, the princess, the hippie, the alcoholic and the one who overheats.
        foreach (var id in guests.Keys.Where(Free))
        {
            var labels = _session.GuestLabels(id);
            if (labels.Count == 0) continue;
            var listening = live?.Stage == LiveSetStage.Live && live.Listeners.Any(l => l.AgentId == id && l.AtPlace);
            if (labels.Contains("Twat"))
                remarks.Add(new(id, queued.ContainsKey(id) ? "Oi, move!" : listening ? "This band's crap" : "Out the way!", Mood.Grumble, 1, "twat", 15));
            if (labels.Contains("Princess"))
                remarks.Add(new(id, Pick(id, "Ew, everything's dirty", "Is there a VIP area?"), Mood.Grumble, 1, "princess", 15));
            if (labels.Contains("Hippie") && listening)
                remarks.Add(new(id, Pick(id, "Such good vibes, man", "Peace and love"), Mood.Happy, 1, "hippie", 15));
            if (labels.Contains("Alcoholic") && consumers.TryGetValue(id, out var c) && c.Held is null)
                remarks.Add(new(id, "Another pint, I think", Mood.Neutral, 1, "alcoholic", 15));
            if (labels.Contains("Easy to overheat") && medical?.IsHot == true && needs.TryGetValue(id, out var hot) && hot.HeatExposure >= 4_000)
                remarks.Add(new(id, "I need shade, now", Mood.Grumble, 2, "overheat", 12));
        }

        // Staff: slackers and sneaky drinkers on a break, stewards calming things, a medic on the way, and robots.
        var staff = prep.People.Where(p => p.Role == ProtectedPersonRole.Staff && p.Admitted && !p.Departed && _attendeeVisuals.ContainsKey(new EntityId(p.AgentId)))
            .Select(p => p.AgentId).ToArray();
        foreach (var id in staff)
        {
            consumers.TryGetValue(id, out var c);
            var atBar = c is not null && (c.VendorId == "drinks" && c.Order == ImmersionProduct.Beer || c.Held?.Product == ImmersionProduct.Beer);
            if (atBar && _session.StaffHas(id, StaffTrait.SneakyAlcoholic))
                remarks.Add(new(id, Pick(id, "Just the one…", "Don't tell the boss"), Mood.Neutral, 2, "staff-sneaky", 15));
            else if (c is not null && (c.VendorId is not null || c.Held is not null) && _session.StaffHas(id, StaffTrait.Slacker))
                remarks.Add(new(id, Pick(id, "Just a quick break…", "Five minutes, tops"), Mood.Neutral, 2, "staff-slacker", 15));
            if (perks.Contains(PerkCatalogue.RobotWorkers))
                remarks.Add(new(id, Pick(id, "BEEP. ALL CLEAR.", "HUMAN, HYDRATE.", "TASK COMPLETE."), Mood.Neutral, 1, "robot", 20));
        }
        foreach (var steward in _session.GetStewardResponses().Where(s => s.Stage == SecurityResponseStage.Calming))
            if (_attendeeVisuals.ContainsKey(new EntityId(steward.WorkerId)))
                remarks.Add(new(steward.WorkerId, Pick(steward.WorkerId, "Easy now, everyone", "Let's keep it friendly"), Mood.Neutral, 3, "steward-calm", 4));
        foreach (var medic in medical?.Medics.Where(m => m.Stage == MedicalResponseStage.Travelling) ?? [])
            if (_attendeeVisuals.ContainsKey(new EntityId(medic.WorkerId)))
                remarks.Add(new(medic.WorkerId, "Medic! Make some room!", Mood.Neutral, 3, "medic", 4));

        // How the set is going: the band's play and the sound, each said so the cause is plain, and the surprises.
        if (live?.Stage == LiveSetStage.Live && _session.PowerBudgetActive && _session.CurrentPerformance is { } play)
        {
            var known = play.Act.Popularity;
            var drunkest = _session.CaptureProgramme()?.Performers.Where(m => m.SlotIndex == _session.CaptureProgramme()!.CurrentSlot)
                .OrderByDescending(m => consumers.TryGetValue(m.AgentId, out var c) ? c.Intoxication : 0).FirstOrDefault();
            var drunkestIntoxication = drunkest is not null && consumers.TryGetValue(drunkest.AgentId, out var dc) ? dc.Intoxication : 0;
            foreach (var listener in live.Listeners.Where(l => l.AtPlace && Free(l.AgentId)))
            {
                var id = listener.AgentId;
                if (drunkest is not null && drunkestIntoxication >= 4_500)
                    remarks.Add(new(id, $"Is the {BandRoleName(drunkest.RoleIndex, play.Act.Genre)} drunk?!", Mood.Grumble, 3, "band-drunk-seen", 10));
                if (play.Band < 35)
                    remarks.Add(new(id, Pick(id, "Are they out of tune?", "Did they even rehearse?", "My nan could play better"), Mood.Grumble, 2, "band-poor", 10));
                else if (play.Overall >= 75)
                    remarks.Add(new(id, Pick(id, "They're so tight!", "What a voice!"), Mood.Happy, 1, "band-great", 8));
                if (play.Sound < 40)
                    remarks.Add(new(id, Pick(id, "Can't hear the vocals!", "Turn it up!"), Mood.Grumble, 2, "sound-poor", 10));
                else if (play.Sound >= 80)
                    remarks.Add(new(id, "This sounds incredible!", Mood.Happy, 1, "sound-great", 12));
                // Fame and talent at odds, or a good band let down by the rig.
                if (known >= 60 && play.Talent <= known - 20 && play.Band < 55)
                    remarks.Add(new(id, Pick(id, "Not as good as I thought they'd be", "Overrated.", "Better on the radio"), Mood.Grumble, 2, "overrated", 12));
                if (known <= 35 && play.Talent >= known + 20 && play.Band >= 60)
                    remarks.Add(new(id, Pick(id, "Wow, I didn't know they could do this!", "Who ARE these? They're brilliant", "New favourite band"), Mood.Happy, 2, "discovery", 12));
                if (play.Talent >= 60 && play.Sound < 40)
                    remarks.Add(new(id, Pick(id, "They deserve a better sound system than this", "They normally sound a lot better than this"), Mood.Grumble, 2, "deserve-better", 12));
            }
            foreach (var performer in live.Performers.Where(p => p.OnStage && _attendeeVisuals.ContainsKey(new EntityId(p.AgentId))))
            {
                var id = performer.AgentId;
                if (consumers.TryGetValue(id, out var c) && c.Intoxication >= 4_500)
                    remarks.Add(new(id, Pick(id, "Thish one's… which one is thish?", "Cheers everyone! *hic*"), Mood.Neutral, 2, "band-drunk", 15));
                if (play.Sound < 40)
                    remarks.Add(new(id, play.Act.Ego >= 70 ? Pick(id, "Sounds shit!", "We're not playing through this crap again") : Pick(id, "Sounds shit!", "I can't hear myself at all!", "Can we get more monitor?"),
                        play.Act.Ego >= 70 ? Mood.Angry : Mood.Grumble, 3, "band-sound", 15));
                else if (play.Sound >= 80)
                    remarks.Add(new(id, "Sounds massive tonight!", Mood.Happy, 1, "band-sound-great", 20));
            }
        }

        // The band, now and then from the stage, and when the crowd turns on them.
        if (live?.Stage == LiveSetStage.Live)
            foreach (var performer in live.Performers.Where(p => p.OnStage && _attendeeVisuals.ContainsKey(new EntityId(p.AgentId))))
                remarks.Add(live.LastReaction == "sustained-boo"
                    ? new(performer.AgentId, "Tough crowd…", Mood.Neutral, 3, "band-boo", 15)
                    : new(performer.AgentId, Pick(performer.AgentId, "Make some noise!", $"Thank you, {FestivalName()}!", "This one's a new one!"),
                        Mood.Happy, 1, "band", 20));
        return remarks;
    }

    /// <summary>A band member by what they play: the lead sings (or DJs), the second plays bass, accordion or keys, the third drums.</summary>
    private static string BandRoleName(int role, int genre) => role switch
    {
        0 => genre == FestivalGenre.Electronic ? "DJ" : "singer",
        1 => genre switch { FestivalGenre.Folk => "accordion player", FestivalGenre.Electronic => "keyboard player", _ => "bassist" },
        _ => "drummer",
    };

    // What the band calls the crowd: the festival's name as the top bar and box office give it.
    private static string FestivalName() => "Lower Wittering";

    private string GenreCheer(int genre) => genre switch
    {
        FestivalGenre.Folk => "Lovely stuff",
        FestivalGenre.Indie => "Saw them before they were famous",
        FestivalGenre.Pop => "This is my song!",
        FestivalGenre.Electronic => "Get ready for the drop!",
        FestivalGenre.Punk => "OI! OI! OI!",
        FestivalGenre.Metal => "HEAVY!",
        _ => "TUNE!",
    };
}
