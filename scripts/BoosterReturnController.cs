namespace Exosphere.Game;

using Godot;
using System.Linq;
using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

/// <summary>
/// R12 — Super Heavy return after staging: boostback toward the pad, coast, dedicated
/// entry/landing burn (13 engines), then a 3-engine chopsticks catch. Reuses the Ship
/// Mechazilla cradle contact path. Does not own Ship EDL; Ship remains
/// <see cref="SimulationBridge.ActiveVessel"/>.
/// </summary>
[GlobalClass]
public partial class BoosterReturnController : Node
{
    public enum Phase
    {
        Idle,
        Boostback,
        Coast,
        EntryBurn,
        Catch,
        Caught,
        Done,
    }

    public static BoosterReturnController? Instance { get; private set; }

    public Phase CurrentPhase { get; private set; } = Phase.Idle;
    public Vessel? Booster { get; private set; }
    public string? StatusLine { get; private set; }

    /// <summary>
    /// |Δv| of the inertial velocity change across the boostback burn (m/s).
    /// Updated at boostback cutoff for HUD / telemetry.
    /// </summary>
    public double LastBoostbackDeltaVMps { get; private set; }

    private bool _wired;
    private Vector3d _boostbackStartVelocity;
    private const double CatchAbortMissM = 80.0;

    public override void _Ready()
    {
        Name = "BoosterReturnController";
        Instance = this;
        TryWire();
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Process(double delta)
    {
        if (!_wired) TryWire();
        var bridge = SimulationBridge.Instance;
        if (bridge?.Universe == null) return;

        if (Booster == null)
        {
            StatusLine = null;
            return;
        }

        if (Booster.IsDestroyed)
        {
            CurrentPhase = Phase.Done;
            StatusLine = "BOOSTER LOST";
            Booster = null;
            return;
        }

        var body = bridge.Universe.GetDominantBody(Booster.Position);
        double alt = Booster.GetAltitude(body);
        var surfVel = Booster.GetSurfaceVelocity(body);
        Vector3d up = (Booster.Position - body.Position).Normalized;
        double vHoriz = (surfVel - up * surfVel.Dot(up)).Magnitude;

        var pad = ResolvePadPosition(bridge, body);
        double outbound = BoosterReturnGuidance.OutboundHorizontalSpeedMps(
            surfVel, up, pad, Booster.Position);
        RefreshCatchCradle(bridge, Booster, body);

        switch (CurrentPhase)
        {
            case Phase.Boostback:
                DriveBoostback(Booster, body, surfVel, up, pad, vHoriz, outbound);
                break;
            case Phase.Coast:
                IdleBooster(Booster);
                StatusLine = $"BOOSTER COAST  out={outbound:F0} m/s  alt={alt / 1000.0:F0} km";
                if (BoosterReturnGuidance.ShouldArmEntryBurn(alt, Booster.HasCatchPins))
                {
                    bridge.ArmTowerCatchApproach(Booster);
                    CurrentPhase = Phase.EntryBurn;
                    StatusLine = "BOOSTER ENTRY BURN";
                    GD.Print($"[R12] entry burn armed alt={alt:F0} m");
                }
                break;
            case Phase.EntryBurn:
                if (Booster.IsCaught)
                {
                    FinishCaught();
                    break;
                }
                DriveLandingBurn(Booster, body, surfVel, up, pad, alt, vHoriz,
                    BoosterReturnGuidance.EntryBurnEngineCount, catchPhase: false);
                if (!BoosterReturnGuidance.ShouldContinueEntryBurn(alt))
                    CurrentPhase = Phase.Catch;
                break;
            case Phase.Catch:
                if (Booster.IsCaught)
                {
                    FinishCaught();
                    break;
                }
                DriveLandingBurn(Booster, body, surfVel, up, pad, alt, vHoriz,
                    BoosterReturnGuidance.CatchEngineCount, catchPhase: true);
                break;
            case Phase.Caught:
                IdleBooster(Booster);
                StatusLine = FormatCaughtStatus();
                break;
        }
    }

    private void TryWire()
    {
        var bridge = SimulationBridge.Instance;
        if (bridge == null) return;
        bridge.VesselStaged += OnVesselStaged;
        _wired = true;
    }

    private void OnVesselStaged(string detachedVesselId)
    {
        var bridge = SimulationBridge.Instance;
        if (bridge?.Universe == null) return;

        var debris = bridge.Universe.Vessels.FirstOrDefault(v => v.Id == detachedVesselId);
        if (debris == null || !BoosterReturnGuidance.IsStarshipBooster(debris))
            return;

        Booster = debris;
        debris.SASEnabled = true;
        debris.ConfigureCatchContactsFromParts();
        debris.ConfigureLandingContactsFromParts();

        var engines = BoosterReturnGuidance.FindBoosterEnginePart(debris);
        engines?.SelectEngineCount(BoosterReturnGuidance.BoostbackEngineCount);

        _boostbackStartVelocity = debris.Velocity;
        LastBoostbackDeltaVMps = 0.0;
        CurrentPhase = Phase.Boostback;
        StatusLine = "BOOSTER BOOSTBACK";
        GD.Print($"[R12] booster return armed on '{debris.Name}' ({debris.Id})");
    }

    private void DriveBoostback(
        Vessel booster, CelestialBody body, Vector3d surfVel, Vector3d up,
        Vector3d pad, double vHoriz, double outbound)
    {
        var engines = BoosterReturnGuidance.FindBoosterEnginePart(booster);
        double fuelFrac = engines != null
            ? BoosterReturnGuidance.RemainingFuelFraction(engines)
            : 0.0;

        if (!BoosterReturnGuidance.ShouldContinueBoostback(outbound, fuelFrac))
        {
            IdleBooster(booster);
            LastBoostbackDeltaVMps = (booster.Velocity - _boostbackStartVelocity).Magnitude;
            CurrentPhase = Phase.Coast;
            StatusLine =
                $"BOOSTER COAST  out={outbound:F0} m/s  Δv={LastBoostbackDeltaVMps:F0} m/s";
            GD.Print(
                $"[R12] boostback cutoff out={outbound:F0} m/s fuel={fuelFrac:P1} " +
                $"Δv={LastBoostbackDeltaVMps:F0} m/s");
            return;
        }

        Vector3d thrustDir = BoosterReturnGuidance.BoostbackThrustDirection(
            surfVel, up, pad, booster.Position);
        if (thrustDir.Magnitude < 1e-6)
        {
            IdleBooster(booster);
            LastBoostbackDeltaVMps = (booster.Velocity - _boostbackStartVelocity).Magnitude;
            CurrentPhase = Phase.Coast;
            return;
        }

        // Engines push along local +Y: point +Y along the desired thrust direction.
        AimThrustAxis(booster, thrustDir);
        engines?.SelectEngineCount(BoosterReturnGuidance.BoostbackEngineCount);
        booster.Throttle = 1.0;
        StatusLine =
            $"BOOSTER BOOSTBACK  out={outbound:F0} m/s  fuel={fuelFrac:P0}";
    }

    private void DriveLandingBurn(
        Vessel booster, CelestialBody body, Vector3d surfVel, Vector3d up,
        Vector3d pad, double alt, double vHoriz, int enginesLit, bool catchPhase)
    {
        Vector3d offset = booster.Position - booster.CatchTargetPositionWorld;
        Vector3d horizOff = offset - up * offset.Dot(up);
        double miss = horizOff.Magnitude;

        if (catchPhase && alt < 400.0 && miss > CatchAbortMissM)
        {
            booster.IsAttemptingTowerCatch = false;
            IdleBooster(booster);
            CurrentPhase = Phase.Done;
            StatusLine = $"BOOSTER CATCH ABORT  miss={miss:F0} m";
            GD.Print($"[R12] catch aborted miss={miss:F1} m alt={alt:F0} m");
            return;
        }

        // Point engines retrograde (landing burn): thrust opposes surface velocity.
        Vector3d burnDir = surfVel.Magnitude > 1.0 ? -surfVel.Normalized : up;
        // Cant slightly toward cradle to null residual miss (same idea as EDL Catch).
        if (miss > 1.0)
        {
            Vector3d toward = -horizOff.Normalized;
            burnDir = (burnDir + toward * 0.25).Normalized;
        }

        AimThrustAxis(booster, burnDir);

        var engines = BoosterReturnGuidance.FindBoosterEnginePart(booster);
        // Suicide-burn-ish throttle: more thrust when stop distance approaches altitude.
        double vDown = System.Math.Max(0.0, -surfVel.Dot(up));
        double twr = System.Math.Max(0.1, EstimateTwr(booster, body, engines));
        double aBrake = (twr - 1.0) * body.GetSurfaceGravity();
        double stopDist = aBrake > 0.5 ? (vDown * vDown) / (2.0 * aBrake) : 0.0;
        double throttle = alt < stopDist * 1.4 || alt < 800.0 ? 1.0 : 0.35;
        // Entry burn always holds meaningful thrust once armed — IFT lights 13 at ~2 km
        // while still fast; our arm is slightly higher so keep a floor.
        if (!catchPhase) throttle = System.Math.Max(throttle, 0.7);
        if (vHoriz > 40.0) throttle = System.Math.Max(throttle, 0.6);

        engines?.SelectEngineCount(enginesLit);
        booster.Throttle = throttle;
        StatusLine = catchPhase
            ? $"BOOSTER CATCH  miss={miss:F0} m  alt={alt:F0} m"
            : $"BOOSTER ENTRY  miss={miss:F0} m  alt={alt / 1000.0:F1} km";
    }

    private void FinishCaught()
    {
        if (Booster != null) IdleBooster(Booster);
        CurrentPhase = Phase.Caught;
        StatusLine = FormatCaughtStatus();
    }

    private string FormatCaughtStatus() =>
        LastBoostbackDeltaVMps > 1.0
            ? $"BOOSTER CAUGHT  boostback Δv={LastBoostbackDeltaVMps:F0} m/s"
            : "BOOSTER CAUGHT";

    private static void IdleBooster(Vessel booster)
    {
        booster.Throttle = 0.0;
        booster.PitchYawRoll = Vector3d.Zero;
        var engines = BoosterReturnGuidance.FindBoosterEnginePart(booster);
        engines?.SelectEngineCount(0);
    }

    private static void AimThrustAxis(Vessel booster, Vector3d desiredThrustWorld)
    {
        // Local +Y is the thrust axis. Never write Orientation at runtime — command
        // actuators through AttitudeGuidance like Ascent/EDL (RF-04).
        booster.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
            booster.Orientation,
            Vector3d.Up,
            desiredThrustWorld.Normalized,
            booster.AngularVelocity,
            proportionalGain: 2.2,
            dampingGain: 6.0);
    }

    private static double EstimateTwr(Vessel booster, CelestialBody body, Part? engines)
    {
        if (engines == null) return 0.0;
        double thrust = engines.GetThrustMagnitude(body.GetAtmosphericPressure(booster.Position));
        double weight = booster.TotalMass * body.GetSurfaceGravity();
        return weight > 1.0 ? thrust / weight : 0.0;
    }

    private static Vector3d ResolvePadPosition(SimulationBridge bridge, CelestialBody body)
    {
        // Prefer cradle (tower catch aim); fall back to site surface position.
        var site = bridge.LaunchSiteOrNull;
        if (site != null)
            return LaunchComplexSpec.StarbasePostDeluge.GetCatchCradlePosition(
                site, body, bridge.Universe.CurrentTime);
        return body.Position + Vector3d.Up * body.Radius;
    }

    private static void RefreshCatchCradle(
        SimulationBridge bridge, Vessel booster, CelestialBody body)
    {
        if (!booster.IsAttemptingTowerCatch) return;
        var site = bridge.LaunchSiteOrNull;
        if (site == null) return;
        var cradle = LaunchComplexSpec.StarbasePostDeluge.GetCatchCradlePosition(
            site, body, bridge.Universe.CurrentTime);
        var frame = site.GetLocalFrame(body, bridge.Universe.CurrentTime);
        booster.CatchTargetPositionWorld = cradle;
        booster.CatchTargetUpWorld = frame.Up;
        booster.CatchTargetVelocityWorld =
            body.Velocity + body.GetSurfaceVelocity(cradle);
    }
}
