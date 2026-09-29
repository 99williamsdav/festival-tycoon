using Festival.Simulation;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Festival.Game;

public partial class Main
{
    private string? _accountsCaptureDirectory;
    private int _accountsCaptureStep;
    private double _accountsCaptureWait;

    private void AccountsCaptureImage(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        if (image.GetWidth() != GetWindow().Size.X || image.GetHeight() != GetWindow().Size.Y ||
            image.SavePng(Path.Combine(_accountsCaptureDirectory!, name + ".png")) != Error.Ok)
            throw new InvalidOperationException("Accounts capture screenshot failed.");
    }

    private void ProcessAccountsCapture(double delta)
    {
        if (_accountsCaptureDirectory is null) return;
        _accountsCaptureWait += delta;
        if (_accountsCaptureWait < .35) return;
        _accountsCaptureWait = 0;
        try
        {
            switch (_accountsCaptureStep++)
            {
                case 0:
                    var perk = _session.CapturePerks()!;
                    foreach (var command in new SessionCommand[] {
                        new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]),
                        new UseDefaultBuildLayoutCommand(),
                        new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.neon-postcards"]),
                        new AcceptPreparationOfferCommand("staff.steward"),
                        new AcceptPreparationOfferCommand("equipment.buy"),
                        new SetPreparationStockCommand(8, 8, 8) })
                        CommitEquipmentAction(command);
                    PreparationStart();
                    if (_session.PreparedStatus != PreparationStatus.Running)
                        throw new InvalidOperationException("Accounts fixture failed to start.");
                    _session.Execute(CampaignEnvelope(new SetPausedCommand(true)));
                    var people = _session.CapturePreparation()!.People;
                    var guests = people.Where(person => person.Role == ProtectedPersonRole.Guest).Take(3).ToArray();
                    var staff = people.First(person => person.Role == ProtectedPersonRole.Staff);
                    var performer = people.First(person => person.Role == ProtectedPersonRole.Performer);
                    var sell = typeof(GameSession).GetMethod("CompleteImmersionSale", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    foreach (var (id, product) in new (ulong, ImmersionProduct)[] {
                        (guests[0].AgentId, ImmersionProduct.Chips), (staff.AgentId, ImmersionProduct.Chips),
                        (guests[1].AgentId, ImmersionProduct.SoftDrink), (staff.AgentId, ImmersionProduct.SoftDrink),
                        (guests[2].AgentId, ImmersionProduct.Beer), (performer.AgentId, ImmersionProduct.Beer) })
                        sell.Invoke(_session, [id, product]);
                    var preparation = _session.CapturePreparation()!;
                    var make = typeof(GameSession).GetMethod("MakeFestivalResult", BindingFlags.NonPublic | BindingFlags.Static)!;
                    var result = (FestivalResult)make.Invoke(null, [preparation, _session.CaptureImmersion(),
                        _session.CaptureMedical(), _session.CaptureDisorder(), _session.CurrentTick])!;
                    typeof(GameSession).GetField("_preparation", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .SetValue(_session, preparation with { Result = result });
                    var report = _session.CompletedFestivalAccounts!;
                    if (!report.Reconciles || report.OperatingExpenses.All(line => line.Category != "Facilities") ||
                        report.Sales.Length != 6)
                        throw new InvalidOperationException("Accounts fixture did not reconcile actual sales and build fees.");
                    RefreshPreparationHud();
                    _accountsTab!.EmitSignal(Button.SignalName.Pressed);
                    var label = new Label { Text = "LABELLED ACCOUNTING FIXTURE · real setup and sale transactions",
                        Position = new Vector2(10, 0) };
                    label.AddThemeFontSizeOverride("font_size", 13);
                    var overlay = new CanvasLayer { Layer = 30 }; AddChild(overlay); overlay.AddChild(label);
                    break;
                case 1:
                    AccountsCaptureImage("00-accounts-income-expenditure");
                    _resultsScroll!.ScrollVertical = 100000;
                    break;
                case 2:
                    AccountsCaptureImage("01-accounts-cash");
                    var accounts = _session.CompletedFestivalAccounts!;
                    File.WriteAllText(Path.Combine(_accountsCaptureDirectory, "result.json"), JsonSerializer.Serialize(new {
                        passed = accounts.Reconciles, resolution = GetWindow().Size.ToString(),
                        salesLines = accounts.Sales.Length, sales = accounts.IncomePennies,
                        soldItemCosts = accounts.SoldItemCostPennies,
                        buildFees = accounts.OperatingExpenses.Where(line => line.Category == "Facilities").Sum(line => line.AmountPennies),
                        operatingExpenses = accounts.OperatingExpensesPennies,
                        capital = accounts.CapitalPurchasesPennies,
                        stockPurchases = accounts.StockPurchasesPennies,
                        openingCash = accounts.OpeningCashPennies, closingCash = accounts.ClosingCashPennies,
                        fixture = "labelled current-attempt transactions; terminal result projected for presentation"
                    }, new JsonSerializerOptions { WriteIndented = true }));
                    GD.Print("ACCOUNTS_CAPTURE_COMPLETE " + _accountsCaptureDirectory);
                    _accountsCaptureDirectory = null; GetTree().Quit(); break;
            }
        }
        catch (Exception error)
        {
            GD.PushError("ACCOUNTS_CAPTURE_FAILED " + error);
            _accountsCaptureDirectory = null; GetTree().Quit(2);
        }
    }
}
