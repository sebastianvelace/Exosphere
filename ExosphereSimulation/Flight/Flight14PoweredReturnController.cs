namespace Exosphere.Simulation.Flight;

/// <summary>Composes continuous launch/entry with the estimated terminal-burn diagnostic.</summary>
public sealed class Flight14PoweredReturnController(Vessel ship, CelestialBody body,
    Flight14DescentController descent, Flight14LaunchController launch,
    Flight14LandingDefinition definition, string seaLevelEngineModelId) : IPhysicsStepController
{
    public Flight14LandingBurn? Landing { get; private set; }
    public string? BlockReason => descent.Phase == Flight14DescentPhase.Blocked
        ? "descent:"+descent.BlockReason : Landing?.BlockReason;
    public bool RequiresFixedCadence(Universe universe) => BlockReason == null
        && Landing?.Phase is not (Flight14LandingPhase.TerminalReached or Flight14LandingPhase.SplashdownReached);
    public void BeforePhysicsStep(Universe universe, double interval)
    {
        if (BlockReason != null || Landing?.Phase is Flight14LandingPhase.TerminalReached or Flight14LandingPhase.SplashdownReached) return;
        if (Landing == null)
        {
            descent.BeforePhysicsStep(universe, interval);
            if (descent.Phase != Flight14DescentPhase.DescentReached) return;
            Landing = new Flight14LandingBurn(ship, body, definition, seaLevelEngineModelId, launch.LiftoffEpoch);
        }
        Landing.Advance(universe, interval);
    }
}
