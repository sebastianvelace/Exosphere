namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

/// <summary>
/// Horizontal guidance that brings the ship over the cradle on the clock of its own descent.
/// A descent decelerating uniformly to rest at the cradle has <c>h = d·T/2</c>, with
/// <c>d</c> the descent speed and <c>T</c> the time left, so the descent itself measures
/// <c>2/T = d/h</c>. A horizontal miss <c>r</c> closed on the same clock at uniform
/// deceleration needs a closing speed <c>r·d/h</c> and a deceleration <c>r·(d/h)²/2</c>: the
/// ship flies a straight line from where it is to the cradle, both channels reaching zero
/// together. That trajectory solves the law exactly with no feedback, so the feedback gain
/// only corrects disturbances and can stay inside the attitude bandwidth of the lit engines,
/// unlike a time-to-go law whose gains grow without bound as <c>T</c> runs out.
/// </summary>
public static class TowerCatchTerminalGuidance
{
    /// <summary>
    /// Uniform deceleration (m/s², upward) that brings the current descent to the terminal
    /// gate: <c>(d² − v_g²) / (2(h − h_g))</c>. Zero once the ship is at or below the gate, or
    /// already slower than the gate speed.
    /// </summary>
    public static double GateDecelerationMps2(TowerCatchBurnDefinition definition, double clearanceM,
        double descentSpeedMps)
    {
        double gate = definition.TerminalProfileClearanceM;
        double gateSpeed = definition.TargetDescentSpeedMps(gate);
        double room = clearanceM - gate;
        if (room <= 0.0 || descentSpeedMps <= gateSpeed) return 0.0;
        return (descentSpeedMps * descentSpeedMps - gateSpeed * gateSpeed) / (2.0 * room);
    }

    /// <summary>
    /// Time (s) the descent takes to the terminal gate at uniform deceleration from the
    /// current state, <c>2(h − h_g)/(d + v_g)</c>. Zero at or below the gate; infinite when
    /// the ship is not descending onto it. The horizontal miss must be closed by then: below
    /// the gate the ship settles almost vertically onto the pins.
    /// </summary>
    public static double TimeToGateS(TowerCatchBurnDefinition definition, double clearanceM, double descentSpeedMps)
    {
        double gate = definition.TerminalProfileClearanceM;
        double gateSpeed = definition.TargetDescentSpeedMps(gate);
        if (clearanceM <= gate) return 0.0;
        if (descentSpeedMps + gateSpeed <= 0.0) return double.PositiveInfinity;
        return 2.0 * (clearanceM - gate) / (descentSpeedMps + gateSpeed);
    }
}
