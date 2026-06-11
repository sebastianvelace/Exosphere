namespace Exosphere.Game;

using Godot;

public partial class CameraController : Node3D
{
    // Orbit state
    private float _yaw      = 0f;   // horizontal angle (degrees)
    private float _pitch    = 15f;  // vertical angle (degrees), clamped
    private float _distance = 12f;  // zoom distance

    // Mouse sensitivity
    [Export] public float OrbitSensitivity { get; set; } = 0.3f;
    [Export] public float ZoomSensitivity  { get; set; } = 1.2f;
    [Export] public float MinDistance      { get; set; } = 3f;
    [Export] public float MaxDistance      { get; set; } = 800f;

    private bool _dragging = false;

    public override void _Input(InputEvent @event)
    {
        // Right mouse button drag = orbit
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right)
                _dragging = mb.Pressed;

            // Scroll wheel = zoom
            if (mb.ButtonIndex == MouseButton.WheelUp)
                _distance = Mathf.Clamp(_distance / ZoomSensitivity, MinDistance, MaxDistance);
            if (mb.ButtonIndex == MouseButton.WheelDown)
                _distance = Mathf.Clamp(_distance * ZoomSensitivity, MinDistance, MaxDistance);
        }

        if (@event is InputEventMouseMotion mm && _dragging)
        {
            _yaw   -= mm.Relative.X * OrbitSensitivity;
            _pitch -= mm.Relative.Y * OrbitSensitivity;
            _pitch  = Mathf.Clamp(_pitch, -89f, 89f);
        }
    }

    public override void _Process(double delta)
    {
        // Convert spherical coordinates to Cartesian
        float yawRad   = Mathf.DegToRad(_yaw);
        float pitchRad = Mathf.DegToRad(_pitch);

        var camPos = new Vector3(
            _distance * Mathf.Cos(pitchRad) * Mathf.Sin(yawRad),
            _distance * Mathf.Sin(pitchRad),
            _distance * Mathf.Cos(pitchRad) * Mathf.Cos(yawRad));

        // Find the Camera3D child and position it
        var camera = GetNodeOrNull<Camera3D>("Camera3D");
        if (camera == null) return;
        camera.Position = camPos;
        camera.LookAt(Vector3.Zero, Vector3.Up);
    }
}
