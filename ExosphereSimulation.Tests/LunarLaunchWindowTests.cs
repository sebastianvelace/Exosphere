namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Navigation;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Apollo launch window: liftoff is held until a parking-orbit plane through the pad and the
/// Moon's arrival position needs an azimuth inside 72°–108°. Flying that azimuth with plane
/// steering lets TLI be planned from the flown orbit, with no scripted plane rotation.
/// </summary>
public sealed class LunarLaunchWindowTests(ITestOutputHelper output)
{
    [Fact]
    public void WindowPlaneContainsThePadAtLiftoffAndTheMoonAtArrival()
    {
        var (universe, earth, moon, site) = Load();
        var window = LunarLaunchWindow.FindNext(earth, moon.OrbitalElements!, site, universe.CurrentTime,
            Apollo8FlightProfile.LoiIgnitionSeconds, Apollo8FlightProfile.MinimumLaunchAzimuthDegrees,
            Apollo8FlightProfile.MaximumLaunchAzimuthDegrees);

        Assert.NotNull(window);
        var w = window.Value;
        Assert.InRange(w.LiftoffTime - universe.CurrentTime, 0.0, 86_400.0);
        Assert.InRange(w.AzimuthDegrees, 72.0, 108.0);
        var pad = (site.GetPosition(earth, w.LiftoffTime) - earth.Position).Normalized;
        var (arrival, _) = moon.OrbitalElements!.GetStateAtTime(
            w.LiftoffTime + Apollo8FlightProfile.LoiIgnitionSeconds, earth.GM);
        Assert.Equal(0.0, w.PlaneNormal.Dot(pad), 9);
        Assert.Equal(0.0, w.PlaneNormal.Dot(arrival.Normalized), 9);
    }

    [Fact]
    public void FlownWindowAscentGivesACoplanarTranslunarInjection()
    {
        var (universe, earth, moon, site) = Load();
        var window = LunarLaunchWindow.FindNext(earth, moon.OrbitalElements!, site, universe.CurrentTime,
            Apollo8FlightProfile.LoiIgnitionSeconds, Apollo8FlightProfile.MinimumLaunchAzimuthDegrees,
            Apollo8FlightProfile.MaximumLaunchAzimuthDegrees)!.Value;

        var flight = SaturnVAscentFlight.Fly(window.LiftoffTime, window.PlaneNormal);
        Assert.True(flight.Inserted);
        Assert.True(flight.SivbPropellantKg > 60_000.0, $"Only {flight.SivbPropellantKg / 1000:F1} t left for TLI.");

        var flown = flight.Orbit;
        var flownNormal = (flight.Vessel.Position - flight.Earth.Position)
            .Cross(flight.Vessel.Velocity - flight.Earth.Velocity).Normalized;
        double planeErrorDeg = System.Math.Acos(System.Math.Clamp(flownNormal.Dot(window.PlaneNormal), -1, 1))
            * MathUtils.RAD_TO_DEG;
        var plan = PlanFromFlownOrbit(flight, window.LiftoffTime);
        output.WriteLine($"window T+{(window.LiftoffTime - universe.CurrentTime) / 3600:F2} h az={window.AzimuthDegrees:F1} "
            + $"planeError={planeErrorDeg:F2} deg orbit={(flown.Periapsis - earth.Radius) / 1000:F0}x"
            + $"{(flown.Apoapsis - earth.Radius) / 1000:F0} km i={flown.Inclination * MathUtils.RAD_TO_DEG:F1} "
            + $"TLI dv={plan.InjectionDeltaVMag:F0} m/s");

        Assert.True(planeErrorDeg < 1.0, $"Flown plane is {planeErrorDeg:F2} deg off the window plane.");
        // Historical Apollo 8 TLI: ~3.05 km/s impulsive equivalent.
        Assert.InRange(plan.InjectionDeltaVMag, 2_900.0, 3_300.0);
    }

    private static LunarTransferPlan PlanFromFlownOrbit(SaturnVAscentResult flight, double liftoff)
    {
        var moon = flight.Universe.GetBody("moon")!;
        return LunarTransferPlanner.Compute(flight.Earth.GM, moon.GM, moon.Radius, moon.SphereOfInfluence,
            flight.Orbit, moon.OrbitalElements!, liftoff + Apollo8FlightProfile.TliIgnitionSeconds,
            Apollo8FlightProfile.LoiIgnitionSeconds - Apollo8FlightProfile.TliIgnitionSeconds,
            Apollo8FlightProfile.InitialLunarPeriluneAltitudeM, windowSamples: 60);
    }

    private static (Universe Universe, CelestialBody Earth, CelestialBody Moon, LaunchSite Site) Load()
    {
        var root = SaturnVAscentFlight.DataDirectory();
        var universe = Universe.LoadFromDataDirectory(root);
        var site = LaunchSite.LoadFromJson(Path.Combine(root, "launch_sites", "kennedy.json"));
        return (universe, universe.GetBody("earth")!, universe.GetBody("moon")!, site);
    }
}
