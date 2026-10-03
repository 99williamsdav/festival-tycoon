namespace Festival.Simulation;

/// <summary>
/// The crowd and staff perks that change how people drink, queue and look after themselves: Beer Festival and
/// Alcoholics make beer more tempting, Cola Fiends soft drinks, Bring Your Own Bottle slows thirst but lengthens a
/// fill at the tap, Friendly Queues make waiting pleasant, and Robot Workers have no needs of their own.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Beer Festival marks beer up by half, relative to whatever the base price is.</summary>
    public const int BeerFestivalPricePercent = 150, BeerFestivalTasteBoost = 4;
    public const int AlcoholicsTasteBoost = 30, ColaFiendsTasteBoost = 30;
    /// <summary>Bring Your Own Bottle: guests' thirst grows at three-quarters the rate, and a tap fills them at 60%.</summary>
    public const int ByobThirstQuarters = 3, ByobFillPercent = 60;
    /// <summary>Friendly Queues: a point of satisfaction this often while waiting in any queue.</summary>
    public const int FriendlyQueueGainEveryTicks = 5;

    /// <summary>How much someone likes beer, more at a Beer Festival and much more with Alcoholics.</summary>
    private int BeerTasteOf(Person person) => Math.Min(100, person.BeerTaste +
        (HasPerk(PerkCatalogue.BeerFestival) ? BeerFestivalTasteBoost : 0) + (HasPerk(PerkCatalogue.Alcoholics) && IsGuest(person.Id) ? AlcoholicsTasteBoost : 0));

    /// <summary>How much someone likes soft drinks, much more among Cola Fiends.</summary>
    private int SoftTasteOf(Person person) =>
        HasPerk(PerkCatalogue.ColaFiends) && IsGuest(person.Id) ? Math.Min(100, person.SoftTaste + ColaFiendsTasteBoost) : person.SoftTaste;

    private bool BringsOwnBottle(ulong id) => HasPerk(PerkCatalogue.BringYourOwnBottle) && IsGuest(id);

    /// <summary>A robot worker: staff, with Robot Workers, who never get thirsty, hot, hungry or need the loo.</summary>
    private bool RobotWorker(ulong id) => HasPerk(PerkCatalogue.RobotWorkers) && IsStaffMember(id);

    /// <summary>Waiting guests chat and cheer up: in a tap, toilet or stall queue, but not once they're being served.</summary>
    private void AdvanceFriendlyQueues()
    {
        if (!HasPerk(PerkCatalogue.FriendlyQueues) || CurrentTick % FriendlyQueueGainEveryTicks != 0) return;
        var waiting = new HashSet<ulong>();
        foreach (var point in WaterPoints())
            foreach (var id in point.Queue.Concat(point.Overflow)) if (id != point.OwnerId) waiting.Add(id);
        foreach (var vendor in Vendors)
            foreach (var id in vendor.Queue) if (id != vendor.OwnerId) waiting.Add(id);
        foreach (var person in PeopleIn(PersonView.Medical))
            if (person.ToiletStage == ToiletVisitStage.Queued) waiting.Add(person.Id);
        foreach (var id in waiting.Order())
            if (IsGuest(id) && _persons[id] is { Admitted: true, Departed: false })
                MutatePerson(id, person => person.Satisfaction = Math.Min(10_000, person.Satisfaction + 1));
    }
}
