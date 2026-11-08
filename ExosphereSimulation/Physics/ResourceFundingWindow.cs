namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

public enum PropellantResourceKind { LiquidFuel, Oxidizer, SolidFuel, Monopropellant, OtherHeader }
public enum ResourceWithdrawalSource { LiquidEngine, OtherEngine, MainVent, ReactionControl, Disturbance }
public readonly record struct ResourceStoreId(string PartInstanceId, PropellantResourceKind Resource, int? HeaderIndex = null);
public readonly record struct ResourceStoreLoad(ResourceStoreId Store, string? HeaderId, double MassKg);
public readonly record struct FundedStoreDebit(ResourceStoreId Store, ResourceWithdrawalSource Source,
    double BeforeKg, double AfterKg, double StartTimeSeconds, double EndTimeSeconds)
{
    public double MassKg => BeforeKg - AfterKg;
}

public readonly record struct FundedSingleResourceEngine(string PartInstanceId, string EngineInstanceId,
    PropellantResourceKind Resource, int FeedPoolIndex, double MassFlowKgPerS, double DurationSeconds);

/// <summary>
/// One preparation's complete resource subinventory, with explicitly attributed gross debits.
/// Stores are committed once; sampling never reads live parts or infers unrecorded flow.
/// Engine and vent reservations are simultaneous prescribed withdrawals from their funded
/// budgets, despite the sequential preparation used to reserve those budgets.
/// </summary>
public sealed class ResourceFundingWindow
{
    public double DurationSeconds { get; }
    public int TopologyVersion { get; }
    public bool UsesInfinitePropellantAssist { get; }
    public IReadOnlyList<ResourceStoreLoad> InitialStores { get; }
    public IReadOnlyList<ResourceStoreLoad> FinalStores { get; }
    public IReadOnlyList<FundedStoreDebit> Debits { get; }
    public string? CoverageError { get; }
    public bool HasCompleteCoverage => CoverageError == null;
    public LiquidEngineFundingSnapshot? LiquidEngines { get; }
    public MainPropellantVentSnapshot? Vent { get; }
    public RcsPropellantFundingSnapshot? ReactionControl { get; }
    public IReadOnlyList<FundedSingleResourceEngine> OtherEngines { get; }

    internal ResourceFundingWindow(ResourceFundingCapture capture, ResourceStoreLoad[] final)
    {
        DurationSeconds = capture.DurationSeconds;
        TopologyVersion = capture.TopologyVersion;
        UsesInfinitePropellantAssist = capture.Assist;
        InitialStores = Array.AsReadOnly(capture.InitialStores);
        FinalStores = Array.AsReadOnly(final);
        Debits = Array.AsReadOnly(capture.Debits.ToArray());
        CoverageError = capture.CoverageError;
        LiquidEngines = capture.LiquidEngines;
        Vent = capture.Vent;
        ReactionControl = capture.ReactionControl;
        OtherEngines = Array.AsReadOnly(capture.OtherEngines.ToArray());
    }

    /// <summary>
    /// Builds one store's fixed-shape profile, including overlapping reserved withdrawals.
    /// Untouched resources remain explicit constant components. Geometry and dry structure
    /// belong to the caller; this method alone does not construct a complete vessel.
    /// </summary>
    public FixedShapeMassComponent CreateStoreMassComponent(ResourceStoreId store, string id,
        Vector3d stationBodyM, Matrix3x3d specificInertiaBodyM2)
    {
        if (!HasCompleteCoverage) throw new InvalidOperationException(CoverageError);
        if (UsesInfinitePropellantAssist)
            throw new InvalidOperationException("Assisted gross flow requires a separate replenishment transition.");
        var initial = InitialStores.Single(load => load.Store == store);
        var final = FinalStores.Single(load => load.Store == store);
        var debits = Debits.Where(debit => debit.Store == store).ToArray();
        var boundaries = new SortedSet<double> { 0, DurationSeconds };
        foreach (var debit in debits)
        {
            if (!double.IsFinite(debit.StartTimeSeconds) || !double.IsFinite(debit.EndTimeSeconds)
                || debit.StartTimeSeconds < 0 || debit.EndTimeSeconds > DurationSeconds
                || debit.StartTimeSeconds >= debit.EndTimeSeconds)
                throw new InvalidOperationException("The debit interval is not representable within this window.");
            boundaries.Add(debit.StartTimeSeconds);
            boundaries.Add(debit.EndTimeSeconds);
        }
        var knots = new List<MassKnot>(boundaries.Count);
        foreach (double time in boundaries)
        {
            // Sum only positive remaining withdrawals onto the final load. This avoids
            // subtracting almost equal bulk masses near exhaustion. Preserve exact ends.
            double mass = final.MassKg;
            foreach (var debit in debits)
                mass += time <= debit.StartTimeSeconds ? debit.MassKg
                    : time >= debit.EndTimeSeconds ? 0
                    : debit.MassKg / (debit.EndTimeSeconds - debit.StartTimeSeconds) * (debit.EndTimeSeconds - time);
            if (time == 0) mass = initial.MassKg;
            if (time == DurationSeconds) mass = final.MassKg;
            if (!double.IsFinite(mass) || mass < 0 || (knots.Count > 0 && mass > knots[^1].MassKg))
                throw new InvalidOperationException("The withdrawal profile cannot preserve monotone finite store mass.");
            knots.Add(new(time, mass));
        }
        return new(id, stationBodyM, specificInertiaBodyM2, knots);
    }
}

// Preparation-only tracking. Exact before/after identities establish coverage without a
// numerical tolerance that could conceal a missing consumer, refill or topology change.
internal sealed class ResourceFundingCapture
{
    internal double DurationSeconds { get; }
    internal int TopologyVersion { get; }
    internal bool Assist { get; }
    internal ResourceStoreLoad[] InitialStores { get; }
    internal List<FundedStoreDebit> Debits { get; } = new();
    internal string? CoverageError { get; private set; }
    internal LiquidEngineFundingSnapshot? LiquidEngines { get; private set; }
    internal MainPropellantVentSnapshot? Vent { get; private set; }
    internal RcsPropellantFundingSnapshot? ReactionControl { get; set; }
    internal List<FundedSingleResourceEngine> OtherEngines { get; } = new();
    private readonly Dictionary<ResourceStoreId, double> _loads;
    private ResourceStoreLoad[] _scratch = [];

    internal ResourceFundingCapture(IReadOnlyList<Part> parts, double duration, bool assist, int topologyVersion)
    {
        DurationSeconds = duration;
        TopologyVersion = topologyVersion;
        Assist = assist;
        InitialStores = ReadStores(parts);
        _loads = new Dictionary<ResourceStoreId, double>(InitialStores.Length);
        foreach (var load in InitialStores)
        {
            if (!_loads.TryAdd(load.Store, load.MassKg)) CoverageError = "Duplicate resource store identity.";
            if (!double.IsFinite(load.MassKg) || load.MassKg < 0) CoverageError = "Invalid initial resource inventory.";
        }
        if (!double.IsFinite(duration) || duration <= 0) CoverageError = "Invalid resource window duration.";
    }

    internal void RecordLiquid(LiquidEngineFundingSnapshot? receipt)
    {
        LiquidEngines = receipt;
        if (receipt == null) return;
        foreach (var depletion in receipt.StoreDepletions)
        {
            var draw = depletion.Withdrawal;
            Record(new(draw.PartInstanceId, draw.Resource == LiquidPropellantKind.LiquidFuel
                ? PropellantResourceKind.LiquidFuel : PropellantResourceKind.Oxidizer, draw.HeaderTankIndex),
                ResourceWithdrawalSource.LiquidEngine, draw.BeforeKg, draw.AfterKg,
                depletion.StartTimeSeconds, depletion.EndTimeSeconds);
        }
    }

    internal void RecordVent(MainPropellantVentSnapshot receipt)
    {
        Vent = receipt;
        foreach (var draw in receipt.Withdrawals)
            Record(new(draw.PartInstanceId, draw.Resource == LiquidPropellantKind.LiquidFuel
                ? PropellantResourceKind.LiquidFuel : PropellantResourceKind.Oxidizer),
                ResourceWithdrawalSource.MainVent, draw.BeforeKg, draw.AfterKg,
                0, receipt.FundedDurationSeconds ?? double.NaN);
    }

    internal void RecordCurrentStores(IReadOnlyList<Part> parts, ResourceWithdrawalSource source,
        PropellantResourceKind? allowedResource = null)
    {
        // Same stores, same order as ReadStores, written into a reused buffer: this runs
        // several times per physics step and must not allocate.
        int count = CountStores(parts);
        if (_scratch.Length < count) _scratch = new ResourceStoreLoad[count];
        FillStores(parts, _scratch);
        for (int i = 0; i < count; i++)
        {
            var load = _scratch[i];
            if (!_loads.TryGetValue(load.Store, out double before))
            { CoverageError = "Resource topology changed during preparation."; continue; }
            if (before == load.MassKg) continue;
            if (load.Store.Resource != allowedResource)
            { CoverageError = "An unattributed resource mutation occurred during preparation."; continue; }
            Record(load.Store, source, before, load.MassKg, 0, DurationSeconds);
        }
    }

    internal void RecordSingleResourceDebit(Part part, PropellantResourceKind resource,
        double before, double after, double duration)
    {
        if (after < before) Record(new(part.InstanceId, resource), ResourceWithdrawalSource.OtherEngine,
            before, after, 0, duration);
    }

    private void Record(ResourceStoreId store, ResourceWithdrawalSource source, double before,
        double after, double start, double end)
    {
        if (!_loads.TryGetValue(store, out double tracked) || tracked != before
            || !double.IsFinite(before) || !double.IsFinite(after) || after < 0 || after >= before)
            CoverageError = "A resource debit does not match its recorded store state.";
        _loads[store] = after;
        Debits.Add(new(store, source, before, after, start, end));
    }

    internal ResourceFundingWindow Complete(IReadOnlyList<Part> parts, int topologyVersion)
    {
        var final = ReadStores(parts);
        if (topologyVersion != TopologyVersion) CoverageError = "Part topology changed during preparation.";
        bool unrecorded = final.Length != InitialStores.Length;
        for (int i = 0; !unrecorded && i < final.Length; i++)
            unrecorded = !_loads.TryGetValue(final[i].Store, out double tracked) || tracked != final[i].MassKg;
        if (unrecorded) CoverageError = "The final resource inventory contains unrecorded changes.";
        return new(this, final);
    }

    private static ResourceStoreLoad[] ReadStores(IReadOnlyList<Part> parts)
    {
        var stores = new ResourceStoreLoad[CountStores(parts)];
        FillStores(parts, stores);
        return stores;
    }

    private static int CountStores(IReadOnlyList<Part> parts)
    {
        int count = 0;
        for (int p = 0; p < parts.Count; p++) count += 4 + parts[p].HeaderPropellantKg.Length;
        return count;
    }

    /// <summary>Every store of every part, in part order: the four main resources, then headers.</summary>
    private static void FillStores(IReadOnlyList<Part> parts, ResourceStoreLoad[] into)
    {
        int n = 0;
        for (int p = 0; p < parts.Count; p++)
        {
            var part = parts[p];
            into[n++] = new(new(part.InstanceId, PropellantResourceKind.LiquidFuel), null, part.LiquidFuel);
            into[n++] = new(new(part.InstanceId, PropellantResourceKind.Oxidizer), null, part.Oxidizer);
            into[n++] = new(new(part.InstanceId, PropellantResourceKind.SolidFuel), null, part.SolidFuel);
            into[n++] = new(new(part.InstanceId, PropellantResourceKind.Monopropellant), null, part.Monopropellant);
            for (int i = 0; i < part.HeaderPropellantKg.Length; i++)
            {
                var header = i < part.Definition.HeaderTanks.Count ? part.Definition.HeaderTanks[i] : null;
                var kind = header?.IsLiquidFuel == true ? PropellantResourceKind.LiquidFuel
                    : header?.IsOxidizer == true ? PropellantResourceKind.Oxidizer : PropellantResourceKind.OtherHeader;
                into[n++] = new(new(part.InstanceId, kind, i), header?.Id, part.HeaderPropellantKg[i]);
            }
        }
    }
}
