using Festival.Simulation;
using Godot;
using System;
using System.IO;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private string? _bookingCaptureDirectory;
    private int _bookingCaptureFrame;
    private string _bookingCaptureShot = "";
    private string _bookingCaptureHash = "";
    private Variant _bookingCapturePayload;
    private Variant _bookingStalePayload;
    private void BookingCheck(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private void ProcessBookingCapture()
    {
        if (_bookingCaptureDirectory is null) return;
        try
        {
            var frame = ++_bookingCaptureFrame;
            if (frame == 1)
            {
                var initial = _session.CapturePerks()!; CommitEquipmentAction(new ChoosePerkCommand(initial.DraftAttempt, initial.Cursor, initial.Hand[0]));
                _perksExpanded = false; RefreshPerkHud(); CommitEquipmentAction(new SetPreparationStockCommand(10000, 10000, 10000));
                BookingCheck(_session.GetPreparationStartBlockers().Select(b => b.Owner).Distinct().Count() == 3, "Three readiness blockers not established");
                SelectHudTab("Programme");
            }
            if (frame == 5) SelectHudTab("Overview");
            if (frame == 10) SelectHudTab("Staff");
            if (frame is 4 or 9 or 14)
            {
                var tab = frame == 4 ? "Programme" : frame == 9 ? "Overview" : "Staff";
                var bounds = GetViewport().GetVisibleRect();
                GD.Print($"BOOKING_READINESS_LAYOUT tab={tab} workspace={_hudWorkspace!.GetGlobalRect()} tabs={_hudTabs!.GetGlobalRect()} start={_preparationStart!.GetGlobalRect()} reason={_hudStartReason!.GetGlobalRect()} lines={_hudStartReason.GetVisibleLineCount()}/{_hudStartReason.GetLineCount()} max={_hudStartReason.MaxLinesVisible}");
                BookingCheck(_hudTabs!.GetTabTitle(_hudTabs.CurrentTab).StartsWith(tab), "Readiness tab switch failed");
                BookingCheck(_hudStartReason!.Text.Contains("choose 3 different acts") && _hudStartReason.Text.Contains("hire 1 worker") && _hudStartReason.Text.Contains("reduce planned cost"), "Readiness footer omits a blocker");
                BookingCheck(_hudStartReason.GetVisibleLineCount() == _hudStartReason.GetLineCount() && bounds.Encloses(_hudStartReason.GetGlobalRect()), "Readiness footer clipped on " + tab);
                BookingCheck(bounds.Encloses(_preparationStart!.GetGlobalRect()) && _preparationStart.GetGlobalRect().End.Y < bounds.End.Y - 54, "Readiness Start footer outside viewport on " + tab);
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_bookingCaptureDirectory, "00-readiness-" + tab.ToLowerInvariant() + ".png"));
            }
            if (frame == 15)
            {
                CommitEquipmentAction(new SetPreparationStockCommand(0, 0, 0)); SelectHudTab("Programme");
            }
            if (frame == 16) _bookingCaptureHash = _session.CaptureSnapshot().AuthoritativeHash;
            if (frame == 17)
            {
                _bookingHeaders[4].GrabFocus();
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, Pressed = true });
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, Pressed = false });
            }
            if (frame == 18)
            {
                BookingCheck(_bookingSort == BookingSortField.Ego && !_bookingDescending && ReferenceEquals(_bookingTableBody!.GetChild(0), _bookingCards["act.meadow-lanterns"]), "Native Enter first header sort failed");
                BookingCheck(_bookingMeaning!.Text.Contains("Ego") && _bookingMeaning.Text.Contains("Demandingness"), "Keyboard-focused rating header lost its meaning");
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, Pressed = true });
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, Pressed = false });
            }
            if (frame == 19)
            {
                BookingCheck(_bookingSort == BookingSortField.Ego && _bookingDescending && ReferenceEquals(_bookingTableBody!.GetChild(0), _bookingCards["act.neon-postcards"]), "Native Enter reverse header sort failed");
                BookingCheck(_bookingCaptureHash == _session.CaptureSnapshot().AuthoritativeHash, "Cosmetic sort changed authoritative hash");
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_bookingCaptureDirectory, "00-ego-descending.png"));
                _bookingGenreFilter!.Select(2); _bookingGenreFilter.EmitSignal(OptionButton.SignalName.ItemSelected, 2);
                BookingCheck(_bookingCards.Values.Count(card => card.Visible) == 2 && _bookingCards["act.copper-static"].Visible && _bookingCards["act.barnstorm-circuit"].Visible, "Rock filter membership failed");
                BookingCheck(_bookingCaptureHash == _session.CaptureSnapshot().AuthoritativeHash, "Cosmetic filter changed authoritative hash");
            }
            if (frame == 20)
            {
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_bookingCaptureDirectory, "00-rock-filter.png"));
                _bookingGenreFilter!.Select(0); _bookingGenreFilter.EmitSignal(OptionButton.SignalName.ItemSelected, 0);
                _bookingHeaders[2].EmitSignal(Button.SignalName.Pressed);
                BookingCheck(_bookingCards.Values.All(card => card.Visible) && ReferenceEquals(_bookingTableBody!.GetChild(0), _bookingCards["act.meadow-lanterns"]), "All/default Price ordering not restored");
                BookingCheck(_bookingCaptureHash == _session.CaptureSnapshot().AuthoritativeHash, "Restored view changed authoritative hash");
            }
            if (frame == 28)
            {
                SelectBookingBand("act.meadow-lanterns");
                _bookingGenreFilter!.Select(2); _bookingGenreFilter.EmitSignal(OptionButton.SignalName.ItemSelected, 2);
                BookingCheck(BookingIds[0] == "act.meadow-lanterns" && !_bookingCards["act.meadow-lanterns"].Visible && _bookingSelected == "act.meadow-lanterns", "Filtering hid a booked act but lost selection or slot");
                BookingCheck(_bookingCaptureHash != _session.CaptureSnapshot().AuthoritativeHash, "Booked plan did not change after placement");
            }
            if (frame == 29)
            {
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_bookingCaptureDirectory, "00-filtered-assigned.png"));
                _bookingGenreFilter!.Select(0); _bookingGenreFilter.EmitSignal(OptionButton.SignalName.ItemSelected, 0);
            }
            var scenarioFrame = frame - 22;
            if (scenarioFrame <= 0) return;
            if (scenarioFrame == 66)
            {
                BookingCheck(_bookingSelected == "act.neon-postcards", "Injected native Enter selection failed");
                _bookingSlots[0].GrabFocus(); Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, Pressed = true }); Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, Pressed = false });
            }
            if (scenarioFrame % 5 == 4 && _bookingCaptureShot != "")
            {
                if (_session.PreparedStatus == PreparationStatus.Preparing)
                {
                    var viewport = GetViewport().GetVisibleRect();
                    GD.Print($"BOOKING_LAYOUT shot={_bookingCaptureShot} viewport={viewport} workspace={_hudWorkspace!.GetGlobalRect()} start={_preparationStart!.GetGlobalRect()} reason={_hudStartReason!.GetGlobalRect()} tabs={_hudTabs!.GetGlobalRect()}");
                    GD.Print($"BOOKING_MIN tabs={_hudTabs.GetCombinedMinimumSize()} page={_hudPages["Programme"].GetCombinedMinimumSize()} controls={_programmeControls!.GetCombinedMinimumSize()} table={_bookingTableBody!.GetCombinedMinimumSize()} lane={_bookingLane!.GetCombinedMinimumSize()} hidden={_hudTabs.UseHiddenTabsForMinSize}");
                    BookingCheck(_preparationStart!.IsVisibleInTree() && viewport.Encloses(_preparationStart.GetGlobalRect()), "Start footer outside viewport");
                    BookingCheck(_hudStartReason!.IsVisibleInTree() && viewport.Encloses(_hudStartReason.GetGlobalRect()) && _hudStartReason.Text.Contains("Paid at Start"), "Lineup cost/readiness footer outside viewport");
                    BookingCheck(_hudStartReason.GetVisibleLineCount() == _hudStartReason.GetLineCount(), "Booking readiness text clipped");
                    BookingCheck(_hudStartReason.GetGlobalRect().End.Y < viewport.End.Y - 54, "Footer overlaps global status bar");
                    BookingCheck(_bookingSlots.All(s => viewport.Encloses(s.GetGlobalRect())), "Stage slot outside viewport");
                    BookingCheck(_bookingLane!.GetGlobalRect().End.X < _bookingHeaders[0].GetGlobalRect().Position.X, "Lineup and hitboxes are not left of the band table");
                    BookingCheck(_bookingHeaders.All(h => viewport.Encloses(h.GetGlobalRect())), "A table column header left the viewport");
                    BookingCheck(_bookingCards.Values.All(card => card.Visible && viewport.Encloses(card.GetGlobalRect())), "A band row is hidden or outside the viewport in All genres");
                    BookingCheck(_bookingMeaning is not null && viewport.Encloses(_bookingMeaning.GetGlobalRect()) && _bookingMeaning.GetVisibleLineCount() == _bookingMeaning.GetLineCount(), "Booking trait meaning line clipped");
                    BookingCheck(((ScrollContainer)_hudTabs.GetChild(1)).ScrollVertical == 0, "Programme page needs scrolling at target size");
                    BookingCheck(_bookingCards.Values.All(card => _hudTabs.GetGlobalRect().Encloses(card.GetGlobalRect())), "A band row is clipped by Programme contents");
                    BookingCheck(_bookingRowCopy.All(pair =>
                        _bookingCards[pair.Key].GetGlobalRect().Encloses(pair.Value.Name.GetGlobalRect()) &&
                        _bookingCards[pair.Key].GetGlobalRect().Encloses(pair.Value.Detail.GetGlobalRect()) &&
                        pair.Value.Name.GetLineCount() == 1 && pair.Value.Name.GetVisibleLineCount() == 1 &&
                        pair.Value.Detail.GetLineCount() == 1 && pair.Value.Detail.GetVisibleLineCount() == 1), "Assigned name or expectation wraps/clips outside its 42px row");
                    BookingCheck(_bookingHeaderTitles.All(label => label.GetLineCount() == 1 && label.GetVisibleLineCount() == 1), "A table header label wraps or clips");
                    BookingCheck(_bookingHeaders.All(button => button.GetChild(0).GetChildCount() == 1), "A rating-header subtitle remains visible");
                    BookingCheck(_bookingRatings.Values.All(rating => rating.Popularity.GetParent().GetChildCount() == 1 && rating.Ego.GetParent().GetChildCount() == 1 && rating.Professionalism.GetParent().GetChildCount() == 1), "A numeric star total remains beside a star strip");
                    BookingCheck(_session.GetFestivalActs().All(act =>
                        _bookingRatings[act.Id].Popularity.Score == act.Popularity && _bookingRatings[act.Id].Ego.Score == act.Ego && _bookingRatings[act.Id].Professionalism.Score == act.Professionalism &&
                        _bookingCards[act.Id].TooltipText.Contains($"Popularity {act.Popularity}/100") && _bookingCards[act.Id].TooltipText.Contains($"Ego {act.Ego}/100") && _bookingCards[act.Id].TooltipText.Contains($"Professionalism {act.Professionalism}/100") &&
                        _bookingRatings[act.Id].Popularity.TooltipText.Contains($"{act.Name} — Popularity {act.Popularity}/100") &&
                        _bookingRatings[act.Id].Ego.TooltipText.Contains($"{act.Name} — Ego {act.Ego}/100") &&
                        _bookingRatings[act.Id].Professionalism.TooltipText.Contains($"{act.Name} — Professionalism {act.Professionalism}/100") &&
                        _bookingRatings[act.Id].Popularity.FocusMode == Control.FocusModeEnum.All &&
                        _bookingRatings[act.Id].Ego.FocusMode == Control.FocusModeEnum.All &&
                        _bookingRatings[act.Id].Professionalism.FocusMode == Control.FocusModeEnum.All), "A star cell or exact raw-value hover/keyboard help disagrees with the catalog");
                }
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_bookingCaptureDirectory, _bookingCaptureShot + ".png"));
            }
            if (scenarioFrame % 5 != 0 && scenarioFrame != 1) return;
            var step = scenarioFrame == 1 ? 0 : scenarioFrame / 5;
            switch (step)
            {
                case 0:
                    SelectHudTab("Programme");
                    _bookingCaptureShot = "01-empty"; _bookingCaptureHash = _session.CaptureSnapshot().AuthoritativeHash;
                    _bookingStalePayload = BookingPayload("act.meadow-lanterns");
                    BookingCheck(BookingIds.All(id => id == ""), "New booking not empty"); break;
                case 1:
                    SelectBookingBand("act.meadow-lanterns"); BookingCheck(_bookingCaptureHash == _session.CaptureSnapshot().AuthoritativeHash, "Selecting paid or placed");
                    _bookingSlots[0].EmitSignal(Button.SignalName.Pressed);
                    BookingCheck(!_bookingSlots[1]._CanDropData(Vector2.Zero, _bookingStalePayload), "Stale source-slot payload accepted after sorted/filtered placement");
                    _bookingCaptureShot = "02-one-set"; break;
                case 2:
                    _bookingCards["act.neon-postcards"].ForceDrag(BookingPayload("act.neon-postcards"), new Label { Text = "Neon Postcards · Pop · £110" });
                    BookingCheck(GetViewport().GuiIsDragging(), "Native ForceDrag did not begin");
                    _bookingCapturePayload = _bookingCards["act.neon-postcards"]._GetDragData(Vector2.Zero);
                    BookingCheck(_bookingSlots[1]._CanDropData(Vector2.Zero, _bookingCapturePayload), "Native CanDrop refused valid payload");
                    _bookingCaptureShot = "03-native-drag-preview"; break;
                case 3:
                    _bookingSlots[1]._DropData(Vector2.Zero, _bookingCapturePayload);
                    GetViewport().GuiCancelDrag(); RefreshBookingControls();
                    _bookingCards["act.field-frequency"].EmitSignal(Button.SignalName.Pressed); _bookingSlots[2].EmitSignal(Button.SignalName.Pressed);
                    _bookingCaptureShot = "04-three-sets"; break;
                case 4:
                    _bookingCapturePayload = BookingPayload("act.copper-static"); PreviewBookingDrop(1, _bookingCapturePayload);
                    _bookingCaptureShot = "05-replacement-preview"; break;
                case 5:
                    _bookingSlots[1]._DropData(Vector2.Zero, _bookingCapturePayload); _bookingCaptureShot = "06-replaced"; break;
                case 6:
                    _bookingCapturePayload = BookingPayload("act.meadow-lanterns"); PreviewBookingDrop(1, _bookingCapturePayload); _bookingCaptureShot = "07-swap-preview"; break;
                case 7:
                    _bookingSlots[1]._DropData(Vector2.Zero, _bookingCapturePayload); _bookingCaptureShot = "08-swapped"; break;
                case 8:
                    _bookingCaptureHash = _session.CaptureSnapshot().AuthoritativeHash;
                    BookingCheck(!_session.PreviewLineupEdit("act.meadow-lanterns", 1, -1).IsValid, "Gap accepted");
                    CommitBookingDrop(0, "invalid"); BookingCheck(_bookingCaptureHash == _session.CaptureSnapshot().AuthoritativeHash, "Invalid drop mutated");
                    _bookingCaptureShot = "09-invalid-retained"; break;
                case 9:
                    _bookingCards["act.field-frequency"].ForceDrag(BookingPayload("act.field-frequency"), new Label { Text = "Field Frequency · native cancel fixture" });
                    _bookingCards["act.field-frequency"]._GetDragData(Vector2.Zero);
                    GetViewport().GuiCancelDrag(); _bookingSelected = null; RefreshBookingControls(); BookingCheck(_bookingCaptureHash == _session.CaptureSnapshot().AuthoritativeHash, "Cancel mutated");
                    _bookingCaptureShot = "10-cancelled"; break;
                case 10:
                    _planCaptureFailureInjector = _ => throw new IOException("R0.05n scripted save failure");
                    CommitBookingDrop(0, BookingPayload("act.orchard-chorus")); _planCaptureFailureInjector = null;
                    BookingCheck(_bookingCaptureHash == _session.CaptureSnapshot().AuthoritativeHash, "Save failure failed rollback"); _bookingCaptureShot = "11-save-failure"; break;
                case 11:
                    PreparationSave(); PreparationLoad(); BookingCheck(_bookingCaptureHash == _session.CaptureSnapshot().AuthoritativeHash, "Loaded plan changed");
                    _bookingCaptureShot = "12-loaded"; break;
                case 12:
                    _bookingRemove[0].EmitSignal(Button.SignalName.Pressed); _bookingCaptureShot = "13-removed"; break;
                case 13:
                    BookingCheck(!GetViewport().GuiIsDragging(), "Native drag retained at keyboard start");
                    _bookingCards["act.neon-postcards"].GrabFocus(); Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, Pressed = true }); Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, Pressed = false });
                    _bookingCaptureShot = "14-keyboard-placement"; break;
                case 14:
                    BookingCheck(BookingIds[0] == "act.neon-postcards", "Injected native Enter/Space placement failed");
                    _bookingHeaders[3].EmitSignal(Control.SignalName.MouseEntered);
                    BookingCheck(_bookingMeaning!.Text.Contains("Popularity — Audience appeal"), "Rating-header hover lost its meaning");
                    _bookingHeaders[3].EmitSignal(Control.SignalName.MouseExited);
                    _bookingRatings["act.field-frequency"].Ego.EmitSignal(Control.SignalName.MouseEntered);
                    BookingCheck(_bookingMeaning!.Text.Contains("Field Frequency — Ego 60/100") && _bookingMeaning.Text.Contains("3.0 stars"), "Star-strip hover lacks exact score and star count");
                    _bookingRatings["act.field-frequency"].Ego.EmitSignal(Control.SignalName.MouseExited);
                    _bookingRatings["act.field-frequency"].Ego.GrabFocus();
                    BookingCheck(_bookingMeaning!.Text.Contains("Field Frequency — Ego 60/100") && _bookingMeaning.Text.Contains("3.0 stars") && _bookingMeaning.Text.Contains("demanding/headline-sensitive"), "Keyboard focus on star strip lacks exact score, star count and meaning");
                    _bookingCards["act.field-frequency"].GrabFocus();
                    BookingCheck(_bookingMeaning!.Text.Contains("Field Frequency") && _bookingMeaning.Text.Contains("Popularity 75/100") && _bookingMeaning.Text.Contains("Ego 60/100") && _bookingMeaning.Text.Contains("Professionalism 75/100") &&
                        _bookingMeaning.Text.Contains("appeal") && _bookingMeaning.Text.Contains("demanding, not quality") && _bookingMeaning.Text.Contains("softens ego, not immunity") &&
                        _bookingMeaning.GetVisibleLineCount() == _bookingMeaning.GetLineCount(), "Keyboard focus lacks full exact scores and factual meanings");
                    PreparationAccept("staff.steward"); _bookingCaptureShot = "15-ready-cost"; break;
                case 15:
                    PreparationStart(); _session.AdvanceWithoutSnapshot(1800); _foundationPresentation.Reset(_session.CaptureObservation());
                    AcceptBookingPause();
                    BookingCheck(_bookingCards.Values.All(card => GetViewport().GetVisibleRect().Encloses(card.GetGlobalRect())), "A band row left the viewport after keyboard focus");
                    _bookingCaptureHash = _session.CaptureSnapshot().AuthoritativeHash; _bookingCaptureShot = "16-live-locked";
                    BookingCheck(!_session.PreviewLineupEdit("act.orchard-chorus", -1, 0).IsValid, "Live edit accepted"); break;
                case 16:
                    BookingCheck(_bookingCaptureHash == _session.CaptureSnapshot().AuthoritativeHash, "Live invalid edit changed");
                    GD.Print($"BOOKING_TABLE_CAPTURE_COMPLETE scripted_native_handlers=True ordinary_OS_drag=False png=22 hash={_bookingCaptureHash}"); GetTree().Quit(); break;
            }
        }
        catch (Exception ex) { GD.PushError("BOOKING_CAPTURE_FAILED " + ex); GetTree().Quit(2); }
    }
    private void AcceptBookingPause() => _session.Execute(CampaignEnvelope(new SetPausedCommand(true)));
}
