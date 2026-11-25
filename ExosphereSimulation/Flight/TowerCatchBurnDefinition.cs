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
    /// <summary>
    /// Seconds before ignition over which the belly-flop lift turns onto the line to the tower.
    /// In the near-vertical fall the nose leans toward the lift, so a glide still turning at
    /// ignition lights with the nose, and the flip throw along it, off the tower line.
    /// </summary>
    [JsonPropertyName("glideFinalAlignmentSeconds")] public double GlideFinalAlignmentSeconds { get; set; }
    /// <summary>Gain from glide velocity error to commanded horizontal acceleration (1/s).</summary>
    [JsonPropertyName("glideVelocityGainPerSecond")] public double GlideVelocityGainPerSecond { get; set; }
    /// <summary>Time the flip and spool take before the braking profile holds (s).</summary>
    [JsonPropertyName("flipLeadSeconds")] public double FlipLeadSeconds { get; set; }
    /// <summary>Angle of attack the physical catch glide settles to below the descent's terminal speed (deg).</summary>
    [JsonPropertyName("glideTerminalAngleOfAttackDegrees")] public double GlideTerminalAngleOfAttackDegrees { get; set; } = 83.0;
    /// <summary>Speed below which the physical catch glide points its nose along the line to the cradle (m/s).</summary>
    [JsonPropertyName("glideTerminalHeadingSpeedMps")] public double GlideTerminalHeadingSpeedMps { get; set; } = 700.0;
    /// <summary>Altitude below which the physical glide lines its nose up with the approach axis for the flip (m).</summary>
    [JsonPropertyName("glideFinalAlignmentAltitudeM")] public double GlideFinalAlignmentAltitudeM { get; set; } = 3000.0;
    /// <summary>Drift speed along the nose per degree of angle of attack off the zero-drift angle (m/s per degree, identified).</summary>
    [JsonPropertyName("glideDriftPerDegreeMps")] public double GlideDriftPerDegreeMps { get; set; } = 2.4;
    /// <summary>Angle of attack at which the terminal belly flop sinks without drifting (deg, identified).</summary>
    [JsonPropertyName("glideZeroDriftAngleDegrees")] public double GlideZeroDriftAngleDegrees { get; set; } = 89.0;
    /// <summary>Trimmable angle-of-attack range of the terminal belly flop at catch mass (deg).</summary>
    [JsonPropertyName("glideMinimumAngleOfAttackDegrees")] public double GlideMinimumAngleOfAttackDegrees { get; set; } = 80.0;
    [JsonPropertyName("glideMaximumAngleOfAttackDegrees")] public double GlideMaximumAngleOfAttackDegrees { get; set; } = 102.0;
    /// <summary>Horizontal distance below which the glide takes its approach axis from the nose instead of the cradle (m).</summary>
    [JsonPropertyName("glideAxisMinimumDistanceM")] public double GlideAxisMinimumDistanceM { get; set; } = 200.0;
    /// <summary>Slew rate of the physical glide's attitude reference in the terminal glide (rad/s).</summary>
    [JsonPropertyName("glideReferenceSlewRateRadPerSecond")] public double GlideReferenceSlewRateRadPerSecond { get; set; } = 0.15;
    /// <summary>Half-angle of the cone about the approach axis the glide may turn its nose through (deg).</summary>
    [JsonPropertyName("glideNoseConeDegrees")] public double GlideNoseConeDegrees { get; set; } = 30.0;
    /// <summary>Largest heading rate the physical glide asks of its nose (deg/s).</summary>
    [JsonPropertyName("glideHeadingRateDegPerS")] public double GlideHeadingRateDegPerS { get; set; } = 6.0;
    /// <summary>Largest lead of the physical glide's attitude reference over the ship (deg).</summary>
    [JsonPropertyName("glideMaximumReferenceLeadDegrees")] public double GlideMaximumReferenceLeadDegrees { get; set; } = 20.0;
    /// <summary>Along-nose acceleration per degree below 90° on the belly flop (m/s² per degree).</summary>
    [JsonPropertyName("glideLiftPerDegreeMps2")] public double GlideLiftPerDegreeMps2 { get; set; } = 0.175;
    /// <summary>Altitude at which the terminal glide guidance aims to reach the ignition point at rest (m).</summary>
    [JsonPropertyName("glideAimAltitudeM")] public double GlideAimAltitudeM { get; set; } = 2000.0;
    /// <summary>Shortest time to go the terminal glide guidance uses (s).</summary>
    [JsonPropertyName("glideMinimumTimeToGoS")] public double GlideMinimumTimeToGoS { get; set; } = 6.0;
    /// <summary>Largest horizontal acceleration the terminal glide guidance asks for (m/s²).</summary>
    [JsonPropertyName("glideMaximumAccelerationMps2")] public double GlideMaximumAccelerationMps2 { get; set; } = 1.5;
    /// <summary>When true the glide's approach axis follows the bearing to the cradle down to the final alignment altitude.</summary>
    [JsonPropertyName("glideRadialApproach")] public bool GlideRadialApproach { get; set; } = true;
    /// <summary>Rate at which the approach axis may turn toward the bearing inside the axis minimum distance (deg/s).</summary>
    [JsonPropertyName("glideAxisTurnRateDegPerS")] public double GlideAxisTurnRateDegPerS { get; set; } = 2.0;
    /// <summary>Weight of arriving at rest in the terminal glide guidance: 0 = position only (zero-effort miss), 1 = position and rest.</summary>
    [JsonPropertyName("glideArrivalVelocityWeight")] public double GlideArrivalVelocityWeight { get; set; } = 0.0;
    /// <summary>Speed below which the physical glide flies the terminal attitude loop (m/s).</summary>
    [JsonPropertyName("glideTerminalAttitudeSpeedMps")] public double GlideTerminalAttitudeSpeedMps { get; set; } = 200.0;
    /// <summary>Terminal attitude loop natural frequencies (rad/s): heading (body X), roll (body Y), angle of attack (body Z).</summary>
    [JsonPropertyName("glideHeadingFrequencyRadPerS")] public double GlideHeadingFrequencyRadPerS { get; set; } = 0.15;
    [JsonPropertyName("glideRollFrequencyRadPerS")] public double GlideRollFrequencyRadPerS { get; set; } = 1.5;
    [JsonPropertyName("glideAngleFrequencyRadPerS")] public double GlideAngleFrequencyRadPerS { get; set; } = 1.0;
    /// <summary>Terminal attitude loop damping ratios: heading, roll, angle of attack.</summary>
    [JsonPropertyName("glideHeadingDampingRatio")] public double GlideHeadingDampingRatio { get; set; } = 1.0;
    [JsonPropertyName("glideRollDampingRatio")] public double GlideRollDampingRatio { get; set; } = 1.0;
    [JsonPropertyName("glideAngleDampingRatio")] public double GlideAngleDampingRatio { get; set; } = 0.9;
    /// <summary>Acceleration and rate limits of the terminal heading reference (deg/s², deg/s).</summary>
    [JsonPropertyName("glideHeadingAccelerationDegPerS2")] public double GlideHeadingAccelerationDegPerS2 { get; set; } = 0.5;
    [JsonPropertyName("glideHeadingRateLimitDegPerS")] public double GlideHeadingReferenceRateDegPerS { get; set; } = 4.0;
    /// <summary>Acceleration and rate limits of the terminal angle-of-attack reference (deg/s², deg/s).</summary>
    [JsonPropertyName("glideAngleAccelerationDegPerS2")] public double GlideAngleAccelerationDegPerS2 { get; set; } = 1.5;
    [JsonPropertyName("glideAngleRateDegPerS")] public double GlideAngleRateDegPerS { get; set; } = 3.0;
    /// <summary>Integral gain (1/s) and limit (deg) on the terminal angle-of-attack tracking error.</summary>
    [JsonPropertyName("glideAngleTrackingIntegralGainPerSecond")] public double GlideAngleTrackingIntegralGainPerSecond { get; set; } = 0.4;
    [JsonPropertyName("glideAngleTrackingIntegralLimitDegrees")] public double GlideAngleTrackingIntegralLimitDegrees { get; set; } = 6.0;
    /// <summary>Reference rate (deg/s) below which the tracking integral runs.</summary>
    [JsonPropertyName("glideAngleIntegralHoldRateDegPerS")] public double GlideAngleIntegralHoldRateDegPerS { get; set; } = 0.1;
    /// <summary>Weight of roll over the other axes in the terminal flap allocation (angular-acceleration space).</summary>
    [JsonPropertyName("glideAllocationRollPriority")] public double GlideAllocationRollPriority { get; set; } = 5.0;
    [JsonPropertyName("brakingDecelerationMps2")] public double BrakingDecelerationMps2 { get; set; }
    [JsonPropertyName("terminalProfileClearanceM")] public double TerminalProfileClearanceM { get; set; }
    [JsonPropertyName("terminalDecelerationMps2")] public double TerminalDecelerationMps2 { get; set; }
    /// <summary>Descent speed the profile reaches at pin contact (m/s).</summary>
    [JsonPropertyName("contactDescentSpeedMps")] public double ContactDescentSpeedMps { get; set; }
    /// <summary>Pin clearance the burn holds until the ship is centred over the cradle (m).</summary>
    [JsonPropertyName("alignmentHoldClearanceM")] public double AlignmentHoldClearanceM { get; set; }
    /// <summary>Horizontal miss below which the ship may descend past the hold clearance (m).</summary>
    [JsonPropertyName("alignedMissM")] public double AlignedMissM { get; set; }
    /// <summary>Rail slack (m) at which the ship counts as centred enough to descend onto the arms.</summary>
    [JsonPropertyName("railDescentSlackM")] public double RailDescentSlackM { get; set; } = 0.5;
    /// <summary>Gain from clearance error to descent speed while holding (1/s).</summary>
    [JsonPropertyName("holdClearanceGainPerSecond")] public double HoldClearanceGainPerSecond { get; set; }
    /// <summary>Tilt the flip may aim past vertical; the rotation carries on past its target (deg).</summary>
    [JsonPropertyName("flipMaximumTiltDegrees")] public double FlipMaximumTiltDegrees { get; set; }
    [JsonPropertyName("maximumTiltDegrees")] public double MaximumTiltDegrees { get; set; }
    /// <summary>
    /// Identified side-acceleration response per lit engine count (one, two, three), each
    /// <c>[K, T_z, T, ζ]</c> of <see cref="LateralPlantModel"/>: steps through the burn's own
    /// actuation path on the 215 t full-physics bench, fitted to <c>K(1 − T_z s)/(T²s² + 2ζTs + 1)</c>.
    /// </summary>
    [JsonPropertyName("lateralPlantByEngines")] public double[][] LateralPlantByEngines { get; set; } = [];
    /// <summary>
    /// Shortest horizon of the side-acceleration plan, per second of the plant's reversal time
    /// <c>T_z</c>: a plan shorter than a few reversal times drives the tilt against its own
    /// initial reversal and oscillates.
    /// </summary>
    [JsonPropertyName("lateralMinimumHorizonPerZeroTime")] public double LateralMinimumHorizonPerZeroTime { get; set; }

    public double LateralMinimumHorizonS(int litEngines) => LateralMinimumHorizonPerZeroTime * LateralPlant(litEngines).ZeroTimeS;
    /// <summary>Weight of command changes against command size in the side-acceleration plan (s²).</summary>
    [JsonPropertyName("lateralPlanSmoothingS2")] public double LateralPlanSmoothingS2 { get; set; }

    public LateralPlantModel LateralPlant(int litEngines)
    {
        var p = LateralPlantByEngines[System.Math.Clamp(litEngines, 1, LateralPlantByEngines.Length) - 1];
        return new LateralPlantModel(p[0], p[1], p[2], p[3]);
    }
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
    /// <summary>
    /// Pin roll error (deg) above which the ship holds at the hold clearance instead of
    /// settling: the fittings must face the arms before they go between them. Zero disables.
    /// </summary>
    [JsonPropertyName("rollAlignedToleranceDegrees")] public double RollAlignedToleranceDegrees { get; set; }
    /// <summary>
    /// Roll damping during the flip. The forward flaps on the ogive excite the belly flop's
    /// roll/sideslip mode as the ship pitches up; left undamped it can spin the ship up to
    /// 1 rad/s and stall the flip. Zero leaves roll free.
    /// </summary>
    [JsonPropertyName("flipRollDampingGain")] public double FlipRollDampingGain { get; set; }
    /// <summary>Share of the attitude command the body flaps follow during the braking (1 = all of it).</summary>
    [JsonPropertyName("brakingFlapCommandScale")] public double BrakingFlapCommandScale { get; set; } = 1.0;
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
        double holdClearance = horizontalMissM > AlignedMissM ? AlignmentHoldClearanceM : 0.0;
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
            GlideVelocityGainPerSecond, GlideClosingGainPerSecond, GlidePlanningTurnRateRadPerS, GlideLiftAccelerationMps2, GlideFinalAlignmentSeconds, IgnitionLeadM, IgnitionLeadPerGlideSeconds,
            IgnitionLeadMaximumGlideMps, FlipLeadSeconds, BrakingDecelerationMps2, TerminalProfileClearanceM,
            TerminalDecelerationMps2, ContactDescentSpeedMps, AlignmentHoldClearanceM, AlignedMissM,
            HoldClearanceGainPerSecond, FlipMaximumTiltDegrees, MaximumTiltDegrees, ArrestThrottleHeadroom,
            VerticalDampingPerSecond, ProportionalAttitudeGain, AttitudeDampingGain, RollDampingGain, RollAlignmentGain, RollAlignmentMaximumTiltDegrees, RollAlignmentClearanceM, RollAlignmentCommandLimit,
            FlipAttitudeDampingGain, LateralMinimumHorizonPerZeroTime, LateralPlanSmoothingS2, SingleEngineMaximumLateralSpeedMps, SingleEngineMaximumMissM, ThrottleHeadroom, ControlAuthorityMargin, MissedApproachClearanceM, MissedApproachRadiusM, MaximumBurnSeconds];
        if (string.IsNullOrWhiteSpace(Id) || Status != "engineering-estimate"
            || string.IsNullOrWhiteSpace(Assumptions) || positive.Any(v => !double.IsFinite(v) || v <= 0.0)
            || BrakingFlapCommandScale < 0.0 || BrakingFlapCommandScale > 1.0 || !double.IsFinite(BrakingFlapCommandScale)
            || FlipRollDampingGain < 0.0 || !double.IsFinite(FlipRollDampingGain)
            || RollAlignedToleranceDegrees < 0.0 || RollAlignedToleranceDegrees >= 90.0
            || GlideTerminalAngleOfAttackDegrees <= 0.0 || GlideTerminalAngleOfAttackDegrees > 90.0
            || GlideLiftPerDegreeMps2 <= 0.0 || GlideDriftPerDegreeMps <= 0.0
            || GlideMinimumAngleOfAttackDegrees >= GlideZeroDriftAngleDegrees || GlideMaximumAngleOfAttackDegrees <= GlideZeroDriftAngleDegrees
            || GlideArrivalVelocityWeight < 0.0 || GlideArrivalVelocityWeight > 1.0 || GlideAxisMinimumDistanceM < 0.0
            || GlideTerminalHeadingSpeedMps < 0.0 || !double.IsFinite(GlideTerminalHeadingSpeedMps)
            || GlideMaximumReferenceLeadDegrees < 0.0 || GlideHeadingRateDegPerS <= 0.0 || GlideFinalAlignmentAltitudeM < 0.0
            || new[] { GlideTerminalAttitudeSpeedMps, GlideHeadingFrequencyRadPerS, GlideRollFrequencyRadPerS,
                GlideAngleFrequencyRadPerS, GlideHeadingDampingRatio, GlideRollDampingRatio, GlideAngleDampingRatio,
                GlideHeadingAccelerationDegPerS2, GlideHeadingReferenceRateDegPerS, GlideAngleAccelerationDegPerS2, GlideMinimumTimeToGoS, GlideMaximumAccelerationMps2, GlideAngleRateDegPerS,
                GlideAngleIntegralHoldRateDegPerS, GlideAllocationRollPriority }.Any(v => !double.IsFinite(v) || v <= 0.0)
            || GlideAngleTrackingIntegralGainPerSecond < 0.0 || GlideAngleTrackingIntegralLimitDegrees < 0.0
            || MaximumTiltDegrees >= 45.0 || ThrottleHeadroom > 1.0 || ArrestThrottleHeadroom > 1.0
            || ContactDescentSpeedMps > TowerCatchCutoff.MaxVerticalSpeedMps
            || AlignmentHoldClearanceM >= TerminalProfileClearanceM
            || LateralPlantByEngines.Length == 0 || LateralPlantByEngines.Any(p => p.Length != 4
                || !new LateralPlantModel(p[0], p[1], p[2], p[3]).IsValid))
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
