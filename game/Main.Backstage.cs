using Godot;

namespace Festival.Game;

/// <summary>
/// Backstage's look: crowd barriers linked along the runs the simulation blocks (Backstage.BarrierRuns), and a few
/// flight cases, crates and chairs for the bands to stand about. Placements come from the designer's layout
/// (assets/source/environment/backstage-v1/out/backstage_layout_v1.json).
/// </summary>
public partial class Main
{
    private const string BarrierModel = "res://assets/environment/lwf_crowd_barrier_v1.glb";

    // World metres and yaw in degrees: the audience line, its angled end at the house, and the line behind the stage.
    private static readonly (float X, float Z, float Yaw)[] BackstageBarriers =
    [
        (-14f, 5.65f, 90f), (-14f, 3.25f, 90f), (-14f, 0.85f, 90f), (-14f, -1.55f, 90f), (-14f, -3.95f, 90f),
        (-14.937f, -5.866f, 144.61f),
        (-19.45f, 7.75f, 180f), (-21.85f, 7.75f, 180f), (-24.25f, 7.75f, 180f), (-26.65f, 7.75f, 180f), (-29.05f, 7.75f, 180f),
    ];

    private static readonly (string Model, float X, float Z, float Yaw)[] BackstageProps =
    [
        ("lwf_backstage_flight_case_stack_v1", -18.6f, 3.6f, 11.5f),
        ("lwf_backstage_flight_case_tall_v1", -19.5f, 2.2f, -23f),
        ("lwf_backstage_crate_pile_v1", -20.5f, -1.5f, 17f),
        ("lwf_folding_chair_v1", -18.5f, -2.6f, 34f),
        ("lwf_folding_chair_v1", -17.65f, -2.2f, -17f),
    ];

    private const string GardenGate = "res://assets/environment/lwf_garden_gate_iron_v1.glb";
    private Node3D? _gardenGateLeaf;
    private float _gardenGateOpen;

    private void BuildBackstage()
    {
        var root = new Node3D { Name = "Backstage" };
        AddChild(root);
        // The bands' way in: an iron garden gate cut into the west hedge, a lane beyond it and a flagstone path to the
        // farmhouse front door. Placements from assets/source/environment/hedge-gate-v1/out/hedge_gate_layout_v1.json.
        string Env(string name) => $"res://assets/environment/{name}.glb";
        foreach (var (name, x, z, yaw) in new (string, float, float, float)[]
                 {
                     ("lwf_hedge_gate_end_v1", -32, 0, 90), ("lwf_hedge_gate_end_v1", -32, -4, 270), ("lwf_hedge_straight_4m_a_v1", -32, -4, 90),
                 })
            RegisterBreezeHedge(PlaceBackstagePiece(root, Env(name), x, z, yaw));
        PlaceBackstagePiece(root, Env("lwf_hedge_gate_end_v1"), -31.45f, 7.75f, 0); // closes the gap between the barriers and the hedge
        var gate = PlaceBackstagePiece(root, GardenGate, -32, -2, 0);
        _gardenGateLeaf = gate.FindChild("LWF_GardenGate_Leaf", true, false) as Node3D;
        PlaceBackstagePiece(root, Env("lwf_garden_path_flagstones_v1"), -32, -2, 0);
        PlaceBackstagePiece(root, Env("lwf_hedge_gate_lane_v1"), -32, -2, 0);
        foreach (var (x, z, yaw) in new (float, float, float)[] { (-22.45f, -8.85f, 0), (-20.25f, -8.85f, 40), (-31.2f, -0.75f, 15) })
            PlaceBackstagePiece(root, Env("lwf_garden_pot_flowers_v1"), x, z, yaw);
        foreach (var (x, z, yaw) in BackstageBarriers)
            PlaceBackstagePiece(root, BarrierModel, x, z, yaw);
        foreach (var (model, x, z, yaw) in BackstageProps)
            PlaceBackstagePiece(root, $"res://assets/environment/{model}.glb", x, z, yaw);
    }

    private static Node3D PlaceBackstagePiece(Node3D root, string path, float x, float z, float yaw)
    {
        var piece = InstantiateAsset(path);
        piece.Position = new Vector3(x, 0, z);
        piece.RotationDegrees = new Vector3(0, yaw, 0);
        root.AddChild(piece);
        return piece;
    }

    /// <summary>The garden gate swings open into the field as someone comes through, and shuts again behind them.</summary>
    private void ProcessGardenGate(double delta)
    {
        if (_gardenGateLeaf is null) return;
        var opening = new Vector3(-32, 0, -2);
        var near = _attendeeVisuals.Values.Any(body => body.Visible && new Vector2(body.Position.X - opening.X, body.Position.Z - opening.Z).LengthSquared() < 2.2f * 2.2f);
        var rate = _session.IsPaused ? 0 : (float)delta * 2.5f;
        _gardenGateOpen = Mathf.MoveToward(_gardenGateOpen, near ? 1 : 0, rate);
        // Shut runs south to the latch (-90°); open lies along the path, into the field (0°).
        _gardenGateLeaf.RotationDegrees = new Vector3(0, -90 + 90 * Mathf.SmoothStep(0, 1, _gardenGateOpen), 0);
    }
}
