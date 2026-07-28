namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;

/// <summary>
/// Autopiloto que orienta la nave hacia la dirección del Δv y ejecuta el burn
/// al ser invocado, entregando el Δv planificado por un <see cref="ManeuverNode"/>.
/// </summary>
public partial class ManeuverExecutor : Node
{
    public static ManeuverExecutor? Instance { get; private set; }

    public bool   IsExecuting  { get; private set; }
    public double RemainingDv  { get; private set; }
    public double TimeToBurn =>
        _node is null || SimulationBridge.Instance?.Universe is not { } universe
            ? 0.0
            : System.Math.Max(0.0, _ignitionTime - universe.CurrentTime);
    public string BurnLabel => _node?.BurnLabel ?? "BURN";
    public double EstimatedBurnDuration { get; private set; }

    private ManeuverNode? _node;
    private bool          _orientationLocked;
    private bool          _restoreSas;
    private double        _ignitionTime;

    public override void _Ready() => Instance = this;

    /// <summary>Comienza la secuencia de burn para el nodo dado.</summary>
    public void ExecuteNode(ManeuverNode node)
    {
        _node              = node;
        RemainingDv        = node.DvMagnitude * node.DvAdjustFactor;
        IsExecuting        = true;
        _orientationLocked = false;

        var vessel = SimulationBridge.Instance?.ActiveVessel;
        if (vessel != null)
        {
            _restoreSas       = vessel.SASEnabled;
            vessel.SASEnabled = false;

            double maximumThrust = vessel.Parts.GetMaximumThrust(0.0);
            EstimatedBurnDuration =
                maximumThrust > 0.0 && vessel.TotalMass > 0.0
                    ? RemainingDv / (maximumThrust / vessel.TotalMass)
                    : 0.0;
        }
        else
        {
            EstimatedBurnDuration = 0.0;
        }

        // Lambert/Hohmann nodes are impulsive epochs. Centre the finite burn on that
        // epoch using the current full-throttle acceleration estimate instead of
        // starting a multi-minute TLI only after the planned state has passed.
        _ignitionTime = node.BurnTime - 0.5 * EstimatedBurnDuration;

        GD.Print($"[ManeuverExecutor] Executing burn Δv={RemainingDv:F0} m/s " +
                 $"for {node.TargetBodyId}; ignition T=" +
                 $"{_ignitionTime:F1}, duration≈{EstimatedBurnDuration:F1} s");
    }

    /// <summary>Cancela el burn en curso.</summary>
    public void Abort()
    {
        if (!IsExecuting) return;
        var vessel = SimulationBridge.Instance?.ActiveVessel;
        if (vessel != null)
        {
            vessel.Throttle    = 0.0;
            vessel.PitchYawRoll = Vector3d.Zero;
            vessel.SASEnabled  = _restoreSas;
        }
        IsExecuting = false;
        _node       = null;
        GD.Print("[ManeuverExecutor] Aborted.");
    }

    public override void _Process(double delta)
    {
        if (!IsExecuting || _node == null) return;

        var bridge   = SimulationBridge.Instance;
        var vessel   = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (vessel == null || universe == null) { Abort(); return; }

        // ── Orientación: alinear +Y del vessel con la dirección del Δv ───────
        var burnDir = _node.DeltaV.Normalized;
        if (burnDir.MagnitudeSquared < 0.01) { Abort(); return; }

        // Eje longitudinal actual del vessel en espacio mundo (+Y local)
        var currentNose = vessel.Orientation.Rotate(Vector3d.Up);
        double alignment = currentNose.Dot(burnDir);

        vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
            vessel.Orientation, Vector3d.Up, burnDir, vessel.AngularVelocity);

        if (!_orientationLocked)
        {
            if (alignment < 0.998 || vessel.AngularVelocity.Magnitude > 0.03)
            {
                vessel.Throttle = 0.0;
                return; // no quemar hasta estar alineado
            }
            _orientationLocked = true;
        }

        // Si perdemos alineación durante el burn, re-alinear
        if (alignment < 0.95)
        {
            _orientationLocked = false;
            vessel.Throttle = 0.0;
            return;
        }

        // Transfer nodes may live in a future orbital window. Enter arms and aligns
        // the vehicle, but engines remain inhibited until the absolute burn time instead
        // of applying the correct Lambert vector at the wrong orbital phase.
        if (universe.CurrentTime < _ignitionTime)
        {
            vessel.Throttle = 0.0;
            return;
        }

        vessel.Throttle = 1.0;

        // ── Contabilidad del Δv entregado ─────────────────────────────────────
        var refBody = universe.GetDominantBody(vessel.Position);
        double thrust = vessel.ComputeThrust(refBody).Magnitude;
        double mass   = vessel.TotalMass;
        if (mass > 0.0)
        {
            // Usar el timestep escalado igual que el AutopilotController
            double simStep = delta * universe.TimeScale;
            RemainingDv -= thrust / mass * simStep;
        }

        if (RemainingDv <= 0.0)
        {
            vessel.Throttle    = 0.0;
            vessel.PitchYawRoll = Vector3d.Zero;
            vessel.SASEnabled  = _restoreSas;
            IsExecuting        = false;
            _node              = null;
            GD.Print("[ManeuverExecutor] Burn complete.");
        }
    }

    // Shortest-arc quaternion que rota `from` hacia `to` (ambos se normalizan).
    private static Quaterniond ShortestArc(Vector3d from, Vector3d to)
    {
        var f = from.Normalized;
        var t = to.Normalized;
        double dot = f.Dot(t);
        if (dot >  0.99999) return Quaterniond.Identity;
        if (dot < -0.99999)
        {
            Vector3d axis = System.Math.Abs(f.X) < 0.9
                ? f.Cross(Vector3d.Right)
                : f.Cross(Vector3d.Up);
            return Quaterniond.FromAxisAngle(axis.Normalized, System.Math.PI);
        }
        var   a     = f.Cross(t).Normalized;
        double angle = System.Math.Acos(System.Math.Clamp(dot, -1.0, 1.0));
        return Quaterniond.FromAxisAngle(a, angle);
    }
}
