namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

/// <summary>
/// Production adapter of the F4 open-system model for one coupled physics step. It reads the
/// step's complete resource window (initial and final loads, attributed debits and receipts),
/// builds the vehicle's continuous mass distribution from the shared
/// <see cref="PartMassGeometry"/> decomposition and groups every outflow with the stores that
/// feed it. Construction fails, and the caller keeps the frozen-mass step, when the window is
/// incomplete, the practice assist replenishes stores, or a flow has no declared outlet.
/// </summary>
public sealed class VariableMassStepModel
{
    public OpenSystemTransport Transport { get; }
    public BodyMassDistribution Distribution => Transport.Distribution;
    /// <summary>Rigidly attached members (docked vessels) folded in as constant components.</summary>
    public int AttachedMemberCount { get; }
    private readonly IReadOnlyDictionary<ResourceStoreId, int> _storeIndex;

    private VariableMassStepModel(OpenSystemTransport transport, int attached,
        IReadOnlyDictionary<ResourceStoreId, int> storeIndex)
    {
        Transport = transport;
        AttachedMemberCount = attached;
        _storeIndex = storeIndex;
    }

    internal void CaptureMomentumShares(double time, MassBoundarySide side, Vessel vessel)
    {
        int count = Distribution.Components.Count;
        if (vessel.TransportLinearScratch.Length < count)
            vessel.TransportLinearScratch = new Vector3d[count];
        if (vessel.TransportAngularScratch.Length < count)
            vessel.TransportAngularScratch = new Vector3d[count];
        Transport.SampleMomentumByComponent(time, side, vessel.TransportLinearScratch.AsSpan(0, count),
            vessel.TransportAngularScratch.AsSpan(0, count));
        var shares = vessel.TransportMomentumShares ??= [];
        shares.Clear();
        foreach (var (store, index) in _storeIndex)
        {
            var linear = vessel.TransportLinearScratch[index];
            var angular = vessel.TransportAngularScratch[index];
            if (linear != Vector3d.Zero || angular != Vector3d.Zero)
                shares.Add(new(store.PartInstanceId, linear, angular));
        }
    }

    /// <summary>Why the last build attempt fell back to frozen mass (null when it succeeded).</summary>
    public static bool TryBuild(PartGraph parts, ResourceFundingWindow? window, Vessel vessel,
        IReadOnlyList<RigidlyAttachedMember>? attached, out VariableMassStepModel? model, out string? reason)
    {
        model = null;
        if (window == null) { reason = "no-resource-window"; return false; }
        if (!window.HasCompleteCoverage) { reason = "incomplete-coverage: " + window.CoverageError; return false; }
        if (window.UsesInfinitePropellantAssist) { reason = "infinite-propellant-assist"; return false; }
        // Universe already discards models without flow. Avoid constructing and validating
        // an entire constant distribution on every coast tick before discarding it.
        if (window.Debits.Count == 0) { reason = "no-outflow"; return false; }
        double dt = window.DurationSeconds;
        var positions = parts.GetTopologyPartLocalPositionsForMass();
        double fallbackLength = System.Math.Max(1.0, parts.VehicleLength / System.Math.Max(1, parts.PartList.Count));
        double fallbackDiameter = System.Math.Max(1.0, parts.MaximumDiameter);

        var components = new List<FixedShapeMassComponent>();
        var storeIndex = new Dictionary<ResourceStoreId, int>();
        var initial = window.InitialStores.ToDictionary(load => load.Store, load => load.MassKg);
        var partsById = new Dictionary<string, Part>(StringComparer.Ordinal);
        foreach (var part in parts.PartList)
        {
            partsById[part.InstanceId] = part;
            if (!positions.TryGetValue(part, out var position)) continue;
            var definition = part.Definition;
            if (part.EffectiveMassDry > 0)
            {
                var shape = PartMassGeometry.DryShape(definition, fallbackLength, fallbackDiameter);
                components.Add(new($"{part.InstanceId}:dry", position + Vector3d.Up * shape.CenterYM(0),
                    Tensor(shape, part.EffectiveMassDry), [new MassKnot(0, part.EffectiveMassDry)]));
            }
            AddMain(part, position, PartMassComponentKind.LiquidFuel, PropellantResourceKind.LiquidFuel);
            AddMain(part, position, PartMassComponentKind.Oxidizer, PropellantResourceKind.Oxidizer);
            AddMain(part, position, PartMassComponentKind.SolidFuel, PropellantResourceKind.SolidFuel);
            AddMain(part, position, PartMassComponentKind.Monopropellant, PropellantResourceKind.Monopropellant);
            var headers = definition.HeaderTanks;
            for (int i = 0; i < part.HeaderPropellantKg.Length; i++)
            {
                var header = i < headers.Count ? headers[i] : null;
                var kind = header?.IsLiquidFuel == true ? PropellantResourceKind.LiquidFuel
                    : header?.IsOxidizer == true ? PropellantResourceKind.Oxidizer : PropellantResourceKind.OtherHeader;
                var store = new ResourceStoreId(part.InstanceId, kind, i);
                double station = header?.CenterYM ?? 0.0;
                AddStore(store, $"{part.InstanceId}:header:{i}", position + Vector3d.Up * station,
                    Matrix3x3d.Diagonal(0, 0, 0), null);
            }
        }
        int attachedCount = 0;
        if (attached != null)
            foreach (var member in attached)
            {
                // A docked vessel is a constant rigid member of the composite this step.
                var props = member.MassProperties;
                var center = member.DatumPositionPrimaryLocal + member.OrientationPrimaryLocal.Rotate(props.CenterOfMassBody);
                var tensor = RotateTensor(props.InertiaBody, member.OrientationPrimaryLocal);
                components.Add(new($"attached:{attachedCount++}", center, Scale(tensor, 1.0 / props.Mass),
                    [new MassKnot(0, props.Mass)]));
            }

        // Flow groups.
        var groups = new List<TransportFlowGroup>();
        var liquid = window.LiquidEngines;
        var liquidDebits = window.Debits.Where(debit => debit.Source == ResourceWithdrawalSource.LiquidEngine).ToArray();
        if (liquid != null && liquidDebits.Length > 0)
        {
            if (liquidDebits.Length != liquid.StoreDepletions.Count) { reason = "liquid-receipt-mismatch"; return false; }
            var pools = liquid.Engines.Select(engine => engine.FeedPoolIndex)
                .Concat(liquid.StoreDepletions.Select(depletion => depletion.Withdrawal.FeedPoolIndex ?? -1)).Distinct();
            foreach (int pool in pools)
            {
                var debits = new List<TransportStoreDebit>();
                for (int i = 0; i < liquidDebits.Length; i++)
                    if ((liquid.StoreDepletions[i].Withdrawal.FeedPoolIndex ?? -1) == pool && !TryDebit(liquidDebits[i], debits))
                    { reason = "liquid-store-not-positioned"; return false; }
                var outlets = new List<TransportOutlet>();
                foreach (var engine in liquid.Engines.Where(engine => engine.FeedPoolIndex == pool))
                {
                    if (engine.DurationSeconds <= 0) continue;
                    if (!TryEngineOutlet(engine.PartInstanceId, engine.EngineInstanceId,
                        engine.LiquidFuelFlowKgPerS + engine.OxidizerFlowKgPerS, engine.DurationSeconds, outlets))
                    { reason = "engine-outlet-undeclared"; return false; }
                }
                if (debits.Count > 0) groups.Add(new($"liquid-pool:{pool}", debits, outlets));
            }
        }
        var otherDebits = window.Debits.Where(debit => debit.Source == ResourceWithdrawalSource.OtherEngine).ToList();
        if (otherDebits.Count > 0)
        {
            var debits = new List<TransportStoreDebit>();
            foreach (var debit in otherDebits)
                if (!TryDebit(debit, debits)) { reason = "engine-store-not-positioned"; return false; }
            var outlets = new List<TransportOutlet>();
            foreach (var engine in window.OtherEngines)
                if (engine.DurationSeconds > 0 && !TryEngineOutlet(engine.PartInstanceId, engine.EngineInstanceId,
                        engine.MassFlowKgPerS, engine.DurationSeconds, outlets))
                { reason = "engine-outlet-undeclared"; return false; }
            groups.Add(new("other-engines", debits, outlets));
        }
        var ventDebits = window.Debits.Where(debit => debit.Source == ResourceWithdrawalSource.MainVent).ToList();
        if (ventDebits.Count > 0)
        {
            var debits = new List<TransportStoreDebit>();
            var outlets = new List<TransportOutlet>();
            foreach (var debit in ventDebits)
            {
                if (!TryDebit(debit, debits)) { reason = "vent-store-not-positioned"; return false; }
                var part = partsById[debit.Store.PartInstanceId];
                if (part.Definition.VentOutlet is not { } vent || !positions.TryGetValue(part, out var position))
                { reason = "vent-outlet-undeclared"; return false; }
                var normal = vent.ExhaustDirection;
                outlets.Add(new(position + vent.Position, normal, vent.DiameterM * 0.5,
                    debit.MassKg / (debit.EndTimeSeconds - debit.StartTimeSeconds), debit.StartTimeSeconds, debit.EndTimeSeconds));
            }
            groups.Add(new("main-vent", debits, outlets));
        }
        var rcsDebits = window.Debits.Where(debit => debit.Source == ResourceWithdrawalSource.ReactionControl).ToList();
        if (rcsDebits.Count > 0 && window.ReactionControl is { } rcs)
        {
            var debits = new List<TransportStoreDebit>();
            foreach (var debit in rcsDebits)
                if (!TryDebit(debit, debits)) { reason = "rcs-store-not-positioned"; return false; }
            var outlets = rcs.Thrusters.Where(thruster => thruster.MassFlowKgPerS > 0)
                .Select(thruster => new TransportOutlet(thruster.NozzlePositionBodyM, -thruster.ForceDirectionBody, 0.0,
                    thruster.MassFlowKgPerS, 0.0, thruster.DurationSeconds)).ToList();
            groups.Add(new("rcs", debits, outlets));
        }
        var disturbance = window.Debits.Where(debit => debit.Source == ResourceWithdrawalSource.Disturbance).ToList();
        if (disturbance.Count > 0)
        {
            if (vessel.DisturbanceOutletBodyM is not { } outletPosition || vessel.DisturbanceExhaustDirectionBody is not { } exhaust)
            { reason = "disturbance-outlet-undeclared"; return false; }
            var debits = new List<TransportStoreDebit>();
            foreach (var debit in disturbance)
                if (!TryDebit(debit, debits)) { reason = "disturbance-store-not-positioned"; return false; }
            groups.Add(new("disturbance", debits, [new TransportOutlet(outletPosition, exhaust, 0.0, 1.0, 0.0, dt)]));
        }

        try
        {
            model = new(new OpenSystemTransport(new BodyMassDistribution(dt, components), groups), attachedCount, storeIndex);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            reason = "unrepresentable-profile: " + exception.Message;
            return false;
        }
        reason = null;
        return true;

        void AddMain(Part part, Vector3d position, PartMassComponentKind kind, PropellantResourceKind resource)
        {
            var store = new ResourceStoreId(part.InstanceId, resource);
            var shape = PartMassGeometry.MainStoreShape(part.Definition, kind, fallbackLength, fallbackDiameter);
            if (shape.IsColumn)
                AddStore(store, $"{part.InstanceId}:{resource}", position + Vector3d.Up * shape.ColumnBottomYM,
                    default, shape);
            else
                AddStore(store, $"{part.InstanceId}:{resource}", position + Vector3d.Up * shape.FixedCenterYM,
                    Tensor(shape, 0.0), null);
        }

        void AddStore(ResourceStoreId store, string id, Vector3d station, Matrix3x3d specific, StoreShape? column)
        {
            double before = initial.GetValueOrDefault(store);
            bool debited = window.Debits.Any(debit => debit.Store == store);
            if (before <= 0 && !debited) return;
            var knots = StoreKnots(window, store, before);
            storeIndex[store] = components.Count;
            components.Add(column is { } shape
                ? FixedShapeMassComponent.SettledColumn(id, station, Vector3d.Up, shape.LinearDensityKgPerM, shape.RadiusM, knots)
                : new FixedShapeMassComponent(id, station, specific, knots));
        }

        bool TryDebit(FundedStoreDebit debit, List<TransportStoreDebit> into)
        {
            if (!storeIndex.TryGetValue(debit.Store, out int index)) return false;
            double duration = debit.EndTimeSeconds - debit.StartTimeSeconds;
            if (!(duration > 0)) return false;
            into.Add(new(index, debit.MassKg / duration, debit.StartTimeSeconds, debit.EndTimeSeconds));
            return true;
        }

        bool TryEngineOutlet(string partId, string engineId, double flow, double duration, List<TransportOutlet> into)
        {
            if (!partsById.TryGetValue(partId, out var part) || !positions.TryGetValue(part, out var position)) return false;
            if (!part.TryGetNozzleExit(engineId, out var exit, out var normal, out double radius)) return false;
            into.Add(new(position + exit, normal, radius, flow, 0.0, System.Math.Min(duration, dt)));
            return true;
        }
    }

    /// <summary>Store mass knots: constant outside its debits, affine within each.</summary>
    private static IEnumerable<MassKnot> StoreKnots(ResourceFundingWindow window, ResourceStoreId store, double initialKg)
    {
        var debits = window.Debits.Where(debit => debit.Store == store).ToArray();
        if (debits.Length == 0) return [new MassKnot(0, initialKg)];
        var final = window.FinalStores.Single(load => load.Store == store).MassKg;
        var boundaries = new SortedSet<double> { 0, window.DurationSeconds };
        foreach (var debit in debits) { boundaries.Add(debit.StartTimeSeconds); boundaries.Add(debit.EndTimeSeconds); }
        var knots = new List<MassKnot>(boundaries.Count);
        foreach (double time in boundaries)
        {
            double mass = final;
            foreach (var debit in debits)
                mass += time <= debit.StartTimeSeconds ? debit.MassKg
                    : time >= debit.EndTimeSeconds ? 0
                    : debit.MassKg / (debit.EndTimeSeconds - debit.StartTimeSeconds) * (debit.EndTimeSeconds - time);
            if (time == 0) mass = initialKg;
            if (time == window.DurationSeconds) mass = final;
            if (knots.Count > 0 && mass > knots[^1].MassKg)
                throw new InvalidOperationException("The withdrawal profile is not monotone.");
            knots.Add(new(time, mass));
        }
        return knots;
    }

    private static Matrix3x3d Tensor(StoreShape shape, double mass)
    {
        double transverse = shape.TransverseSpecific(mass);
        return new Matrix3x3d(transverse, 0, 0, 0, shape.AxialSpecific, 0, 0, 0, transverse);
    }

    private static Matrix3x3d Scale(Matrix3x3d m, double s) => new(
        m.M11 * s, m.M12 * s, m.M13 * s, m.M21 * s, m.M22 * s, m.M23 * s, m.M31 * s, m.M32 * s, m.M33 * s);

    private static Matrix3x3d RotateTensor(Matrix3x3d tensor, Quaterniond rotation)
    {
        var ex = rotation.Rotate(new Vector3d(1, 0, 0));
        var ey = rotation.Rotate(new Vector3d(0, 1, 0));
        var ez = rotation.Rotate(new Vector3d(0, 0, 1));
        // R I Rᵀ with R's columns ex, ey, ez.
        Vector3d Column(Vector3d v) => new(
            ex.X * v.X + ey.X * v.Y + ez.X * v.Z,
            ex.Y * v.X + ey.Y * v.Y + ez.Y * v.Z,
            ex.Z * v.X + ey.Z * v.Y + ez.Z * v.Z);
        var c0 = Column(tensor.Multiply(new Vector3d(ex.X, ey.X, ez.X)));
        var c1 = Column(tensor.Multiply(new Vector3d(ex.Y, ey.Y, ez.Y)));
        var c2 = Column(tensor.Multiply(new Vector3d(ex.Z, ey.Z, ez.Z)));
        double xy = 0.5 * (c1.X + c0.Y), xz = 0.5 * (c2.X + c0.Z), yz = 0.5 * (c2.Y + c1.Z);
        return new Matrix3x3d(c0.X, xy, xz, xy, c1.Y, yz, xz, yz, c2.Z);
    }
}
