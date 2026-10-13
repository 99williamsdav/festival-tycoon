using System.Text;
using Festival.Simulation;

namespace Festival.Tests;

/// <summary>
/// Writes how real walk-outs go, to tune them: when each guest sets off after the close, who stops at a loo or a tap on
/// the way, and when the last guest is off site. Six Tier 1 days and a Tier 2 day. Set FESTIVAL_WALKOUT_PROBE_DIR to run it.
/// </summary>
[TestClass]
public sealed class WalkOutProbe
{
    internal sealed record WalkOut(long Close, long LastOut, long Finished, PreparationStatus Status, int Guests, long[] SetOffs,
        int LooStops, int WaterStops, long HalfwayTick);

    /// <summary>
    /// Plays a started day through its walk-out, tick by tick from the close, noting what each guest does. With
    /// <paramref name="callDav"/> the loos are emptied an hour before the close, as a player would before the headliner
    /// (a default Tier 1 day otherwise fills both).
    /// </summary>
    internal static WalkOut Watch(GameSession s, bool callDav = false)
    {
        var p = s.CapturePreparation()!;
        var close = p.StartedTick + s.PreparedEditionDurationTicks;
        if (callDav)
        {
            while (s.CurrentTick < close - 4_800 && s.PreparedStatus == PreparationStatus.Running) s.AdvanceWithoutSnapshot((int)Math.Min(1_000, close - 4_800 - s.CurrentTick));
            foreach (var toilet in s.CaptureToilets().Where(t => s.LavSuckerUnavailable(t.Id) is null)) BuildSession.Accept(s, new CallLavSuckerCommand(toilet.Id));
        }
        while (s.CurrentTick < close && s.PreparedStatus == PreparationStatus.Running) s.AdvanceWithoutSnapshot((int)Math.Min(1_000, close - s.CurrentTick));
        var guests = s.CapturePreparation()!.People.Where(person => person.Role == ProtectedPersonRole.Guest).Select(person => person.AgentId).ToArray();
        var setOff = guests.ToDictionary(id => id, _ => -1L);
        var loo = new HashSet<ulong>();
        var water = new HashSet<ulong>();
        long lastOut = -1, halfway = -1;
        while (s.PreparedStatus == PreparationStatus.Departing)
        {
            s.AdvanceWithoutSnapshot(1);
            var people = s.CapturePreparation()!.People.ToDictionary(person => person.AgentId);
            var navs = s.CaptureObservation().NavigationAgents.ToDictionary(nav => nav.Id.Value);
            var onSite = 0;
            foreach (var id in guests)
            {
                if (people[id].Departed) continue;
                onSite++;
                if (setOff[id] < 0 && navs[id].IntentId != "edition.linger") setOff[id] = s.CurrentTick - close;
                if (s.CapturePerson(id) is { } person)
                {
                    if (person.ToiletStage is ToiletVisitStage.Approaching or ToiletVisitStage.Queued or ToiletVisitStage.Using) loo.Add(id);
                    if (person.Intent is MedicalIntent.SeekWater or MedicalIntent.Drinking) water.Add(id);
                }
            }
            if (halfway < 0 && onSite <= guests.Length / 2) halfway = s.CurrentTick - close;
            if (onSite > 0) lastOut = s.CurrentTick - close + 1;
        }
        return new(close, lastOut, s.CurrentTick - close, s.PreparedStatus!.Value, guests.Length, setOff.Values.Order().ToArray(), loo.Count, water.Count, halfway);
    }

    [TestMethod, TestCategory("Slow")]
    public void WritesWalkOuts()
    {
        var folder = Environment.GetEnvironmentVariable("FESTIVAL_WALKOUT_PROBE_DIR");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        var text = new StringBuilder();
        void Write(string label, WalkOut w) => text.Append($"{label}: {w.Status} guests {w.Guests} last out +{w.LastOut} finished +{w.Finished} half out +{w.HalfwayTick} " +
            $"set off min {w.SetOffs.Min()} median {w.SetOffs[w.SetOffs.Length / 2]} max {w.SetOffs.Max()} loo stops {w.LooStops} water stops {w.WaterStops}\n");
        foreach (var seed in new ulong[] { 2, 20260922, 20260923, 20260924, 20260925, 20260926, 51, 99 })
        {
            Write($"Tier 1 seed {seed}", Watch(BuildSession.Started(seed)));
            Write($"Tier 1 seed {seed} loos emptied", Watch(BuildSession.Started(seed), callDav: true));
        }
        foreach (var seed in new ulong[] { 20260922, 20260923 })
        {
            var s = BuildSession.Started(seed);
            BandRelationshipProbe.RunToEnd(s);
            if (!s.CanStartNextFestival) { text.Append($"Tier 2 after {seed}: no next festival ({s.PreparedStatus})\n"); continue; }
            var next = NextFestivalTests.Ready(s.CreateNextFestival());
            BuildSession.Accept(next, new AcceptPreparationOfferCommand(BuildSession.ExtraId(next, StaffRole.Medic)));
            BuildSession.Accept(next, new StartPreparedEditionCommand());
            Write($"Tier 2 after {seed}", Watch(next));
        }
        File.WriteAllText(Path.Combine(folder, "walk-outs.txt"), text.ToString());
    }
}
