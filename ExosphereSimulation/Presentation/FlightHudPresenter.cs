namespace Exosphere.Simulation.Presentation;

using Exosphere.Simulation.Math;

public enum FlightHudViewMode
{
    Exterior,
    Cockpit,
    Map,
}

public enum FlightNavigationMode
{
    Surf,
    Orb,
    Tgt,
    Entry,
    Land,
}

public enum FlightAlertSeverity
{
    Advisory,
    Caution,
    Critical,
}

public sealed record FlightAlertSnapshot(
    string Code,
    FlightAlertSeverity Severity,
    string Title,
    string Value,
    string Limit,
    string RecommendedAction,
    bool Acknowledged);

public sealed record FlightHudSnapshot
{
    public required string VesselId { get; init; }
    public required string VesselName { get; init; }
    public required string ReferenceBodyId { get; init; }
    public required string MissionPhase { get; init; }
    public required FlightHudViewMode ViewMode { get; init; }
    public required FlightNavigationMode NavigationMode { get; init; }
    public required double MissionTimeS { get; init; }
    public required double TimeScale { get; init; }
    public required double AltitudeM { get; init; }
    public required double SurfaceSpeedMps { get; init; }
    public required double VerticalSpeedMps { get; init; }
    public required double ProperAccelerationG { get; init; }
    public required double DynamicPressurePa { get; init; }
    public required double FlightPathAngleDeg { get; init; }
    public required double HeadingDeg { get; init; }
    public required double VehiclePitchDeg { get; init; }
    public required double DownrangeM { get; init; }
    public required bool HasDownrangeReference { get; init; }
    public required double TotalMassKg { get; init; }
    public required double StageDeltaVMps { get; init; }
    public required double CurrentThrustN { get; init; }
    public required double ThrustToWeightRatio { get; init; }
    public required double Throttle { get; init; }
    public required bool IsGroundHeld { get; init; }
    public required double LiquidFuelKg { get; init; }
    public required double LiquidFuelFraction { get; init; }
    public required double OxidizerKg { get; init; }
    public required double OxidizerFraction { get; init; }
    public required double? ApoapsisAltitudeM { get; init; }
    public required double? PeriapsisAltitudeM { get; init; }
    public required bool IsImpactTrajectory { get; init; }
    public required IReadOnlyList<FlightAlertSnapshot> Alerts { get; init; }
}

/// <summary>
/// The single physics-to-UI boundary for flight instrumentation. Godot controls render this
/// immutable snapshot and never derive orbital, aerodynamic or propulsion values themselves.
/// </summary>
public sealed class FlightHudPresenter
{
    private const double StandardGravity = 9.80665;
    private readonly HashSet<string> _acknowledgedAlerts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _latchedAlerts = new(StringComparer.Ordinal);
    private Vector3d _launchSurfacePoint;
    private bool _launchCaptured;
    private double _smoothedG;
    private string? _activeVesselId;

    public FlightHudSnapshot Capture(
        Universe universe,
        Vessel vessel,
        string missionPhase,
        FlightHudViewMode viewMode,
        bool hasNavigationTarget = false)
    {
        ArgumentNullException.ThrowIfNull(universe);
        ArgumentNullException.ThrowIfNull(vessel);

        if (!string.Equals(_activeVesselId, vessel.Id, StringComparison.Ordinal))
            ResetForVessel(vessel.Id);

        var body = universe.GetDominantBody(vessel.Position);
        double altitude = vessel.GetAltitude(body);
        var surfaceVelocity = vessel.GetSurfaceVelocity(body);
        double surfaceSpeed = surfaceVelocity.Magnitude;
        var radial = vessel.Position - body.Position;
        var up = radial.MagnitudeSquared > 1e-12 ? radial.Normalized : Vector3d.Up;
        double verticalSpeed = surfaceVelocity.Dot(up);
        double gNow = vessel.GetProperAcceleration(body).Magnitude / StandardGravity;
        _smoothedG += (gNow - _smoothedG) * 0.2;
        double dynamicPressure = vessel.GetDynamicPressure(body);

        (double flightPathAngle, double heading) =
            ResolveVelocityAngles(surfaceVelocity, up, body.RotationAxis);
        double vehiclePitch = ResolveVehiclePitch(vessel, up);

        if (!_launchCaptured
            && IsLaunchPhase(missionPhase)
            && altitude > 30.0)
        {
            _launchSurfacePoint = body.ToBodyFixedDirection(up, universe.CurrentTime).Normalized;
            _launchCaptured = true;
        }

        double downrange = 0.0;
        if (_launchCaptured)
        {
            var current = body.ToBodyFixedDirection(up, universe.CurrentTime).Normalized;
            double cosine = System.Math.Clamp(current.Dot(_launchSurfacePoint), -1.0, 1.0);
            downrange = System.Math.Acos(cosine) * body.Radius;
        }

        double? apoapsis = null;
        double? periapsis = null;
        try
        {
            var elements = OrbitalElements.FromStateVector(
                vessel.Position - body.Position,
                vessel.Velocity - body.Velocity,
                body.GM,
                body.Id,
                universe.CurrentTime);
            apoapsis = elements.Apoapsis - body.Radius;
            periapsis = elements.Periapsis - body.Radius;
        }
        catch
        {
            // A stationary pad state has no useful osculating orbit.
        }

        bool impactTrajectory = periapsis is < 0.0;
        double liquidFuel = vessel.Parts.TotalLiquidFuel;
        double oxidizer = vessel.Parts.TotalOxidizer;
        double liquidCapacity = vessel.Parts.Parts.Sum(p => p.Definition.FuelCapacityLF);
        double oxidizerCapacity = vessel.Parts.Parts.Sum(p => p.Definition.FuelCapacityOx);
        double liquidFraction = liquidCapacity > 0.0 ? liquidFuel / liquidCapacity : 0.0;
        double oxidizerFraction = oxidizerCapacity > 0.0 ? oxidizer / oxidizerCapacity : 0.0;

        var alerts = BuildAlerts(
            missionPhase,
            vessel.IsGroundHeld,
            surfaceSpeed,
            _smoothedG,
            dynamicPressure,
            liquidCapacity,
            liquidFraction,
            oxidizerCapacity,
            oxidizerFraction,
            impactTrajectory);

        return new FlightHudSnapshot
        {
            VesselId = vessel.Id,
            VesselName = vessel.Name,
            ReferenceBodyId = body.Id,
            MissionPhase = missionPhase,
            ViewMode = viewMode,
            NavigationMode = ResolveNavigationMode(
                missionPhase, viewMode, hasNavigationTarget, impactTrajectory),
            MissionTimeS = universe.CurrentTime,
            TimeScale = universe.TimeScale,
            AltitudeM = altitude,
            SurfaceSpeedMps = surfaceSpeed,
            VerticalSpeedMps = verticalSpeed,
            ProperAccelerationG = _smoothedG,
            DynamicPressurePa = dynamicPressure,
            FlightPathAngleDeg = flightPathAngle,
            HeadingDeg = heading,
            VehiclePitchDeg = vehiclePitch,
            DownrangeM = downrange,
            HasDownrangeReference = _launchCaptured,
            TotalMassKg = vessel.TotalMass,
            StageDeltaVMps = vessel.GetCurrentStageDeltaV(body),
            CurrentThrustN = vessel.GetCurrentThrust(body),
            ThrustToWeightRatio = vessel.GetThrustToWeightRatio(body),
            Throttle = vessel.Throttle,
            IsGroundHeld = vessel.IsGroundHeld,
            LiquidFuelKg = liquidFuel,
            LiquidFuelFraction = liquidFraction,
            OxidizerKg = oxidizer,
            OxidizerFraction = oxidizerFraction,
            ApoapsisAltitudeM = apoapsis,
            PeriapsisAltitudeM = periapsis,
            IsImpactTrajectory = impactTrajectory,
            Alerts = alerts,
        };
    }

    public void AcknowledgeAlert(string code)
    {
        if (!string.IsNullOrWhiteSpace(code) && _latchedAlerts.Contains(code))
            _acknowledgedAlerts.Add(code);
    }

    private void ResetForVessel(string vesselId)
    {
        _activeVesselId = vesselId;
        _launchCaptured = false;
        _launchSurfacePoint = Vector3d.Zero;
        _smoothedG = 0.0;
        _acknowledgedAlerts.Clear();
        _latchedAlerts.Clear();
    }

    private IReadOnlyList<FlightAlertSnapshot> BuildAlerts(
        string phase,
        bool isGroundHeld,
        double surfaceSpeed,
        double g,
        double dynamicPressure,
        double liquidCapacity,
        double liquidFraction,
        double oxidizerCapacity,
        double oxidizerFraction,
        bool impactTrajectory)
    {
        var alerts = new List<FlightAlertSnapshot>();

        AddThresholdAlert(
            alerts, "LOAD-G", g, 4.0, 3.7, 6.0,
            "CREW LOAD", $"{g:F1} g", "4.0 g",
            "Reduce thrust or adjust the flight profile");
        AddThresholdAlert(
            alerts, "MAX-Q", dynamicPressure, 35_000.0, 30_000.0, 55_000.0,
            "DYNAMIC PRESSURE", $"{dynamicPressure / 1000.0:F1} kPa", "35.0 kPa",
            "Throttle down until aerodynamic load decreases");

        if (liquidCapacity > 0.0)
            AddLowAlert(
                alerts, "FUEL-LOW", liquidFraction, 0.12, 0.15,
                "FUEL RESERVE", $"{liquidFraction * 100.0:F0}%", "12%",
                "Review burn plan and reserve margin");
        if (oxidizerCapacity > 0.0)
            AddLowAlert(
                alerts, "OX-LOW", oxidizerFraction, 0.12, 0.15,
                "OXIDIZER RESERVE", $"{oxidizerFraction * 100.0:F0}%", "12%",
                "Review burn plan and reserve margin");

        bool activeFlight = !isGroundHeld
            && surfaceSpeed > 100.0
            && !IsTerminalPhase(phase);
        SetLatch("TRAJECTORY", activeFlight && impactTrajectory, !impactTrajectory);
        if (_latchedAlerts.Contains("TRAJECTORY"))
        {
            alerts.Add(new FlightAlertSnapshot(
                "TRAJECTORY",
                FlightAlertSeverity.Critical,
                "IMPACT TRAJECTORY",
                "PERIAPSIS BELOW SURFACE",
                "PERIAPSIS 0 m",
                "Raise periapsis or prepare for entry",
                _acknowledgedAlerts.Contains("TRAJECTORY")));
        }

        return alerts
            .OrderByDescending(a => a.Severity)
            .ThenBy(a => a.Code, StringComparer.Ordinal)
            .ToArray();
    }

    private void AddThresholdAlert(
        ICollection<FlightAlertSnapshot> alerts,
        string code,
        double value,
        double trigger,
        double clear,
        double critical,
        string title,
        string valueText,
        string limit,
        string action)
    {
        SetLatch(code, value >= trigger, value < clear);
        if (!_latchedAlerts.Contains(code)) return;
        alerts.Add(new FlightAlertSnapshot(
            code,
            value >= critical ? FlightAlertSeverity.Critical : FlightAlertSeverity.Caution,
            title,
            valueText,
            limit,
            action,
            _acknowledgedAlerts.Contains(code)));
    }

    private void AddLowAlert(
        ICollection<FlightAlertSnapshot> alerts,
        string code,
        double value,
        double trigger,
        double clear,
        string title,
        string valueText,
        string limit,
        string action)
    {
        SetLatch(code, value <= trigger, value > clear);
        if (!_latchedAlerts.Contains(code)) return;
        alerts.Add(new FlightAlertSnapshot(
            code,
            value <= trigger * 0.4
                ? FlightAlertSeverity.Critical
                : FlightAlertSeverity.Caution,
            title,
            valueText,
            limit,
            action,
            _acknowledgedAlerts.Contains(code)));
    }

    private void SetLatch(string code, bool trigger, bool clear)
    {
        if (trigger)
            _latchedAlerts.Add(code);
        else if (clear)
        {
            _latchedAlerts.Remove(code);
            _acknowledgedAlerts.Remove(code);
        }
    }

    private static (double flightPathAngle, double heading) ResolveVelocityAngles(
        Vector3d surfaceVelocity,
        Vector3d up,
        Vector3d spinAxis)
    {
        if (surfaceVelocity.Magnitude <= 0.5) return (0.0, 0.0);

        var direction = surfaceVelocity.Normalized;
        double flightPath = System.Math.Asin(
            System.Math.Clamp(direction.Dot(up), -1.0, 1.0)) * 180.0 / System.Math.PI;
        var north = spinAxis - up * spinAxis.Dot(up);
        if (north.MagnitudeSquared <= 1e-9) return (flightPath, 0.0);

        north = north.Normalized;
        var east = north.Cross(up).Normalized;
        var horizontal = direction - up * direction.Dot(up);
        if (horizontal.MagnitudeSquared <= 1e-9) return (flightPath, 0.0);

        horizontal = horizontal.Normalized;
        double heading = (
            System.Math.Atan2(horizontal.Dot(east), horizontal.Dot(north))
            * 180.0 / System.Math.PI + 360.0) % 360.0;
        return (flightPath, heading);
    }

    private static double ResolveVehiclePitch(Vessel vessel, Vector3d up)
    {
        var nose = vessel.Orientation.Rotate(Vector3d.Up);
        return System.Math.Asin(System.Math.Clamp(nose.Dot(up), -1.0, 1.0))
            * 180.0 / System.Math.PI;
    }

    private static FlightNavigationMode ResolveNavigationMode(
        string phase,
        FlightHudViewMode viewMode,
        bool hasTarget,
        bool impactTrajectory)
    {
        if (viewMode == FlightHudViewMode.Map)
            return hasTarget ? FlightNavigationMode.Tgt : FlightNavigationMode.Orb;
        if (phase is "ENTRY" or "PEAK_HEATING" or "AERO_DESCENT")
            return FlightNavigationMode.Entry;
        if (phase is "RETRO_BURN" or "FINAL_DESCENT" or "LANDED")
            return FlightNavigationMode.Land;
        if (!impactTrajectory && phase is ("ORBIT" or "COAST"))
            return FlightNavigationMode.Orb;
        return FlightNavigationMode.Surf;
    }

    private static bool IsLaunchPhase(string phase) =>
        phase is "LIFTOFF" or "ASCENT_SH" or "MAX_Q" or "MECO"
            or "SEPARATION" or "ASCENT_SHIP";

    private static bool IsTerminalPhase(string phase) =>
        phase is "PRE_LAUNCH" or "COUNTDOWN" or "IGNITION" or "LANDED" or "CRASHED";
}
