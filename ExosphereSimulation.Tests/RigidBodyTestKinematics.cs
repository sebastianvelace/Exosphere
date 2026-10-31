namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;

/// <summary>
/// Rigid-body bookkeeping for separation tests. <see cref="Vessel.Position"/> is the datum
/// (the stack's lowest point); the physical state is the centre of mass, which moves at
/// v_datum + ω×(q·c). Momentum and centre-of-mass conservation are checked on these.
/// </summary>
internal static class RigidBodyTestKinematics
{
    public static Vector3d CenterOfMassVelocity(Vessel vessel) =>
        vessel.Velocity + vessel.AngularVelocity.Cross(vessel.Orientation.Rotate(vessel.Parts.CenterOfMass));

    public static Vector3d Momentum(Vessel vessel) => CenterOfMassVelocity(vessel) * vessel.TotalMass;
}
