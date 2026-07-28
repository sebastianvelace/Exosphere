namespace Exosphere.Simulation.Persistence;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

/// <summary>
/// Pure serialize/restore for mid-mission saves. Used by the Godot <c>SaveSystem</c>
/// wrapper and by xUnit roundtrip tests (no Godot Instance required).
/// </summary>
public static class MissionSaveSerializer
{
    public static MissionSaveState Capture(
        Universe universe,
        string? missionPhase = null,
        int warpIndex = 0)
    {
        var state = new MissionSaveState
        {
            CurrentTime    = universe.CurrentTime,
            ActiveVesselId = universe.ActiveVessel?.Id,
            MissionPhase   = missionPhase ?? "PRE_LAUNCH",
            WarpIndex      = warpIndex,
        };

        foreach (var vessel in universe.Vessels)
            state.Vessels.Add(CaptureVessel(vessel));

        return state;
    }

    public static void Restore(
        Universe universe,
        MissionSaveState state,
        IReadOnlyDictionary<string, PartDefinition> partDefs)
    {
        ArgumentNullException.ThrowIfNull(universe);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(partDefs);

        universe.SetCurrentTime(state.CurrentTime);

        foreach (var existing in universe.Vessels.ToList())
            universe.RemoveVessel(existing);
        universe.ActiveVessel = null;

        Vessel? active = null;
        foreach (var vs in state.Vessels)
        {
            var vessel = RestoreVessel(vs, partDefs);
            universe.AddVessel(vessel);
            if (!string.IsNullOrEmpty(state.ActiveVesselId) && vs.Id == state.ActiveVesselId)
                active = vessel;
        }

        universe.ActiveVessel = active ?? universe.Vessels.FirstOrDefault();
    }

    /// <summary>
    /// Convenience restore that loads part definitions from a filesystem directory
    /// (e.g. <c>data/parts</c>).
    /// </summary>
    public static void RestoreFromPartsDirectory(
        Universe universe,
        MissionSaveState state,
        string partsDirectory)
    {
        var defs = PartDefinition.LoadAllFromDirectory(partsDirectory);
        Restore(universe, state, defs);
    }

    private static VesselSaveState CaptureVessel(Vessel vessel)
    {
        var parts = vessel.Parts.Parts;
        var partIndex = new Dictionary<Part, int>(parts.Count);
        for (int i = 0; i < parts.Count; i++)
            partIndex[parts[i]] = i;

        int rootIndex = 0;
        if (vessel.Parts.Root != null && partIndex.TryGetValue(vessel.Parts.Root, out int ri))
            rootIndex = ri;

        var vs = new VesselSaveState
        {
            Id              = vessel.Id,
            Name            = vessel.Name,
            PositionX       = vessel.Position.X,
            PositionY       = vessel.Position.Y,
            PositionZ       = vessel.Position.Z,
            VelocityX       = vessel.Velocity.X,
            VelocityY       = vessel.Velocity.Y,
            VelocityZ       = vessel.Velocity.Z,
            OrientationW    = vessel.Orientation.W,
            OrientationX    = vessel.Orientation.X,
            OrientationY    = vessel.Orientation.Y,
            OrientationZ    = vessel.Orientation.Z,
            IsOnRails       = vessel.IsOnRails,
            ReferenceBodyId = vessel.ReferenceBodyId ?? "",
            Throttle        = vessel.Throttle,
            SASEnabled      = vessel.SASEnabled,
            IsGroundHeld    = vessel.IsGroundHeld,
            GroundNormalX   = vessel.GroundNormal.X,
            GroundNormalY   = vessel.GroundNormal.Y,
            GroundNormalZ   = vessel.GroundNormal.Z,
            GroundOffset    = vessel.GroundOffset,
            RootIndex       = rootIndex,
        };

        foreach (var part in parts)
        {
            vs.Parts.Add(new PartSaveState
            {
                DefinitionId    = part.Definition.Id,
                LiquidFuel      = part.LiquidFuel,
                Oxidizer        = part.Oxidizer,
                SolidFuel       = part.SolidFuel,
                Monopropellant  = part.Monopropellant,
                Temperature     = part.Temperature,
                IsStagingActive = part.IsStagingActive,
                IsBroken        = part.IsBroken,
            });
        }

        foreach (var joint in vessel.Parts.Joints)
        {
            if (!partIndex.TryGetValue(joint.Parent, out int pi)
                || !partIndex.TryGetValue(joint.Child, out int ci))
                continue;
            vs.Joints.Add(new JointSaveState
            {
                ParentIndex  = pi,
                ChildIndex   = ci,
                ParentNodeId = joint.ParentNodeId,
                ChildNodeId  = joint.ChildNodeId,
            });
        }

        return vs;
    }

    private static Vessel RestoreVessel(
        VesselSaveState vs,
        IReadOnlyDictionary<string, PartDefinition> partDefs)
    {
        var vessel = Vessel.CreateWithId(
            string.IsNullOrWhiteSpace(vs.Id) ? Guid.NewGuid().ToString() : vs.Id);
        vessel.Name            = vs.Name;
        vessel.Position        = new Vector3d(vs.PositionX, vs.PositionY, vs.PositionZ);
        vessel.Velocity        = new Vector3d(vs.VelocityX, vs.VelocityY, vs.VelocityZ);
        vessel.Orientation     = new Quaterniond(
            vs.OrientationW, vs.OrientationX, vs.OrientationY, vs.OrientationZ);
        vessel.IsOnRails       = vs.IsOnRails;
        vessel.OrbitalState    = null;
        vessel.ReferenceBodyId = string.IsNullOrEmpty(vs.ReferenceBodyId) ? "earth" : vs.ReferenceBodyId;
        vessel.Throttle        = vs.Throttle;
        vessel.SASEnabled      = vs.SASEnabled;
        vessel.IsGroundHeld    = vs.IsGroundHeld;
        vessel.GroundNormal    = new Vector3d(vs.GroundNormalX, vs.GroundNormalY, vs.GroundNormalZ);
        vessel.GroundOffset    = vs.GroundOffset;

        var liveParts = new List<Part>(vs.Parts.Count);
        foreach (var ps in vs.Parts)
        {
            if (!partDefs.TryGetValue(ps.DefinitionId, out var def))
                throw new InvalidOperationException(
                    $"Unknown part definition '{ps.DefinitionId}' while restoring vessel '{vs.Id}'.");

            var part = new Part(def)
            {
                LiquidFuel      = ps.LiquidFuel,
                Oxidizer        = ps.Oxidizer,
                SolidFuel       = ps.SolidFuel,
                Monopropellant  = ps.Monopropellant,
                Temperature     = ps.Temperature,
                IsStagingActive = ps.IsStagingActive,
                IsBroken        = ps.IsBroken,
            };
            liveParts.Add(part);
            vessel.Parts.AddPart(part);
        }

        if (liveParts.Count > 0)
        {
            int root = vs.RootIndex;
            if (root < 0 || root >= liveParts.Count) root = 0;
            vessel.Parts.SetRoot(liveParts[root]);
        }

        foreach (var js in vs.Joints)
        {
            if (js.ParentIndex < 0 || js.ParentIndex >= liveParts.Count
                || js.ChildIndex < 0 || js.ChildIndex >= liveParts.Count)
                continue;
            vessel.Parts.AddJoint(new Joint(
                liveParts[js.ParentIndex],
                liveParts[js.ChildIndex],
                js.ParentNodeId,
                js.ChildNodeId));
        }

        vessel.ConfigureLandingContactsFromParts();
        return vessel;
    }
}
