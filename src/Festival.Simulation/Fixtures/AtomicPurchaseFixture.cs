namespace Festival.Simulation.Fixtures;

/// <summary>Development-only exact-value setup for M0.04 purchase verification.</summary>
public static class AtomicPurchaseFixture
{
    public static PurchaseFixtureState Create(int stockQuantity = 2, int unitCostBasisPennies = 120)
    {
        var session = new GameSession(20_260_908, new CampaignId(4));
        var festivalId = CreateEntity(session, new CommandId(1), new CreateFestivalFinanceCommand(0));
        var serviceId = CreateEntity(session, new CommandId(2), new CreateOwnedStockCommand(festivalId, stockQuantity, unitCostBasisPennies));
        var primaryBuyerId = CreateEntity(session, new CommandId(3), new CreateGuestWalletCommand(500));
        var exactCashBuyerId = CreateEntity(session, new CommandId(4), new CreateGuestWalletCommand(300));
        var competingBuyerId = CreateEntity(session, new CommandId(5), new CreateGuestWalletCommand(500));
        return new PurchaseFixtureState(session, festivalId, serviceId, primaryBuyerId, exactCashBuyerId, competingBuyerId);
    }

    public static CommandEnvelope PurchaseEnvelope(
        PurchaseFixtureState fixture,
        CommandId commandId,
        TransactionId transactionId,
        EntityId buyerId,
        long unitPricePennies = 300,
        int quantity = 1,
        EntityId? festivalId = null,
        EntityId? serviceId = null) =>
        new(
            commandId,
            fixture.Session.CampaignId,
            fixture.Session.Phase,
            fixture.Session.CurrentTick,
            fixture.Session.NextSubmissionSequence,
            serviceId ?? fixture.ServiceId,
            new PurchaseItemCommand(transactionId, buyerId, festivalId ?? fixture.FestivalId, unitPricePennies, quantity));

    private static EntityId CreateEntity(GameSession session, CommandId commandId, SessionCommand command)
    {
        var result = session.Execute(new CommandEnvelope(
            commandId,
            session.CampaignId,
            session.Phase,
            session.CurrentTick,
            session.NextSubmissionSequence,
            null,
            command));
        if (!result.IsAccepted || result.TargetId is null)
        {
            throw new InvalidOperationException($"Purchase fixture setup failed: {result.Message}");
        }

        return result.TargetId.Value;
    }
}

public sealed record PurchaseFixtureState(
    GameSession Session,
    EntityId FestivalId,
    EntityId ServiceId,
    EntityId PrimaryBuyerId,
    EntityId ExactCashBuyerId,
    EntityId CompetingBuyerId);
