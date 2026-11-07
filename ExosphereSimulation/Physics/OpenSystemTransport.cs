namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>
/// One outlet of a flow group: its exit-plane centre, outward normal and radius in the body
/// (skirt-datum) frame, and its relative weight while it is open on [start, end).
/// </summary>
public readonly record struct TransportOutlet(Vector3d ExitBodyM, Vector3d ExitNormalBody, double ExitRadiusM,
    double WeightKgPerS, double StartSeconds, double EndSeconds);

/// <summary>A store debit feeding a flow group: a constant rate on [start, end).</summary>
public readonly record struct TransportStoreDebit(int ComponentIndex, double RateKgPerS,
    double StartSeconds, double EndSeconds);

/// <summary>
/// Stores and outlets that exchange mass only with each other (one engine feed pool, an RCS
/// system, a vent). At every instant the open outlets share the group's store outflow in
/// proportion to their weights, so mass leaving the stores leaves the vehicle exactly.
/// </summary>
public sealed class TransportFlowGroup
{
    public string Id { get; }
    public IReadOnlyList<TransportStoreDebit> Debits { get; }
    public IReadOnlyList<TransportOutlet> Outlets { get; }

    public TransportFlowGroup(string id, IEnumerable<TransportStoreDebit> debits, IEnumerable<TransportOutlet> outlets)
    {
        Id = id;
        Debits = debits.ToArray();
        Outlets = outlets.ToArray();
        foreach (var debit in Debits)
            if (!double.IsFinite(debit.RateKgPerS) || debit.RateKgPerS < 0 || !(debit.EndSeconds > debit.StartSeconds))
                throw new ArgumentException($"Flow group '{id}' has an invalid store debit.", nameof(debits));
        foreach (var outlet in Outlets)
        {
            if (!double.IsFinite(outlet.WeightKgPerS) || outlet.WeightKgPerS < 0 || !(outlet.EndSeconds > outlet.StartSeconds)
                || !double.IsFinite(outlet.ExitRadiusM) || outlet.ExitRadiusM < 0)
                throw new ArgumentException($"Flow group '{id}' has an invalid outlet.", nameof(outlets));
            DynamicsInputs.RequireFinite(outlet.ExitBodyM, nameof(outlets));
        }
    }

    internal static bool Active(double start, double end, double time, MassBoundarySide side) =>
        side == MassBoundarySide.Right ? time >= start && time < end : time > start && time <= end;
}

/// <summary>The open-system wrench beside thrust: body-frame transport force and torque.</summary>
/// <remarks>
/// <see cref="JetDampingTensorBody"/> is İ + J_out about the instantaneous centre of mass: the
/// moment −(İ + J_out)·ω is the vehicle's jet damping (with no relative swirl), in N·m·s/rad;
/// <see cref="OutflowTensorBody"/> is J_out alone.
/// </remarks>
public readonly record struct OpenSystemTransportSample(
    Vector3d ForceBodyN, Vector3d TorqueBodyNm,
    Vector3d RelativeLinearMomentumBodyKgMps, Vector3d RelativeAngularMomentumBodyKgM2PerS,
    double OutflowKgPerS, Matrix3x3d JetDampingTensorBody, Matrix3x3d OutflowTensorBody);

/// <summary>
/// Variable-mass transport for one physics step (F4). Mass properties come from the shared
/// component decomposition (<see cref="BodyMassDistribution"/>); flow leaves each store at its
/// loss point (a settled column's free surface, or a fixed shape's centroid), runs inside the
/// tank to the store outlet (the column floor), then straight to the nozzle exit.
/// <para>That path is the declared internal-flow convention. Its linear momentum
/// p_rel = Σ q (e − t) satisfies continuity exactly, p_rel = m·ċ + Σ_k Q_k (e_k − c); its
/// angular momentum about the centre of mass is h_rel = Σ q [(t − c) × (b − c) + (b − c) × (e − c)].
/// Real feed lines bend; only their end points enter p_rel, while h_rel depends on the route.</para>
/// <para>Force: F = m(c̈ + 2ω×ċ) − ṗ_rel − 2ω×p_rel. Torque: −İω − J_out ω − ω×h_rel − ḣ_rel −
/// ċ×p_rel (<see cref="VariableMassRotationalDynamics"/>). Thrust and its moment stay separate.</para>
/// </summary>
public sealed class OpenSystemTransport
{
    public BodyMassDistribution Distribution { get; }
    public IReadOnlyList<TransportFlowGroup> Groups { get; }
    private readonly List<MassOutflow> _outflowScratch = new();
    private readonly double[] _storeRateScratch;

    public OpenSystemTransport(BodyMassDistribution distribution, IEnumerable<TransportFlowGroup> groups)
    {
        Distribution = distribution ?? throw new ArgumentNullException(nameof(distribution));
        Groups = groups.ToArray();
        foreach (var group in Groups)
            foreach (var debit in group.Debits)
                if (debit.ComponentIndex < 0 || debit.ComponentIndex >= distribution.Components.Count)
                    throw new ArgumentException($"Flow group '{group.Id}' names an unknown store.", nameof(groups));
        _storeRateScratch = new double[distribution.Components.Count];
    }

    /// <summary>Store loss point t and outlet b of a component at mass m.</summary>
    private static (Vector3d Loss, Vector3d Outlet) StorePoints(FixedShapeMassComponent component, double mass) =>
        component.IsColumn
            ? (component.PositionBodyM + component.ColumnAxisBody * (mass / component.ColumnLinearDensityKgPerM),
                component.PositionBodyM)
            : (component.PositionBodyM, component.PositionBodyM);

    /// <summary>
    /// Partitions the same routed momentum by debited component. Each route follows its
    /// store at fragmentation; angular contributions use the datum, not the moving CoM.
    /// Caller-owned buffers avoid per-step allocations for this persistent bookkeeping.
    /// </summary>
    public void SampleMomentumByComponent(double time, MassBoundarySide side,
        Span<Vector3d> linearBody, Span<Vector3d> angularAboutDatumBody)
    {
        if (linearBody.Length < Distribution.Components.Count
            || angularAboutDatumBody.Length < Distribution.Components.Count)
            throw new ArgumentException("Momentum buffers do not cover the mass components.");
        linearBody.Clear();
        angularAboutDatumBody.Clear();
        foreach (var group in Groups)
        {
            double rate = 0;
            foreach (var debit in group.Debits)
                if (TransportFlowGroup.Active(debit.StartSeconds, debit.EndSeconds, time, side))
                    rate += debit.RateKgPerS;
            if (rate == 0) continue;
            double weights = 0;
            var exit = Vector3d.Zero;
            foreach (var outlet in group.Outlets)
                if (TransportFlowGroup.Active(outlet.StartSeconds, outlet.EndSeconds, time, side))
                {
                    weights += outlet.WeightKgPerS;
                    exit += outlet.ExitBodyM * outlet.WeightKgPerS;
                }
            if (!(weights > 0))
                throw new InvalidOperationException($"Flow group '{group.Id}' loses mass with no open outlet.");
            exit /= weights;
            foreach (var debit in group.Debits)
            {
                if (!TransportFlowGroup.Active(debit.StartSeconds, debit.EndSeconds, time, side)) continue;
                var component = Distribution.Components[debit.ComponentIndex];
                var (loss, outlet) = StorePoints(component, component.SampleMass(time, side).Mass);
                linearBody[debit.ComponentIndex] += (exit - loss) * debit.RateKgPerS;
                angularAboutDatumBody[debit.ComponentIndex] +=
                    (loss.Cross(outlet) + outlet.Cross(exit)) * debit.RateKgPerS;
            }
        }
    }

    public OpenSystemTransportSample Sample(double time, MassBoundarySide side, BodyMassSample mass,
        Vector3d angularVelocityBody)
    {
        var c = mass.CenterOfMassBodyM;
        var cRate = mass.CenterOfMassRateBodyMps;
        Array.Clear(_storeRateScratch);
        var components = Distribution.Components;
        var p = Vector3d.Zero; var h = Vector3d.Zero; var pRate = Vector3d.Zero; var hRate = Vector3d.Zero;
        double outflow = 0;
        _outflowScratch.Clear();
        double jxx = 0, jyy = 0, jzz = 0, jxy = 0, jxz = 0, jyz = 0;
        foreach (var group in Groups)
        {
            double groupRate = 0;
            var weightedOutlet = Vector3d.Zero;
            foreach (var debit in group.Debits)
            {
                if (!TransportFlowGroup.Active(debit.StartSeconds, debit.EndSeconds, time, side)) continue;
                groupRate += debit.RateKgPerS;
                _storeRateScratch[debit.ComponentIndex] += debit.RateKgPerS;
                var component = components[debit.ComponentIndex];
                double storeMass = component.SampleMass(time, side).Mass;
                weightedOutlet += StorePoints(component, storeMass).Outlet * debit.RateKgPerS;
            }
            if (groupRate == 0) continue;
            double weights = 0;
            foreach (var outlet in group.Outlets)
                if (TransportFlowGroup.Active(outlet.StartSeconds, outlet.EndSeconds, time, side))
                    weights += outlet.WeightKgPerS;
            if (!(weights > 0))
                throw new InvalidOperationException($"Flow group '{group.Id}' loses mass with no open outlet.");
            var outletMean = weightedOutlet / groupRate;
            foreach (var outlet in group.Outlets)
            {
                if (!TransportFlowGroup.Active(outlet.StartSeconds, outlet.EndSeconds, time, side)) continue;
                double q = groupRate * outlet.WeightKgPerS / weights;
                if (q == 0) continue;
                var e = outlet.ExitBodyM;
                // Duct from the flow-weighted store outlet to this exit.
                p += (e - outletMean) * q;
                h += (outletMean - c).Cross(e - c) * q;
                hRate += ((-cRate).Cross(e - c) + (outletMean - c).Cross(-cRate)) * q;
                _outflowScratch.Add(new MassOutflow(q, e - c, outlet.ExitNormalBody, outlet.ExitRadiusM));
                outflow += q;
                // J_out = q[(r·r)E − r rᵀ] + q a²/4 (E + n nᵀ) for a uniform circular exit.
                var r = e - c;
                double disk = q * MassOutflow.UniformDiskSecondMomentFactor * outlet.ExitRadiusM * outlet.ExitRadiusM;
                var n = outlet.ExitRadiusM > 0 ? outlet.ExitNormalBody.Normalized : Vector3d.Zero;
                double rr = r.Dot(r);
                jxx += q * (rr - r.X * r.X) + disk * (1 + n.X * n.X);
                jyy += q * (rr - r.Y * r.Y) + disk * (1 + n.Y * n.Y);
                jzz += q * (rr - r.Z * r.Z) + disk * (1 + n.Z * n.Z);
                jxy += -q * r.X * r.Y + disk * n.X * n.Y;
                jxz += -q * r.X * r.Z + disk * n.X * n.Z;
                jyz += -q * r.Y * r.Z + disk * n.Y * n.Z;
            }
        }
        // Inside each store: from its loss point to its outlet.
        for (int i = 0; i < components.Count; i++)
        {
            double q = _storeRateScratch[i];
            if (q == 0) continue;
            var component = components[i];
            double storeMass = component.SampleMass(time, side).Mass;
            var (loss, outlet) = StorePoints(component, storeMass);
            if (loss == outlet) continue;
            var lossRate = component.ColumnAxisBody * (-q / component.ColumnLinearDensityKgPerM);
            p += (outlet - loss) * q;
            pRate -= lossRate * q;
            h += (loss - c).Cross(outlet - c) * q;
            hRate += ((lossRate - cRate).Cross(outlet - c) + (loss - c).Cross(-cRate)) * q;
        }
        var torque = VariableMassRotationalDynamics.ComputeTransportTorqueBody(angularVelocityBody,
            mass.InertiaRateBodyKgM2PerS, _outflowScratch, h, hRate, cRate, p);
        var force = (mass.CenterOfMassAccelerationBodyMps2 + angularVelocityBody.Cross(cRate) * 2.0) * mass.MassKg
            - pRate - angularVelocityBody.Cross(p) * 2.0;
        var rate = mass.InertiaRateBodyKgM2PerS;
        var damping = new Matrix3x3d(
            rate.M11 + jxx, rate.M12 + jxy, rate.M13 + jxz,
            rate.M21 + jxy, rate.M22 + jyy, rate.M23 + jyz,
            rate.M31 + jxz, rate.M32 + jyz, rate.M33 + jzz);
        var flux = new Matrix3x3d(jxx, jxy, jxz, jxy, jyy, jyz, jxz, jyz, jzz);
        return new(force, torque, p, h, outflow, damping, flux);
    }
}
