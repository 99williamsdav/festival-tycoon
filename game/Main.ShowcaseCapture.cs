using Festival.Simulation;
using Godot;
using System;

namespace Festival.Game;

public partial class Main
{
    private string? _showcaseDirectory;
    private int _showcaseFrame;

    private void ShowcaseScene(string name)
    {
        GD.Print($"SHOWCASE_SCENE frame={_showcaseFrame} second={_showcaseFrame / 30.0:0.0} tick={_session.CurrentTick} name={name}");
    }

    private void ShowcaseAdvanceTo(long tick)
    {
        if (_session.CurrentTick < tick)
            _session.AdvanceWithoutSnapshot((int)(tick - _session.CurrentTick));
        _foundationPresentation.Reset(_session.CaptureObservation());
        AdvancePreparationPresentation(0);
        RefreshPreparationHud();
    }

    private void ProcessShowcaseCapture()
    {
        if (_showcaseDirectory is null) return;
        _showcaseFrame++;
        try
        {
            switch (_showcaseFrame)
            {
                case 1:
                    ShowcaseScene("perk draft");
                    break;
                case 90:
                    var perk = _session.CapturePerks()!;
                    CommitEquipmentAction(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]));
                    ShowcaseScene("perk chosen; build catalogue");
                    break;
                case 210:
                    CommitEquipmentAction(new UseDefaultBuildLayoutCommand());
                    RefreshPreparationHud();
                    ShowcaseScene("six real facilities placed");
                    break;
                case 330:
                    CommitEquipmentAction(new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.neon-postcards"]));
                    SelectHudTab("Programme");
                    ShowcaseScene("three acts booked");
                    break;
                case 450:
                    CommitEquipmentAction(new AcceptPreparationOfferCommand("staff.steward"));
                    SelectHudTab("Staff");
                    ShowcaseScene("steward hired");
                    break;
                case 540:
                    CommitEquipmentAction(new AcceptPreparationOfferCommand("equipment.buy"));
                    CommitEquipmentAction(new SetPreparationStockCommand(8, 8, 8));
                    SelectHudTab("Stock");
                    ShowcaseScene("equipment and vendor stock committed");
                    break;
                case 630:
                    PreparationStart();
                    if (_session.PreparedStatus != PreparationStatus.Running)
                        throw new InvalidOperationException("Showcase failed to start a real Build festival.");
                    _hudWorkspaceOpen = false; _buildDrawerOpen = false; _hudProgrammeOpen = false; ClearSelection();
                    _focus = new Vector3(4, 0, 8); _camera.Size = 50; ApplyCamera();
                    RefreshPreparationHud();
                    ShowcaseScene("festival opens; natural arrivals");
                    break;
                case 750:
                    ShowcaseAdvanceTo(2900);
                    _focus = new Vector3(4, 0, 8); _camera.Size = 42; ApplyCamera();
                    ShowcaseScene("accelerated to staggered arrivals");
                    break;
                case 960:
                    ShowcaseAdvanceTo(5300);
                    _focus = new Vector3(-12, 0, 12); _camera.Size = 28; ApplyCamera();
                    ShowcaseScene("accelerated to first live set");
                    break;
                case 1230:
                    _focus = new Vector3(6, 0, -4.25f); _camera.Size = 34; ApplyCamera();
                    ShowcaseScene("food bar and held items");
                    break;
                case 1500:
                    _focus = _toiletBody?.Position ?? new Vector3(12, 0, 0);
                    _camera.Size = 19; _orientation = 2; ApplyCamera();
                    ShowcaseScene("toilet and service area");
                    break;
                case 1740:
                    ShowcaseAdvanceTo(_session.PreparedEditionDurationTicks + 15000);
                    if (_session.PreparedStatus != PreparationStatus.Finished ||
                        _session.CompletedFestivalAccounts?.Reconciles != true)
                        throw new InvalidOperationException("Natural Build completion or accounts reconciliation failed.");
                    ShowcaseScene("natural completed festival newspaper");
                    break;
                case 1950:
                    _accountsTab!.EmitSignal(Button.SignalName.Pressed);
                    ShowcaseScene("actual completed festival accounts");
                    break;
                case 2130:
                    _resultsScroll!.ScrollVertical = 100000;
                    ShowcaseScene("accounts cash reconciliation");
                    break;
                case 2280:
                    ShowcaseScene("capture complete");
                    _showcaseDirectory = null;
                    GetTree().Quit();
                    break;
            }
        }
        catch (Exception error)
        {
            GD.PushError("SHOWCASE_CAPTURE_FAILED " + error);
            _showcaseDirectory = null;
            GetTree().Quit(2);
        }
    }
}
