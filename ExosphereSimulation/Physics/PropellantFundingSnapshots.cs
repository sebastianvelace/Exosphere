namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Parts;

public enum LiquidPropellantKind { LiquidFuel, Oxidizer }

/// <summary>An actual gross store debit; a null header index identifies a main tank.</summary>
public readonly record struct LiquidStoreWithdrawal(
    string PartInstanceId, LiquidPropellantKind Resource, int? HeaderTankIndex,
    string? HeaderTankId, int? FeedPoolIndex, double BeforeKg, double AfterKg)
{
    public double MassKg => BeforeKg - AfterKg;
}

/// <summary>
/// Prepared liquid-engine consumption, constant from step start until DurationSeconds.
/// A feed-pool identity associates consumers and stores without inventing a pipe route.
/// This is resource funding, not a measurement of pressure-varying delivered impulse.
/// </summary>
public readonly record struct FundedLiquidEngine(
    string PartInstanceId, string EngineInstanceId, int FeedPoolIndex,
    double LiquidFuelFlowKgPerS, double OxidizerFlowKgPerS, double DurationSeconds)
{
    public double LiquidFuelKg => LiquidFuelFlowKgPerS * DurationSeconds;
    public double OxidizerKg => OxidizerFlowKgPerS * DurationSeconds;
    public double MassKg => LiquidFuelKg + OxidizerKg;
}

/// <summary>
/// Immutable liquid-engine funding for one preparation, before any practice replenishment.
/// It excludes solid/monopropellant engines, RCS, disturbance flow and venting.
/// No exhaust geometry or open-system transport is inferred from these receipts.
/// </summary>
public sealed class LiquidEngineFundingSnapshot
{
    public double StepDurationSeconds { get; }
    public bool UsesInfinitePropellantAssist { get; }
    public IReadOnlyList<LiquidStoreWithdrawal> Withdrawals { get; }
    public IReadOnlyList<FundedLiquidEngine> Engines { get; }
    public double WithdrawnMassKg { get; }
    public double FundedMassKg { get; }
    public double MassBalanceResidualKg => WithdrawnMassKg - FundedMassKg;

    internal LiquidEngineFundingSnapshot(double dt, bool assist,
        IEnumerable<LiquidStoreWithdrawal> withdrawals, IEnumerable<FundedLiquidEngine> engines)
    {
        StepDurationSeconds = dt;
        UsesInfinitePropellantAssist = assist;
        Withdrawals = Array.AsReadOnly(withdrawals.ToArray());
        Engines = Array.AsReadOnly(engines.ToArray());
        WithdrawnMassKg = Withdrawals.Sum(draw => draw.MassKg);
        FundedMassKg = Engines.Sum(engine => engine.MassKg);
    }
}

/// <summary>
/// Immutable main-tank vent debit. Direct mass-only calls have no declared duration.
/// A timed call funds the requested constant-rate valve interval until the open load runs out.
/// No vent location, exhaust velocity or recoil is established here.
/// </summary>
public sealed class MainPropellantVentSnapshot
{
    public bool UsesInfinitePropellantAssist { get; }
    public double RequestedMassKg { get; }
    public double FundedMassKg { get; }
    public double? StepDurationSeconds { get; }
    public double? FundedDurationSeconds => StepDurationSeconds * (FundedMassKg / RequestedMassKg);
    public IReadOnlyList<LiquidStoreWithdrawal> Withdrawals { get; }
    public double WithdrawnMassKg { get; }
    public double MassBalanceResidualKg => WithdrawnMassKg - FundedMassKg;

    internal MainPropellantVentSnapshot(double requestedMass, double fundedMass, double? dt,
        bool assist, IEnumerable<LiquidStoreWithdrawal> withdrawals)
    {
        RequestedMassKg = requestedMass;
        FundedMassKg = fundedMass;
        StepDurationSeconds = dt;
        UsesInfinitePropellantAssist = assist;
        Withdrawals = Array.AsReadOnly(withdrawals.ToArray());
        WithdrawnMassKg = Withdrawals.Sum(draw => draw.MassKg);
    }
}

// Mutable preparation-only scratch. Published snapshots contain copied value records,
// never references to live parts, header arrays or reusable engine-demand buffers.
internal sealed class LiquidFundingCapture
{
    private readonly List<(Part Part, LiquidPropellantKind Resource, int? Header, double Before)> _stores = new();
    private readonly List<LiquidStoreWithdrawal> _withdrawals = new();
    internal List<FundedLiquidEngine> Engines { get; } = new();
    private int _nextPool;

    internal int BeginPool(IReadOnlyList<Part> parts, bool includeHeaders)
    {
        _stores.Clear();
        foreach (var part in parts)
        {
            _stores.Add((part, LiquidPropellantKind.LiquidFuel, null, part.LiquidFuel));
            _stores.Add((part, LiquidPropellantKind.Oxidizer, null, part.Oxidizer));
            if (!includeHeaders) continue;
            for (int i = 0; i < part.HeaderPropellantKg.Length && i < part.Definition.HeaderTanks.Count; i++)
            {
                var header = part.Definition.HeaderTanks[i];
                if (header.IsLiquidFuel || header.IsOxidizer)
                    _stores.Add((part, header.IsOxidizer ? LiquidPropellantKind.Oxidizer
                        : LiquidPropellantKind.LiquidFuel, i, part.HeaderPropellantKg[i]));
            }
        }
        return _nextPool++;
    }

    internal void EndPool(int? pool)
    {
        foreach (var (part, resource, header, before) in _stores)
        {
            double after = header.HasValue ? part.HeaderPropellantKg[header.Value]
                : resource == LiquidPropellantKind.LiquidFuel ? part.LiquidFuel : part.Oxidizer;
            if (after < before)
                _withdrawals.Add(new(part.InstanceId, resource, header,
                    header.HasValue ? part.Definition.HeaderTanks[header.Value].Id : null,
                    pool, before, after));
        }
        _stores.Clear();
    }

    internal LiquidEngineFundingSnapshot? BuildEngines(double dt, bool assist) =>
        Engines.Count == 0 && _withdrawals.Count == 0 ? null
            : new(dt, assist, _withdrawals, Engines);

    internal MainPropellantVentSnapshot BuildVent(double requestedMass, double fundedMass,
        double? dt, bool assist) => new(requestedMass, fundedMass, dt, assist, _withdrawals);
}
