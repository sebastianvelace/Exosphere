namespace Exosphere.Simulation.Parts;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Propulsion;

public class Part
{
    public string InstanceId { get; }
    public PartDefinition Definition { get; }

    public Part(PartDefinition def, string? instanceId = null)
    {
        Definition = def ?? throw new ArgumentNullException(nameof(def));
        InstanceId = string.IsNullOrWhiteSpace(instanceId)
            ? Guid.NewGuid().ToString()
            : instanceId;
        ResetResources();
        InitializeEngineStates();
    }

    // ── Recursos actuales ─────────────────────────────────────────────────
    public double LiquidFuel      { get; set; }
    public double Oxidizer        { get; set; }
    public double SolidFuel       { get; set; }
    public double Monopropellant  { get; set; }
    public double ElectricCharge  { get; set; }

    // ── Estado físico ─────────────────────────────────────────────────────

    /// <summary>
    /// Structure temperature (K) — the load-bearing skin BEHIND the thermal protection.
    /// This is what has to survive: <see cref="ThermalRatio"/> and burn-through are measured
    /// against it, because the vehicle is lost when the structure fails, not when the tiles
    /// glow (they are supposed to glow).
    /// </summary>
    public double Temperature     { get; set; } = 290.0;  // K

    /// <summary>
    /// Outer TPS/skin temperature (K) — the tile face that meets the plasma. Thin and low
    /// mass, so it climbs fast and settles at radiative equilibrium (~1700 K at peak entry
    /// heating) while insulating the structure behind it.
    /// </summary>
    public double SkinTemperature { get; set; } = 290.0;  // K

    public double ThermalDamage   { get; set; } = 0.0;    // 0..1 progressive burn-through
    public bool   IsBroken        { get; set; }
    public bool   IsDeployed      { get; set; }
    public bool   IsStagingActive { get; set; } = true;
    public double ThermalRatio => Definition.HeatTolerance > 0.0
        ? Temperature / Definition.HeatTolerance
        : 0.0;
    public bool IsThermallyBurned => ThermalDamage >= 1.0;

    // ── Control del motor ─────────────────────────────────────────────────
    public double   ThrottleLevel { get; set; }          // [0, 1]
    public Vector3d GimbalOffset  { get; set; } = Vector3d.Zero;  // deflexión normalizada
    private readonly List<EngineInstanceState> _engineStates = new();
    private readonly List<EngineFailureInjection> _scheduledFailures = new();
    public IReadOnlyList<EngineInstanceState> EngineStates => _engineStates;
    public IReadOnlyList<EngineFailureInjection> ScheduledEngineFailures =>
        _scheduledFailures;
    public bool HasEngineRuntime => _engineStates.Count > 0;
    private double _activeEngineFraction = 1.0;

    /// <summary>
    /// Fraction of the engines represented by this aggregate part that are selected.
    /// Defaults to one (the complete cluster). EDL can select a discrete 1/2/3-engine
    /// centre cluster without pretending all six Raptors deep-throttle together.
    /// </summary>
    public double ActiveEngineFraction
    {
        get => _activeEngineFraction;
        set => _activeEngineFraction = System.Math.Clamp(value, 0.0, 1.0);
    }

    public int SelectedEngineCount => Definition.Category == PartCategory.Engine
        ? (int)System.Math.Round(System.Math.Max(1, Definition.EngineCount) * ActiveEngineFraction)
        : 0;

    public void SelectEngineCount(int count)
    {
        int total = System.Math.Max(1, Definition.EngineCount);
        ActiveEngineFraction = System.Math.Clamp(count, 0, total) / (double)total;
    }

    private void InitializeEngineStates()
    {
        if (Definition.Category != PartCategory.Engine
            || string.IsNullOrWhiteSpace(Definition.EngineModelId)
            || Definition.ResolvedEngineModel == null)
            return;
        int count = System.Math.Max(1, Definition.EngineCount);
        for (int i = 0; i < count; i++)
        {
            var mount = Definition.ResolvedEngineCluster?.Engines
                .ElementAtOrDefault(i);
            string modelId = !string.IsNullOrWhiteSpace(mount?.EngineModelId)
                ? mount.EngineModelId
                : Definition.EngineModelId;
            _engineStates.Add(new EngineInstanceState
            {
                // Preserve the save/test-stand identity contract. Mount ids describe
                // physical topology; runtime ids remain scoped to the part instance.
                InstanceId = $"{InstanceId}:engine:{i + 1:00}",
                EngineModelId = modelId,
            });
        }
    }

    public void AdvanceEngineRuntime(double commandedThrottle, double dt)
    {
        if (!HasEngineRuntime)
        {
            SpoolToward(commandedThrottle, dt);
            return;
        }
        if (!double.IsFinite(commandedThrottle) || !double.IsFinite(dt) || dt < 0.0)
            throw new ArgumentOutOfRangeException(nameof(commandedThrottle));

        int selected = SelectedEngineCount;
        double floored = ApplyThrottleFloor(commandedThrottle);
        for (int i = 0; i < _engineStates.Count; i++)
        {
            var state = _engineStates[i];
            double command = i < selected ? floored : 0.0;
            state.CommandedThrottle = command;
            if (!ApplyScheduledFailure(state, dt))
                AdvanceEngineState(state, command, dt);
            AdvanceChamberPressure(state, dt);
            AdvanceGimbal(state, i, i < selected, dt);
            AdvanceEngineThermalState(state, dt);
        }

        RefreshAggregateThrottle();
    }

    public bool FailEngine(string instanceId, string failureCode)
    {
        var engine = _engineStates.FirstOrDefault(
            e => string.Equals(e.InstanceId, instanceId, StringComparison.Ordinal));
        if (engine == null || engine.State == EngineLifecycleState.Failed) return false;
        engine.State = EngineLifecycleState.Failed;
        engine.StateElapsedSeconds = 0.0;
        engine.ActualThrottle = 0.0;
        engine.ChamberPressureFraction = 0.0;
        engine.FailureCode = string.IsNullOrWhiteSpace(failureCode)
            ? "INJECTED_FAILURE"
            : failureCode;
        _scheduledFailures.RemoveAll(injection =>
            string.Equals(
                injection.EngineInstanceId,
                instanceId,
                StringComparison.Ordinal));
        RefreshAggregateThrottle();
        return true;
    }

    public void FailAllEngines(string failureCode)
    {
        foreach (var engine in _engineStates)
            FailEngine(engine.InstanceId, failureCode);
        RefreshAggregateThrottle();
    }

    public void ScheduleEngineFailure(EngineFailureInjection injection)
    {
        injection.Validate();
        if (!_engineStates.Any(state =>
                string.Equals(
                    state.InstanceId,
                    injection.EngineInstanceId,
                    StringComparison.Ordinal)))
            throw new ArgumentException(
                $"Unknown engine instance '{injection.EngineInstanceId}'.",
                nameof(injection));
        _scheduledFailures.Add(injection);
    }

    public void RestoreScheduledEngineFailures(
        IEnumerable<EngineFailureInjection> injections)
    {
        _scheduledFailures.Clear();
        foreach (var injection in injections)
            ScheduleEngineFailure(injection);
    }

    public void RestoreEngineStates(IEnumerable<EngineInstanceState> states)
    {
        if (!HasEngineRuntime) return;
        var restored = states.ToDictionary(s => s.InstanceId, StringComparer.Ordinal);
        foreach (var target in _engineStates)
        {
            if (!restored.TryGetValue(target.InstanceId, out var source)) continue;
            target.State = source.State;
            target.StateElapsedSeconds = source.StateElapsedSeconds;
            target.CommandedThrottle = source.CommandedThrottle;
            target.ActualThrottle = source.ActualThrottle;
            target.GimbalDeg = source.GimbalDeg;
            target.GimbalVelocityDegPerS = source.GimbalVelocityDegPerS;
            target.ChamberPressureFraction = source.ChamberPressureFraction;
            target.TemperatureK = source.TemperatureK;
            target.StartAttempts = source.StartAttempts;
            target.StartsCompleted = source.StartsCompleted;
            target.FailureCode = source.FailureCode;
        }
    }

    public IEnumerable<EngineTelemetry> GetEngineTelemetry(double ambientPressure)
    {
        if (!HasEngineRuntime) yield break;
        foreach (var state in _engineStates)
        {
            var performance = EvaluateEnginePerformance(state, ambientPressure);
            yield return new EngineTelemetry(
                state.InstanceId,
                state.State,
                state.CommandedThrottle,
                state.ActualThrottle,
                performance.ThrustN,
                performance.MassFlowKgS,
                state.ChamberPressureFraction,
                Definition.MixtureRatio,
                state.GimbalDeg,
                state.TemperatureK,
                ResolveEngineModel(state)?.MaximumSafeTemperatureK
                    ?? double.PositiveInfinity,
                state.StartAttempts,
                state.StartsCompleted,
                state.FailureCode);
        }
    }

    private bool ApplyScheduledFailure(EngineInstanceState state, double dt)
    {
        int activeAttempt = state.State is
            EngineLifecycleState.Chill or EngineLifecycleState.SpinPrime
            ? state.StartAttempts + 1
            : state.StartAttempts;
        int index = _scheduledFailures.FindIndex(injection =>
            string.Equals(
                injection.EngineInstanceId,
                state.InstanceId,
                StringComparison.Ordinal)
            && injection.TriggerState == state.State
            && (injection.TriggerStartAttempt == 0
                || injection.TriggerStartAttempt == activeAttempt)
            && state.StateElapsedSeconds + dt
                >= injection.TriggerAfterStateSeconds);
        if (index < 0) return false;
        string failureCode = _scheduledFailures[index].FailureCode;
        _scheduledFailures.RemoveAt(index);
        return FailEngine(state.InstanceId, failureCode);
    }

    private void AdvanceEngineThermalState(
        EngineInstanceState state,
        double dt)
    {
        var model = ResolveEngineModel(state);
        if (model != null
            && state.State != EngineLifecycleState.Failed
            && state.TemperatureK > model.MaximumSafeTemperatureK)
        {
            FailEngine(state.InstanceId, "ENGINE_OVERTEMPERATURE");
            return;
        }
        double chamber = state.ChamberPressureFraction;
        double target = model != null
            ? 290.0 + (model.NominalOperatingTemperatureK - 290.0) * chamber
            : chamber > 1e-3 ? 900.0 + 350.0 * chamber : 290.0;
        double timeConstant = model != null
            ? chamber > 1e-3
                ? model.ThermalTimeConstantSeconds
                : model.CooldownTimeConstantSeconds
            : chamber > 1e-3 ? 1.0 / 1.5 : 4.0;
        double alpha = 1.0 - System.Math.Exp(-dt / timeConstant);
        state.TemperatureK += (target - state.TemperatureK) * alpha;
        if (model != null
            && state.State != EngineLifecycleState.Failed
            && state.TemperatureK > model.MaximumSafeTemperatureK)
            FailEngine(state.InstanceId, "ENGINE_OVERTEMPERATURE");
    }

    public double GetEngineFeedLimitKgS(int engineIndex)
    {
        var branches = Definition.ResolvedEngineCluster?.FeedNetwork.Branches;
        return branches != null && engineIndex >= 0 && engineIndex < branches.Count
            ? branches[engineIndex].MaximumFlowKgS
            : double.PositiveInfinity;
    }

    private EnginePerformanceSample EvaluateEnginePerformance(
        EngineInstanceState state,
        double ambientPressure)
    {
        double effectiveThrottle = state.State == EngineLifecycleState.Failed
            ? 0.0
            : state.ChamberPressureFraction;
        if (ResolveEngineModel(state) is { } model)
            return EnginePerformanceEvaluator.Evaluate(
                model, ambientPressure, effectiveThrottle);

        double ratedPerEngine =
            GetLegacyRatedFullThrottleThrustMagnitude(ambientPressure)
            / System.Math.Max(1, _engineStates.Count);
        double isp = GetLegacyIsp(ambientPressure);
        double thrust = ratedPerEngine * effectiveThrottle;
        double flow = isp > 1.0 ? thrust / (isp * 9.80665) : 0.0;
        return new EnginePerformanceSample(
            thrust, isp, flow, state.ChamberPressureFraction);
    }

    private void AdvanceChamberPressure(EngineInstanceState state, double dt)
    {
        double target = state.State == EngineLifecycleState.Failed
            ? 0.0
            : state.ActualThrottle;
        double seconds = target >= state.ChamberPressureFraction
            ? System.Math.Max(Definition.EngineStartupSeconds * 1.25, 1e-6)
            : System.Math.Max(Definition.EngineShutdownSeconds * 1.25, 1e-6);
        double step = dt / seconds;
        double delta = target - state.ChamberPressureFraction;
        state.ChamberPressureFraction = System.Math.Clamp(
            System.Math.Abs(delta) <= step
                ? target
                : state.ChamberPressureFraction + System.Math.Sign(delta) * step,
            0.0,
            1.0);
    }

    private void AdvanceGimbal(
        EngineInstanceState state,
        int engineIndex,
        bool selected,
        double dt)
    {
        var model = ResolveEngineModel(state);
        double range = model?.GimbalRangeDeg ?? Definition.GimbalRange;
        bool mountCanGimbal =
            Definition.ResolvedEngineCluster?.Engines.ElementAtOrDefault(engineIndex)
                ?.Gimballed ?? true;
        // A differential-TVC command (R5b) for this specific instance takes priority over
        // the part-wide GimbalOffset every other mount of this part still shares.
        var commanded = state.GimbalCommandOverride ?? GimbalOffset;
        var target = selected && mountCanGimbal
            ? new Vector3d(
                System.Math.Clamp(commanded.X, -1.0, 1.0) * range,
                0.0,
                System.Math.Clamp(commanded.Z, -1.0, 1.0) * range)
            : Vector3d.Zero;
        if (model == null
            || model.GimbalRateDegPerS <= 0.0
            || model.GimbalAccelerationDegPerS2 <= 0.0)
        {
            state.GimbalDeg = target;
            state.GimbalVelocityDegPerS = Vector3d.Zero;
            return;
        }

        var x = AdvanceGimbalAxis(
            state.GimbalDeg.X,
            state.GimbalVelocityDegPerS.X,
            target.X,
            model.GimbalRateDegPerS,
            model.GimbalAccelerationDegPerS2,
            dt);
        var z = AdvanceGimbalAxis(
            state.GimbalDeg.Z,
            state.GimbalVelocityDegPerS.Z,
            target.Z,
            model.GimbalRateDegPerS,
            model.GimbalAccelerationDegPerS2,
            dt);
        state.GimbalDeg = new Vector3d(x.position, 0.0, z.position);
        state.GimbalVelocityDegPerS = new Vector3d(x.velocity, 0.0, z.velocity);
    }

    private static (double position, double velocity) AdvanceGimbalAxis(
        double position,
        double velocity,
        double target,
        double maximumRate,
        double maximumAcceleration,
        double dt)
    {
        if (dt <= 0.0) return (position, velocity);
        double error = target - position;
        double desiredVelocity = System.Math.Clamp(
            error / dt, -maximumRate, maximumRate);
        double velocityStep = maximumAcceleration * dt;
        double newVelocity = velocity
            + System.Math.Clamp(
                desiredVelocity - velocity, -velocityStep, velocityStep);
        double movement = newVelocity * dt;
        if (System.Math.Abs(movement) >= System.Math.Abs(error)
            && System.Math.Sign(movement) == System.Math.Sign(error))
            return (target, 0.0);
        return (position + movement, newVelocity);
    }

    private void AdvanceEngineState(
        EngineInstanceState state,
        double command,
        double dt)
    {
        if (state.State == EngineLifecycleState.Failed)
        {
            state.ActualThrottle = 0.0;
            return;
        }

        state.StateElapsedSeconds += dt;
        if (command <= 1e-3
            && state.State is not (
                EngineLifecycleState.Off
                or EngineLifecycleState.Shutdown
                or EngineLifecycleState.Purge))
            Transition(state, EngineLifecycleState.Shutdown);

        switch (state.State)
        {
            case EngineLifecycleState.Off:
                state.ActualThrottle = 0.0;
                if (command > 1e-3)
                {
                    int permittedStarts = ResolveEngineModel(state) is { } model
                        ? model.RestartLimit + 1
                        : int.MaxValue;
                    if (state.StartsCompleted >= permittedStarts)
                        FailEngine(
                            state.InstanceId, "RESTART_LIMIT_EXCEEDED");
                    else
                        Transition(state, EngineLifecycleState.Chill);
                }
                break;
            case EngineLifecycleState.Chill:
                if (state.StateElapsedSeconds >= Definition.EngineChillSeconds)
                    Transition(state, EngineLifecycleState.SpinPrime);
                break;
            case EngineLifecycleState.SpinPrime:
                if (state.StateElapsedSeconds >= Definition.EngineSpinPrimeSeconds)
                {
                    state.StartAttempts++;
                    Transition(state, EngineLifecycleState.Ignition);
                }
                break;
            case EngineLifecycleState.Ignition:
                if (state.StateElapsedSeconds >= Definition.EngineIgnitionSeconds)
                {
                    state.StartsCompleted++;
                    Transition(state, EngineLifecycleState.Ramp);
                }
                break;
            case EngineLifecycleState.Ramp:
                MoveThrottle(
                    state,
                    command,
                    System.Math.Max(Definition.EngineStartupSeconds, 1e-6),
                    dt);
                if (System.Math.Abs(state.ActualThrottle - command) <= 1e-6)
                    Transition(state, EngineLifecycleState.Running);
                break;
            case EngineLifecycleState.Running:
                MoveThrottle(
                    state,
                    command,
                    command >= state.ActualThrottle
                        ? System.Math.Max(Definition.EngineStartupSeconds, 1e-6)
                        : System.Math.Max(Definition.EngineShutdownSeconds, 1e-6),
                    dt);
                break;
            case EngineLifecycleState.Shutdown:
                MoveThrottle(
                    state,
                    0.0,
                    System.Math.Max(Definition.EngineShutdownSeconds, 1e-6),
                    dt);
                if (state.ActualThrottle <= 1e-6)
                    Transition(state, EngineLifecycleState.Purge);
                break;
            case EngineLifecycleState.Purge:
                state.ActualThrottle = 0.0;
                if (command > 1e-3)
                    Transition(state, EngineLifecycleState.Chill);
                else if (state.StateElapsedSeconds >= Definition.EnginePurgeSeconds)
                    Transition(state, EngineLifecycleState.Off);
                break;
        }
    }

    private static void MoveThrottle(
        EngineInstanceState state,
        double target,
        double fullRangeSeconds,
        double dt)
    {
        double maximumStep = dt / fullRangeSeconds;
        double delta = target - state.ActualThrottle;
        state.ActualThrottle = System.Math.Abs(delta) <= maximumStep
            ? target
            : state.ActualThrottle + System.Math.Sign(delta) * maximumStep;
    }

    private static void Transition(
        EngineInstanceState state,
        EngineLifecycleState next)
    {
        state.State = next;
        state.StateElapsedSeconds = 0.0;
    }

    private void RefreshAggregateThrottle()
    {
        int selected = SelectedEngineCount;
        ThrottleLevel = selected > 0
            ? _engineStates.Take(selected).Sum(s => s.ActualThrottle) / selected
            : 0.0;
    }

    // ── Masa actual (seca + propelante) ───────────────────────────────────
    public double CurrentMass =>
        Definition.MassDry + LiquidFuel + Oxidizer + SolidFuel + Monopropellant;

    // ── Deep-throttle floor (Raptor 2 ≈ 40 %) ─────────────────────────────
    /// <summary>
    /// Returns <paramref name="requested"/> snapped UP to the engine's documented minimum
    /// throttle (<see cref="PartDefinition.MinThrottle"/>) — but only when it is genuinely
    /// firing: a request of (near) 0 is a deliberate shutdown and is left at 0, never floored.
    /// A real Raptor either runs at ≥40 % or is off; it does not hover at 12 %. Ascent and EDL
    /// opt into this; EDL combines the floor with discrete engine selection for lower thrust.
    /// </summary>
    public double ApplyThrottleFloor(double requested)
    {
        if (Definition.Category != PartCategory.Engine) return requested;
        double floor = Definition.MinThrottle;
        if (floor <= 0.0 || requested <= 1e-3) return requested;          // off stays off
        return System.Math.Clamp(System.Math.Max(requested, floor), 0.0, 1.0);
    }

    // ── Inicializar recursos al máximo de capacidad ───────────────────────
    public void ResetResources()
    {
        LiquidFuel     = Definition.FuelCapacityLF;
        Oxidizer       = Definition.FuelCapacityOx;
        SolidFuel      = Definition.FuelCapacitySolid;
        Monopropellant = Definition.FuelCapacityMono;
        ElectricCharge = Definition.ECCapacity;
    }

    // ── Startup / shutdown spool transient ────────────────────────────────
    // A real Raptor cannot step its thrust instantly: the turbopumps spin up over a fraction
    // of a second and the chamber pressure builds before full thrust. We model that with a
    // first-order ramp of ThrottleLevel toward a commanded value. Startup at ~2.0/s reaches
    // 100 % in ~0.5 s; shutdown uses a faster 5.0/s (~0.2 s) so cutoff does not keep injecting
    // landing impulse as long as chamber-pressure buildup. Callers that want an instant set
    // still just assign ThrottleLevel directly.
    public const double SpoolRate = 2.0;           // startup throttle units per second
    public const double ShutdownSpoolRate = 5.0;   // shutdown throttle units per second

    /// <summary>
    /// Advances <see cref="ThrottleLevel"/> toward <paramref name="commanded"/> at no more than
    /// the direction-specific spool rate over <paramref name="dt"/>. Returns the new level.
    /// </summary>
    public double SpoolToward(double commanded, double dt)
    {
        commanded = System.Math.Clamp(commanded, 0.0, 1.0);
        double delta   = commanded - ThrottleLevel;
        double rate = delta < 0.0 ? ShutdownSpoolRate : SpoolRate;
        double maxStep = rate * dt;
        if (System.Math.Abs(delta) <= maxStep) ThrottleLevel = commanded;
        else ThrottleLevel += System.Math.Sign(delta) * maxStep;
        return ThrottleLevel;
    }

    private const double SeaLevelPressurePa = 101_325.0;

    /// <summary>
    /// Pressure-corrected specific impulse (s): Isp_vac in vacuum, Isp_sl at Earth
    /// sea level, and linear extrapolation above one atmosphere. The extrapolation is
    /// intentionally not capped so dense-atmosphere back-pressure can cause flameout.
    /// </summary>
    public double GetIsp(double ambientPressure = 0.0)
    {
        if (HasEngineRuntime)
        {
            double thrust = 0.0;
            double flow = 0.0;
            foreach (var state in _engineStates)
            {
                var sample = EvaluateEnginePerformance(state, ambientPressure);
                thrust += sample.ThrustN;
                flow += sample.MassFlowKgS;
            }
            return flow > 1e-12 ? thrust / (flow * 9.80665) : 0.0;
        }
        return GetLegacyIsp(ambientPressure);
    }

    private double GetLegacyIsp(double ambientPressure)
    {
        double pf = System.Math.Max(0.0, ambientPressure / SeaLevelPressurePa);
        return System.Math.Max(0.0,
            Definition.IspVac + (Definition.IspSL - Definition.IspVac) * pf);
    }

    /// <summary>
    /// Current propellant mass flow (kg/s) this engine is drawing: ṁ = F(p)/(Isp(p)·g₀),
    /// using the pressure-corrected thrust at the present throttle. 0 when not firing.
    /// </summary>
    public double GetMassFlow(double ambientPressure = 0.0)
    {
        if (Definition.Category != PartCategory.Engine
            || IsBroken || !IsStagingActive)
            return 0.0;
        if (HasEngineRuntime)
            return _engineStates.Sum(
                state => EvaluateEnginePerformance(state, ambientPressure).MassFlowKgS);
        if (ThrottleLevel <= 0.0) return 0.0;
        double isp = GetIsp(ambientPressure);
        if (isp < 1.0) return 0.0;
        return GetThrustMagnitude(ambientPressure) / (isp * 9.80665);
    }

    /// <summary>
    /// Pressure-corrected thrust magnitude (N) for this engine at the given ambient
    /// pressure (Pa).  A rocket engine's thrust rises with altitude because the
    /// pressure term in the thrust equation falls:
    ///     F(p) = F_vac − (p / p₀) · (F_vac − F_sl)
    /// so F = F_sl at sea level (p = p₀) and F = F_vac in vacuum (p = 0).
    /// Falls back to vacuum thrust when no sea-level figure is provided.
    /// </summary>
    public double GetThrustMagnitude(double ambientPressure = 0.0)
        => HasEngineRuntime
            ? _engineStates.Sum(
                state => EvaluateEnginePerformance(state, ambientPressure).ThrustN)
            : GetFullThrottleThrustMagnitude(ambientPressure) * ThrottleLevel;

    /// <summary>Pressure-corrected thrust of the selected engines at 100% throttle (N).</summary>
    public double GetFullThrottleThrustMagnitude(double ambientPressure = 0.0)
        => HasEngineRuntime
            ? _engineStates.Take(SelectedEngineCount).Sum(state =>
                {
                    var model = ResolveEngineModel(state);
                    return model == null
                        ? 0.0
                        : EnginePerformanceEvaluator.Evaluate(
                            model, ambientPressure, model.MaximumThrottle).ThrustN;
                })
            : GetRatedFullThrottleThrustMagnitude(ambientPressure) * ActiveEngineFraction;

    /// <summary>Pressure-corrected rated thrust of the complete represented cluster.</summary>
    public double GetRatedFullThrottleThrustMagnitude(double ambientPressure = 0.0)
        => HasEngineRuntime
            ? _engineStates.Sum(state =>
                {
                    var model = ResolveEngineModel(state);
                    return model == null
                        ? 0.0
                        : EnginePerformanceEvaluator.Evaluate(
                            model, ambientPressure, model.MaximumThrottle).ThrustN;
                })
            : GetLegacyRatedFullThrottleThrustMagnitude(ambientPressure);

    private EngineModelDefinition? ResolveEngineModel(EngineInstanceState state)
    {
        if (Definition.ResolvedEngineModels.TryGetValue(
                state.EngineModelId, out var model))
            return model;
        return Definition.ResolvedEngineModel;
    }

    private double GetLegacyRatedFullThrottleThrustMagnitude(double ambientPressure)
    {
        double fVac = Definition.ThrustVac;
        double fSL  = Definition.ThrustSL > 0.0 ? Definition.ThrustSL : fVac;
        // Do not cap at one atmosphere. Dense worlds such as Venus impose far more
        // back-pressure than Earth, eventually reducing net nozzle thrust to zero.
        double pf   = System.Math.Max(0.0, ambientPressure / SeaLevelPressurePa);
        double f    = fVac - pf * (fVac - fSL);
        return System.Math.Max(0.0, f);
    }

    // ── Vector de empuje en espacio local de la pieza (+Y = arriba) ───────
    // Overload sin presión: usa empuje de vacío (compatibilidad).
    public Vector3d GetThrustVector() => GetThrustVector(0.0);

    /// <summary>
    /// Thrust vector in the part's local frame (+Y = up), pressure-corrected and gimballed.
    /// </summary>
    public Vector3d GetThrustVector(double ambientPressure)
    {
        if (Definition.Category != PartCategory.Engine
            || IsBroken || !IsStagingActive
            || (!HasEngineRuntime && ThrottleLevel <= 0.0))
            return Vector3d.Zero;

        double thrust = GetThrustMagnitude(ambientPressure);
        if (thrust <= 0.0) return Vector3d.Zero;

        // Gimbal: GimbalOffset.{X,Z} ∈ [-1,1] is the normalized deflection of each axis.
        // The actual deflection angle is (offset · GimbalRange) in degrees; the thrust
        // direction is the unit vector tilted off +Y by that angle.
        double gimbalRange = Definition.GimbalRange;
        double normalizedX = System.Math.Clamp(GimbalOffset.X, -1.0, 1.0);
        double normalizedZ = System.Math.Clamp(GimbalOffset.Z, -1.0, 1.0);
        if (HasEngineRuntime)
        {
            var live = _engineStates.Where(
                state => state.ChamberPressureFraction > 1e-3).ToArray();
            if (live.Length > 0)
            {
                double actualX = live.Average(state => state.GimbalDeg.X);
                double actualZ = live.Average(state => state.GimbalDeg.Z);
                gimbalRange = 1.0;
                normalizedX = actualX;
                normalizedZ = actualZ;
            }
        }
        double gimbalRad = gimbalRange * MathUtils.DEG_TO_RAD;
        double ax = normalizedX * gimbalRad;
        double az = normalizedZ * gimbalRad;
        var dir = new Vector3d(System.Math.Sin(ax), 1.0, System.Math.Sin(az)).Normalized;
        return dir * thrust;
    }

    /// <summary>
    /// Per-engine-instance thrust geometry in the part's local frame (+Y = up): mount
    /// position and gimballed thrust vector for every live engine instance, pressure-
    /// corrected at <paramref name="ambientPressure"/>. Unlike <see cref="GetThrustVector"/>
    /// (which collapses the whole part into a single averaged-gimbal force), this exposes
    /// each mount's real 3D position and direction so a caller can compute genuine torque
    /// (τ = r×F) instead of a scalar lever approximation. Legacy (non-cluster) parts yield
    /// exactly one tuple built from <see cref="GetThrustVector"/> so behaviour is unchanged
    /// for every part that has no engine cluster data.
    /// </summary>
    public IEnumerable<(Vector3d PositionM, Vector3d ThrustVectorN)>
        GetEngineInstanceThrustGeometry(double ambientPressure)
    {
        if (!HasEngineRuntime)
        {
            yield return (
                new Vector3d(0.0, Definition.ThrustPositionYM, 0.0),
                GetThrustVector(ambientPressure));
            yield break;
        }

        for (int i = 0; i < _engineStates.Count; i++)
        {
            var state = _engineStates[i];
            var mount = Definition.ResolvedEngineCluster?.Engines.ElementAtOrDefault(i);
            var position = mount != null
                ? mount.Position
                : new Vector3d(0.0, Definition.ThrustPositionYM, 0.0);
            var baseDirection = mount != null
                ? mount.Direction
                : Vector3d.Up;

            double thrust = EvaluateEnginePerformance(state, ambientPressure).ThrustN;
            var direction = TiltDirection(baseDirection, state.GimbalDeg.X, state.GimbalDeg.Z);
            yield return (position, direction * thrust);
        }
    }

    /// <summary>
    /// Clears every live engine instance's <see cref="EngineInstanceState.GimbalCommandOverride"/>
    /// back to <c>null</c>. Called once per tick before a differential-TVC solve (R5b)
    /// re-populates it, so a command from a previous tick — or from a tick where the pilot
    /// stopped commanding attitude — never lingers on an instance the current tick doesn't
    /// touch.
    /// </summary>
    public void ClearGimbalCommandOverrides()
    {
        foreach (var state in _engineStates)
            state.GimbalCommandOverride = null;
    }

    /// <summary>
    /// Per-engine-instance ingredients for differential gimbal allocation (R5b): mount
    /// position, zero-deflection thrust magnitude and this instance's own gimbal range —
    /// the same raw data <see cref="GetEngineInstanceThrustGeometry"/> uses, but without the
    /// live <see cref="EngineInstanceState.GimbalDeg"/> baked in, so a caller can linearize
    /// "how much lateral force a unit normalized gimbal command would add" instead of reading
    /// the already-deflected thrust vector. Skips instances that cannot presently gimbal
    /// (unselected, ungimballed mount, zero thrust) — a caller allocates torque only across
    /// instances that can actually receive a command.
    /// </summary>
    public IEnumerable<(EngineInstanceState State, Vector3d PositionM, double ThrustN, double GimbalRangeDeg)>
        GetEngineInstanceGimbalAuthority(double ambientPressure)
    {
        if (!HasEngineRuntime || IsBroken || !IsStagingActive) yield break;

        int selected = SelectedEngineCount;
        for (int i = 0; i < _engineStates.Count; i++)
        {
            if (i >= selected) continue;
            var state = _engineStates[i];
            var mount = Definition.ResolvedEngineCluster?.Engines.ElementAtOrDefault(i);
            bool gimballed = mount?.Gimballed ?? true;
            if (!gimballed) continue;

            var position = mount != null
                ? mount.Position
                : new Vector3d(0.0, Definition.ThrustPositionYM, 0.0);
            double thrust = EvaluateEnginePerformance(state, ambientPressure).ThrustN;
            if (thrust <= 0.0) continue;

            double range = ResolveEngineModel(state)?.GimbalRangeDeg ?? Definition.GimbalRange;
            if (range <= 0.0) continue;

            yield return (state, position, thrust, range);
        }
    }

    /// <summary>
    /// Tilts <paramref name="baseDirection"/> by (<paramref name="gimbalXDeg"/>,
    /// <paramref name="gimbalZDeg"/>) degrees. For the vertical case (<c>baseDirection ≈
    /// (0,1,0)</c>, true of every mount in every current data file) this reproduces the
    /// exact expression <see cref="GetThrustVector"/> already uses, so the two stay
    /// bit-for-bit consistent at zero deflection. For a general (non-vertical) base
    /// direction — untested by real data today, but must not crash or misbehave — an
    /// orthonormal basis is built around it and the same sine tilt is applied within the
    /// plane perpendicular to it; this branch is synthetic/general-purpose.
    /// </summary>
    private static Vector3d TiltDirection(
        Vector3d baseDirection, double gimbalXDeg, double gimbalZDeg)
    {
        double ax = gimbalXDeg * MathUtils.DEG_TO_RAD;
        double az = gimbalZDeg * MathUtils.DEG_TO_RAD;
        var up = baseDirection.Normalized;
        if (up == Vector3d.Zero) up = Vector3d.Up;

        // Vertical fast path: identical formula to GetThrustVector's tilt.
        if ((up - Vector3d.Up).MagnitudeSquared < 1e-12)
            return new Vector3d(
                System.Math.Sin(ax), 1.0, System.Math.Sin(az)).Normalized;

        // General case: build an orthonormal basis (right, up, fwd) around the base
        // direction and apply the same sine tilt within that local frame.
        var reference = System.Math.Abs(up.Y) < 0.999 ? Vector3d.Up : Vector3d.Right;
        var right = reference.Cross(up).Normalized;
        if (right == Vector3d.Zero) right = Vector3d.Right;
        var fwd = up.Cross(right).Normalized;
        return (right * System.Math.Sin(ax) + up + fwd * System.Math.Sin(az)).Normalized;
    }

    // ── Consumir propelante por dt segundos. Retorna false si se agota. ──
    public bool ConsumePropellant(double dt, double ambientPressure = 0.0)
    {
        if (Definition.Category != PartCategory.Engine
            || ThrottleLevel <= 0.0 || IsBroken || !IsStagingActive)
            return true;

        // ISP interpolado por presión (vac ↔ sl).
        // pf = 0 en vacío (p=0) → Isp_vac;  pf = 1 a nivel del mar → Isp_sl.
        double pressureFraction = System.Math.Max(0.0, ambientPressure / SeaLevelPressurePa);
        double isp = System.Math.Max(0.0,
            Definition.IspVac + (Definition.IspSL - Definition.IspVac) * pressureFraction);
        if (isp < 1.0) return false;

        // Flujo másico ṁ = F(p) / (Isp(p)·g₀), g₀ = 9.80665 m/s².
        // Se usa el empuje corregido por presión para que ṁ sea consistente
        // con el empuje realmente producido (GetThrustMagnitude).
        double thrust       = GetThrustMagnitude(ambientPressure);
        double massFlowRate = thrust / (isp * 9.80665);  // kg/s

        var fuelType = Definition.FuelTypeStr.ToLowerInvariant();

        if (fuelType.Contains("liquidfuel") || fuelType.Contains("liquid_fuel+oxidizer") || fuelType.Contains("liquidfuelandoxidizer"))
        {
            // Reparte ṁ entre LF y Ox según la proporción REALMENTE cargada en la pieza,
            // de modo que el O/F del motor (p. ej. 3.55 para Raptor) se respete y ambos
            // recursos se agoten juntos. (Antes se usaba un 9:11 fijo → O/F ≈ 1.22, erróneo.)
            // Esto deja este camino coherente con PartGraph.ConsumePropellant, la ruta
            // autoritativa que invoca Vessel.Tick.
            double total  = LiquidFuel + Oxidizer;
            double lfFrac = Definition.MixtureRatio > 0.0
                ? 1.0 / (1.0 + Definition.MixtureRatio)
                : total > 1e-9 ? LiquidFuel / total : 0.45;
            double lfRate = massFlowRate * lfFrac;
            double oxRate = massFlowRate * (1.0 - lfFrac);
            if (LiquidFuel < lfRate * dt || Oxidizer < oxRate * dt) return false;
            LiquidFuel -= lfRate * dt;
            Oxidizer   -= oxRate * dt;
        }
        else if (fuelType.Contains("solid"))
        {
            if (SolidFuel < massFlowRate * dt) return false;
            SolidFuel -= massFlowRate * dt;
        }
        else if (fuelType.Contains("mono"))
        {
            if (Monopropellant < massFlowRate * dt) return false;
            Monopropellant -= massFlowRate * dt;
        }
        return true;
    }
}
