namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation.Math;

public partial class NavBallController : Node
{
    // Datos calculados cada frame — la UI los lee
    public Godot.Vector3 ProgradeWorld   { get; private set; }
    public Godot.Vector3 RetrogradeWorld { get; private set; }
    public Godot.Vector3 NormalWorld     { get; private set; }
    public Godot.Vector3 RadialOutWorld  { get; private set; }
    public Godot.Vector3 UpWorld         { get; private set; }  // alejándose del planeta

    // Heading, pitch y roll del vessel (grados)
    public double Heading { get; private set; }
    public double Pitch   { get; private set; }
    public double Roll    { get; private set; }

    // ¿Está la nave apuntando hacia prograde? (error en grados)
    public double ProgradeError { get; private set; }

    public override void _Process(double delta)
    {
        var bridge   = SimulationBridge.Instance;
        var vessel   = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (vessel == null || universe == null) return;

        var refBody = universe.GetDominantBody(vessel.Position);

        // ── Vectores orbitales ────────────────────────────────────────────

        // Velocidad relativa al cuerpo de referencia (orbital velocity)
        var orbitalVel = vessel.Velocity - refBody.Velocity;
        double orbSpeed = orbitalVel.Magnitude;

        // Prograde: dirección de la velocidad orbital
        var prograde = orbSpeed > 0.01 ? orbitalVel.Normalized : Vector3d.Up;

        // Normal: perpendicular al plano orbital (momento angular)
        var radialIn = (refBody.Position - vessel.Position).Normalized;
        var normal   = prograde.Cross(radialIn).Normalized;

        // Radial-out: alejándose del cuerpo
        var radialOut = -radialIn;

        // Up: dirección "arriba" del vessel en espacio world
        var upWorld = vessel.Orientation.Rotate(Vector3d.Up);

        ProgradeWorld   = ToV3(prograde);
        RetrogradeWorld = ToV3(-prograde);
        NormalWorld     = ToV3(normal);
        RadialOutWorld  = ToV3(radialOut);
        UpWorld         = ToV3(upWorld);

        // ── Orientación del vessel (heading, pitch, roll) ──────────────────
        // Usamos el quaternion de orientación del vessel
        var fwd   = vessel.Orientation.Rotate(new Vector3d(0, 0, -1));
        var right = vessel.Orientation.Rotate(new Vector3d(1, 0, 0));

        // Pitch: ángulo entre forward y el plano horizontal del cuerpo de referencia
        var radialDir = (vessel.Position - refBody.Position).Normalized;
        double pitchRad = System.Math.Asin(System.Math.Clamp(fwd.Dot(radialDir), -1.0, 1.0));
        Pitch = pitchRad * (180.0 / System.Math.PI);

        // Heading: ángulo norte medido en el plano horizontal
        // (simplificado para Phase 1)
        var northApprox = new Vector3d(0, 0, 1);  // aproximación
        var east        = radialDir.Cross(northApprox).Normalized;
        double hRad     = System.Math.Atan2(fwd.Dot(east), fwd.Dot(northApprox));
        Heading = ((hRad * 180.0 / System.Math.PI) + 360.0) % 360.0;

        // Error prograde (ángulo en grados entre nose y prograde)
        double dot = System.Math.Clamp(upWorld.Dot(prograde), -1.0, 1.0);
        ProgradeError = System.Math.Acos(dot) * (180.0 / System.Math.PI);

        // Roll: rotación alrededor del eje prograde
        var rightOnOrbitPlane = prograde.Cross(radialDir).Normalized;
        double rollRad = System.Math.Atan2(right.Dot(radialDir), right.Dot(rightOnOrbitPlane));
        Roll = rollRad * (180.0 / System.Math.PI);
    }

    private static Godot.Vector3 ToV3(Vector3d v) => new((float)v.X, (float)v.Y, (float)v.Z);
}
