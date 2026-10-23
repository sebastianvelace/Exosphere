namespace Exosphere.Simulation.Training;

using Exosphere.Simulation.Math;

/// <summary>
/// Places a drill's vehicles in their starting situation. States are set once, at the start,
/// on real conics about the real bodies; from then on the drill is ordinary flight.
/// </summary>
public static class TrainingSeeder
{
    public const double DockingStandoffM = 200.0;
    public const double TargetOrbitAltitudeM = 300_000.0;

    public static void Seed(TrainingDefinition training, Universe universe, Vessel player, Vessel? target = null)
    {
        ArgumentNullException.ThrowIfNull(training);
        ArgumentNullException.ThrowIfNull(universe);
        ArgumentNullException.ThrowIfNull(player);
        var earth = universe.GetBody("earth") ?? throw new InvalidOperationException("Training needs Earth.");
        var moon = universe.GetBody("moon") ?? throw new InvalidOperationException("Training needs the Moon.");
        double now = universe.CurrentTime;
        switch (training.Start)
        {
            case TrainingStart.EllipticalEarthOrbit:
            {
                // Apogee of a 200 x 1000 km orbit: one prograde burn here circularizes.
                double perigee = earth.Radius + 200_000.0, apogee = earth.Radius + 1_000_000.0;
                double speed = System.Math.Sqrt(earth.GM * (2.0 / apogee - 2.0 / (apogee + perigee)));
                Place(player, earth, Vector3d.Right * apogee, Vector3d.Forward * speed, Vector3d.Forward);
                break;
            }
            case TrainingStart.DockingApproach:
            {
                ArgumentNullException.ThrowIfNull(target);
                double radius = earth.Radius + TargetOrbitAltitudeM;
                double speed = System.Math.Sqrt(earth.GM / radius);
                Place(target, earth, Vector3d.Right * radius, Vector3d.Forward * speed, Vector3d.Forward);
                // Same orbit, a 200 m arc behind, nose on the target's docking face.
                double behind = DockingStandoffM / radius;
                var position = (Vector3d.Right * System.Math.Cos(behind) - Vector3d.Forward * System.Math.Sin(behind)) * radius;
                var velocity = (Vector3d.Forward * System.Math.Cos(behind) + Vector3d.Right * System.Math.Sin(behind)) * speed;
                Place(player, earth, position, velocity, (target.Position - earth.Position - position).Normalized);
                break;
            }
            case TrainingStart.LunarArrival:
            {
                // Apollo-like 0.9 km/s approach, 111 km perilune in three hours.
                double perilune = moon.Radius + 111_000.0;
                double vInfinity = 900.0;
                double periluneSpeed = System.Math.Sqrt(vInfinity * vInfinity + 2.0 * moon.GM / perilune);
                PlaceBefore(player, moon, perilune, periluneSpeed, now, 3.0 * 3_600.0);
                break;
            }
            case TrainingStart.LunarDescentOrbit:
            {
                double perilune = moon.Radius + 15_000.0, apolune = moon.Radius + 110_000.0;
                double semiMajorAxis = 0.5 * (perilune + apolune);
                double periluneSpeed = System.Math.Sqrt(moon.GM * (2.0 / perilune - 1.0 / semiMajorAxis));
                PlaceBefore(player, moon, perilune, periluneSpeed, now, 600.0);
                break;
            }
        }
    }

    /// <summary>On the conic through (perilune, speed) about <paramref name="body"/>, <paramref name="lead"/> s early.</summary>
    private static void PlaceBefore(Vessel vessel, CelestialBody body, double perilune, double speed, double now, double lead)
    {
        var conic = OrbitalElements.FromStateVector(Vector3d.Right * perilune, Vector3d.Forward * speed,
            body.GM, body.Id, now + lead);
        var (position, velocity) = conic.GetStateAtTime(now, body.GM);
        Place(vessel, body, position, velocity, -velocity.Normalized);
    }

    private static void Place(Vessel vessel, CelestialBody body, Vector3d relativePosition, Vector3d relativeVelocity,
        Vector3d noseDirection)
    {
        vessel.ReleaseGroundHold();
        vessel.Position = body.Position + relativePosition;
        vessel.Velocity = body.Velocity + relativeVelocity;
        vessel.Orientation = Quaterniond.FromTo(Vector3d.Up, noseDirection);
        vessel.AngularVelocity = Vector3d.Zero;
        vessel.ReferenceBodyId = body.Id;
        vessel.IsOnRails = false;
        vessel.OrbitalState = null;
        vessel.Throttle = 0.0;
        vessel.SASEnabled = true;
    }
}
