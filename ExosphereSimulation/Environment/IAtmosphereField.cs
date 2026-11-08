namespace Exosphere.Simulation.Environment;

using Exosphere.Simulation.Math;

/// <summary>
/// A position- and time-dependent atmosphere (environment plan E4.3) behind the single
/// atmosphere point (<see cref="CelestialBody.AtmosphericDensityAt"/> and its temperature and
/// pressure siblings). <paramref name="altitude"/> is the caller's geodetic altitude of the
/// position; time-dependent fields read <see cref="CelestialBody.StateTime"/>.
/// </summary>
public interface IAtmosphereField
{
    string Id { get; }

    double Density(CelestialBody body, Vector3d worldPosition, double altitude);

    double Temperature(CelestialBody body, Vector3d worldPosition, double altitude);

    double Pressure(CelestialBody body, Vector3d worldPosition, double altitude);
}
