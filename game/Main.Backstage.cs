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

    private void BuildBackstage()
    {
        var root = new Node3D { Name = "Backstage" };
        AddChild(root);
        foreach (var (x, z, yaw) in BackstageBarriers)
            Place(root, BarrierModel, x, z, yaw);
        foreach (var (model, x, z, yaw) in BackstageProps)
            Place(root, $"res://assets/environment/{model}.glb", x, z, yaw);

        static void Place(Node3D root, string path, float x, float z, float yaw)
        {
            var piece = InstantiateAsset(path);
            piece.Position = new Vector3(x, 0, z);
            piece.RotationDegrees = new Vector3(0, yaw, 0);
            root.AddChild(piece);
        }
    }
}
