namespace Exosphere.Simulation.Persistence;

using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Propulsion;
using System.Text.Json;

public sealed class SaveGameV2
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DateTimeOffset SavedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public double SimulationTime { get; set; }
    public double TimeScale { get; set; } = 1.0;
    public string? ActiveVesselId { get; set; }
    public List<VesselSaveV2> Vessels { get; set; } = new();
    public MissionSaveV2 Mission { get; set; } = new();
    public NavigationSaveV2 Navigation { get; set; } = new();
    public CampaignSaveV2 Campaign { get; set; } = new();
    public List<PersistentAssetSaveV2> PersistentAssets { get; set; } = new();
    public Dictionary<string, JsonElement> Systems { get; set; } = new();
}

public sealed class VesselSaveV2
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public VectorSaveV2 Position { get; set; } = new();
    public VectorSaveV2 Velocity { get; set; } = new();
    public QuaternionSaveV2 Orientation { get; set; } = QuaternionSaveV2.Identity;
    public VectorSaveV2 AngularVelocity { get; set; } = new();
    public bool IsOnRails { get; set; }
    public OrbitalSaveV2? Orbit { get; set; }
    public string? ReferenceBodyId { get; set; }
    public double Throttle { get; set; }
    public VectorSaveV2 PitchYawRoll { get; set; } = new();
    public bool SasEnabled { get; set; }
    public bool IsGroundHeld { get; set; }
    public VectorSaveV2 GroundNormal { get; set; } = new();
    public double GroundOffset { get; set; }
    public bool IsDestroyed { get; set; }
    public VesselDestructionCause DestructionCause { get; set; }
    public double CrashImpactSpeed { get; set; }
    public VectorSaveV2 CrashPosition { get; set; } = new();
    public string? RootPartInstanceId { get; set; }
    public List<PartSaveV2> Parts { get; set; } = new();
    public List<JointSaveV2> Joints { get; set; } = new();
    public List<CrewSaveV2> Crew { get; set; } = new();
    public string? PayloadManifestId { get; set; }
}

public sealed class PartSaveV2
{
    public string InstanceId { get; set; } = "";
    public string DefinitionId { get; set; } = "";
    public double LiquidFuel { get; set; }
    public double Oxidizer { get; set; }
    public double SolidFuel { get; set; }
    public double Monopropellant { get; set; }
    public double ElectricCharge { get; set; }
    public double Temperature { get; set; }
    public double SkinTemperature { get; set; }
    public double ThermalDamage { get; set; }
    public bool IsBroken { get; set; }
    public bool IsDeployed { get; set; }
    public bool IsStagingActive { get; set; }
    public double ThrottleLevel { get; set; }
    public VectorSaveV2 GimbalOffset { get; set; } = new();
    public double ActiveEngineFraction { get; set; } = 1.0;
    public List<EngineInstanceSaveV2> EngineInstances { get; set; } = new();
    public List<EngineFailureInjectionSaveV2> ScheduledEngineFailures { get; set; } = new();
}

public sealed class EngineInstanceSaveV2
{
    public string InstanceId { get; set; } = "";
    public string EngineModelId { get; set; } = "";
    public EngineLifecycleState State { get; set; }
    public double StateElapsedSeconds { get; set; }
    public double CommandedThrottle { get; set; }
    public double ActualThrottle { get; set; }
    public VectorSaveV2 GimbalDeg { get; set; } = new();
    public VectorSaveV2 GimbalVelocityDegPerS { get; set; } = new();
    public double? ChamberPressureFraction { get; set; }
    public double TemperatureK { get; set; } = 290.0;
    public int StartAttempts { get; set; }
    public int StartsCompleted { get; set; }
    public string? FailureCode { get; set; }
}

public sealed class EngineFailureInjectionSaveV2
{
    public string EngineInstanceId { get; set; } = "";
    public EngineLifecycleState TriggerState { get; set; }
    public int TriggerStartAttempt { get; set; }
    public double TriggerAfterStateSeconds { get; set; }
    public string FailureCode { get; set; } = "";
}

public sealed class JointSaveV2
{
    public string ParentPartId { get; set; } = "";
    public string ChildPartId { get; set; } = "";
    public string ParentNodeId { get; set; } = "";
    public string ChildNodeId { get; set; } = "";
    public double TensileStrength { get; set; }
    public double CompressiveStrength { get; set; }
    public double ShearStrength { get; set; }
    public double CurrentTensileLoad { get; set; }
    public double CurrentShearLoad { get; set; }
}

public sealed class CrewSaveV2
{
    public string Id { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public int PilotLevel { get; set; }
    public int EngineerLevel { get; set; }
    public int ScientistLevel { get; set; }
    public double EvaExperience { get; set; }
    public int MissionsCompleted { get; set; }
    public CrewStatus Status { get; set; }
    public double EvaSuitEc { get; set; }
    public double EvaSuitO2 { get; set; }
}

public sealed class OrbitalSaveV2
{
    public double SemiMajorAxis { get; set; }
    public double Eccentricity { get; set; }
    public double Inclination { get; set; }
    public double LongitudeOfAscendingNode { get; set; }
    public double ArgumentOfPeriapsis { get; set; }
    public double MeanAnomalyAtEpoch { get; set; }
    public double Epoch { get; set; }
    public string ReferenceBodyId { get; set; } = "";
    public double SpecificAngularMomentum { get; set; }
    public double PeriapsisRadius { get; set; }
    public bool IsRadial { get; set; }
}

public sealed class MissionSaveV2
{
    public string? MissionId { get; set; }
    public string Phase { get; set; } = "";
    public Dictionary<string, double> ObjectiveProgress { get; set; } = new();
    public HashSet<string> CompletedObjectives { get; set; } = new();
}

public sealed class NavigationSaveV2
{
    public string Mode { get; set; } = "SURF";
    public string? TargetVesselId { get; set; }
    public List<JsonElement> ManeuverNodes { get; set; } = new();
}

public sealed class CampaignSaveV2
{
    public string ProfileId { get; set; } = "default";
    public HashSet<string> UnlockedIds { get; set; } = new();
    public HashSet<string> CompletedMissionIds { get; set; } = new();
    public Dictionary<string, int> RewardLedger { get; set; } = new();
}

public sealed class PersistentAssetSaveV2
{
    public string AssetId { get; set; } = "";
    public string AssetType { get; set; } = "";
    public string? VesselId { get; set; }
    public Dictionary<string, double> Resources { get; set; } = new();
}

public sealed class VectorSaveV2
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }

    public static VectorSaveV2 From(Vector3d value) =>
        new() { X = value.X, Y = value.Y, Z = value.Z };
    public Vector3d ToVector() => new(X, Y, Z);
}

public sealed class QuaternionSaveV2
{
    public double W { get; set; } = 1.0;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public static QuaternionSaveV2 Identity => new();
    public static QuaternionSaveV2 From(Quaterniond value) =>
        new() { W = value.W, X = value.X, Y = value.Y, Z = value.Z };
    public Quaterniond ToQuaternion() => new Quaterniond(W, X, Y, Z).Normalize();
}

public static class SaveGameV2Codec
{
    public static SaveGameV2 Capture(Universe universe, SaveGameV2? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(universe);
        var save = metadata ?? new SaveGameV2();
        save.SchemaVersion = SaveGameV2.CurrentSchemaVersion;
        save.SavedAtUtc = DateTimeOffset.UtcNow;
        save.SimulationTime = universe.CurrentTime;
        save.TimeScale = universe.TimeScale;
        save.ActiveVesselId = universe.ActiveVessel?.Id;
        save.Vessels = universe.Vessels.Select(CaptureVessel).ToList();
        Validate(save);
        return save;
    }

    public static void Restore(Universe universe, SaveGameV2 save, PartCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(universe);
        ArgumentNullException.ThrowIfNull(catalog);
        Validate(save);

        // Build the complete replacement first. A bad definition or joint cannot leave the
        // live universe half-cleared.
        var replacements = save.Vessels.Select(v => RestoreVessel(v, catalog)).ToList();
        foreach (var existing in universe.Vessels.ToArray())
            universe.RemoveVessel(existing);
        foreach (var vessel in replacements)
            universe.AddVessel(vessel);

        universe.SetSimulationTime(save.SimulationTime);
        universe.TimeScale = save.TimeScale;
        if (save.ActiveVesselId != null && !universe.SetActiveVessel(save.ActiveVesselId))
            throw new InvalidDataException($"Active vessel '{save.ActiveVesselId}' is missing.");
    }

    public static void Validate(SaveGameV2 save)
    {
        ArgumentNullException.ThrowIfNull(save);
        if (save.SchemaVersion != SaveGameV2.CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported save schema {save.SchemaVersion}.");
        RequireFinite(save.SimulationTime, nameof(save.SimulationTime));
        RequireFinite(save.TimeScale, nameof(save.TimeScale));
        if (save.TimeScale <= 0.0) throw new InvalidDataException("TimeScale must be positive.");

        var vesselIds = new HashSet<string>(StringComparer.Ordinal);
        var partIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var vessel in save.Vessels)
        {
            RequireId(vessel.Id, "vessel");
            if (!vesselIds.Add(vessel.Id))
                throw new InvalidDataException($"Duplicate vessel id '{vessel.Id}'.");
            foreach (double value in VesselFiniteValues(vessel)) RequireFinite(value, vessel.Id);
            foreach (var part in vessel.Parts)
            {
                RequireId(part.InstanceId, "part");
                RequireId(part.DefinitionId, "definition");
                if (!partIds.Add(part.InstanceId))
                    throw new InvalidDataException($"Duplicate global part id '{part.InstanceId}'.");
                foreach (double value in PartFiniteValues(part)) RequireFinite(value, part.InstanceId);
                var engineIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var engine in part.EngineInstances)
                {
                    RequireId(engine.InstanceId, "engine");
                    RequireId(engine.EngineModelId, "engine model");
                    if (!engineIds.Add(engine.InstanceId))
                        throw new InvalidDataException(
                            $"Duplicate engine id '{engine.InstanceId}'.");
                    foreach (double value in EngineFiniteValues(engine))
                        RequireFinite(value, engine.InstanceId);
                    if (engine.CommandedThrottle is < 0.0 or > 1.0
                        || engine.ActualThrottle is < 0.0 or > 1.0
                        || engine.ChamberPressureFraction is < 0.0 or > 1.0
                        || engine.StateElapsedSeconds < 0.0
                        || engine.TemperatureK < 0.0
                        || engine.StartAttempts < 0
                        || engine.StartsCompleted < 0)
                        throw new InvalidDataException(
                            $"Invalid engine state '{engine.InstanceId}'.");
                }
                foreach (var injection in part.ScheduledEngineFailures)
                {
                    RequireId(injection.EngineInstanceId, "scheduled engine");
                    RequireId(injection.FailureCode, "scheduled failure");
                    RequireFinite(
                        injection.TriggerAfterStateSeconds,
                        injection.EngineInstanceId);
                    if (!engineIds.Contains(injection.EngineInstanceId)
                        || !Enum.IsDefined(injection.TriggerState)
                        || injection.TriggerState is EngineLifecycleState.Off
                            or EngineLifecycleState.Failed
                        || injection.TriggerStartAttempt < 0
                        || injection.TriggerAfterStateSeconds < 0.0)
                        throw new InvalidDataException(
                            $"Invalid scheduled failure for '{injection.EngineInstanceId}'.");
                }
            }
        }
        if (save.ActiveVesselId != null && !vesselIds.Contains(save.ActiveVesselId))
            throw new InvalidDataException($"Active vessel '{save.ActiveVesselId}' is missing.");
    }

    private static VesselSaveV2 CaptureVessel(Vessel vessel)
    {
        var result = new VesselSaveV2
        {
            Id = vessel.Id,
            Name = vessel.Name,
            Position = VectorSaveV2.From(vessel.Position),
            Velocity = VectorSaveV2.From(vessel.Velocity),
            Orientation = QuaternionSaveV2.From(vessel.Orientation),
            AngularVelocity = VectorSaveV2.From(vessel.AngularVelocity),
            IsOnRails = vessel.IsOnRails,
            Orbit = vessel.OrbitalState == null ? null : CaptureOrbit(vessel.OrbitalState),
            ReferenceBodyId = vessel.ReferenceBodyId,
            Throttle = vessel.Throttle,
            PitchYawRoll = VectorSaveV2.From(vessel.PitchYawRoll),
            SasEnabled = vessel.SASEnabled,
            IsGroundHeld = vessel.IsGroundHeld,
            GroundNormal = VectorSaveV2.From(vessel.GroundNormal),
            GroundOffset = vessel.GroundOffset,
            IsDestroyed = vessel.IsDestroyed,
            DestructionCause = vessel.DestructionCause,
            CrashImpactSpeed = vessel.CrashImpactSpeed,
            CrashPosition = VectorSaveV2.From(vessel.CrashSimPosition),
            RootPartInstanceId = vessel.Parts.Root?.InstanceId,
            Parts = vessel.Parts.Parts.Select(CapturePart).ToList(),
            Joints = vessel.Parts.Joints.Select(j => new JointSaveV2
            {
                ParentPartId = j.Parent.InstanceId,
                ChildPartId = j.Child.InstanceId,
                ParentNodeId = j.ParentNodeId,
                ChildNodeId = j.ChildNodeId,
                TensileStrength = j.TensileStrength,
                CompressiveStrength = j.CompressiveStrength,
                ShearStrength = j.ShearStrength,
                CurrentTensileLoad = j.CurrentTensileLoad,
                CurrentShearLoad = j.CurrentShearLoad,
            }).ToList(),
            Crew = vessel.Crew.Select(CaptureCrew).ToList(),
        };
        return result;
    }

    private static Vessel RestoreVessel(VesselSaveV2 saved, PartCatalog catalog)
    {
        var vessel = new Vessel(saved.Id)
        {
            Name = saved.Name,
            Position = saved.Position.ToVector(),
            Velocity = saved.Velocity.ToVector(),
            Orientation = saved.Orientation.ToQuaternion(),
            AngularVelocity = saved.AngularVelocity.ToVector(),
            IsOnRails = saved.IsOnRails,
            OrbitalState = saved.Orbit == null ? null : RestoreOrbit(saved.Orbit),
            ReferenceBodyId = saved.ReferenceBodyId,
            Throttle = saved.Throttle,
            PitchYawRoll = saved.PitchYawRoll.ToVector(),
            SASEnabled = saved.SasEnabled,
            IsGroundHeld = saved.IsGroundHeld,
            GroundNormal = saved.GroundNormal.ToVector(),
            GroundOffset = saved.GroundOffset,
            IsDestroyed = saved.IsDestroyed,
            DestructionCause = saved.DestructionCause,
            CrashImpactSpeed = saved.CrashImpactSpeed,
            CrashSimPosition = saved.CrashPosition.ToVector(),
        };

        var parts = new Dictionary<string, Part>(StringComparer.Ordinal);
        foreach (var state in saved.Parts)
        {
            if (!catalog.TryGet(state.DefinitionId, out var definition))
                throw new InvalidDataException($"Unknown part definition '{state.DefinitionId}'.");
            parts.Add(state.InstanceId, RestorePart(state, definition));
        }
        if (saved.RootPartInstanceId != null)
            vessel.Parts.SetRoot(parts.TryGetValue(saved.RootPartInstanceId, out var root)
                ? root : throw new InvalidDataException($"Missing root part '{saved.RootPartInstanceId}'."));
        foreach (var part in parts.Values) vessel.Parts.AddPart(part);
        foreach (var state in saved.Joints)
        {
            if (!parts.TryGetValue(state.ParentPartId, out var parent)
                || !parts.TryGetValue(state.ChildPartId, out var child))
                throw new InvalidDataException("Joint references a missing part.");
            var joint = new Joint(parent, child, state.ParentNodeId, state.ChildNodeId)
            {
                TensileStrength = state.TensileStrength,
                CompressiveStrength = state.CompressiveStrength,
                ShearStrength = state.ShearStrength,
                CurrentTensileLoad = state.CurrentTensileLoad,
                CurrentShearLoad = state.CurrentShearLoad,
            };
            vessel.Parts.AddJoint(joint);
        }
        vessel.Crew.AddRange(saved.Crew.Select(RestoreCrew));
        vessel.ConfigureLandingContactsFromParts();
        return vessel;
    }

    private static PartSaveV2 CapturePart(Part part) => new()
    {
        InstanceId = part.InstanceId,
        DefinitionId = part.Definition.Id,
        LiquidFuel = part.LiquidFuel,
        Oxidizer = part.Oxidizer,
        SolidFuel = part.SolidFuel,
        Monopropellant = part.Monopropellant,
        ElectricCharge = part.ElectricCharge,
        Temperature = part.Temperature,
        SkinTemperature = part.SkinTemperature,
        ThermalDamage = part.ThermalDamage,
        IsBroken = part.IsBroken,
        IsDeployed = part.IsDeployed,
        IsStagingActive = part.IsStagingActive,
        ThrottleLevel = part.ThrottleLevel,
        GimbalOffset = VectorSaveV2.From(part.GimbalOffset),
        ActiveEngineFraction = part.ActiveEngineFraction,
        EngineInstances = part.EngineStates.Select(engine => new EngineInstanceSaveV2
        {
            InstanceId = engine.InstanceId,
            EngineModelId = engine.EngineModelId,
            State = engine.State,
            StateElapsedSeconds = engine.StateElapsedSeconds,
            CommandedThrottle = engine.CommandedThrottle,
            ActualThrottle = engine.ActualThrottle,
            GimbalDeg = VectorSaveV2.From(engine.GimbalDeg),
            GimbalVelocityDegPerS = VectorSaveV2.From(
                engine.GimbalVelocityDegPerS),
            ChamberPressureFraction = engine.ChamberPressureFraction,
            TemperatureK = engine.TemperatureK,
            StartAttempts = System.Math.Max(
                engine.StartAttempts, engine.StartsCompleted),
            StartsCompleted = engine.StartsCompleted,
            FailureCode = engine.FailureCode,
        }).ToList(),
        ScheduledEngineFailures = part.ScheduledEngineFailures
            .Select(injection => new EngineFailureInjectionSaveV2
            {
                EngineInstanceId = injection.EngineInstanceId,
                TriggerState = injection.TriggerState,
                TriggerStartAttempt = injection.TriggerStartAttempt,
                TriggerAfterStateSeconds = injection.TriggerAfterStateSeconds,
                FailureCode = injection.FailureCode,
            })
            .ToList(),
    };

    private static Part RestorePart(PartSaveV2 state, PartDefinition definition)
    {
        var part = new Part(definition, state.InstanceId)
        {
            LiquidFuel = state.LiquidFuel,
            Oxidizer = state.Oxidizer,
            SolidFuel = state.SolidFuel,
            Monopropellant = state.Monopropellant,
            ElectricCharge = state.ElectricCharge,
            Temperature = state.Temperature,
            SkinTemperature = state.SkinTemperature,
            ThermalDamage = state.ThermalDamage,
            IsBroken = state.IsBroken,
            IsDeployed = state.IsDeployed,
            IsStagingActive = state.IsStagingActive,
            ThrottleLevel = state.ThrottleLevel,
            GimbalOffset = state.GimbalOffset.ToVector(),
            ActiveEngineFraction = state.ActiveEngineFraction,
        };
        part.RestoreEngineStates(state.EngineInstances.Select(engine => new EngineInstanceState
        {
            InstanceId = engine.InstanceId,
            EngineModelId = engine.EngineModelId,
            State = engine.State,
            StateElapsedSeconds = engine.StateElapsedSeconds,
            CommandedThrottle = engine.CommandedThrottle,
            ActualThrottle = engine.ActualThrottle,
            GimbalDeg = engine.GimbalDeg.ToVector(),
            GimbalVelocityDegPerS = engine.GimbalVelocityDegPerS.ToVector(),
            ChamberPressureFraction =
                engine.ChamberPressureFraction ?? engine.ActualThrottle,
            TemperatureK = engine.TemperatureK,
            StartAttempts = System.Math.Max(
                engine.StartAttempts, engine.StartsCompleted),
            StartsCompleted = engine.StartsCompleted,
            FailureCode = engine.FailureCode,
        }));
        part.RestoreScheduledEngineFailures(
            state.ScheduledEngineFailures.Select(
                injection => new EngineFailureInjection
                {
                    EngineInstanceId = injection.EngineInstanceId,
                    TriggerState = injection.TriggerState,
                    TriggerStartAttempt = injection.TriggerStartAttempt,
                    TriggerAfterStateSeconds =
                        injection.TriggerAfterStateSeconds,
                    FailureCode = injection.FailureCode,
                }));
        return part;
    }

    private static CrewSaveV2 CaptureCrew(CrewMember crew) => new()
    {
        Id = crew.Id, FirstName = crew.FirstName, LastName = crew.LastName,
        PilotLevel = crew.PilotLevel, EngineerLevel = crew.EngineerLevel,
        ScientistLevel = crew.ScientistLevel, EvaExperience = crew.EVAExperience,
        MissionsCompleted = crew.MissionsCompleted, Status = crew.Status,
        EvaSuitEc = crew.EVASuitEC, EvaSuitO2 = crew.EVASuitO2,
    };

    private static CrewMember RestoreCrew(CrewSaveV2 crew) => new()
    {
        Id = crew.Id, FirstName = crew.FirstName, LastName = crew.LastName,
        PilotLevel = crew.PilotLevel, EngineerLevel = crew.EngineerLevel,
        ScientistLevel = crew.ScientistLevel, EVAExperience = crew.EvaExperience,
        MissionsCompleted = crew.MissionsCompleted, Status = crew.Status,
        EVASuitEC = crew.EvaSuitEc, EVASuitO2 = crew.EvaSuitO2,
    };

    private static OrbitalSaveV2 CaptureOrbit(OrbitalElements o) => new()
    {
        SemiMajorAxis = o.SemiMajorAxis, Eccentricity = o.Eccentricity,
        Inclination = o.Inclination, LongitudeOfAscendingNode = o.LongitudeOfAscendingNode,
        ArgumentOfPeriapsis = o.ArgumentOfPeriapsis,
        MeanAnomalyAtEpoch = o.MeanAnomalyAtEpoch, Epoch = o.Epoch,
        ReferenceBodyId = o.ReferenceBodyId,
        SpecificAngularMomentum = o.SpecificAngularMomentum,
        PeriapsisRadius = o.PeriapsisRadius, IsRadial = o.IsRadial,
    };

    private static OrbitalElements RestoreOrbit(OrbitalSaveV2 o) => new()
    {
        SemiMajorAxis = o.SemiMajorAxis, Eccentricity = o.Eccentricity,
        Inclination = o.Inclination, LongitudeOfAscendingNode = o.LongitudeOfAscendingNode,
        ArgumentOfPeriapsis = o.ArgumentOfPeriapsis,
        MeanAnomalyAtEpoch = o.MeanAnomalyAtEpoch, Epoch = o.Epoch,
        ReferenceBodyId = o.ReferenceBodyId,
        SpecificAngularMomentum = o.SpecificAngularMomentum,
        PeriapsisRadius = o.PeriapsisRadius, IsRadial = o.IsRadial,
    };

    private static IEnumerable<double> VesselFiniteValues(VesselSaveV2 v) =>
    [
        v.Position.X, v.Position.Y, v.Position.Z, v.Velocity.X, v.Velocity.Y, v.Velocity.Z,
        v.Orientation.W, v.Orientation.X, v.Orientation.Y, v.Orientation.Z,
        v.AngularVelocity.X, v.AngularVelocity.Y, v.AngularVelocity.Z,
        v.Throttle, v.PitchYawRoll.X, v.PitchYawRoll.Y, v.PitchYawRoll.Z,
        v.GroundNormal.X, v.GroundNormal.Y, v.GroundNormal.Z, v.GroundOffset,
        v.CrashImpactSpeed, v.CrashPosition.X, v.CrashPosition.Y, v.CrashPosition.Z,
    ];

    private static IEnumerable<double> PartFiniteValues(PartSaveV2 p) =>
    [
        p.LiquidFuel, p.Oxidizer, p.SolidFuel, p.Monopropellant, p.ElectricCharge,
        p.Temperature, p.SkinTemperature, p.ThermalDamage, p.ThrottleLevel,
        p.GimbalOffset.X, p.GimbalOffset.Y, p.GimbalOffset.Z, p.ActiveEngineFraction,
    ];

    private static IEnumerable<double> EngineFiniteValues(EngineInstanceSaveV2 e) =>
    [
        e.StateElapsedSeconds,
        e.CommandedThrottle,
        e.ActualThrottle,
        e.GimbalDeg.X,
        e.GimbalDeg.Y,
        e.GimbalDeg.Z,
        e.GimbalVelocityDegPerS.X,
        e.GimbalVelocityDegPerS.Y,
        e.GimbalVelocityDegPerS.Z,
        e.ChamberPressureFraction ?? e.ActualThrottle,
        e.TemperatureK,
    ];

    private static void RequireFinite(double value, string field)
    {
        if (!double.IsFinite(value)) throw new InvalidDataException($"Non-finite value in '{field}'.");
    }

    private static void RequireId(string value, string kind)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Missing stable {kind} id.");
    }
}

public static class SaveGameV2Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string Serialize(SaveGameV2 save)
    {
        SaveGameV2Codec.Validate(save);
        return JsonSerializer.Serialize(save, Options);
    }

    public static SaveGameV2 DeserializeOrMigrate(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
        if (document.RootElement.TryGetProperty("SchemaVersion", out _)
            || document.RootElement.TryGetProperty("schemaVersion", out _))
        {
            var save = JsonSerializer.Deserialize<SaveGameV2>(json, Options)
                ?? throw new InvalidDataException("Save file is empty.");
            SaveGameV2Codec.Validate(save);
            return save;
        }
        return MigrateLegacy(document.RootElement);
    }

    private static SaveGameV2 MigrateLegacy(JsonElement root)
    {
        var save = new SaveGameV2
        {
            SimulationTime = ReadDouble(root, "CurrentTime"),
            ActiveVesselId = ReadString(root, "ActiveVesselId"),
        };
        if (root.TryGetProperty("Vessels", out var vessels))
        {
            foreach (var old in vessels.EnumerateArray())
            {
                save.Vessels.Add(new VesselSaveV2
                {
                    Id = ReadString(old, "Id") ?? Guid.NewGuid().ToString(),
                    Name = ReadString(old, "Name") ?? "Migrated Vessel",
                    Position = ReadVector(old, "Position"),
                    Velocity = ReadVector(old, "Velocity"),
                    Orientation = new QuaternionSaveV2
                    {
                        W = ReadDouble(old, "OrientationW", 1.0),
                        X = ReadDouble(old, "OrientationX"),
                        Y = ReadDouble(old, "OrientationY"),
                        Z = ReadDouble(old, "OrientationZ"),
                    },
                    IsOnRails = ReadBool(old, "IsOnRails"),
                    ReferenceBodyId = ReadString(old, "ReferenceBodyId"),
                    SasEnabled = true,
                });
            }
        }
        // Old saves generated IDs on load. Preserve the requested selection where possible.
        if (save.ActiveVesselId != null && save.Vessels.All(v => v.Id != save.ActiveVesselId))
            save.ActiveVesselId = save.Vessels.FirstOrDefault()?.Id;
        SaveGameV2Codec.Validate(save);
        return save;
    }

    private static VectorSaveV2 ReadVector(JsonElement value, string prefix) => new()
    {
        X = ReadDouble(value, prefix + "X"),
        Y = ReadDouble(value, prefix + "Y"),
        Z = ReadDouble(value, prefix + "Z"),
    };

    private static double ReadDouble(JsonElement value, string name, double fallback = 0.0) =>
        value.TryGetProperty(name, out var property) && property.TryGetDouble(out double result)
            ? result : fallback;
    private static string? ReadString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) ? property.GetString() : null;
    private static bool ReadBool(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;
}
