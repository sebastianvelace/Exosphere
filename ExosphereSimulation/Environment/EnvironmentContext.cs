namespace Exosphere.Simulation.Environment;

/// <summary>
/// The environment a <see cref="Universe"/> flies in (environment plan E0.3): the ephemeris
/// shared by all bodies, and the per-body orientation and gravity providers it assigns. With
/// the defaults (Kepler ephemeris, fixed-axis rotation, zonal J2) the simulation is bit for
/// bit the one it has always been; dated providers (E1-E4) replace them one capability at a
/// time behind parity gates. Atmosphere (USSA-76 layers, <see cref="AtmosphereModel"/>) and
/// wind (none yet; the single air point is <see cref="CelestialBody.GetAirVelocity"/>) get
/// their provider contracts with the units that need them (E4), where the time argument they
/// take is defined.
/// </summary>
public sealed class EnvironmentContext
{
    public IEphemerisProvider Ephemeris { get; set; } = new KeplerEphemeris();

    /// <summary>
    /// One line naming every provider, for logs, saves and telemetry, e.g.
    /// "ephemeris=kepler; earth:orientation=fixed-axis,gravity=zonal-j2".
    /// </summary>
    public string Describe(IEnumerable<CelestialBody> bodies) =>
        $"ephemeris={Ephemeris.Id}; " + string.Join("; ", bodies.Select(b =>
            $"{b.Id}:orientation={b.Orientation.Id},gravity={b.Gravity.Id}"));
}
