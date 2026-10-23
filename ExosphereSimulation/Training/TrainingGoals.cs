namespace Exosphere.Simulation.Training;

/// <summary>Whether a drill's goal is met by the flown state.</summary>
public static class TrainingGoals
{
    public static bool IsMet(TrainingDefinition training, Universe universe, Vessel player)
    {
        ArgumentNullException.ThrowIfNull(training);
        ArgumentNullException.ThrowIfNull(universe);
        ArgumentNullException.ThrowIfNull(player);
        if (player.IsDestroyed) return false;
        var body = universe.GetDominantBody(player.Position);
        var orbit = OrbitalElements.FromStateVector(player.Position - body.Position, player.Velocity - body.Velocity,
            body.GM, body.Id, universe.CurrentTime);
        double periapsisAltitude = orbit.Periapsis - body.Radius;
        double apoapsisAltitude = orbit.Apoapsis - body.Radius;
        return training.Goal switch
        {
            TrainingGoal.CircularOrbit => body.Id == "earth" && orbit.Eccentricity < 0.01 && periapsisAltitude > 150_000.0,
            TrainingGoal.Docked => universe.DockingConnections.Any(connection =>
                connection.PrimaryVesselId == player.Id || connection.SecondaryVesselId == player.Id),
            TrainingGoal.LunarOrbit => body.Id == "moon" && orbit.Eccentricity < 1.0
                && periapsisAltitude > 20_000.0 && apoapsisAltitude < 500_000.0,
            TrainingGoal.SoftLanding => body.Id == "moon" && player.IsSurfaceSettled,
            _ => false,
        };
    }
}
