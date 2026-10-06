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
    private Node3D InstantiateImmersionVendor(bool food)
    {
        if (!food) return InstantiateAsset("res://assets/environment/lwf_drinks_stall_prototype_v1.glb");
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
        var art = _session.FoodTrader.Art;
        if (ResourceLoader.Exists($"res://assets/environment/lwf_food_van_name_panel_{art}_v1.glb"))
        {
            var panel = InstantiateAsset($"res://assets/environment/lwf_food_van_name_panel_{art}_v1.glb");
            panel.Position = new Vector3(0, 0, 0.056f);
            fascia.AddChild(panel);
        }
        if (ResourceLoader.Exists($"res://assets/environment/lwf_food_van_palette_{art}_v1.png"))
            ApplyFoodVanLivery(assembly, $"res://assets/environment/lwf_food_van_palette_{art}_v1.png");
        var sign = _session.FoodTrader.Menu == "Pizza" ? "pizza" : "chips";
        if (ResourceLoader.Exists($"res://assets/environment/lwf_food_van_sign_{sign}_v1.glb"))
            assembly.AddChild(InstantiateAsset($"res://assets/environment/lwf_food_van_sign_{sign}_v1.glb"));
        return vendor;
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
    private void SetImmersionHeldVisual(EntityId id, Node3D body, string? product,
        bool canHold, int intoxication, double delta, bool empty = false)
    {
        var path = product switch
        {
            "chips" => "res://assets/props/lwf_chips_tray_v1.glb",
            "soft" => "res://assets/props/lwf_soft_drink_cup_v1.glb",
            "beer" => "res://assets/props/lwf_beer_cup_v2.glb",
            null => null,
            _ => throw new ArgumentOutOfRangeException(nameof(product), product, "Unknown immersion product")
        };
        if (empty && product is not null) path = LitterAsset(product == "chips" ? ImmersionProduct.Chips : product == "beer" ? ImmersionProduct.Beer : ImmersionProduct.SoftDrink);
        var identity = product + (empty ? ":empty" : "");
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
            Bodies.AnchorProp(body, existing, product!);
            return;
        }
        RemoveImmersionHeldVisual(id);
        var prop = InstantiateAsset(path);
        if (empty) prop.SetMeta("EmptyWasteProp", true);
        prop.Name = "ImmersionHeldItem";
        prop.Position = product == "chips"
            ? new Vector3(.36f, 1.005f, -.075f)
            : new Vector3(.405f, .94f, -.04f);
        body.AddChild(prop);
        Bodies.AnchorProp(body, prop, product!);
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
