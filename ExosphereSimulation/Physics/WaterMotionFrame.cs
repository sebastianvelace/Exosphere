namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

/// <summary>Conversion between the rendered skirt datum and a mass-centred rigid body.</summary>
public static class WaterMotionFrame
{
    public static RigidBodyMassProperties ResolveMassProperties(PartGraph parts, WaterContactDefinition water)
    {
        var properties = parts.GetMassProperties();
        var center = water.SkirtDatumYM is { } skirtY
            ? properties.CenterOfMassBody-Vector3d.Up*skirtY
            : Vector3d.Up*water.CenterOfMassYM;
        return new(properties.Mass, center, properties.InertiaBody);
    }

    public static RigidBody6DofState FromSkirt(Vector3d position, Vector3d velocity,
        Quaterniond orientation, Vector3d angularVelocityWorld, Vector3d centerOfMassFromSkirtBody)
    {
        var offset = orientation.Rotate(centerOfMassFromSkirtBody);
        return new(position+offset, velocity+angularVelocityWorld.Cross(offset), orientation,
            orientation.Inverse().Rotate(angularVelocityWorld));
    }

    public static (Vector3d Position, Vector3d Velocity) ToSkirt(in RigidBody6DofState state,
        Vector3d centerOfMassFromSkirtBody)
    {
        var offset = state.Orientation.Rotate(centerOfMassFromSkirtBody);
        return (state.Position-offset, state.Velocity-state.AngularVelocityWorld.Cross(offset));
    }
}
