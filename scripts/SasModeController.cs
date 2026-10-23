namespace Exosphere.Game;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Godot;

/// <summary>
/// Pilot SAS hold modes (KSP-style): [Y] / [Shift+Y] cycle stability, prograde, retrograde,
/// normal, anti-normal, radial out/in, target, anti-target and maneuver. A pointing mode
/// writes the attitude command each frame from <see cref="SasHold"/>; the stick always wins,
/// and any other autopilot (ascent, EDL, maneuver executor, Mission Control) takes priority.
/// </summary>
public partial class SasModeController : Node
{
    public static SasModeController? Instance { get; private set; }

    private static readonly SasHoldMode[] Cycle =
    {
        SasHoldMode.Stability, SasHoldMode.Prograde, SasHoldMode.Retrograde, SasHoldMode.Normal,
        SasHoldMode.AntiNormal, SasHoldMode.RadialOut, SasHoldMode.RadialIn, SasHoldMode.Target,
        SasHoldMode.AntiTarget, SasHoldMode.Maneuver,
    };

    public SasHoldMode Mode { get; private set; } = SasHoldMode.Stability;

    /// <summary>Direction currently held (world), for the navball marker; null when none.</summary>
    public Vector3d? HeldDirection { get; private set; }

    /// <summary>True while a pointing mode is writing the attitude command.</summary>
    public bool IsHolding { get; private set; }

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (ReferenceEquals(Instance, this)) Instance = null;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Y } key) return;
        var vessel = SimulationBridge.Instance?.ActiveVessel;
        if (vessel == null) return;
        int index = System.Array.IndexOf(Cycle, Mode);
        index = (index + (key.ShiftPressed ? Cycle.Length - 1 : 1)) % Cycle.Length;
        Mode = Cycle[index];
        if (Mode != SasHoldMode.Stability) vessel.SASEnabled = true;
        GD.Print($"[SAS] hold {Label(Mode, UserInterfaceSettings.Language == InterfaceLanguage.Spanish)}");
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        IsHolding = false;
        HeldDirection = null;
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        if (bridge == null || vessel == null || vessel.IsDestroyed) return;
        if (!vessel.SASEnabled) Mode = SasHoldMode.Stability;
        if (Mode == SasHoldMode.Stability) return;

        var body = bridge.Universe.GetDominantBody(vessel.Position);
        var target = DockingTargeting.FindNearest(bridge.Universe, vessel)?.Target;
        HeldDirection = SasHold.Direction(vessel, body, Mode, target, ManeuverDeltaV());
        if (HeldDirection is not { } direction || OtherAutopilotFlying() || PilotHasStick()) return;
        IsHolding = true;
        vessel.PitchYawRoll = SasHold.Command(vessel, body, direction);
    }

    /// <summary>The planned burn's inertial Δv: a transfer node first, then a map node.</summary>
    private static Vector3d? ManeuverDeltaV()
    {
        if (TransferPlanner.Instance?.CurrentNode is { } node) return node.DeltaV;
        var planner = MapViewController.Instance?.Planner;
        return planner is { HasNode: true } ? planner.DeltaVInertial() : null;
    }

    private static bool OtherAutopilotFlying() =>
        AscentController.Instance is { } ascent && (ascent.IsEngaged || ascent.IsAssistEngaged)
        || EDLController.Instance?.IsPresentationActive == true
        || ManeuverExecutor.Instance?.IsExecuting == true
        || HistoricalFlightProfileController.Instance?.IsEngaged == true;

    private static bool PilotHasStick() =>
        Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.A)
        || Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Q) || Input.IsKeyPressed(Key.E);

    public static string Label(SasHoldMode mode, bool spanish) => mode switch
    {
        SasHoldMode.Stability => spanish ? "ESTABILIDAD" : "STABILITY",
        SasHoldMode.Prograde => "PROGRADE",
        SasHoldMode.Retrograde => spanish ? "RETRÓGRADO" : "RETROGRADE",
        SasHoldMode.Normal => "NORMAL",
        SasHoldMode.AntiNormal => spanish ? "ANTINORMAL" : "ANTI-NORMAL",
        SasHoldMode.RadialOut => spanish ? "RADIAL AFUERA" : "RADIAL OUT",
        SasHoldMode.RadialIn => spanish ? "RADIAL ADENTRO" : "RADIAL IN",
        SasHoldMode.Target => spanish ? "OBJETIVO" : "TARGET",
        SasHoldMode.AntiTarget => spanish ? "ANTIOBJETIVO" : "ANTI-TARGET",
        SasHoldMode.Maneuver => spanish ? "NODO" : "MANEUVER",
        _ => mode.ToString().ToUpperInvariant(),
    };
}
