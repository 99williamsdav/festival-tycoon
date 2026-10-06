namespace Festival.Simulation;

/// <summary>Switch free water at the bar on (paying for it) or off (no refund). Only during the festival.</summary>
public sealed record SetFreeWaterCommand(bool Enabled) : SessionCommand;

// An emergency measure for broken or swamped taps: the bar hands out free cups of water from its own queue.
// Only the thirsty want it (it has no pleasure in it), and it slows the bar's paying customers.
public sealed partial class GameSession
{
    public const int FreeWaterChargePennies = 2_000;

    public bool FreeWaterOn => _immersion?.FreeWater == true;

    private static long FreeWaterSpend(ImmersionSnapshot? immersion) => (immersion?.FreeWaterChargeTicks.Length ?? 0) * (long)FreeWaterChargePennies;

    private CommandResult? ValidateFreeWaterCommand(EntityId? target, SetFreeWaterCommand command)
    {
        if (target is not null || _immersion is null || _preparation?.Status != PreparationStatus.Running)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Free water at the bar is only for the festival day itself.");
        if (command.Enabled == _immersion.FreeWater)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, command.Enabled ? "The bar is already giving out free water." : "The bar isn't giving out free water.");
        if (command.Enabled && _festivalFinances[new(_preparation.FinanceOwnerId)].CashPennies + CampaignDefaults.OverdraftPennies < FreeWaterChargePennies)
            return CommandResult.Rejected(CommandReasonCode.InsufficientFunds, $"Free water costs {FreeWaterChargePennies / 100} pounds up front.");
        return null;
    }

    private void ApplyFreeWater(SetFreeWaterCommand command)
    {
        if (command.Enabled)
        {
            _festivalFinances[new(_preparation!.FinanceOwnerId)].CashPennies -= FreeWaterChargePennies;
            _immersion = _immersion! with { FreeWater = true, FreeWaterChargeTicks = _immersion.FreeWaterChargeTicks.Append(CurrentTick).ToArray() };
        }
        // Switching off ends new orders; anyone already queuing for a cup is still served.
        else _immersion = _immersion! with { FreeWater = false };
    }

    private static string? ValidateFreeWater(ImmersionSnapshot m, PreparationSnapshot prep, SessionPersistenceSnapshot s)
    {
        var charges = m.FreeWaterChargeTicks;
        if (charges is null || charges.Any(tick => tick < prep.StartedTick || tick > s.CurrentTick) || !charges.SequenceEqual(charges.Order()) ||
            m.FreeWater && charges.Length == 0 || prep.Status == PreparationStatus.Preparing && (m.FreeWater || charges.Length > 0))
            return "Free water charges invalid.";
        // Every free cup was handed out after free water was first paid for.
        if (m.Purchases.Any(p => p.Product == ImmersionProduct.Water && (charges.Length == 0 || p.Tick < charges[0])))
            return "Free water served before it was paid for.";
        return null;
    }
}
