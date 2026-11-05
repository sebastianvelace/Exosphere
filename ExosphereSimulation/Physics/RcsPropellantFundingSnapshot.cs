namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

/// <summary>A physical store's gross withdrawal before any practice-assist replenishment.</summary>
public readonly record struct RcsPropellantWithdrawal(string PartInstanceId, double BeforeKg, double AfterKg)
{
    public double MassKg => BeforeKg - AfterKg;
}

/// <summary>
/// One funded thruster's consumption and declared nozzle point in the skirt-datum frame.
/// A point location does not establish exit area, velocity profile or internal feed geometry.
/// </summary>
public readonly record struct FundedRcsThruster(
    string PartInstanceId, string ThrusterId, Vector3d NozzlePositionBodyM,
    Vector3d ForceDirectionBody, double MassFlowKgPerS, double DurationSeconds)
{
    public double MassKg => MassFlowKgPerS * DurationSeconds;
}

/// <summary>
/// Immutable evidence of one successfully funded RCS allocation. Stores and consumers
/// remain separate: the shared pool does not establish a particular internal flow path.
/// This snapshot does not activate jet damping or infer outflow from net vessel mass.
/// </summary>
public sealed class RcsPropellantFundingSnapshot
{
    public double DurationSeconds { get; }
    public bool UsesInfinitePropellantAssist { get; }
    public IReadOnlyList<RcsPropellantWithdrawal> Withdrawals { get; }
    public IReadOnlyList<FundedRcsThruster> Thrusters { get; }
    public double WithdrawnMassKg { get; }
    public double OutflowMassKg { get; }
    /// <summary>Signed roundoff from store subtraction versus summed allocated flow.</summary>
    public double MassBalanceResidualKg => WithdrawnMassKg - OutflowMassKg;

    private RcsPropellantFundingSnapshot(double duration, bool assist,
        List<RcsPropellantWithdrawal> withdrawals, List<FundedRcsThruster> thrusters)
    {
        DurationSeconds = duration;
        UsesInfinitePropellantAssist = assist;
        Withdrawals = Array.AsReadOnly(withdrawals.ToArray());
        Thrusters = Array.AsReadOnly(thrusters.ToArray());
        WithdrawnMassKg = withdrawals.Sum(draw => draw.MassKg);
        OutflowMassKg = thrusters.Sum(flow => flow.MassKg);
    }

    internal static RcsPropellantFundingSnapshot Capture(IReadOnlyList<Part> parts,
        double[] before, IReadOnlyList<RcsThrusterGeometry> geometry, double[] duty,
        double dt, bool assist)
    {
        var withdrawals = new List<RcsPropellantWithdrawal>();
        for (int i = 0; i < parts.Count; i++)
            if (parts[i].Monopropellant < before[i])
                withdrawals.Add(new(parts[i].InstanceId, before[i], parts[i].Monopropellant));
        var flows = new List<FundedRcsThruster>();
        for (int i = 0; i < geometry.Count; i++)
            if (duty[i] > 0 && geometry[i].MassFlowKgPerS > 0)
                flows.Add(new(geometry[i].Owner.InstanceId, geometry[i].ThrusterId,
                    geometry[i].PositionLocal, geometry[i].DirectionLocal,
                    geometry[i].MassFlowKgPerS * duty[i], dt));
        return new(dt, assist, withdrawals, flows);
    }
}
