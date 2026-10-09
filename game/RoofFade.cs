using Godot;
using System.Collections.Generic;

namespace Festival.Game;

/// <summary>
/// The faded roof, the standard for every roofed build: the camera looks down at about 39°, so an opaque roof hides
/// whoever is under it. While anyone is inside, the roof drops to 30% over a quarter second; it comes back over half
/// a second, a second after the last one leaves. Only materials named *_Canvas or *_Roof fade, each duplicated for its
/// own instance; poles, ropes and signs stay solid.
/// </summary>
/// <remarks>
/// Godot casts no shadow from an alpha-blended surface, so each roof mesh gets a shadow-only twin and the shade on the
/// ground stays at full strength however faded the roof. The roof is blended only while it fades; fully solid, it
/// draws as an ordinary opaque surface, so the name sign above it sorts as on any other building.
/// </remarks>
internal sealed class RoofFade
{
    public const float FadedAlpha = .30f;
    private const double FadeSeconds = .25, SolidSeconds = .5, HoldSeconds = 1;
    private readonly Node3D _owner;
    private readonly List<StandardMaterial3D> _roofs = [];
    private Tween? _tween;
    private bool _faded;
    private double _emptySince = -1;

    private RoofFade(Node3D owner) => _owner = owner;

    public int RoofCount => _roofs.Count;
    public float Alpha => _roofs.Count == 0 ? 1 : _roofs[0].AlbedoColor.A;

    /// <summary>Gives every roof surface under <paramref name="root"/> its own fading material and a shadow-only twin.</summary>
    public static RoofFade Attach(Node3D root)
    {
        var fade = new RoofFade(root);
        foreach (var node in root.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is not MeshInstance3D mesh || mesh.Mesh is null) continue;
            var roofed = false;
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.GetActiveMaterial(surface) is not StandardMaterial3D material || !IsRoof(material.ResourceName)) continue;
                var own = (StandardMaterial3D)material.Duplicate();
                own.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                own.Transparency = BaseMaterial3D.TransparencyEnum.Disabled;
                mesh.SetSurfaceOverrideMaterial(surface, own);
                fade._roofs.Add(own);
                roofed = true;
            }
            if (!roofed) continue;
            mesh.GetParent().AddChild(new MeshInstance3D { Name = mesh.Name + "_Shadow", Mesh = mesh.Mesh, Transform = mesh.Transform,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly });
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        return fade;
    }

    private static bool IsRoof(string name) => name.EndsWith("_Canvas", System.StringComparison.Ordinal) || name.EndsWith("_Roof", System.StringComparison.Ordinal);

    /// <summary>Call every frame with whether anyone is under the roof (or the player wants to see in).</summary>
    public void Update(bool occupied, double nowSeconds)
    {
        if (_roofs.Count == 0) return;
        if (occupied)
        {
            _emptySince = -1;
            if (!_faded) { _faded = true; TweenTo(FadedAlpha, FadeSeconds); }
            return;
        }
        if (!_faded) return;
        if (_emptySince < 0) _emptySince = nowSeconds;
        if (nowSeconds - _emptySince < HoldSeconds) return;
        _faded = false;
        TweenTo(1, SolidSeconds);
    }

    private void TweenTo(float alpha, double seconds)
    {
        _tween?.Kill();
        foreach (var roof in _roofs) roof.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        if (!_owner.IsInsideTree()) { foreach (var roof in _roofs) roof.AlbedoColor = roof.AlbedoColor with { A = alpha }; Settle(alpha); return; }
        _tween = _owner.CreateTween().SetParallel();
        foreach (var roof in _roofs)
            _tween.TweenProperty(roof, "albedo_color:a", alpha, seconds);
        _tween.Chain().TweenCallback(Callable.From(() => Settle(alpha)));
    }

    /// <summary>Back to a plain opaque surface once fully solid.</summary>
    private void Settle(float alpha)
    {
        if (alpha < 1) return;
        foreach (var roof in _roofs) roof.Transparency = BaseMaterial3D.TransparencyEnum.Disabled;
    }
}
