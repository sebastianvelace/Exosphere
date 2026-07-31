namespace Exosphere.Simulation.Parts;

using System.Text.Json;
using System.Text.Json.Serialization;
using Exosphere.Simulation.Propulsion;

public enum PartCategory
{
    Command, Engine, FuelTank, Structure,
    Electrical, Landing, Decoupler, Fairing, RCS
}

public class AttachmentNodeDef
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("position")]
    public double[] Position { get; set; } = [0, 0, 0];
    [JsonPropertyName("size")]
    public int Size { get; set; } = 1;
    [JsonPropertyName("type")]
    public string Type { get; set; } = "stack";  // stack | radial | engine_bell
}

public class PartDefinition
{
    [JsonPropertyName("id")]          public string Id          { get; set; } = "";
    [JsonPropertyName("name")]        public string Name        { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("category")]    public string CategoryStr { get; set; } = "structure";
    [JsonPropertyName("vehicle_family")] public string VehicleFamily { get; set; } = "";
    [JsonPropertyName("vehicle_role")] public string VehicleRole { get; set; } = "";
    [JsonPropertyName("mass_dry")]    public double MassDry     { get; set; }
    [JsonPropertyName("cost")]        public double Cost        { get; set; }
    [JsonPropertyName("drag_coefficient")] public double DragCoefficient { get; set; } = 0.2;
    // Optional vehicle-specific axial Cd. Zero preserves the legacy 0.6 cylinder model.
    [JsonPropertyName("axial_drag_coefficient")]
    public double AxialDragCoefficient { get; set; }
    /// <summary>
    /// Signed aerodynamic centre offset from the vessel centre of mass along local +Y.
    /// Null preserves the generic launch-vehicle estimate; capsules use a positive offset
    /// so their blunt heat-shield end (-Y) is statically stable into the flow.
    /// </summary>
    [JsonPropertyName("aerodynamic_center_offset_y_m")]
    public double? AerodynamicCenterOffsetYM { get; set; }
    [JsonPropertyName("heat_tolerance")]   public double HeatTolerance   { get; set; } = 1200;
    [JsonPropertyName("has_heat_shield")]  public bool   HasHeatShield   { get; set; }
    /// <summary>
    /// Outward normal of the protected windward face in vessel-local coordinates.
    /// The legacy/default value preserves Starship's ventral -X tile orientation;
    /// capsules can declare an axial shield without vehicle-family branches.
    /// </summary>
    [JsonPropertyName("heat_shield_normal")]
    public double[] HeatShieldNormal { get; set; } = [-1.0, 0.0, 0.0];
    [JsonPropertyName("attachment_nodes")] public List<AttachmentNodeDef> AttachmentNodes { get; set; } = new();

    // Thermal protection. The defaults describe Starship's ventral tiles over a stainless
    // hull; a part only uses them when has_heat_shield is set.

    /// <summary>
    /// Heat capacity of the TPS layer per unit area (J/(m²·K)). Starship's silica tiles are
    /// ~7 kg/m² at ~1000 J/(kg·K). Low on purpose: a heat shield works by getting hot fast
    /// and radiating, not by soaking heat up.
    /// </summary>
    [JsonPropertyName("tps_heat_capacity_per_area")]
    public double TpsHeatCapacityPerArea { get; set; } = 7_000.0;

    /// <summary>
    /// Thermal conductance from the TPS face through to the structure (W/(m²·K)). This is
    /// the number that decides whether the vehicle survives: ~0.1 W/(m·K) of silica over
    /// 2 cm gives ~5, which passes only a few kW/m² inward out of hundreds.
    /// </summary>
    [JsonPropertyName("tps_conductance")]
    public double TpsConductance { get; set; } = 5.0;

    /// <summary>
    /// Heat capacity of the load-bearing skin per unit area (J/(m²·K)). ~4 mm of stainless
    /// at 7900 kg/m³ and 500 J/(kg·K). Only the shell participates on entry timescales —
    /// the propellant and structure deeper in do not have time to warm up.
    /// </summary>
    [JsonPropertyName("structure_heat_capacity_per_area")]
    public double StructureHeatCapacityPerArea { get; set; } = 15_800.0;

    // Physical envelope. These values let the aerodynamic model use the actual vehicle
    // dimensions instead of inferring a fictional length from the number of logical parts.
    [JsonPropertyName("length_m")]   public double LengthM   { get; set; }
    [JsonPropertyName("diameter_m")] public double DiameterM { get; set; }

    /// <summary>
    /// Radius of curvature (m) of the part's forward stagnation region, for the
    /// Sutton-Graves stagnation-point correlation. 0 (default) means "not a nose":
    /// the aggregate falls back to the hull radius.
    /// </summary>
    [JsonPropertyName("nose_radius_m")]
    public double NoseRadiusM { get; set; }

    /// <summary>
    /// Declares this part as carrying a pair of tower catch-pin hardpoints (real-world
    /// example: Starship V3's planned ship-catch fittings near the nose flare). Zero (the
    /// default) means "no catch pins" — the same "0 = not applicable" convention already
    /// used by <see cref="NoseRadiusM"/>, so most parts need no new field at all.
    /// </summary>
    [JsonPropertyName("catch_pin_lateral_offset_m")]
    public double CatchPinLateralOffsetM { get; set; }
    /// <summary>Offset along local +Y from this part's own datum to the pin pair (m).</summary>
    [JsonPropertyName("catch_pin_offset_y_m")]
    public double CatchPinOffsetYM { get; set; }
    /// <summary>Physical radius of one catch pin, for the contact-penetration test (m).</summary>
    [JsonPropertyName("catch_pin_radius_m")]
    public double CatchPinRadiusM { get; set; } = 0.4;

    // Engine
    [JsonPropertyName("thrust_vac")]   public double ThrustVac   { get; set; }
    [JsonPropertyName("thrust_sl")]    public double ThrustSL    { get; set; }
    [JsonPropertyName("isp_vac")]      public double IspVac      { get; set; }
    [JsonPropertyName("isp_sl")]       public double IspSL       { get; set; }
    [JsonPropertyName("gimbal_range")] public double GimbalRange { get; set; }
    [JsonPropertyName("fuel_type")]    public string FuelTypeStr { get; set; } = "";
    [JsonPropertyName("is_rcs")]       public bool IsRCS         { get; set; }
    [JsonPropertyName("engine_count")] public int EngineCount    { get; set; } = 1;
    [JsonPropertyName("engine_model_id")] public string EngineModelId { get; set; } = "";
    [JsonPropertyName("engine_cluster_id")] public string EngineClusterId { get; set; } = "";
    [JsonPropertyName("engine_chill_seconds")] public double EngineChillSeconds { get; set; } = 0.15;
    [JsonPropertyName("engine_spin_prime_seconds")] public double EngineSpinPrimeSeconds { get; set; } = 0.15;
    [JsonPropertyName("engine_ignition_seconds")] public double EngineIgnitionSeconds { get; set; } = 0.10;
    [JsonPropertyName("engine_purge_seconds")] public double EnginePurgeSeconds { get; set; } = 0.20;
    [JsonPropertyName("engine_startup_seconds")] public double EngineStartupSeconds { get; set; } = 0.50;
    [JsonPropertyName("engine_shutdown_seconds")] public double EngineShutdownSeconds { get; set; } = 0.20;
    [JsonPropertyName("mixture_ratio")] public double MixtureRatio { get; set; }
    [JsonIgnore] public EngineModelDefinition? ResolvedEngineModel { get; set; }
    [JsonIgnore] public IReadOnlyDictionary<string, EngineModelDefinition>
        ResolvedEngineModels { get; set; } =
            new Dictionary<string, EngineModelDefinition>(StringComparer.Ordinal);
    [JsonIgnore] public EngineClusterDefinition? ResolvedEngineCluster { get; set; }
    // Axial location of the effective thrust plane relative to this part's centre.
    [JsonPropertyName("thrust_position_y_m")] public double ThrustPositionYM { get; set; }

    // Deepest stable throttle the engine can sustain, as a fraction of rated thrust.
    // Real Raptor 2 deep-throttles to ~40 % (full-flow staged combustion stays lit but the
    // turbopumps cannot run arbitrarily slow). This is informational: it is ENFORCED only when
    // the caller opts in (see Part.ApplyThrottleFloor), so ascent and EDL can combine the
    // physical floor with discrete engine selection.
    // 0 means "no documented floor" (e.g. throwaway test engines), and the engine may idle to 0.
    [JsonPropertyName("min_throttle")] public double MinThrottle { get; set; }

    // Tank
    [JsonPropertyName("fuel_capacity_lf")]    public double FuelCapacityLF    { get; set; }
    [JsonPropertyName("fuel_capacity_ox")]    public double FuelCapacityOx    { get; set; }
    [JsonPropertyName("fuel_capacity_solid")] public double FuelCapacitySolid { get; set; }
    [JsonPropertyName("fuel_capacity_mono")]  public double FuelCapacityMono  { get; set; }
    [JsonPropertyName("ec_capacity")]         public double ECCapacity        { get; set; }

    // Crew / landing / parachute
    [JsonPropertyName("max_crew")]       public int    MaxCrew       { get; set; }
    [JsonPropertyName("max_load")]       public double MaxLoad       { get; set; }
    [JsonPropertyName("deployable")]     public bool   Deployable    { get; set; }
    // Landing-contact properties use SI units. Aggregate gear parts may represent a ring of
    // several identical feet; the reference offset follows the current render/skirt datum,
    // while the CoM offset is the physical moment arm used for torque.
    [JsonPropertyName("spring_strength")]       public double SpringStrength       { get; set; }
    [JsonPropertyName("damper_strength")]       public double DamperStrength       { get; set; }
    [JsonPropertyName("suspension_travel_m")]   public double SuspensionTravelM    { get; set; }
    [JsonPropertyName("contact_radius_m")]      public double ContactRadiusM       { get; set; }
    [JsonPropertyName("static_friction")]       public double StaticFriction       { get; set; }
    [JsonPropertyName("dynamic_friction")]      public double DynamicFriction      { get; set; }
    [JsonPropertyName("contact_point_count")]   public int    ContactPointCount    { get; set; }
    [JsonPropertyName("contact_ring_radius_m")] public double ContactRingRadiusM   { get; set; }
    [JsonPropertyName("contact_offset_y_m")]    public double ContactOffsetYM      { get; set; }
    [JsonPropertyName("contact_com_offset_y_m")]public double ContactComOffsetYM   { get; set; }
    [JsonPropertyName("drag_chute")]     public double DragChute     { get; set; }
    [JsonPropertyName("deploy_altitude")]public double DeployAltitude{ get; set; }
    [JsonPropertyName("semi_deploy_altitude")]
    public double SemiDeployAltitude { get; set; }
    [JsonPropertyName("semi_deploy_drag")]
    public double SemiDeployDrag { get; set; }
    [JsonPropertyName("min_pressure_deploy")]
    public double MinPressureDeploy { get; set; }
    [JsonPropertyName("splashdown_capable")]
    public bool SplashdownCapable { get; set; }
    [JsonPropertyName("max_splashdown_speed_mps")]
    public double MaxSplashdownSpeedMps { get; set; }
    /// <summary>
    /// When staged, detach the decoupler itself with the subtree below it. This models
    /// clamp rings that remain on the launch vehicle instead of the spacecraft.
    /// </summary>
    [JsonPropertyName("detach_with_lower_stage")]
    public bool DetachWithLowerStage { get; set; }
    /// <summary>
    /// Explicit mechanical-stage order. Higher values fire first; equal/default
    /// values preserve stack order for legacy crafts.
    /// </summary>
    [JsonPropertyName("stage_priority")]
    public int StagePriority { get; set; }

    /// <summary>
    /// Total separation impulse (N·s) the decoupler's pushers/springs deliver along the
    /// stack axis. Applied equal-and-opposite, so the relative opening rate is J/μ with
    /// μ the reduced mass. 0 (default) falls back to Vessel.DefaultSeparationOpeningMs.
    /// </summary>
    [JsonPropertyName("separation_impulse_ns")]
    public double SeparationImpulseNs { get; set; }

    // Docking. A docking part names an attachment node whose local position is
    // resolved through the same graph geometry used by VAB and centre-of-mass.
    [JsonPropertyName("is_docking_port")]
    public bool IsDockingPort { get; set; }
    [JsonPropertyName("docking_node_id")]
    public string DockingNodeId { get; set; } = "dock";
    [JsonPropertyName("docking_axis_local")]
    public double[] DockingAxisLocal { get; set; } = [0.0, 1.0, 0.0];
    [JsonPropertyName("docking_capture_range_m")]
    public double DockingCaptureRangeM { get; set; } = 0.5;
    [JsonPropertyName("docking_max_capture_speed_mps")]
    public double DockingMaxCaptureSpeedMps { get; set; } = 0.3;
    [JsonPropertyName("docking_alignment_tolerance_deg")]
    public double DockingAlignmentToleranceDeg { get; set; } = 15.0;

    [JsonIgnore]
    public PartCategory Category => CategoryStr.ToLowerInvariant() switch
    {
        "command"     => PartCategory.Command,
        "engine"      => PartCategory.Engine,
        "fuel_tank"   => PartCategory.FuelTank,
        "structure"   => PartCategory.Structure,
        "electrical"  => PartCategory.Electrical,
        "landing"     => PartCategory.Landing,
        "decoupler"   => PartCategory.Decoupler,
        "fairing"     => PartCategory.Fairing,
        "rcs"         => PartCategory.RCS,
        _             => PartCategory.Structure
    };

    [JsonIgnore]
    public bool IsStarshipFamily =>
        string.Equals(VehicleFamily, "starship", StringComparison.OrdinalIgnoreCase);

    public bool HasVehicleRole(string role) =>
        string.Equals(VehicleRole, role, StringComparison.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static PartDefinition LoadFromJson(string jsonPath)
    {
        var text = File.ReadAllText(jsonPath);
        var definition = JsonSerializer.Deserialize<PartDefinition>(text, _opts)
            ?? throw new InvalidOperationException(
                $"Failed to parse part JSON: {jsonPath}");
        definition.ValidateDockingDefinition(jsonPath);
        return definition;
    }

    private void ValidateDockingDefinition(string source)
    {
        if (!IsDockingPort) return;
        bool finiteAxis = DockingAxisLocal is { Length: >= 3 }
            && DockingAxisLocal.Take(3).All(double.IsFinite);
        double axisMagnitudeSquared = finiteAxis
            ? DockingAxisLocal.Take(3).Sum(value => value * value)
            : 0.0;
        if (string.IsNullOrWhiteSpace(DockingNodeId)
            || AttachmentNodes.All(node =>
                !string.Equals(
                    node.Id, DockingNodeId, StringComparison.Ordinal))
            || !finiteAxis
            || axisMagnitudeSquared < 0.5
            || !double.IsFinite(DockingCaptureRangeM)
            || DockingCaptureRangeM <= 0.0
            || !double.IsFinite(DockingMaxCaptureSpeedMps)
            || DockingMaxCaptureSpeedMps <= 0.0
            || !double.IsFinite(DockingAlignmentToleranceDeg)
            || DockingAlignmentToleranceDeg is <= 0.0 or > 90.0)
            throw new InvalidDataException(
                $"Invalid docking port definition '{Id}' in '{source}'.");
    }

    public static Dictionary<string, PartDefinition> LoadAllFromDirectory(string dirPath)
    {
        var result = new Dictionary<string, PartDefinition>();
        foreach (var file in Directory.GetFiles(dirPath, "*.json"))
        {
            var def = LoadFromJson(file);
            result[def.Id] = def;
        }
        return result;
    }
}
