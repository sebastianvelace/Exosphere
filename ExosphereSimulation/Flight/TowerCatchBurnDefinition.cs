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
