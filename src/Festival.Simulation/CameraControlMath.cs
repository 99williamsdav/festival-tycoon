namespace Festival.Simulation;

/// <summary>Pure screen-to-ground mapping shared by the Godot camera and deterministic tests.</summary>
public static class CameraControlMath
{
    public static (double X, double Z) ScreenPanToWorld(int orientation, double screenX, double screenY)
    {
        var angle = Math.PI / 4 + (orientation & 3) * Math.PI / 2;
        return (Math.Cos(angle) * screenX + Math.Sin(angle) * screenY,
            -Math.Sin(angle) * screenX + Math.Cos(angle) * screenY);
    }
}
