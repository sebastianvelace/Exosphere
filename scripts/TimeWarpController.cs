namespace Exosphere.Game;

using Godot;

public partial class TimeWarpController : Node
{
    private static readonly double[] WarpRates = [1, 5, 10, 50, 100, 1000, 10000, 100000];

    private int _warpIndex = 0;
    public double CurrentRate => WarpRates[_warpIndex];

    [Signal] public delegate void WarpChangedEventHandler(double newRate);

    public override void _Ready()
    {
        ApplyWarp();
    }

    public void WarpUp()
    {
        if (_warpIndex >= WarpRates.Length - 1) return;
        if (!CanWarpUp()) return;
        _warpIndex++;
        ApplyWarp();
    }

    public void WarpDown()
    {
        if (_warpIndex <= 0) return;
        _warpIndex--;
        ApplyWarp();
    }

    public void SetWarpRate(double rate)
    {
        for (int i = 0; i < WarpRates.Length; i++)
        {
            if (System.Math.Abs(WarpRates[i] - rate) < 0.01)
            {
                _warpIndex = i;
                ApplyWarp();
                return;
            }
        }
    }

    public void ResetToRealTime()
    {
        _warpIndex = 0;
        ApplyWarp();
    }

    private void ApplyWarp()
    {
        var bridge = SimulationBridge.Instance;
        if (bridge != null)
            bridge.SetTimeScale(WarpRates[_warpIndex]);
        EmitSignal(SignalName.WarpChanged, WarpRates[_warpIndex]);
    }

    // Restricciones de warp: no aumentar warp si:
    // 1. El vessel está en atmósfera densa
    // 2. Hay thrust activo
    // 3. Aceleración estructural alta
    private bool CanWarpUp()
    {
        var bridge   = SimulationBridge.Instance;
        var vessel   = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (vessel == null || universe == null) return false;

        // No warpar con thrust
        if (vessel.Throttle > 0.01) return false;

        // No warpar en atmósfera densa (density > 0.01 kg/m³)
        var refBody = universe.GetDominantBody(vessel.Position);
        double density = refBody.GetAtmosphericDensity(vessel.Position);
        if (density > 0.01) return false;

        return true;
    }
}
