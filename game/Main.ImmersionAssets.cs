using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;

namespace Festival.Game;

public partial class Main
{
    private readonly Dictionary<EntityId, Node3D> _immersionHeldVisuals = [];
    private readonly Dictionary<EntityId, string> _immersionHeldProducts = [];

    // Delivered GLBs contain the complete curated body, fascia, artwork and kitchen.
    // Keep their original transforms under the unchanged shared service anchor.
    private static string FoodVanProductionAsset(ImmersionProduct product) =>
        "res://assets/environment/food-vans-production-v1/ft_food_" + (product switch
        {
            ImmersionProduct.Chips => "chips",
            ImmersionProduct.Pizza => "pizza",
            ImmersionProduct.Curry => "curry",
            _ => throw new ArgumentOutOfRangeException(nameof(product), product, "Unknown food van")
        }) + "_production_v1.glb";

    private Node3D InstantiateImmersionVendor(FoodTrader? trader)
    {
        if (trader is null) return InstantiateAsset("res://assets/environment/lwf_drinks_stall_prototype_v1.glb");
        var vendor = new Node3D { Name = "ImmersionFoodVan" };
        var assembly = new Node3D { Name = "ApprovedFoodVanAssembly", Position = new(-2.15f, 0, 0) };
        vendor.AddChild(assembly);
        var model = InstantiateAsset(FoodVanProductionAsset(trader.Product));
        model.Name = "ApprovedFoodVanModel";
        assembly.AddChild(model);
        // Curated palettes/artwork remain untouched; no legacy chassis or decoration is added.
        if (trader.Product == ImmersionProduct.Curry)
            foreach (var vent in new[] { "LWF_FoodVan_SteamVent_L", "LWF_FoodVan_SteamVent_R" })
                ((Node3D)model.FindChild(vent, true, false)).AddChild(VanSteam());
        if (ResourceLoader.Exists($"res://assets/environment/lwf_food_van_menu_board_{trader.Art}_v1.glb"))
        {
            var menu = InstantiateAsset($"res://assets/environment/lwf_food_van_menu_board_{trader.Art}_v1.glb");
            menu.Name = "FoodVanChalkMenu";
            menu.Position = new(4.10f, 0, 2.30f);
            assembly.AddChild(menu);
        }
        return vendor;
    }
    private const string VanSteamName = "VanSteam";
    private static StandardMaterial3D? _vanSteamMaterial;

    /// <summary>Soft white puffs rising from a roof vent: on while the van's serving or has a queue (see <see cref="ProcessVanSteam"/>).</summary>
    private static CpuParticles3D VanSteam()
    {
        _vanSteamMaterial ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true,
            AlbedoTexture = new GradientTexture2D
            {
                Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(.5f, .5f), FillTo = new Vector2(1, .5f), Width = 32, Height = 32,
                Gradient = new Gradient { Colors = [Colors.White, new Color(1, 1, 1, 0)], Offsets = [0, 1] }
            }
        };
        var grow = new Curve { MinValue = 0, MaxValue = 3 };
        grow.AddPoint(new Vector2(0, 1)); grow.AddPoint(new Vector2(1, 2.9f));
        var steam = new CpuParticles3D
        {
            Name = VanSteamName, Emitting = false, Amount = 8, Lifetime = 1.9, Randomness = .3f,
            Mesh = new QuadMesh { Size = new Vector2(.12f, .12f) }, MaterialOverride = _vanSteamMaterial,
            Direction = Vector3.Up, Spread = 10, InitialVelocityMin = .5f, InitialVelocityMax = .7f, Gravity = new Vector3(.08f, 0, 0),
            ScaleAmountCurve = grow,
            ColorRamp = new Gradient { Colors = [new Color(1, 1, 1, .45f), new Color(1, 1, 1, 0)], Offsets = [0, 1] },
        };
        steam.AddToGroup(PausableWorldEffects);
        return steam;
    }

    private readonly Dictionary<ulong, CpuParticles3D[]> _vanSteam = [];

    /// <summary>Each frame: steam from a van's vents while it's serving or has a queue; paused, the puffs hold where they are.</summary>
    private void ProcessVanSteam()
    {
        if (_session.CaptureImmersion() is null) return;
        // Vans rebuilt for a new trader leave their old entries behind: start afresh.
        if (_vanSteam.Count > _immersionVendors.Count) _vanSteam.Clear();
        foreach (var vendor in _session.CaptureVendors())
        {
            if (!Stalls.IsVan(vendor.Id) || !_immersionVendors.TryGetValue(vendor.Id, out var body) || !IsInstanceValid(body)) continue;
            if (!_vanSteam.TryGetValue(body.GetInstanceId(), out var vents))
                _vanSteam[body.GetInstanceId()] = vents = body.FindChildren(VanSteamName, "", true, false).OfType<CpuParticles3D>().ToArray();
            if (_session.IsPaused) continue;
            var busy = vendor.Queue.Length > 0 || vendor.OwnerId is not null;
            foreach (var steam in vents) if (steam.Emitting != busy) steam.Emitting = busy;
        }
    }

    // Presentation only: product identity and hands eligibility come from simulation.
    // The generic 1.75 m adult is a single unrigged mesh. Its right neutral hand
    // spans X .26-.38, Y .90-.98, Z -.10-.00 m; no invented arm socket or mesh
    // scaling is used. Cup edge meets the outer palm; the tray rests atop it.
    // Performer idle arms share that adult pose, with shoulder origin
    // (.255,1.315,-.005) and lower arm end at local Y -.440 m.
    /// <summary>The prop in someone's hand for what they're holding.</summary>
    private static string HeldAsset(ImmersionProduct product) => product switch
    {
        ImmersionProduct.Chips => "res://assets/props/lwf_chips_tray_v1.glb",
        ImmersionProduct.Pizza => "res://assets/props/lwf_pizza_plate_v1.glb",
        ImmersionProduct.Curry => "res://assets/props/lwf_curry_tray_v1.glb",
        ImmersionProduct.SoftDrink or ImmersionProduct.Water => "res://assets/props/lwf_soft_drink_cup_v1.glb",
        ImmersionProduct.Beer => "res://assets/props/lwf_beer_cup_v2.glb",
        _ => throw new ArgumentOutOfRangeException(nameof(product), product, "Unknown immersion product")
    };

    private void SetImmersionHeldVisual(EntityId id, Node3D body, ImmersionProduct? product,
        bool canHold, int intoxication, double delta, bool empty = false)
    {
        var path = product is { } held ? empty ? LitterAsset(held) : HeldAsset(held) : null;
        var identity = path + (empty ? ":empty" : "");
        // A kit may have become attached since the caller captured eligibility.
        canHold &= !_performerInstruments.ContainsKey(id);
        if (path is null || !canHold)
        {
            RemoveImmersionHeldVisual(id);
            return;
        }
        if (_immersionHeldVisuals.TryGetValue(id, out var existing) &&
            GodotObject.IsInstanceValid(existing) && existing.GetParent() == body &&
            _immersionHeldProducts[id] == identity)
        {
            Bodies.AnchorProp(body, existing, product!.Value);
            return;
        }
        RemoveImmersionHeldVisual(id);
        var prop = InstantiateAsset(path);
        if (empty) prop.SetMeta("EmptyWasteProp", true);
        prop.Name = "ImmersionHeldItem";
        prop.Position = product!.Value.IsFood()
            ? new Vector3(.36f, 1.005f, -.075f)
            : new Vector3(.405f, .94f, -.04f);
        body.AddChild(prop);
        Bodies.AnchorProp(body, prop, product!.Value);
        _immersionHeldVisuals.Add(id, prop);
        _immersionHeldProducts.Add(id, identity);
        // intoxication/delta are reserved for caller-owned cosmetic sway. This
        // helper never overwrites medical collapse, navigation or stage transforms.
    }

    private void RemoveImmersionHeldVisual(EntityId id)
    {
        if (_immersionHeldVisuals.Remove(id, out var visual) && GodotObject.IsInstanceValid(visual))
        {
            visual.Visible = false;
            visual.QueueFree();
        }
        _immersionHeldProducts.Remove(id);
    }

    private void ResetImmersionHeldVisuals()
    {
        foreach (var id in new List<EntityId>(_immersionHeldVisuals.Keys)) RemoveImmersionHeldVisual(id);
    }
}
