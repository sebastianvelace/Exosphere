namespace Exosphere.Simulation.Flight;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Profile of the single landing burn that ends in a tower catch. The burn flips the ship
/// out of the belly flop on three Raptors, brakes on a constant-deceleration segment, and
/// eases onto the pins on a gentler terminal segment. The heights are pin clearance above
/// the cradle, not altitude above the ground.
/// </summary>
public sealed class TowerCatchBurnDefinition
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("assumptions")] public string Assumptions { get; set; } = "";
    /// <summary>Ground distance short of the tower to ignite with no horizontal glide (m).</summary>
    [JsonPropertyName("ignitionLeadM")] public double IgnitionLeadM { get; set; }
    /// <summary>Extra lead per m/s of horizontal glide toward the tower at ignition (s).</summary>
    [JsonPropertyName("ignitionLeadPerGlideSeconds")] public double IgnitionLeadPerGlideSeconds { get; set; }
    /// <summary>Glide speed above which the lead stops growing (m/s); faster flight is still far out.</summary>
    [JsonPropertyName("ignitionLeadMaximumGlideMps")] public double IgnitionLeadMaximumGlideMps { get; set; }
    /// <summary>Altitude below which the belly flop steers its glide toward the ignition point (m).</summary>
    [JsonPropertyName("terminalGlideAltitudeM")] public double TerminalGlideAltitudeM { get; set; }
    /// <summary>Largest glide speed the steering asks for (m/s).</summary>
    [JsonPropertyName("glideMaximumClosingSpeedMps")] public double GlideMaximumClosingSpeedMps { get; set; }
    /// <summary>Closing speed per metre to the ignition point the glide aims for (1/s).</summary>
    [JsonPropertyName("glideClosingGainPerSecond")] public double GlideClosingGainPerSecond { get; set; }
    /// <summary>Turn rate the glide predictor plans with; below the measured ~0.1 rad/s (rad/s).</summary>
    [JsonPropertyName("glidePlanningTurnRateRadPerS")] public double GlidePlanningTurnRateRadPerS { get; set; }
    /// <summary>Horizontal acceleration the belly-flop lift gives when turned fully sideways (m/s²).</summary>
    [JsonPropertyName("glideLiftAccelerationMps2")] public double GlideLiftAccelerationMps2 { get; set; }
    /// <summary>Gain from glide velocity error to commanded horizontal acceleration (1/s).</summary>
    [JsonPropertyName("glideVelocityGainPerSecond")] public double GlideVelocityGainPerSecond { get; set; }
    /// <summary>Time the flip and spool take before the braking profile holds (s).</summary>
    [JsonPropertyName("flipLeadSeconds")] public double FlipLeadSeconds { get; set; }
    [JsonPropertyName("brakingDecelerationMps2")] public double BrakingDecelerationMps2 { get; set; }
    [JsonPropertyName("terminalProfileClearanceM")] public double TerminalProfileClearanceM { get; set; }
    [JsonPropertyName("terminalDecelerationMps2")] public double TerminalDecelerationMps2 { get; set; }
    /// <summary>Descent speed the profile reaches at pin contact (m/s).</summary>
    [JsonPropertyName("contactDescentSpeedMps")] public double ContactDescentSpeedMps { get; set; }
    /// <summary>Pin clearance the burn holds until the ship is centred over the cradle (m).</summary>
    [JsonPropertyName("alignmentHoldClearanceM")] public double AlignmentHoldClearanceM { get; set; }
    /// <summary>Horizontal miss below which the ship may descend past the hold clearance (m).</summary>
    [JsonPropertyName("alignedMissM")] public double AlignedMissM { get; set; }
    /// <summary>Lowest pin clearance per metre of horizontal miss while translating to the arms.</summary>
    [JsonPropertyName("approachSlope")] public double ApproachSlope { get; set; }
    /// <summary>Gain from clearance error to descent speed while holding (1/s).</summary>
    [JsonPropertyName("holdClearanceGainPerSecond")] public double HoldClearanceGainPerSecond { get; set; }
    /// <summary>Tilt the flip may aim past vertical; the rotation carries on past its target (deg).</summary>
    [JsonPropertyName("flipMaximumTiltDegrees")] public double FlipMaximumTiltDegrees { get; set; }
    [JsonPropertyName("maximumTiltDegrees")] public double MaximumTiltDegrees { get; set; }
    /// <summary>Closing speed commanded per metre of horizontal miss (1/s).</summary>
    [JsonPropertyName("lateralClosingGainPerSecond")] public double LateralClosingGainPerSecond { get; set; }
    /// <summary>Deceleration the commanded closing speed assumes on the way in (m/s²).</summary>
    [JsonPropertyName("closingDecelerationMps2")] public double ClosingDecelerationMps2 { get; set; }
    /// <summary>Upper bound on the commanded closing speed toward the cradle (m/s).</summary>
    [JsonPropertyName("maximumClosingSpeedMps")] public double MaximumClosingSpeedMps { get; set; }
    /// <summary>Gain from horizontal velocity error to horizontal acceleration (1/s).</summary>
    [JsonPropertyName("lateralDampingPerSecond")] public double LateralDampingPerSecond { get; set; }
    /// <summary>Integral gain on the horizontal velocity error inside <see cref="LateralIntegralRadiusM"/> (1/s²).</summary>
    [JsonPropertyName("lateralIntegralGainPerS2")] public double LateralIntegralGainPerS3 { get; set; }
    /// <summary>Miss below which the integral term accumulates (m).</summary>
    [JsonPropertyName("lateralIntegralRadiusM")] public double LateralIntegralRadiusM { get; set; }
    /// <summary>Largest horizontal acceleration the integral term may command (m/s²).</summary>
    [JsonPropertyName("lateralIntegralLimitMps2")] public double LateralIntegralLimitMps2 { get; set; }
    /// <summary>
    /// Share of the remaining engines' rated thrust that must still arrest the current descent
    /// within the remaining clearance before one more engine may shut down.
    /// </summary>
    [JsonPropertyName("arrestThrottleHeadroom")] public double ArrestThrottleHeadroom { get; set; }
    [JsonPropertyName("verticalDampingPerSecond")] public double VerticalDampingPerSecond { get; set; }
    [JsonPropertyName("proportionalAttitudeGain")] public double ProportionalAttitudeGain { get; set; }
    [JsonPropertyName("attitudeDampingGain")] public double AttitudeDampingGain { get; set; }
    [JsonPropertyName("rollDampingGain")] public double RollDampingGain { get; set; }
    /// <summary>Roll command per radian of pin misalignment with the arms.</summary>
    [JsonPropertyName("rollAlignmentGain")] public double RollAlignmentGain { get; set; }
    /// <summary>Tilt from vertical above which the fittings are not rolled toward the arms (deg).</summary>
    [JsonPropertyName("rollAlignmentMaximumTiltDegrees")] public double RollAlignmentMaximumTiltDegrees { get; set; }
    /// <summary>Pin clearance below which the fittings are rolled toward the arms (m).</summary>
    [JsonPropertyName("rollAlignmentClearanceM")] public double RollAlignmentClearanceM { get; set; }
    /// <summary>Largest roll command the alignment may add.</summary>
    [JsonPropertyName("rollAlignmentCommandLimit")] public double RollAlignmentCommandLimit { get; set; }
    [JsonPropertyName("flipAttitudeDampingGain")] public double FlipAttitudeDampingGain { get; set; }
    /// <summary>Horizontal speed above which the second engine stays lit (m/s).</summary>
    [JsonPropertyName("singleEngineMaximumLateralSpeedMps")] public double SingleEngineMaximumLateralSpeedMps { get; set; }
    /// <summary>Horizontal miss above which the second engine stays lit (m).</summary>
    [JsonPropertyName("singleEngineMaximumMissM")] public double SingleEngineMaximumMissM { get; set; }
    [JsonPropertyName("throttleHeadroom")] public double ThrottleHeadroom { get; set; }
    [JsonPropertyName("controlAuthorityMargin")] public double ControlAuthorityMargin { get; set; }
    /// <summary>Pin clearance below which a ship still far from the tower abandons the catch (m).</summary>
    [JsonPropertyName("missedApproachClearanceM")] public double MissedApproachClearanceM { get; set; }
    /// <summary>Horizontal miss that counts as far from the tower for that decision (m).</summary>
    [JsonPropertyName("missedApproachRadiusM")] public double MissedApproachRadiusM { get; set; }
    [JsonPropertyName("maximumBurnSeconds")] public double MaximumBurnSeconds { get; set; }

    /// <summary>Descent speed (m/s, positive down) the profile asks for at a pin clearance.</summary>
    public double TargetDescentSpeedMps(double clearanceM)
    {
        double h = System.Math.Max(0.0, clearanceM);
        return System.Math.Sqrt(ContactDescentSpeedMps * ContactDescentSpeedMps
            + 2.0 * TerminalDecelerationMps2 * System.Math.Min(h, TerminalProfileClearanceM)
            + 2.0 * BrakingDecelerationMps2 * System.Math.Max(0.0, h - TerminalProfileClearanceM));
    }

    /// <summary>
    /// Descent speed for the current horizontal miss. The ship may not sink below an approach
    /// slope over the remaining miss while it is still translating toward the arms, and below
    /// the hold clearance it may only keep descending once centred: the near-hover on one
    /// engine seen just before the arms closed. Otherwise the profile applies.
    /// </summary>
    public double TargetDescentSpeedMps(double clearanceM, double horizontalMissM)
    {
        double profile = TargetDescentSpeedMps(clearanceM);
        double holdClearance = System.Math.Max(
            horizontalMissM > AlignedMissM ? AlignmentHoldClearanceM : 0.0,
            ApproachSlope * System.Math.Max(0.0, horizontalMissM - AlignedMissM));
        if (holdClearance <= 0.0) return profile;
        double hold = HoldClearanceGainPerSecond * (clearanceM - holdClearance);
        return System.Math.Min(profile, hold);
    }

    /// <summary>Deceleration (m/s², upward) the profile asks for at a clearance.</summary>
    public double TargetDecelerationMps2(double clearanceM) =>
        clearanceM > TerminalProfileClearanceM ? BrakingDecelerationMps2 : TerminalDecelerationMps2;

    /// <summary>
    /// The single burn must start once the descent, carried through the flip, meets the
    /// profile: igniting later leaves more speed than the braking segment can remove; igniting
    /// earlier spends propellant hovering. There is no second ignition to fall back on.
    /// </summary>
    public bool ShouldIgnite(double clearanceM, double descentSpeedMps) =>
        descentSpeedMps > 0.0
        && descentSpeedMps >= TargetDescentSpeedMps(clearanceM - descentSpeedMps * FlipLeadSeconds);

    /// <summary>
    /// How far short of the tower, along the approach, the burn should light. The flip throws
    /// the ship along its nose, which leads the belly-flop glide, and the braking that follows
    /// carries it further; igniting this far short brings it toward rest over the cradle.
    /// </summary>
    public double IgnitionLeadDistanceM(double glideTowardTowerMps) =>
        IgnitionLeadM + IgnitionLeadPerGlideSeconds
            * System.Math.Clamp(glideTowardTowerMps, 0.0, IgnitionLeadMaximumGlideMps);

    /// <summary>
    /// Pin clearance at which <see cref="ShouldIgnite"/> fires for a descent speed: the
    /// profile inverted for the speed, plus the drop during the flip lead.
    /// </summary>
    public double IgnitionClearanceM(double descentSpeedMps)
    {
        double v = System.Math.Max(0.0, descentSpeedMps);
        double c2 = ContactDescentSpeedMps * ContactDescentSpeedMps;
        double atTerminal = c2 + 2.0 * TerminalDecelerationMps2 * TerminalProfileClearanceM;
        double h = v * v <= atTerminal
            ? System.Math.Max(0.0, (v * v - c2) / (2.0 * TerminalDecelerationMps2))
            : TerminalProfileClearanceM + (v * v - atTerminal) / (2.0 * BrakingDecelerationMps2);
        return h + v * FlipLeadSeconds;
    }

    public void Validate()
    {
        double[] positive = [TerminalGlideAltitudeM, GlideMaximumClosingSpeedMps,
            GlideVelocityGainPerSecond, GlideClosingGainPerSecond, GlidePlanningTurnRateRadPerS, GlideLiftAccelerationMps2, IgnitionLeadM, IgnitionLeadPerGlideSeconds,
            IgnitionLeadMaximumGlideMps, FlipLeadSeconds, BrakingDecelerationMps2, TerminalProfileClearanceM,
            TerminalDecelerationMps2, ContactDescentSpeedMps, AlignmentHoldClearanceM, AlignedMissM,
            HoldClearanceGainPerSecond, ApproachSlope, FlipMaximumTiltDegrees, MaximumTiltDegrees, LateralClosingGainPerSecond, ClosingDecelerationMps2, MaximumClosingSpeedMps, ArrestThrottleHeadroom,
            LateralIntegralGainPerS3, LateralIntegralRadiusM, LateralIntegralLimitMps2,
            LateralDampingPerSecond, VerticalDampingPerSecond, ProportionalAttitudeGain, AttitudeDampingGain, RollDampingGain, RollAlignmentGain, RollAlignmentMaximumTiltDegrees, RollAlignmentClearanceM, RollAlignmentCommandLimit,
            FlipAttitudeDampingGain, SingleEngineMaximumLateralSpeedMps, SingleEngineMaximumMissM, ThrottleHeadroom, ControlAuthorityMargin, MissedApproachClearanceM, MissedApproachRadiusM, MaximumBurnSeconds];
        if (string.IsNullOrWhiteSpace(Id) || Status != "engineering-estimate"
            || string.IsNullOrWhiteSpace(Assumptions) || positive.Any(v => !double.IsFinite(v) || v <= 0.0)
            || MaximumTiltDegrees >= 45.0 || ThrottleHeadroom > 1.0 || ArrestThrottleHeadroom > 1.0
            || ContactDescentSpeedMps > TowerCatchCutoff.MaxVerticalSpeedMps
            || AlignmentHoldClearanceM >= TerminalProfileClearanceM)
            throw new InvalidDataException($"Invalid tower catch burn estimate '{Id}'.");
    }

    public static TowerCatchBurnDefinition LoadFromJson(string path)
    {
        var definition = JsonSerializer.Deserialize<TowerCatchBurnDefinition>(File.ReadAllText(path))
            ?? throw new InvalidDataException($"Empty tower catch burn estimate: {path}");
        definition.Validate();
        return definition;
    }
}
