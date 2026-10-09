using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;

namespace Festival.Game;

public partial class Main
{
    private readonly Dictionary<EntityId, Node3D> _immersionHeldVisuals = [];
    private readonly Dictionary<EntityId, string> _immersionHeldProducts = [];

    // Food-van module transforms preserve the approved chassis origin.
    // Blender (X,Y,Z) -> Godot (X,Z,-Y). Both vendor fronts are local +Z.
    /// <summary>A bar, or a food van in its trader's livery.</summary>
    private Node3D InstantiateImmersionVendor(FoodTrader? trader)
    {
        if (trader is null) return InstantiateAsset("res://assets/environment/lwf_drinks_stall_prototype_v1.glb");
        var vendor = new Node3D { Name = "ImmersionFoodVan" };
        // Centre the serving opening at the shared vendor anchor. Original module
        // origins, hinge rotations and relative placement stay within this child.
        var assembly = new Node3D { Name = "ApprovedFoodVanAssembly", Position = new Vector3(-2.15f, 0, 0) };
        vendor.AddChild(assembly);
        assembly.AddChild(InstantiateAsset("res://assets/environment/lwf_food_van_chassis_v1.glb"));
        // User requested removal of the overhead awning. The separate raised
        // serving flap is omitted; chassis aperture/counter and fascia remain.
        // Keep the archived flap asset unchanged for provenance.
        var fascia = InstantiateAsset("res://assets/environment/lwf_food_van_fascia_v1.glb");
        fascia.Position = new Vector3(2.15f, 2.49f, 1.18f);
        assembly.AddChild(fascia);
        // The trader's livery: their colours on the van, their name painted across the fascia, and a big picture sign
        // of what they sell. Each piece waits for its artwork.
        var art = trader.Art;
        if (ResourceLoader.Exists($"res://assets/environment/lwf_food_van_name_panel_{art}_v1.glb"))
        {
            var panel = InstantiateAsset($"res://assets/environment/lwf_food_van_name_panel_{art}_v1.glb");
            panel.Position = new Vector3(0, 0, 0.056f);
            fascia.AddChild(panel);
        }
        if (ResourceLoader.Exists($"res://assets/environment/lwf_food_van_palette_{art}_v1.png"))
            ApplyFoodVanLivery(assembly, $"res://assets/environment/lwf_food_van_palette_{art}_v1.png");
        var sign = trader.Product.ToString().ToLowerInvariant();
        if (ResourceLoader.Exists($"res://assets/environment/lwf_food_van_sign_{sign}_v1.glb"))
        {
            // On a pole from the middle of the roof.
            var board = InstantiateAsset($"res://assets/environment/lwf_food_van_sign_{sign}_v1.glb");
            board.Position = new Vector3(2.10f, 2.75f, 0);
            assembly.AddChild(board);
        }
        // A cooking trader's kitchen: roof vents that steam while they're busy, and foil trays on the counter.
        if (CookingArts.Contains(art))
        {
            var kitchen = InstantiateAsset("res://assets/environment/lwf_food_van_kitchen_dressing_v1.glb");
            assembly.AddChild(kitchen);
            foreach (var vent in new[] { "LWF_FoodVan_SteamVent_L", "LWF_FoodVan_SteamVent_R" })
                if (kitchen.FindChild(vent, true, false) is Node3D marker) marker.AddChild(VanSteam());
        }
        // The chalk menu out front, beside the queue rather than in it.
        if (ResourceLoader.Exists($"res://assets/environment/lwf_food_van_menu_board_{art}_v1.glb"))
        {
            var menu = InstantiateAsset($"res://assets/environment/lwf_food_van_menu_board_{art}_v1.glb");
            menu.Position = new Vector3(4.10f, 0, 2.30f);
            assembly.AddChild(menu);
        }
        return vendor;
    }

    /// <summary>Traders who cook on board, and so get the kitchen dressing.</summary>
    private static readonly string[] CookingArts = ["korma"];
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
        return new CpuParticles3D
        {
            Name = VanSteamName, Emitting = false, Amount = 8, Lifetime = 1.9, Randomness = .3f,
            Mesh = new QuadMesh { Size = new Vector2(.12f, .12f) }, MaterialOverride = _vanSteamMaterial,
            Direction = Vector3.Up, Spread = 10, InitialVelocityMin = .5f, InitialVelocityMax = .7f, Gravity = new Vector3(.08f, 0, 0),
            ScaleAmountCurve = grow,
            ColorRamp = new Gradient { Colors = [new Color(1, 1, 1, .45f), new Color(1, 1, 1, 0)], Offsets = [0, 1] },
        };
    }

    private readonly Dictionary<ulong, CpuParticles3D[]> _vanSteam = [];

    /// <summary>Each frame: steam from a van's vents while it's serving or has a queue, and not while paused.</summary>
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
            var busy = !_session.IsPaused && (vendor.Queue.Length > 0 || vendor.OwnerId is not null);
            foreach (var steam in vents) if (steam.Emitting != busy) steam.Emitting = busy;
        }
    }

    private readonly Dictionary<string, StandardMaterial3D> _foodVanLiveries = [];

    /// <summary>Swaps the van's palette for the trader's; wheels, counter and hatch keep their swatches.</summary>
    private void ApplyFoodVanLivery(Node3D van, string palette)
    {
        foreach (var mesh in van.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.Mesh.SurfaceGetMaterial(surface) is not StandardMaterial3D source ||
                    !source.ResourceName.StartsWith("LWF_FoodVan_MattePalette", StringComparison.Ordinal)) continue;
                if (!_foodVanLiveries.TryGetValue(palette, out var livery))
                {
                    livery = (StandardMaterial3D)source.Duplicate();
                    livery.AlbedoTexture = GD.Load<Texture2D>(palette);
                    livery.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
                    _foodVanLiveries.Add(palette, livery);
                }
                mesh.SetSurfaceOverrideMaterial(surface, livery);
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
