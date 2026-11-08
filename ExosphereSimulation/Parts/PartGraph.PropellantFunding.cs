namespace Exosphere.Simulation.Parts;

using Exosphere.Simulation.Physics;

public partial class PartGraph
{
    /// <summary>Opt-in liquid-engine and main-vent receipts; physical behavior is unchanged.</summary>
    public bool CapturePropellantFunding { get; set; }
    public LiquidEngineFundingSnapshot? LastLiquidEngineFunding { get; private set; }
    public MainPropellantVentSnapshot? LastMainVentFunding { get; private set; }
    private LiquidFundingCapture? _liquidFundingCapture;
    private LiquidFundingCapture? _engineFundingScratch, _ventFundingScratch;
    internal ResourceFundingCapture? ResourceFundingCapture { get; set; }
}
