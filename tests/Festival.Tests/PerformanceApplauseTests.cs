using System.Reflection;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Festival.Persistence;
using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class PerformanceApplauseTests
{
    private static CommandResult Send(GameSession session, SessionCommand command) => session.Execute(new(
        new CommandId(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase,
        session.CurrentTick, session.NextSubmissionSequence, null, command));

    private static void Medical(GameSession session, Func<MedicalNeed, MedicalNeed> change)
    {
        var state = session.CaptureMedical()!;
        typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session, state with { Needs = state.Needs.Select(change).ToArray() });
    }

    private static GameSession Started()
    {
        var session = GameSession.CreateTimetableCampaign(20260926);
        Assert.IsTrue(Send(session, new SetProgrammeCommand(["act.meadow-lanterns", "act.neon-postcards", "act.field-frequency"])).IsAccepted);
        foreach (var id in new[] { "staff.steward", "equipment.buy" })
            Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand(id)).IsAccepted);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        Medical(session, need => need with { Thirst = 0, HeatExposure = 0 });
        return session;
    }

    private static GameSession Restored(GameSession session)
    {
        var loaded = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(loaded.IsSuccess, loaded.Error);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, loaded.Session!.CaptureSnapshot().AuthoritativeHash);
        return loaded.Session;
    }

    [TestMethod]
    public void PerformedPoweredSetEndsOnceWithFrozenActualAudienceAndEnjoymentAndExactReload()
    {
        var session = Started();
        session.AdvanceWithoutSnapshot(GameSession.FestivalSlotEnds[0] - 1);
        var before = session.CaptureLivePerformance()!;
        Assert.AreEqual(LiveSetStage.Live, before.Stage);
        Assert.AreEqual(0, before.SetEndAudienceCount);
        var replay = Restored(session);
        session.AdvanceWithoutSnapshot(1);
        replay.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, replay.CaptureSnapshot().AuthoritativeHash);
        var ended = session.CaptureLivePerformance()!;
        Assert.AreEqual(LiveSetStage.Finished, ended.Stage);
        Assert.AreEqual("set-finished-applause", ended.LastReaction);
        Assert.AreEqual(before.ReactionSequence + 1, ended.ReactionSequence);
        Assert.IsTrue(ended.SetEndAudienceCount > 0);
        CollectionAssert.AreEqual(ended.Listeners.Where(listener => listener.AtPlace && listener.ListenedTicks > 0)
            .Select(listener => listener.AgentId).ToArray(), ended.SetEndAudienceIds);
        Assert.AreEqual(ended.Listeners.Where(listener => ended.SetEndAudienceIds.Contains(listener.AgentId)).Sum(listener => listener.EnjoymentEarned), ended.SetEndEnjoymentTotal);
        Console.WriteLine($"actual end {ended.EndedTick}; audience {ended.SetEndAudienceCount}; enjoyment {ended.SetEndEnjoymentTotal}; strength {PerformanceApplauseMath.Strength(ended.SetEndAudienceCount, ended.SetEndEnjoymentTotal):F4}; enthusiastic {PerformanceApplauseMath.IsEnthusiastic(ended.SetEndAudienceCount, ended.SetEndEnjoymentTotal)}");
        var originalIds = ended.SetEndAudienceIds.ToArray();
        ended.SetEndAudienceIds[0] = ulong.MaxValue;
        CollectionAssert.AreEqual(originalIds, session.CaptureLivePerformance()!.SetEndAudienceIds, "Published capture must not mutate authoritative end membership.");
        var restored = Restored(session);
        var baselineSequence = restored.CaptureLivePerformance()!.ReactionSequence; // Presentation reset baselines this saved sequence.
        restored.AdvanceWithoutSnapshot(80);
        Assert.AreEqual(baselineSequence, restored.CaptureLivePerformance()!.ReactionSequence, "Loading a finished reaction cannot create a fresh applause event.");
        CollectionAssert.AreEqual(originalIds, restored.CaptureLivePerformance()!.SetEndAudienceIds);
        Assert.AreEqual(session.CaptureLivePerformance()!.SetEndEnjoymentTotal, restored.CaptureLivePerformance()!.SetEndEnjoymentTotal);
    }

    [TestMethod]
    public void ActualPowerCutAtDeadlineCannotBecomeSetEndApplause()
    {
        var session = Started();
        session.AdvanceWithoutSnapshot(GameSession.FestivalSlotEnds[0] - 8);
        Assert.AreEqual(LiveSetStage.Live, session.CaptureLivePerformance()!.Stage);
        Assert.IsTrue(Send(session, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted);
        session.AdvanceWithoutSnapshot(8);
        var ended = session.CaptureLivePerformance()!;
        Assert.AreEqual(LiveSetStage.Finished, ended.Stage);
        Assert.AreEqual("set-finished-interrupted", ended.LastReaction);
        Assert.AreEqual(0, ended.SetEndAudienceCount);
        Assert.AreEqual(0, ended.SetEndEnjoymentTotal);
        Restored(session);

        session = Started();
        session.AdvanceWithoutSnapshot(GameSession.FestivalSlotEnds[0] - 8);
        Assert.IsTrue(Send(session, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted);
        session.AdvanceWithoutSnapshot(7);
        Assert.AreEqual(LiveSetStage.Interrupted, session.CaptureLivePerformance()!.Stage);
        Assert.IsTrue(Send(session, new DisorderCommand(DisorderAction.RestoreMusic)).IsAccepted);
        session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual("set-finished-interrupted", session.CaptureLivePerformance()!.LastReaction,
            "Power returning exactly at the deadline cannot turn an interrupted prior tick into an applause-worthy played ending.");
        Assert.AreEqual(0, session.CaptureLivePerformance()!.SetEndAudienceCount);
        Restored(session);
    }

    [TestMethod]
    public void MissedUnplayedSlotHasNoApplauseAndCannotCountOwnedMedicalListeners()
    {
        var session = Started();
        var performer = session.CaptureProgramme()!.Performers[0].AgentId;
        Medical(session, need => need.AgentId == performer ? need with { Intent = MedicalIntent.AwaitMedic } : need);
        // Audio-boundary fixture isolates an unplayed slot from the separate tested disorder escalation chain.
        var disorder = session.CaptureDisorder()!;
        typeof(GameSession).GetField("_disorder", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
            disorder with { People = disorder.People.Select(person => person with { CooldownUntilTick = long.MaxValue }).ToArray() });
        session.AdvanceWithoutSnapshot(GameSession.FestivalSlotEnds[0]);
        var missed = session.CaptureLivePerformance()!;
        Assert.AreEqual("slot-missed-not-ready", missed.LastReaction);
        Assert.AreEqual(-1L, missed.StartedTick);
        Assert.AreEqual(0, missed.SetEndAudienceCount);
        Assert.IsTrue(missed.Listeners.All(listener => listener.ListenedTicks == 0 && listener.EnjoymentEarned == 0));
        Restored(session);

        session = Started();
        session.AdvanceWithoutSnapshot(GameSession.FestivalSlotEnds[0] - 1);
        Medical(session, need => need.Profile == MedicalNeedProfile.Guest ? need with { Intent = MedicalIntent.AwaitMedic } : need);
        session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual("set-finished-muted", session.CaptureLivePerformance()!.LastReaction);
        Assert.AreEqual(0, session.CaptureLivePerformance()!.SetEndAudienceCount);
        Restored(session);
    }

    [TestMethod]
    public void EndAudienceTamperAndImpossibleEnjoymentAreRejectedAndAbsentPayloadPreservesLegacyCanonical()
    {
        var session = Started();
        session.AdvanceWithoutSnapshot(GameSession.FestivalSlotEnds[0]);
        var snapshot = session.CapturePersistenceSnapshot();
        var ended = snapshot.LivePerformance!;
        Assert.IsTrue(ended.SetEndAudienceIds.Length > 0);
        foreach (var invalid in new[]
        {
            ended with { SetEndAudienceIds = [ended.SetEndAudienceIds[0], ended.SetEndAudienceIds[0]] },
            ended with { SetEndAudienceIds = [ulong.MaxValue] },
            ended with { SetEndAudienceIds = null! },
            ended with { Stage = LiveSetStage.Live },
            ended with { InterruptedTick = ended.EndedTick },
            ended with { LastReaction = "set-finished-muted" },
            ended with { Listeners = ended.Listeners.Select((listener, index) => index == 0 ? listener with { EnjoymentEarned = int.MaxValue } : listener).ToArray() }
        }) Assert.IsFalse(GameSession.Restore(snapshot with { LivePerformance = invalid }).IsSuccess);
        var legacy = GameSession.CreateEquipmentCampaign(2);
        foreach (var id in new[] { "act.folk", "staff.steward", "equipment.buy" })
            Assert.IsTrue(Send(legacy, new AcceptPreparationOfferCommand(id)).IsAccepted);
        Assert.IsTrue(Send(legacy, new StartPreparedEditionCommand()).IsAccepted);
        var priorShape = JsonSerializer.SerializeToNode(legacy.CaptureLivePerformance())!.AsObject();
        priorShape.Remove(nameof(LivePerformanceSnapshot.SetEndAudienceIds));
        var canonical = typeof(GameSession).GetProperty("LivePerformanceCanonicalJson", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(legacy);
        Assert.AreEqual(priorShape.ToJsonString(), canonical);
        Restored(legacy);
    }

    [TestMethod]
    public void ApplauseStrengthAndClipChoiceScaleWithActualAudienceAndEarnedEnjoyment()
    {
        Assert.AreEqual(0d, PerformanceApplauseMath.Strength(0, 10_000));
        Assert.AreEqual(0.02d, PerformanceApplauseMath.Strength(1, 0));
        Assert.AreEqual(0.0375d, PerformanceApplauseMath.Strength(10, 0), 0.000001);
        Assert.AreEqual(0.25d, PerformanceApplauseMath.Strength(10, 10_000), 0.000001);
        Assert.AreEqual(0.65d, PerformanceApplauseMath.Strength(40, 40_000));
        Assert.IsFalse(PerformanceApplauseMath.IsEnthusiastic(9, 9_000));
        Assert.IsFalse(PerformanceApplauseMath.IsEnthusiastic(10, 3_999));
        Assert.IsTrue(PerformanceApplauseMath.IsEnthusiastic(10, 4_000));
    }

    [TestMethod]
    public void LegacySaveFileChecksumStillLoadsWhenEndAudienceFieldIsAbsent()
    {
        var legacy = GameSession.CreateEquipmentCampaign(2);
        foreach (var id in new[] { "act.folk", "staff.steward", "equipment.buy" })
            Assert.IsTrue(Send(legacy, new AcceptPreparationOfferCommand(id)).IsAccepted);
        Assert.IsTrue(Send(legacy, new StartPreparedEditionCommand()).IsAccepted);
        var snapshot = legacy.CapturePersistenceSnapshot();
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
            { Modifiers = { StaffSaveDefaults.Configure } } };
        var oldPayload = JsonSerializer.SerializeToNode(snapshot, options)!.AsObject();
        oldPayload["livePerformance"]!.AsObject().Remove("setEndAudienceIds");
        var priorChecksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(oldPayload.ToJsonString(options)))).ToLowerInvariant();
        Assert.AreEqual(priorChecksum, SaveFileAdapter.ComputePayloadChecksum(snapshot), "Default end payload must not change an older file's checksum.");
        var compatibility = new SaveCompatibility("applause-legacy-test", "content", "rules");
        var header = new SaveHeaderV1(SaveFileAdapter.FormatId, SaveMigrationPipeline.CurrentSchemaVersion,
            compatibility.BuildId, compatibility.ContentHash, compatibility.RulesetHash, snapshot.CampaignId,
            DateTimeOffset.UtcNow.ToString("O"), snapshot.Phase, "legacy-no-end-payload", priorChecksum);
        var envelope = new System.Text.Json.Nodes.JsonObject { ["header"] = JsonSerializer.SerializeToNode(header, options), ["payload"] = oldPayload };
        var directory = Path.Combine(Path.GetTempPath(), "festival-applause-legacy-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "legacy.ftsave");
            using (var file = File.Create(path))
            using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
                gzip.Write(Encoding.UTF8.GetBytes(envelope.ToJsonString(options)));
            var loaded = SaveFileAdapter.LoadFile(path, compatibility);
            Assert.IsTrue(loaded.IsSuccess, loaded.Error);
            Assert.AreEqual(legacy.CaptureSnapshot().AuthoritativeHash, loaded.Session!.CaptureSnapshot().AuthoritativeHash);
            Assert.AreEqual(0, loaded.Session.CaptureLivePerformance()!.SetEndAudienceCount);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
