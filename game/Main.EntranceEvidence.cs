using Festival.Simulation;
using Festival.Simulation.Fixtures;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Festival.Game;

// Opt-in static diagnostic: actual gate/hedge meshes, authoritative cells and route, no player save.
public partial class Main
{
    private string? _entranceEvidenceOutput;
    private int _entranceEvidenceFrame;
    private Node3D? _entranceEvidenceOverlay;
    private void ProcessEntranceEvidence()
    {
        if (_entranceEvidenceOutput is null) return;
        var frame = _entranceEvidenceFrame++;
        if (frame == 0)
        {
            Directory.CreateDirectory(_entranceEvidenceOutput);
            _hudWorkspaceOpen = _buildDrawerOpen = false; Dock.Readiness?.Hide();
            _rig.Frame(new(-.5f, 0, 30), 6);
            _entranceEvidenceOverlay = new Node3D { Name = "EntranceTraversalProof", Visible = false }; AddChild(_entranceEvidenceOverlay);
            var cells = FarmGateGeometry.SolidCells().Concat(FarmGateGeometry.HedgeEndSolidCells()).Distinct().ToArray();
            var mesh = new BoxMesh { Size = new(.48f, .025f, .48f) };
            var material = new StandardMaterial3D { AlbedoColor = new(.85f, .15f, .12f, .55f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
            foreach (var cell in cells)
            {
                var at = TraversalGrid.CellCentre(cell);
                _entranceEvidenceOverlay.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material,
                    Position = new(at.XMillimetres / 1000f, .07f, at.ZMillimetres / 1000f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            }
            var grid = new TraversalGrid(NavigationFixture.CreateLowerWitteringTerrain());
            var route = DeterministicPathfinder.FindPath(grid, new(122, 190), TraversalGrid.WorldToCell(0, 25_000));
            if (!route.Found || route.Path.Any(cells.Contains)) throw new InvalidOperationException("Entrance route proof failed");
            var path = new ImmediateMesh(); path.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
            foreach (var cell in route.Path) { var at = TraversalGrid.CellCentre(cell); path.SurfaceAddVertex(new(at.XMillimetres / 1000f, .12f, at.ZMillimetres / 1000f)); }
            path.SurfaceEnd();
            _entranceEvidenceOverlay.AddChild(new MeshInstance3D { Mesh = path, MaterialOverride = new StandardMaterial3D {
                AlbedoColor = Colors.Cyan, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } });
            var canvas = new CanvasLayer { Layer = 90 }; AddChild(canvas);
            canvas.AddChild(new Label { Position = new(22, 75), Text = "ENTRANCE TRAVERSAL DIAGNOSTIC · Actual current meshes\nRed: authoritative solids. Cyan: unchanged guest endpoint to real opening. No staged purchase or player save." });
            File.WriteAllText(Path.Combine(_entranceEvidenceOutput, "geometry-map.json"), JsonSerializer.Serialize(new {
                gate = FarmGateGeometry.ObstaclePolygons(), hedgeEnds = FarmGateGeometry.HedgeEndPolygons(),
                leafYaw = FarmGateGeometry.LeafYawDegrees, bodyClearanceMm = FarmGateGeometry.PersonClearanceMillimetres,
                blockedCells = cells, route = route.Path, routeFound = route.Found }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Dock.Readiness?.Hide(); Perks.Panel?.Hide();
        if (frame == 4) GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_entranceEvidenceOutput, "actual-entrance.png"));
        if (frame == 5) _entranceEvidenceOverlay!.Show();
        if (frame == 9) GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_entranceEvidenceOutput, "entrance-blockers-and-route.png"));
        if (frame == 10) { GD.Print("ENTRANCE_EVIDENCE_COMPLETE"); GetTree().Quit(); }
    }
}
