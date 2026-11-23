namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Ties the side-acceleration plant declared in the catch estimate to the full-physics ship.
/// An upright ship on one, two or three Raptors near their floor is commanded a side
/// acceleration step and its reversal through the catch burn's own actuation path (axis
/// pointing, then throttle for the vertical need); the declared model must reproduce the
/// measured side acceleration, including the thrust-vector reversal that starts it the
/// wrong way.
/// </summary>
public sealed class LateralPlantIdentificationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1, 1.0)]
    [InlineData(1, 3.0)]
    [InlineData(2, 1.0)]
    [InlineData(2, 3.0)]
    [InlineData(3, 1.0)]
    [InlineData(3, 3.0)]
    public void DeclaredPlantReproducesTheBenchStepResponse(int engines, double amplitude)
    {
        var definition = TowerCatchBurnTests.Bench.Definition();
        var bench = TowerCatchBurnTests.Bench.Create(380.0, 0.0, 0.0, massKg: engines == 1 ? 190_000.0 : 205_000.0);
        var ship = bench.Ship;
        var up = ship.CatchTargetUpWorld.Normalized;
        var east = bench.East;
        ship.Position = ship.CatchTargetPositionWorld + up * 3000.0 - east * 3000.0;
        ship.Velocity = ship.CatchTargetVelocityWorld - up * 20.0;
        ship.Orientation = AerodynamicsModel.ComputeBellyFirstOrientation(up, -east);
        ship.AngularVelocity = Vector3d.Zero;
        var parts = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("ship_engines"));
        // Vertical need near each count's floor, as the catch flies them.
        double verticalExtra = engines switch { 1 => 0.0, 2 => 2.0, _ => 5.0 };
        var model = new TowerCatchLateralPlanner(definition.LateralPlant(engines));
        const double dt = 0.02;
        double sumSquared = 0.0, firstSecondMeasured = 0.0, firstSecondModel = 0.0;
        int samples = 0;
        for (int i = 0; i < 1200; i++)
        {
            double t = i * dt;
            double command = t < 4.0 ? 0.0 : t < 12.0 ? amplitude : t < 20.0 ? -amplitude : 0.0;
            var relative = ship.Velocity - ship.CatchTargetVelocityWorld;
            var before = relative - up * relative.Dot(up);
            double gravity = bench.Earth.GM / (ship.Position - bench.Earth.Position).MagnitudeSquared;
            var thrustAcceleration = up * (gravity + verticalExtra) + east * command;
            double pressure = ship.GetAmbientPressure(bench.Earth);
            parts.SelectEngineCount(engines);
            ship.SASEnabled = false; ship.PilotRateEnvelopeEnabled = false; ship.FlapCommandOverride = null;
            ship.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(ship.Orientation, Vector3d.Up,
                thrustAcceleration.Normalized, ship.AngularVelocity, definition.ProportionalAttitudeGain,
                definition.AttitudeDampingGain, rollDampingGain: definition.RollDampingGain);
            double axisUp = System.Math.Max(0.5, ship.Orientation.Rotate(Vector3d.Up).Normalized.Dot(up));
            ship.Throttle = parts.ApplyThrottleFloor(System.Math.Max(parts.Definition.MinThrottle,
                (gravity + verticalExtra) * ship.TotalMass / axisUp
                    / System.Math.Max(1, parts.GetFullThrottleThrustMagnitude(pressure))));
            bench.Universe.Tick(dt);
            model.Advance(east * command, dt);
            relative = ship.Velocity - ship.CatchTargetVelocityWorld;
            double measured = ((relative - up * relative.Dot(up)) - before).Dot(east) / dt;
            double predicted = model.ModelAcceleration.Dot(east);
            if (t >= 3.0 && t < 24.0)
            {
                sumSquared += (measured - predicted) * (measured - predicted);
                samples++;
            }
            if (t >= 4.0 && t < 5.0)
            {
                firstSecondMeasured += measured * dt;
                firstSecondModel += predicted * dt;
            }
        }

        double relativeRms = System.Math.Sqrt(sumSquared / samples) / amplitude;
        output.WriteLine($"engines={engines} amplitude={amplitude} relativeRms={relativeRms:F3} "
            + $"firstSecond measured={firstSecondMeasured:F2} model={firstSecondModel:F2} m/s");
        // The tilt reversal: in the first second the ship moves against the command, as the model says.
        Assert.True(firstSecondMeasured < 0.0 && firstSecondModel < 0.0);
        Assert.InRange(relativeRms, 0.0, engines == 3 ? 0.45 : 0.25);
    }
}
