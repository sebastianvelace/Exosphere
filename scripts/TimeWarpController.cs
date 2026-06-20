namespace Exosphere.Game;

using Godot;

public partial class TimeWarpController : Node
{
    public double CurrentRate => SimulationBridge.Instance != null
        ? SimulationBridge.WarpLevels[SimulationBridge.Instance.WarpIndex]
        : 1.0;

    [Signal] public delegate void WarpChangedEventHandler(double newRate);

    public override void _Ready()
    {
        EmitSignal(SignalName.WarpChanged, CurrentRate);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed)
        {
            bool handled = true;
            switch (key.Keycode)
            {
                case Key.Period:
                    WarpUp();
                    break;
                case Key.Comma:
                    WarpDown();
                    break;
                case Key.Backspace:
                    ResetToRealTime();
                    break;
                default:
                    handled = false;
                    break;
            }
            if (handled) GetViewport().SetInputAsHandled();
        }
    }

    public void WarpUp()
    {
        var bridge = SimulationBridge.Instance;
        if (bridge == null) return;
        bridge.SetWarpIndex(bridge.WarpIndex + 1);
        EmitSignal(SignalName.WarpChanged, CurrentRate);
    }

    public void WarpDown()
    {
        var bridge = SimulationBridge.Instance;
        if (bridge == null) return;
        bridge.SetWarpIndex(bridge.WarpIndex - 1);
        EmitSignal(SignalName.WarpChanged, CurrentRate);
    }

    public void SetWarpRate(double rate)
    {
        for (int i = 0; i < SimulationBridge.WarpLevels.Length; i++)
        {
            if (System.Math.Abs(SimulationBridge.WarpLevels[i] - rate) < 0.01)
            {
                SimulationBridge.Instance?.SetWarpIndex(i);
                EmitSignal(SignalName.WarpChanged, CurrentRate);
                return;
            }
        }
    }

    public void ResetToRealTime()
    {
        SimulationBridge.Instance?.SetWarpIndex(0);
        EmitSignal(SignalName.WarpChanged, CurrentRate);
    }
}
