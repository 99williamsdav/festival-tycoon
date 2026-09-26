using Festival.Simulation;
using Festival.Persistence;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private string? _immersionCaptureDirectory;
    private int _immersionCaptureFrame;
    private readonly HashSet<ImmersionProduct> _immersionCapturedProducts = [];
    private readonly HashSet<ulong> _immersionObservedOnStage = [];
    private ImmersionProduct? _immersionPendingProductImage;
    private bool _immersionPartialReloadChecked;
    private bool _immersionCaptureCompleted;
    private bool _immersionFinalImagePending;
    private bool _immersionSevereFixture;
    private bool _immersionDepartureFixture;
    private bool _immersionLayoutFixture;
    private bool _immersionDepartureReloadChecked;
    private int _immersionClosingSales = -1;

    private void ImmersionImage(string name) => GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_immersionCaptureDirectory!, name + ".png"));
    private void ImmersionCaptureState()
    {
        var state = _session.CaptureImmersion()!;
        if (_session.PreparedStatus == PreparationStatus.Departing && !_immersionDepartureReloadChecked)
        {
            var hash = _session.CaptureSnapshot().AuthoritativeHash;
            _immersionClosingSales = state.Purchases.Length;
            PreparationSave(); PreparationLoad();
            if (_session.CaptureSnapshot().AuthoritativeHash != hash) throw new InvalidOperationException("Departure save/load changed authoritative state.");
            _immersionDepartureReloadChecked = true;
            GD.Print("IMMERSION_DEPARTURE_RELOAD exact_hash=True counters_closed=True");
            state = _session.CaptureImmersion()!;
        }
        if (_immersionClosingSales >= 0 && state.Purchases.Length != _immersionClosingSales) throw new InvalidOperationException("Purchase occurred after closing.");
        foreach (var performer in _session.CaptureLivePerformance()?.Performers.Where(p => p.OnStage) ?? []) _immersionObservedOnStage.Add(performer.AgentId);
        GD.Print($"IMMERSION_STATE tick={_session.CurrentTick} status={_session.PreparedStatus} sales={state.Purchases.Length} chips={state.ChipsStock} soft={state.SoftStock} beer={state.BeerStock} held={state.People.Count(p => p.Held is not null)} max_intox={state.People.Max(p => p.Intoxication)} queues={string.Join(',', state.Vendors.Select(v => v.Queue.Length))} hash={_session.CaptureSnapshot().AuthoritativeHash}");
    }
    private void ProcessImmersionCapture()
    {
        if (_immersionFinalImagePending)
        {
            ImmersionImage("festival-finished-physical-departure");
            GD.Print($"IMMERSION_CAPTURE_COMPLETE departed={_session.CapturePreparation()!.People.Count(p => p.Departed)} products_captured={string.Join(',', _immersionCapturedProducts)} onstage_performers={_immersionObservedOnStage.Count} partial_reload=True departure_reload={_immersionDepartureReloadChecked} no_sales_after_close=True");
            _immersionFinalImagePending = false; GetTree().Quit(); return;
        }
        if (_immersionCaptureDirectory is null || _immersionCaptureCompleted) return;
        try { ProcessImmersionCaptureStep(); }
        catch (Exception error) { GD.PushError("IMMERSION_CAPTURE_FAILED " + error); GetTree().Quit(1); _immersionCaptureCompleted = true; }
    }
    private void ProcessImmersionCaptureStep()
    {
        if (_immersionSevereFixture) { ProcessImmersionSevereCapture(); return; }
        if (_immersionLayoutFixture) { ProcessImmersionLayoutCapture(); return; }
        _immersionCaptureFrame++;
        if (_immersionCaptureFrame == 4)
        {
            _programmeDraft = ["act.meadow-lanterns", "act.neon-postcards", "act.field-frequency"];
            RefreshProgrammeControls(); _programmeBook!.EmitSignal(Button.SignalName.Pressed);
            foreach (var offer in new[] { "staff.steward", "equipment.buy" }) _offerButtons[offer].EmitSignal(Button.SignalName.Pressed);
            _immersionStockButton!.EmitSignal(Button.SignalName.Pressed);
            if (!_session.CaptureImmersion()!.StockPurchased || _offerButtons.ContainsKey("contract.stock")) throw new InvalidOperationException("Immersion stock/legacy offer UI mismatch.");
            var prepared = _session; var hash = prepared.CaptureSnapshot().AuthoritativeHash;
            var old = GameSession.CreateTimetableCampaign(20260922);
            var saved = SaveFileAdapter.SaveSlot(SaveDirectory, "manual-preparation", new SaveWriteRequest(old, _saveCompatibility, "legacy-timetable-fixture", DateTimeOffset.UtcNow));
            if (!saved.IsSuccess) throw new InvalidOperationException(saved.Error);
            PreparationLoad();
            if (_session.CaptureImmersion() is not null || _immersionControls!.Visible || !_offerButtons.ContainsKey("contract.stock") || _immersionVendors.Count != 0) throw new InvalidOperationException("Legacy mode retained immersion presentation.");
            saved = SaveFileAdapter.SaveSlot(SaveDirectory, "manual-preparation", new SaveWriteRequest(prepared, _saveCompatibility, "immersion-fixture", DateTimeOffset.UtcNow));
            if (!saved.IsSuccess) throw new InvalidOperationException(saved.Error);
            PreparationLoad();
            if (_session.CaptureSnapshot().AuthoritativeHash != hash || !_immersionControls.Visible || _offerButtons.ContainsKey("contract.stock") || _immersionVendors.Count != 2) throw new InvalidOperationException("Immersion mode load changed identity/UI.");
            GD.Print("IMMERSION_CROSS_MODE_LOAD exact_hash=True legacy_controls=True vendors_resynced=True");
            _focus = new Vector3(-6, 0, 13); _camera.Size = 44; ApplyCamera();
            SelectImmersionVendor("drinks"); ImmersionCaptureState(); return;
        }
        if (_immersionCaptureFrame == 5)
        {
            var vendor = _session.CaptureImmersion()!.Vendors.Single(v => v.Id == "drinks");
            BeginImmersionPlacement("drinks"); _immersionQuarterTurns = 1;
            UpdateImmersionPlacementPreview(_camera.UnprojectPosition(ImmersionPosition(vendor.Cell)));
            if (_immersionCandidate != vendor.Cell || _immersionPlacementIssue is not null) throw new InvalidOperationException("Normal rotated vendor preview rejected: " + _immersionPlacementIssue);
            return;
        }
        if (_immersionCaptureFrame == 6)
        {
            ImmersionImage("rotated-vendor-footprint-queue-preview");
            var vendor = _session.CaptureImmersion()!.Vendors.Single(v => v.Id == "drinks");
            CommitImmersionPlacement(_camera.UnprojectPosition(ImmersionPosition(vendor.Cell)));
            if (_session.CaptureImmersion()!.Vendors.Single(v => v.Id == "drinks").QuarterTurns != 1 || Math.Abs(_immersionVendors["drinks"].RotationDegrees.Y - 90) > .01) throw new InvalidOperationException("Rotated vendor did not commit/resync.");
            var hash = _session.CaptureSnapshot().AuthoritativeHash;
            PreparationSave(); PreparationLoad();
            if (_session.CaptureSnapshot().AuthoritativeHash != hash || Math.Abs(_immersionVendors["drinks"].RotationDegrees.Y - 90) > .01) throw new InvalidOperationException("Rotated placement did not reload exactly.");
            SelectImmersionVendor("drinks"); GD.Print("IMMERSION_ROTATED_PLACEMENT preview=True command=True exact_saved_hash=True visual_yaw=90"); return;
        }
        if (_immersionCaptureFrame == 7)
        {
            ImmersionImage("prepared-approved-vendors-stock"); PreparationStart();
            if (_session.PreparedStatus != PreparationStatus.Running) throw new InvalidOperationException(_preparationMessage);
            TimetableAdvanceTo(1600);
            var medical = _session.CaptureMedical()!;
            StaffCaptureSend(new StaffInterventionCommand(medical.AtRiskGuestId, medical.MedicId, StaffInterventionAction.GuideToRest));
            TimetableAdvanceTo(2000); ImmersionCaptureState(); return;
        }
        if (_immersionCaptureFrame < 8 || _immersionCaptureFrame % 2 != 0) return;
        if (_immersionPendingProductImage is { } product)
        {
            ImmersionImage("actual-held-" + ImmersionProductKey(product)); _immersionPendingProductImage = null;
            _focus = new Vector3(-6, 0, 13); _camera.Size = 44; ApplyCamera();
        }
        var state = _session.CaptureImmersion()!;
        if (!_immersionPartialReloadChecked && state.People.FirstOrDefault(p => p.Held is { ConsumedTicks: > 0 }) is { } partial)
        {
            var hash = _session.CaptureSnapshot().AuthoritativeHash;
            PreparationSave(); PreparationLoad();
            if (_session.CaptureSnapshot().AuthoritativeHash != hash || _session.CaptureImmersion()!.People.Single(p => p.AgentId == partial.AgentId).Held != partial.Held) throw new InvalidOperationException("Partial item save/load changed authoritative state.");
            _immersionPartialReloadChecked = true;
            GD.Print($"IMMERSION_PARTIAL_RELOAD exact_hash=True owner={partial.AgentId} consumed_ticks={partial.Held!.ConsumedTicks} product={partial.Held.Product}");
            state = _session.CaptureImmersion()!;
        }
        // Presentation-only evidence gate: wait for an actual buyer to walk clear
        // of vendor roofs, rather than taking an occluded "held prop" picture.
        var firstNew = state.People.FirstOrDefault(p => p.Held is { } item && !_immersionCapturedProducts.Contains(item.Product) && _session.ImmersionHandsAvailable(p.AgentId) &&
            _attendeeVisuals.TryGetValue(new EntityId(p.AgentId), out var holder) &&
            _immersionVendors.Values.All(vendor => new Vector2(holder.Position.X - vendor.Position.X, holder.Position.Z - vendor.Position.Z).LengthSquared() > 25));
        if (firstNew?.Held is { } newItem && _attendeeVisuals.TryGetValue(new EntityId(firstNew.AgentId), out var visual))
        {
            SelectAttendee(new EntityId(firstNew.AgentId));
            _focus = visual.Position; _camera.Size = 9; ApplyCamera();
            AdvanceImmersionPresentation(0);
            if (!_immersionHeldVisuals.ContainsKey(new EntityId(firstNew.AgentId))) throw new InvalidOperationException("Eligible purchased item missing its approved prop.");
            _immersionCapturedProducts.Add(newItem.Product); _immersionPendingProductImage = newItem.Product;
            GD.Print($"IMMERSION_ACTUAL_HELD_PROP owner={firstNew.AgentId} product={newItem.Product} scale=1 hands_available=True purchase_id={newItem.TransactionId}");
            return; // Give rendering a fresh frame before image, without changing simulation.
        }
        if (_session.CurrentTick < 24001) TimetableAdvanceTo((int)Math.Min(24001, _session.CurrentTick + 400));
        else if (_session.PreparedStatus == PreparationStatus.Departing)
        {
            StaffCaptureSend(new SetPausedCommand(false)); _session.AdvanceWithoutSnapshot(80);
            if (_session.PreparedStatus == PreparationStatus.Departing) StaffCaptureSend(new SetPausedCommand(true));
            _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary(); RefreshPreparationHud();
        }
        ImmersionCaptureState();
        if (_session.PreparedStatus == PreparationStatus.Failed) throw new InvalidOperationException("Ordinary immersion day failed.");
        if (_session.CurrentTick > 33600 && _session.PreparedStatus != PreparationStatus.Finished) throw new InvalidOperationException("Ordinary physical departure exceeded bound.");
        if (_session.PreparedStatus != PreparationStatus.Finished) return;
        if (_immersionObservedOnStage.Count != 9 || _session.CapturePreparation()!.People.Count(p => p.Role == ProtectedPersonRole.Performer) != 9) throw new InvalidOperationException("Missing nine physical onstage performer proof.");
        if (!_immersionCapturedProducts.SetEquals(Enum.GetValues<ImmersionProduct>())) throw new InvalidOperationException("Missing actual held-product graphical proof: " + string.Join(',', Enum.GetValues<ImmersionProduct>().Except(_immersionCapturedProducts)));
        if (_session.CapturePreparation()!.People.Any(p => !p.Departed) || _session.CaptureLifecycleSnapshot()!.Casualties.Count != 0 || !_immersionPartialReloadChecked || !_immersionDepartureReloadChecked || state.Purchases.Length == 0) throw new InvalidOperationException("Final immersion capture missed physical sale/partial-save/departure proof.");
        _preparationSaveBlocked = true; ClearSelection(); RefreshPreparationHud();
        _focus = new Vector3(-6, 0, 13); _camera.Size = 44; ApplyCamera();
        _immersionCaptureCompleted = true;
        // Image after the final state has had a normal rendered frame.
        _immersionFinalImagePending = true;
    }
}
