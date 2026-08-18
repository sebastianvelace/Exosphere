namespace Exosphere.Simulation.Presentation;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Flight;

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
    public required int NominalEngineCount { get; init; }
    public required int ActiveEngineCount { get; init; }
    public required int FailedEngineCount { get; init; }
    public required string? PrimaryEngineFailureCode { get; init; }
    public required double LiquidFuelKg { get; init; }
    public required double LiquidFuelFraction { get; init; }
    public required double OxidizerKg { get; init; }
    public required double OxidizerFraction { get; init; }
    public required double? ApoapsisAltitudeM { get; init; }
    public required double? PeriapsisAltitudeM { get; init; }
    public required double TimeToPeriapsisS { get; init; }
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
    private readonly List<EngineReadout> _engineReadoutScratch = new(39);
    private readonly List<FlightAlertSnapshot> _alertScratch = new(6);

    private enum AlertValueFormat
    {
        G,
        KiloPascal,
        Percent,
    }

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
        double timeToPeriapsis = double.NaN;
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
            if (!elements.IsRadial && !elements.IsHyperbolic)
            {
                timeToPeriapsis = MissionPhaseTrack.ApproximateTimeToPeriapsisSec(
                    elements.SemiMajorAxis,
                    elements.Eccentricity,
                    elements.GetMeanAnomaly(universe.CurrentTime, body.GM),
                    body.GM);
            }
        }
        catch
        {
            // A stationary pad state has no useful osculating orbit.
        }

        bool impactTrajectory = periapsis is < 0.0;
        double liquidFuel = vessel.Parts.TotalLiquidFuel;
        double oxidizer = vessel.Parts.TotalOxidizer;
        double liquidCapacity = 0.0;
        double oxidizerCapacity = 0.0;
        for (int i = 0; i < vessel.Parts.Parts.Count; i++)
        {
            var part = vessel.Parts.Parts[i];
            liquidCapacity += part.Definition.FuelCapacityLF;
            oxidizerCapacity += part.Definition.FuelCapacityOx;
        }
        double liquidFraction = liquidCapacity > 0.0 ? liquidFuel / liquidCapacity : 0.0;
        double oxidizerFraction = oxidizerCapacity > 0.0 ? oxidizer / oxidizerCapacity : 0.0;
        vessel.FillEngineReadouts(body, _engineReadoutScratch);
        int nominalEngines = _engineReadoutScratch.Count;
        int activeEngines = 0;
        int failedEngines = 0;
        string? primaryEngineFailureCode = null;
        for (int i = 0; i < _engineReadoutScratch.Count; i++)
        {
            var readout = _engineReadoutScratch[i];
            if (readout.Throttle > 1e-3) activeEngines++;
            if (readout.FailureCode != null)
            {
                failedEngines++;
                primaryEngineFailureCode ??= readout.FailureCode;
            }
        }

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
            impactTrajectory,
            nominalEngines,
            failedEngines);

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
            NominalEngineCount = nominalEngines,
            ActiveEngineCount = activeEngines,
            FailedEngineCount = failedEngines,
            PrimaryEngineFailureCode = primaryEngineFailureCode,
            LiquidFuelKg = liquidFuel,
            LiquidFuelFraction = liquidFraction,
            OxidizerKg = oxidizer,
            OxidizerFraction = oxidizerFraction,
            ApoapsisAltitudeM = apoapsis,
            PeriapsisAltitudeM = periapsis,
            TimeToPeriapsisS = timeToPeriapsis,
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
        bool impactTrajectory,
        int nominalEngines,
        int failedEngines)
    {
        _alertScratch.Clear();

        AddThresholdAlert(
            _alertScratch, "LOAD-G", g, 4.0, 3.7, 6.0,
            "CREW LOAD", AlertValueFormat.G, "4.0 g",
            "Reduce thrust or adjust the flight profile");
        AddThresholdAlert(
            _alertScratch, "MAX-Q", dynamicPressure, 35_000.0, 30_000.0, 55_000.0,
            "DYNAMIC PRESSURE", AlertValueFormat.KiloPascal, "35.0 kPa",
            "Throttle down until aerodynamic load decreases");

        if (liquidCapacity > 0.0)
            AddLowAlert(
                _alertScratch, "FUEL-LOW", liquidFraction, 0.12, 0.15,
                "FUEL RESERVE", "12%",
                "Review burn plan and reserve margin");
        if (oxidizerCapacity > 0.0)
            AddLowAlert(
                _alertScratch, "OX-LOW", oxidizerFraction, 0.12, 0.15,
                "OXIDIZER RESERVE", "12%",
                "Review burn plan and reserve margin");

        bool activeFlight = !isGroundHeld
            && surfaceSpeed > 100.0
            && !IsTerminalPhase(phase);
        SetLatch("TRAJECTORY", activeFlight && impactTrajectory, !impactTrajectory);
        if (_latchedAlerts.Contains("TRAJECTORY"))
        {
            _alertScratch.Add(new FlightAlertSnapshot(
                "TRAJECTORY",
                FlightAlertSeverity.Critical,
                "IMPACT TRAJECTORY",
                "PERIAPSIS BELOW SURFACE",
                "PERIAPSIS 0 m",
                "Raise periapsis or prepare for entry",
                _acknowledgedAlerts.Contains("TRAJECTORY")));
        }

        SetLatch("ENGINE-OUT", failedEngines > 0, failedEngines == 0);
        if (_latchedAlerts.Contains("ENGINE-OUT"))
        {
            _alertScratch.Add(new FlightAlertSnapshot(
                "ENGINE-OUT",
                failedEngines >= System.Math.Max(2, nominalEngines / 3)
                    ? FlightAlertSeverity.Critical
                    : FlightAlertSeverity.Caution,
                "ENGINE OUT",
                $"{failedEngines} FAILED / {nominalEngines} INSTALLED",
                "0 FAILED",
                "Verify guidance authority and remaining performance",
                _acknowledgedAlerts.Contains("ENGINE-OUT")));
        }

        if (_alertScratch.Count == 0)
            return Array.Empty<FlightAlertSnapshot>();

        SortAlerts(_alertScratch);
        // A snapshot must remain stable after the next capture clears the scratch list.
        return _alertScratch.ToArray();
    }

    private void AddThresholdAlert(
        List<FlightAlertSnapshot> alerts,
        string code,
        double value,
        double trigger,
        double clear,
        double critical,
        string title,
        AlertValueFormat valueFormat,
        string limit,
        string action)
    {
        SetLatch(code, value >= trigger, value < clear);
        if (!_latchedAlerts.Contains(code)) return;
        alerts.Add(new FlightAlertSnapshot(
            code,
            value >= critical ? FlightAlertSeverity.Critical : FlightAlertSeverity.Caution,
            title,
            FormatAlertValue(value, valueFormat),
            limit,
            action,
            _acknowledgedAlerts.Contains(code)));
    }

    private void AddLowAlert(
        List<FlightAlertSnapshot> alerts,
        string code,
        double value,
        double trigger,
        double clear,
        string title,
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
            FormatAlertValue(value, AlertValueFormat.Percent),
            limit,
            action,
            _acknowledgedAlerts.Contains(code)));
    }

    private static string FormatAlertValue(double value, AlertValueFormat format) =>
        format switch
        {
            AlertValueFormat.G => $"{value:F1} g",
            AlertValueFormat.KiloPascal => $"{value / 1000.0:F1} kPa",
            AlertValueFormat.Percent => $"{value * 100.0:F0}%",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };

    private static void SortAlerts(List<FlightAlertSnapshot> alerts)
    {
        // The HUD has at most six alert types. Insertion sort avoids LINQ iterators and a
        // temporary ordering array while retaining the existing severity/code ordering.
        for (int i = 1; i < alerts.Count; i++)
        {
            var candidate = alerts[i];
            int j = i - 1;
            while (j >= 0 && CompareAlerts(alerts[j], candidate) > 0)
            {
                alerts[j + 1] = alerts[j];
                j--;
            }

            alerts[j + 1] = candidate;
        }
    }

    private static int CompareAlerts(
        FlightAlertSnapshot left,
        FlightAlertSnapshot right)
    {
        int severity = right.Severity.CompareTo(left.Severity);
        return severity != 0
            ? severity
            : string.CompareOrdinal(left.Code, right.Code);
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
