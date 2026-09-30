using Festival.Simulation;

namespace Festival.ContentAdapter;

// Cosmetic read model only. No mutable clock, gameplay random stream or saved fields.
public static class AttendeePose
{
    public const int CycleTicks = 320;
    public const int LiftStartTicks = 240;
    public static string Variant(ulong seed, ulong id)
    {
        var mixed = unchecked(id * 6364136223846793005UL + seed);
        mixed = unchecked((mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL);
        mixed = unchecked((mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL);
        return ((mixed ^ (mixed >> 31)) & 1) == 0 ? "male" : "female";
    }
    public static string State(ImmersionHeldItem? held, bool handsAvailable, bool consumptionEligible)
    {
        if (held is null || !handsAvailable) return "relaxed";
        var lifted = consumptionEligible && held.ConsumedTicks % CycleTicks >= LiftStartTicks;
        return held.Product == ImmersionProduct.Chips ? lifted ? "eating" : "food_hold" : lifted ? "drinking" : "drink_hold";
    }
    // Only the lifted drinking state has a product-specific body; holds share one grip.
    public static string Key(string variant, string state, ImmersionProduct? product) =>
        $"{variant}_{state}{(state == "drinking" ? product == ImmersionProduct.Beer ? "_beer" : "_soft" : "")}";
}
