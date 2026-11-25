namespace ExosphereSimulation.Tests;

using System.Text.Json;
using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Persistence;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;

/// <summary>
/// Flies the Flight 15 single-burn catch from a state the game saved, with the same control
/// laws the game's EDL director runs: the terminal belly-flop glide
/// (<see cref="TowerCatchGlideGuidance"/>, <see cref="BellyFlopAttitudeReference"/>), the
/// ignition test, and <see cref="TowerCatchBurn"/>. Control runs through
/// <see cref="IPhysicsStepController"/> at the universe's deterministic control period, as
/// in the game, so the frame rate only changes how the time is sliced.
/// </summary>
internal sealed class Flight15CatchBench : IPhysicsStepController
{
    // The EDL director's belly-flop attitude gains on a catch return.
    private const double CatchAeroProportionalGain = 5.5;
    private const double CatchAeroDampingGain = 2.4;

    private readonly CelestialBody _earth;
    private readonly LaunchSite _site;
    private readonly Part _engines;
    private readonly TowerCatchGlideGuidance _glide = new();
    private readonly BellyFlopAttitudeReference _attitude = new();
    private readonly TowerCatchPhysicalGlide _physicalGlide = new();
    private Vector3d _liftReference;

    /// <summary>When set, the glide flies the physical Flight 14/15 belly-flop attitude law.</summary>
    public Flight14DescentDefinition? PhysicalDescent { get; set; }
    public double LastGlideAngleOfAttackDeg { get; private set; }
    public Quaterniond LastDescentReference => _physicalGlide.Reference;
    public TowerCatchPhysicalGlide PhysicalGlide => _physicalGlide;
    public Vector3d LastLiftReference { get; private set; }
    /// <summary>Replaces the glide guidance's lift reference (bench, flow-up, flow) for identification.</summary>
    public Func<Flight15CatchBench, Vector3d, Vector3d, Vector3d>? LiftOverride { get; set; }
    /// <summary>When true the bench never ignites; it keeps gliding.</summary>
    public bool GlideOnly { get; set; }
    public Universe Universe { get; }
    public Vessel Ship { get; }
    public TowerCatchBurnDefinition Definition { get; }
    public TowerCatchBurn? Burn { get; private set; }
    public double IgnitionTime { get; private set; } = double.NaN;
    public double IgnitionClearanceM { get; private set; } = double.NaN;
    public double IgnitionDescentMps { get; private set; } = double.NaN;
    public double IgnitionGlideTowardTowerMps { get; private set; } = double.NaN;
    public double IgnitionMissM { get; private set; } = double.NaN;
    public double IgnitionNoseTowardTower { get; private set; } = double.NaN;
    /// <summary>Optional per-control-step observer, called after the commands are set.</summary>
    public Action<Flight15CatchBench>? OnControlStep { get; set; }

    private Flight15CatchBench(Universe universe, Vessel ship, CelestialBody earth, LaunchSite site,
        TowerCatchBurnDefinition definition)
    {
        Universe = universe; Ship = ship; _earth = earth; _site = site; Definition = definition;
        _engines = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("ship_engines"));
        universe.PhysicsStepController = this;
    }

    /// <summary>Restores fixture <paramref name="capture"/> (SaveGameV2 plus its universe switches).</summary>
    public static Flight14DescentDefinition Flight15Descent() =>
        Flight14DescentDefinition.LoadFromJson(Path.Combine(DataDirectory(), "flight_profiles",
            "starship_flight15_descent_estimate.json"));

    public static Flight15CatchBench Restore(string capture, TowerCatchBurnDefinition? definition = null,
        Action<PartCatalog>? catalogEdit = null)
    {
        string data = DataDirectory();
        string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "flight15_catch");
        if (!Directory.Exists(fixtures))
            fixtures = Path.Combine(data, "..", "ExosphereSimulation.Tests", "Fixtures", "flight15_catch");
        var env = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, capture + ".env.json"))).RootElement;
        // The game flies a heliocentric Earth; the bench holds it at the origin and moves the
        // vessels into its frame, which leaves every body-relative quantity unchanged.
        var earth = CelestialBody.LoadFromJson(Path.Combine(data, "bodies/earth.json"));
        earth.Position = Vector3d.Zero; earth.Velocity = Vector3d.Zero; earth.OrbitalElements = null;
        var site = LaunchSite.LoadFromJson(Path.Combine(data, "launch_sites/starbase_pad2.json"));
        var universe = new Universe();
        universe.AddBody(earth);
        var save = SaveGameV2Json.DeserializeOrMigrate(File.ReadAllText(Path.Combine(fixtures, capture + ".save.json")));
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(data, "parts"));
        catalogEdit?.Invoke(catalog);
        SaveGameV2Codec.Restore(universe, save, catalog);
        universe.SchedulerBudgetEnabled = env.GetProperty("SchedulerBudgetEnabled").GetBoolean();
        universe.DeferredPhysicsCandidateEnabled = env.GetProperty("DeferredPhysicsCandidateEnabled").GetBoolean();
        universe.DistributedAerodynamicsEnabled = env.GetProperty("DistributedAerodynamicsEnabled").GetBoolean();
        universe.PhysicalBodyFlapsEnabled = env.GetProperty("PhysicalBodyFlapsEnabled").GetBoolean();
        universe.GravityGradientEnabled = env.GetProperty("GravityGradientEnabled").GetBoolean();
        universe.VariableMassTransportEnabled = env.GetProperty("VariableMassTransportEnabled").GetBoolean();
        universe.PropellantSloshEnabled = env.GetProperty("PropellantSloshEnabled").GetBoolean();
        var p = env.GetProperty("bodyPosition"); var v = env.GetProperty("bodyVelocity");
        var bodyPosition = new Vector3d(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble());
        var bodyVelocity = new Vector3d(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble());
        foreach (var vessel in universe.Vessels) { vessel.Position -= bodyPosition; vessel.Velocity -= bodyVelocity; }
        var ship = universe.ActiveVessel!;
        ship.ConfigureCatchContactsFromParts();
        ship.IsAttemptingTowerCatch = true;
        var bench = new Flight15CatchBench(universe, ship, earth, site,
            definition ?? TowerCatchBurnTests.Bench.Definition());
        bench.RefreshCatchTarget(universe.CurrentTime);
        return bench;
    }

    /// <summary>
    /// The Flight 15 catch chapter's 70 km entry state, built as <c>SimulationBridge</c> builds
    /// it (1.8 km/s, 120 m/s down, belly first with lift down), <paramref name="upstreamM"/>
    /// up the cradle's ground track and <paramref name="northM"/> across it. The vessel and
    /// switches come from the 4 km glide capture, whose mass is the 215 t chapter entry mass.
    /// </summary>
    public static Flight15CatchBench FromChapterEntry(double upstreamM, double northM,
        TowerCatchBurnDefinition? definition = null, Action<PartCatalog>? catalogEdit = null)
    {
        var bench = Restore("glide_4km", definition, catalogEdit);
        var ship = bench.Ship;
        var earth = bench._earth;
        var cradle = ship.CatchTargetPositionWorld - earth.Position;
        var cradleUp = cradle.Normalized;
        var cradleEast = earth.RotationAxis.Cross(cradleUp).Normalized;
        var ground = cradle - cradleEast * upstreamM + cradleUp.Cross(cradleEast) * northM;
        var up = ground.Normalized;
        var east = earth.RotationAxis.Cross(up).Normalized;
        var air = east * 1_800.0 - up * 120.0;
        ship.Position = earth.Position + up * (earth.Radius + 70_000.0);
        ship.Velocity = earth.Velocity + earth.GetSurfaceVelocity(ship.CatchTargetPositionWorld) + air;
        ship.Orientation = AerodynamicsModel.ComputeBellyFirstOrientation(
            AerodynamicsModel.ComputeLiftDownEntryAxis(up, air.Normalized), air.Normalized);
        ship.AngularVelocity = Vector3d.Zero;
        return bench;
    }

    /// <summary>Starts the burn now, as when the state was saved at ignition.</summary>
    public void IgniteNow() => Ignite(Universe.CurrentTime);

    /// <summary>Runs until caught, blocked or <paramref name="maximumS"/>, in frames of <paramref name="frameS"/>.</summary>
    public void Run(double maximumS, double frameS = 1.0 / 60.0)
    {
        double end = Universe.CurrentTime + maximumS;
        while (Universe.CurrentTime < end && !Ship.IsDestroyed
            && Burn?.Phase is not (TowerCatchBurnPhase.Caught or TowerCatchBurnPhase.Blocked))
            Universe.Tick(frameS);
    }

    public bool RequiresFixedCadence(Universe universe) => true;

    public void BeforePhysicsStep(Universe universe, double controlIntervalSeconds)
    {
        double time = universe.CurrentTime;
        RefreshCatchTarget(time);
        if (Burn == null)
        {
            var cradleUp = Ship.CatchTargetUpWorld.Normalized;
            double clearance = TowerCatchBurn.UprightPinClearanceM(Ship, time);
            double descent = -(Ship.Velocity - Ship.CatchTargetVelocityWorld).Dot(cradleUp);
            if (!GlideOnly && Definition.ShouldIgnite(clearance, descent)) Ignite(time);
        }
        if (Burn != null)
            Burn.Advance(universe, controlIntervalSeconds);
        else
            FlyGlide(time, controlIntervalSeconds);
        OnControlStep?.Invoke(this);
    }

    private void FlyGlide(double time, double interval)
    {
        var up = (Ship.Position - _earth.Position).Normalized;
        var surfVel = Ship.GetSurfaceVelocity(_earth);
        double altitude = _earth.GetAltitude(Ship.Position);
        double verticalSpeed = surfVel.Dot(up);
        var velDir = surfVel.Magnitude > 1e-3 ? surfVel.Normalized : -up;
        double gForce = Ship.GetProperAcceleration(_earth).Magnitude / EntryLoadTracker.StandardGravity;
        if (_liftReference.MagnitudeSquared < 1e-12)
        {
            // A save does not carry the director's previous lift reference; the lift the
            // attitude now flies is the axis across the airflow.
            var axis = Ship.Orientation.Rotate(Vector3d.Up).Normalized;
            var across = axis - velDir * axis.Dot(velDir);
            if (across.MagnitudeSquared > 1e-12) _liftReference = across.Normalized;
        }
        Ship.FlapCommandOverride = null;
        Ship.SASEnabled = false;
        _engines.SelectEngineCount(System.Math.Min(3, System.Math.Max(1, _engines.Definition.EngineCount)));
        Ship.Throttle = 0.0;
        var steering = _glide.Steer(Ship, _earth, time, Ship.GetCatchTargetPositionAt(time), up, velDir, surfVel,
            altitude, verticalSpeed, _earth.GetSurfaceGravity(), gForce, _liftReference, Definition);
        _liftReference = steering.LiftReference;
        var desired = _attitude.Update(Ship.Orientation, steering.AimAxis, velDir, interval);
        if (PhysicalDescent != null)
        {
            // The physical catch glide shared with the game's EDL director.
            var liftReference = LiftOverride?.Invoke(this, up, velDir) ?? steering.LiftReference;
            Ship.PilotRateEnvelopeEnabled = false;
            Ship.PitchYawRoll = _physicalGlide.Command(Ship, _earth, PhysicalDescent, Definition, liftReference,
                Ship.GetCatchTargetPositionAt(time), interval);
            LastGlideAngleOfAttackDeg = _physicalGlide.LastAngleOfAttackDegrees;
            LastLiftReference = _physicalGlide.LastLiftReference;
            return;
        }
        Ship.PitchYawRoll = AttitudeGuidance.ComputeCommand(Ship.Orientation, desired, Ship.AngularVelocity,
            proportionalGain: CatchAeroProportionalGain, dampingGain: CatchAeroDampingGain, allowRoll: true);
    }

    private void Ignite(double time)
    {
        Ship.BodyFlapAllocationAxisWeights = null;
        var up = Ship.CatchTargetUpWorld.Normalized;
        IgnitionTime = time;
        IgnitionClearanceM = TowerCatchBurn.UprightPinClearanceM(Ship, time);
        var relative = Ship.Velocity - Ship.CatchTargetVelocityWorld;
        IgnitionDescentMps = -relative.Dot(up);
        var offset = Ship.Position - Ship.GetCatchTargetPositionAt(time);
        offset -= up * offset.Dot(up);
        IgnitionMissM = offset.Magnitude;
        IgnitionGlideTowardTowerMps = offset.MagnitudeSquared > 1e-9 ? -relative.Dot(offset.Normalized) : 0.0;
        var axis = Ship.Orientation.Rotate(Vector3d.Up);
        var nose = axis - up * axis.Dot(up);
        IgnitionNoseTowardTower = nose.MagnitudeSquared > 1e-9 && offset.MagnitudeSquared > 1e-9
            ? -nose.Normalized.Dot(offset.Normalized) : 0.0;
        Burn = new TowerCatchBurn(Ship, _earth, Definition, _engines.EngineStates.First().EngineModelId);
    }

    private void RefreshCatchTarget(double time)
    {
        var spec = LaunchComplexSpec.StarbasePostDeluge;
        var cradle = spec.GetCatchCradlePosition(_site, _earth, time, Ship);
        Ship.CatchTargetPositionWorld = cradle;
        Ship.CatchTargetUpWorld = _site.GetLocalFrame(_earth, time).Up;
        Ship.CatchTargetVelocityWorld = _earth.Velocity + _earth.GetSurfaceVelocity(cradle);
        Ship.CatchTargetEpochSeconds = time;
        Ship.CatchTargetRails = spec.GetCatchArmRails(_site, _earth, time);
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data"))) directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
