using Festival.Simulation;
using Godot;

namespace Festival.Game;

/// <summary>
/// The isometric orthographic camera: four 90-degree views around a focus point, wheel zoom by
/// orthographic size, and WASD/arrow or middle-drag panning in screen axes, clamped to the site.
/// </summary>
internal sealed class CameraRig
{
    private const float MinZoom = 18f;
    internal const float MaxZoom = 82f;
    internal const float PanLimit = 24f;
    private static readonly string[] OrientationNames = ["South", "West", "North", "East"];
    private Vector3 _focus = Vector3.Zero;
    private int _orientation;
    private bool _middleDragging;

    public CameraRig(Node parent)
    {
        Camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 62, Current = true };
        parent.AddChild(Camera);
    }

    public Camera3D Camera { get; }
    public Vector3 Focus => _focus;
    public string OrientationName => OrientationNames[_orientation];

    /// <summary>Centres the view on a point at the given orthographic size.</summary>
    public void Frame(Vector3 focus, float size) { _focus = focus; Camera.Size = size; Apply(); }
    public void FocusOn(Vector3 point) { _focus = point; Apply(); }
    public void Rotate(int step) { _orientation = (_orientation + step + 4) % 4; Apply(); }
    public void Zoom(float amount) { Camera.Size = Mathf.Clamp(Camera.Size + amount, MinZoom, MaxZoom); }

    /// <summary>Pans in screen axes: amount.X is screen-right and amount.Y is screen-down.</summary>
    public void Pan(Vector2 amount)
    {
        var world = CameraControlMath.ScreenPanToWorld(_orientation, amount.X, amount.Y);
        _focus += new Vector3((float)world.X, 0, (float)world.Z);
        _focus.X = Mathf.Clamp(_focus.X, -PanLimit, PanLimit); _focus.Z = Mathf.Clamp(_focus.Z, -PanLimit, PanLimit); Apply();
    }

    /// <summary>Keyboard panning, once per frame.</summary>
    public void Process(double delta)
    {
        if (_middleDragging && !Input.IsMouseButtonPressed(MouseButton.Middle)) _middleDragging = false;
        var input = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) input.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) input.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) input.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) input.X += 1;
        if (input.LengthSquared() > 0) Pan(input.Normalized() * (float)delta * 18f);
    }

    /// <summary>Sees a middle-button release before a HUD control can consume it.</summary>
    public void ObserveRelease(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Middle, Pressed: false })
            _middleDragging = false;
    }

    /// <summary>Wheel zoom and middle-drag start/stop; true when the button belongs to the camera.</summary>
    public bool HandleButton(InputEventMouseButton mouse)
    {
        if (mouse.ButtonIndex == MouseButton.WheelUp) { if (mouse.Pressed) Zoom(-4); return true; }
        if (mouse.ButtonIndex == MouseButton.WheelDown) { if (mouse.Pressed) Zoom(4); return true; }
        if (mouse.ButtonIndex == MouseButton.Middle) { _middleDragging = mouse.Pressed; return true; }
        return false;
    }

    public void HandleMotion(InputEventMouseMotion motion)
    {
        if (_middleDragging) Pan(motion.Relative * 0.055f);
    }

    private void Apply()
    {
        var yaw = Mathf.DegToRad(45 + _orientation * 90);
        Camera.Position = _focus + new Vector3(Mathf.Sin(yaw) * 72, 58, Mathf.Cos(yaw) * 72);
        Camera.LookAt(_focus, Vector3.Up);
    }
}
