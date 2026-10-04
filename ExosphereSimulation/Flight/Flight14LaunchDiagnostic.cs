namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;

/// <summary>
/// Shared continuous launch assembly. Defaults to an isolated-Earth diagnostic;
/// a supplied empty gameplay universe retains its bodies, ephemerides and epoch.
/// Only initial pad placement seeds state.
/// Includes an estimated payload manifest; not a dated replay or water-return mission.
/// </summary>
public sealed record Flight14LaunchDiagnostic(Universe Universe, CelestialBody Earth,
    Vessel Ship, Flight14LaunchController Controller, Flight14LaunchGuidanceDefinition Guidance,
    Flight14PayloadDefinition PayloadDefinition, Flight14PayloadDeploymentController PayloadController)
{
    private double _pendingFrameSeconds;
    public Flight14ReturnController? ReturnController { get; private set; }
    public Flight14DescentController? DescentController { get; private set; }
    public Flight14PoweredReturnController? PoweredReturnController { get; private set; }
    public Flight14BoosterReturnController? BoosterReturnController { get; private set; }

    /// <summary>Continue both original stages, retaining Starship as the physics owner.</summary>
    public static Flight14LaunchDiagnostic CreateWithBoosterReturn(string dataDirectory, Universe? universe = null)
    {
        var run = CreateWithPoweredReturn(dataDirectory, universe, continueToWaterContact: true);
        var definition = Flight14BoosterReturnDefinition.LoadFromJson(Path.Combine(dataDirectory,
            "flight_profiles/starship_flight14_booster_return_estimate.json"));
        var reference = Flight14MissionDefinition.LoadFromJson(Path.Combine(dataDirectory,
            "flight_profiles/starship_flight14_2026.json"));
        run.BoosterReturnController = new(run.Ship, run.Earth, run.Controller, definition, reference);
        run.Universe.PhysicsStepController = new Flight14IndependentReturnController(
            run.PoweredReturnController!, run.BoosterReturnController);
        return run;
    }

    /// <summary>Propagate the same loaded carrier through its physical flip and terminal burn.</summary>
    public static Flight14LaunchDiagnostic CreateWithPoweredReturn(string dataDirectory, Universe? universe = null, bool continueToWaterContact = false)
    {
        var run = CreateWithReturn(dataDirectory, universe);
        var landing = Flight14LandingDefinition.LoadFromJson(Path.Combine(dataDirectory,
            "flight_profiles/starship_flight14_landing_estimate.json"));
        landing.ContinueToWaterContact = continueToWaterContact;
        var descent = Flight14DescentDefinition.LoadFromJson(Path.Combine(dataDirectory,
            "flight_profiles/starship_flight14_descent_estimate.json"));
        // A control endpoint from the separate estimated profile, never a state assignment.
        descent.DiagnosticEndAltitudeM = landing.FlipAltitudeM;
        run.DescentController = new Flight14DescentController(run.Ship, run.Earth,
            run.ReturnController!, run.Controller, descent);
        run.PoweredReturnController = new Flight14PoweredReturnController(run.Ship, run.Earth,
            run.DescentController, run.Controller, landing, run.Guidance.SeaLevelEngineModelId);
        run.Universe.PhysicsStepController = run.PoweredReturnController;
        return run;
    }

    /// <summary>Continue the loaded return through a bounded aerodynamic-descent diagnostic.</summary>
    public static Flight14LaunchDiagnostic CreateWithDescent(string dataDirectory)
    {
        var run = CreateWithReturn(dataDirectory);
        var definition = Flight14DescentDefinition.LoadFromJson(Path.Combine(dataDirectory,
            "flight_profiles/starship_flight14_descent_estimate.json"));
        run.DescentController = new Flight14DescentController(run.Ship, run.Earth,
            run.ReturnController!, run.Controller, definition);
        run.Universe.PhysicsStepController = run.DescentController;
        return run;
    }

    /// <summary>Extend the same loaded pad fixture through deorbit and atmospheric entry.</summary>
    public static Flight14LaunchDiagnostic CreateWithReturn(string dataDirectory, Universe? universe = null)
    {
        var run = Create(dataDirectory, universe);
        var definition = Flight14ReturnDefinition.LoadFromJson(Path.Combine(dataDirectory,
            "flight_profiles/starship_flight14_return_estimate.json"));
        run.ReturnController = new Flight14ReturnController(run.Ship, run.Earth,
            run.Controller, run.PayloadController, definition, run.Guidance);
        run.Universe.PhysicsStepController = run.ReturnController;
        return run;
    }

    /// <summary>
    /// Diagnostic driver: commit whole 20 ms steps, retaining the frame remainder.
    /// This isolates legacy actuator integration from render-frame fragmentation.
    /// Gameplay and warp adapters must adopt an equivalent driver before promotion.
    /// </summary>
    public void AdvanceFrame(double frameSeconds)
    {
        if (!double.IsFinite(frameSeconds) || frameSeconds <= 0 || frameSeconds > 1)
            throw new ArgumentOutOfRangeException(nameof(frameSeconds));
        if (Universe.TimeScale != 1)
            throw new InvalidOperationException("The launch diagnostic driver requires real-time physics.");
        double pending = _pendingFrameSeconds+frameSeconds;
        if (!double.IsFinite(pending)) throw new ArgumentOutOfRangeException(nameof(frameSeconds));
        while (pending+1e-12 >= Universe.DeterministicControlPeriodSeconds)
        {
            Universe.Tick(Universe.DeterministicControlPeriodSeconds);
            pending -= Universe.DeterministicControlPeriodSeconds;
        }
        _pendingFrameSeconds = System.Math.Max(0, pending);
    }

    public static Flight14LaunchDiagnostic Create(string dataDirectory, Universe? universe = null)
    {
        var guidance = Flight14LaunchGuidanceDefinition.LoadFromJson(Path.Combine(
            dataDirectory, "flight_profiles/starship_flight14_guidance_estimate.json"));
        var reference = Flight14MissionDefinition.LoadFromJson(Path.Combine(
            dataDirectory, "flight_profiles/starship_flight14_2026.json"));
        CelestialBody earth;
        if (universe == null)
        {
            earth = CelestialBody.LoadFromJson(Path.Combine(dataDirectory, "bodies/earth.json"));
            earth.Position = Vector3d.Zero;
            earth.Velocity = Vector3d.Zero;
            earth.OrbitalElements = null; // Explicit isolated diagnostic, with no Sun parent.
            universe = new Universe();
            universe.AddBody(earth);
        }
        else
        {
            if (universe.Vessels.Count != 0 || universe.PhysicsStepController != null)
                throw new InvalidOperationException("Flight 14 requires an unowned, empty launch universe.");
            earth = universe.GetBody("earth")
                ?? throw new InvalidOperationException("Flight 14 requires Earth.");
        }
        var ship = VehicleVariantDefinition.LoadFromJson(Path.Combine(dataDirectory,
            "vehicles", guidance.BaselineVehicleFile)).Build(PartCatalog.LoadFromDirectory(
                Path.Combine(dataDirectory, "parts"))).ToVessel("Flight 14 engineering launch diagnostic");
        var payloadDefinition = Flight14PayloadDefinition.LoadFromJson(Path.Combine(dataDirectory,
            "flight_profiles/starship_flight14_payload_estimate.json"));
        if (payloadDefinition.Count != reference.PayloadCount)
            throw new InvalidDataException("Estimated manifest count differs from the source-backed reference.");
        var payloadIds = payloadDefinition.AttachTo(ship, dataDirectory);
        var site = LaunchSite.LoadFromJson(Path.Combine(dataDirectory,
            "launch_sites", reference.LaunchSiteId+".json"));
        var pad = site.GetPosition(earth, universe.CurrentTime);
        var up = earth.GetGeodeticUp(pad);
        double offset = LaunchComplexSpec.StarbasePostDeluge.VehicleInterfaceElevation;
        ship.Position = pad+up*offset;
        ship.Velocity = site.GetVelocity(earth);
        ship.Orientation = Quaterniond.FromTo(Vector3d.Up, up);
        ship.IsGroundHeld = true;
        ship.GroundNormal = (ship.Position-earth.Position).Normalized;
        ship.GroundOffset = site.Altitude+offset;
        ship.ReferenceBodyId = earth.Id;
        universe.AddVessel(ship);
        universe.SetActiveVessel(ship.Id);
        var controller = new Flight14LaunchController(ship, earth, guidance);
        var deployment = new Flight14PayloadDeploymentController(ship, earth, controller, payloadDefinition, payloadIds);
        universe.PhysicsStepController = deployment;
        return new(universe, earth, ship, controller, guidance, payloadDefinition, deployment);
    }
}
