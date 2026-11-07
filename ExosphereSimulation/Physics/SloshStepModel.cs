namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Integrators;

/// <summary>
/// Pure candidate adapter over an already funded F4 step. Main liquid stores with declared
/// cylindrical geometry have one two-coordinate mode. Uniform fallback stores, solids and
/// point headers have no inferred free surface. Runtime state belongs to the source vessel.
/// </summary>
public sealed class SloshStepModel
{
    /// <summary>Estimated quasi-static scheduling limit, not a measured hardware tolerance.</summary>
    public const double AdiabaticRelativeRateLimit = 0.1;
    private sealed record Binding(Vessel Owner, ResourceStoreId Store, PropellantTankDef Definition,
        FixedShapeMassComponent? Component, double ConstantMassKg, Vector3d FloorPrimaryBodyM,
        Quaterniond TankToPrimary, SloshTankRuntime Runtime);
    private readonly VariableMassStepModel _funded;
    private readonly Binding[] _bindings;
    private sealed record CurvatureCut(double TimeSeconds, SloshCurvatureHold Hold);
    private readonly CurvatureCut?[] _curvatureCuts;
    public int ModeCount => _bindings.Length;

    public SloshStepModel(Vessel primary, VariableMassStepModel funded,
        IReadOnlyList<SloshAttachedVessel>? attached = null)
    {
        _funded = funded;
        var bindings = new List<Binding>();
        Add(primary, Vector3d.Zero, Quaterniond.Identity, true);
        if (attached != null)
            foreach (var member in attached) Add(member.Vessel, member.DatumPrimaryBodyM, member.OrientationPrimaryBody, false);
        _bindings = bindings.ToArray();
        _curvatureCuts = _bindings.Select(b => b.Runtime.CurvatureHold is { } hold
            ? new CurvatureCut(0, hold) : null).ToArray();

        void Add(Vessel vessel, Vector3d datum, Quaterniond frame, bool isPrimary)
        {
            var positions = vessel.Parts.ComputePartLocalPositions();
            foreach (var part in vessel.Parts.Parts)
            {
                if (!positions.TryGetValue(part, out var position)) continue;
                foreach (var definition in part.Definition.PropellantTanks)
                {
                    if (definition.Kind == PartMassComponentKind.SolidFuel) continue;
                    var resource = Resource(definition.Kind);
                    var store = new ResourceStoreId(part.InstanceId, resource);
                    var runtime = vessel.SloshStates.SingleOrDefault(s => s.Store == store);
                    FixedShapeMassComponent? component = null;
                    if (isPrimary && funded.StoreComponents.TryGetValue(store, out int index))
                        component = funded.Distribution.Components[index];
                    double constant = StoreMass(part, resource);
                    double startMass = component?.SampleMass(0, MassBoundarySide.Right).Mass ?? constant;
                    if (startMass == 0 && runtime == null) continue;
                    runtime ??= new(store, default);
                    var floor = datum + frame.Rotate(position + Vector3d.Up * definition.BottomYM);
                    bindings.Add(new(vessel, store, definition, component, constant, floor, frame, runtime));
                }
            }
        }
    }

    public SloshDisplacementState[] InitialStates() => _bindings.Select(b => b.Runtime.State).ToArray();

    public Slosh6DofProfile Sample(double time, MassBoundarySide side)
    {
        var tanks = new SloshTankParameters[_bindings.Length];
        for (int i = 0; i < tanks.Length; i++)
        {
            var b = _bindings[i]; var mass = b.Component?.SampleMass(time, side) ?? (b.ConstantMassKg, 0.0);
            var parameters = Parameters(b.Definition, mass.Item1, mass.Item2);
            if (_curvatureCuts[i] is { } cut && (time > cut.TimeSeconds
                || time == cut.TimeSeconds && side == MassBoundarySide.Right))
                parameters = parameters with { CurvaturePerM = new(cut.Hold.CurvaturePerM, 0, 0) };
            tanks[i] = new(parameters, b.FloorPrimaryBodyM, b.TankToPrimary);
        }
        return new(_funded.Distribution.Sample(time, side), tanks);
    }

    /// <summary>
    /// Selects a finite geometry continuation for this already funded smooth interval.
    /// Call only with its actual opening proper acceleration, after the engine interval
    /// is selected. Funding and live runtime state are not changed by this decision.
    /// </summary>
    public void ConfigureContinuations(double start, double end, Vector3d properAccelerationPrimaryBody)
    {
        DynamicsInputs.RequireFinite(properAccelerationPrimaryBody, nameof(properAccelerationPrimaryBody));
        if (!(end > start) || start < 0 || end > _funded.Distribution.DurationSeconds)
            throw new ArgumentOutOfRangeException(nameof(end));
        for (int i = 0; i < _bindings.Length; i++)
        {
            if (_curvatureCuts[i] != null) continue;
            var b = _bindings[i]; var component = b.Component;
            if (component == null) continue;
            var opening = component.SampleMass(start, MassBoundarySide.Right);
            if (!(opening.Mass > 0) || !(opening.Rate < 0)) continue;
            double axial = b.TankToPrimary.Inverse().Rotate(properAccelerationPrimaryBody).Y;
            double time = start;
            var reason = axial <= 0 ? SloshContinuationKind.NonsettlingDrain : SloshContinuationKind.RapidDrain;
            if (axial > 0 && Ratio(opening.Mass) <= AdiabaticRelativeRateLimit)
            {
                double endingMass = component.SampleMass(end, MassBoundarySide.Left).Mass;
                if (Ratio(endingMass) <= AdiabaticRelativeRateLimit) continue;
                // With t=tanh(beta*m), epsilon is proportional to t^(-3/2)-t^(1/2),
                // strictly decreasing with mass. Bisect only this smooth funded interval.
                double low = endingMass, high = opening.Mass;
                for (int iteration = 0; iteration < 64; iteration++)
                {
                    double middle = low + (high - low) / 2;
                    if (middle == low || middle == high) break;
                    if (Ratio(middle) > AdiabaticRelativeRateLimit) low = middle; else high = middle;
                }
                time = System.Math.Clamp(start + (opening.Mass - high) / -opening.Rate, start, end);
            }
            // Derive the held value from the exact scheduled cut time, so left/right
            // geometry values agree even when root/time arithmetic rounds a mass knot.
            double atCut = component.SampleMass(time, MassBoundarySide.Right).Mass;
            double curvature = Parameters(b.Definition, atCut).CurvaturePerM.Value;
            if (!(curvature > 0) || !double.IsFinite(curvature))
                throw new InvalidOperationException("A drain continuation needs a positive finite curvature.");
            _curvatureCuts[i] = new(time, new(curvature, reason));

            double Ratio(double mass)
            {
                if (mass == 0) return double.PositiveInfinity;
                var k = Parameters(b.Definition, mass, opening.Rate).CurvaturePerM;
                return System.Math.Abs(k.Rate) / k.Value / System.Math.Sqrt(axial) / System.Math.Sqrt(k.Value);
            }
        }
    }

    public IReadOnlyList<double> ContinuationCuts(double start, double end) => _curvatureCuts
        .Where(c => c != null && c.TimeSeconds > start && c.TimeSeconds < end)
        .Select(c => c!.TimeSeconds).Distinct().Order().ToArray();

    public string DescribeRegime(Slosh6DofStage stage, Vector3d properAccelerationPrimaryBody)
    {
        for (int i = 0; i < _bindings.Length; i++)
            if (_curvatureCuts[i] != null) return "conservative-drain";
        for (int i = 0; i < _bindings.Length; i++)
            if (stage.Corrections[i].ModalMassKg > 0 && (_bindings[i].TankToPrimary.Inverse()
                .Rotate(properAccelerationPrimaryBody).Y <= 0 || stage.State.Modes[i]
                    .Sample(stage.Profile.Tanks[i].Parameters.CurvaturePerM).Normal.Y >= 0))
                return "conservative-unsettled";
        return "axial-first-mode";
    }

    public Slosh6DofProfile PreviousProfile(Slosh6DofProfile opening)
    {
        var tanks = new SloshTankParameters[_bindings.Length];
        for (int i = 0; i < tanks.Length; i++) tanks[i] = opening.Tanks[i] with
        {
            Parameters = _bindings[i].Runtime.PreviousParameters ?? opening.Tanks[i].Parameters,
        };
        return new(opening.Baseline, tanks);
    }

    /// <summary>Commit once after successful integration; candidate evaluation writes nothing.</summary>
    public void Commit(Slosh6DofState final, Slosh6DofProfile ending)
    {
        if (final.Modes.Count != _bindings.Length || ending.Tanks.Count != _bindings.Length)
            throw new ArgumentException("Ending modes must match the funded model.");
        for (int i = 0; i < _bindings.Length; i++)
        {
            var b = _bindings[i]; var states = b.Owner.SloshStates;
            int index = states.FindIndex(s => s.Store == b.Store);
            var parameters = ending.Tanks[i].Parameters;
            bool retired = parameters.ModalMassKg == default;
            if (retired)
            {
                if (index >= 0) states.RemoveAt(index);
                continue;
            }
            var hold = _curvatureCuts[i] is { } cut && cut.TimeSeconds <= _funded.Distribution.DurationSeconds
                ? cut.Hold : b.Runtime.CurvatureHold;
            var state = new SloshTankRuntime(b.Store, final.Modes[i], parameters, hold);
            if (index >= 0) states[index] = state; else states.Add(state);
        }
    }

    /// <summary>Equivalent viscous force per modal mass, based on actual axial proper acceleration.</summary>
    public Vector3d[] InternalAccelerations(Slosh6DofStage stage, Vector3d properAccelerationPrimaryBody,
        Func<Vector3d, Vector3d>? differentialGravityPrimaryBody = null)
    {
        var accelerations = new Vector3d[_bindings.Length];
        for (int i = 0; i < accelerations.Length; i++)
        {
            var b = _bindings[i]; var tank = stage.Profile.Tanks[i];
            double axial = b.TankToPrimary.Inverse().Rotate(properAccelerationPrimaryBody).Y;
            double frequency = axial > 0 ? System.Math.Sqrt(axial) * System.Math.Sqrt(tank.Parameters.CurvaturePerM.Value) : 0;
            double damping = 2 * (b.Definition.SloshDampingRatio ?? 0) * frequency;
            var velocity = b.TankToPrimary.Rotate(stage.State.Modes[i].RateBodyMps);
            var normal = b.TankToPrimary.Rotate(stage.State.Modes[i].Sample(tank.Parameters.CurvaturePerM).Normal);
            accelerations[i] = -(velocity - normal * normal.Dot(velocity)) * damping
                + (differentialGravityPrimaryBody?.Invoke(stage.Corrections[i].PositionBodyM) ?? Vector3d.Zero);
        }
        return accelerations;
    }

    internal static CylindricalSloshParameters Parameters(PropellantTankDef tank, double mass, double rate = 0) =>
        PropellantSlosh.SampleParameters(tank.RadiusM,
            tank.DensityKgPerM3 * System.Math.PI * tank.RadiusM * tank.RadiusM, mass, rate);

    internal static PropellantResourceKind Resource(PartMassComponentKind kind) => kind switch
    {
        PartMassComponentKind.LiquidFuel => PropellantResourceKind.LiquidFuel,
        PartMassComponentKind.Oxidizer => PropellantResourceKind.Oxidizer,
        PartMassComponentKind.Monopropellant => PropellantResourceKind.Monopropellant,
        _ => throw new ArgumentException("Only declared main liquid stores have a slosh mode.", nameof(kind)),
    };

    internal static double StoreMass(Part part, PropellantResourceKind resource) => resource switch
    {
        PropellantResourceKind.LiquidFuel => part.LiquidFuel,
        PropellantResourceKind.Oxidizer => part.Oxidizer,
        PropellantResourceKind.Monopropellant => part.Monopropellant,
        _ => throw new ArgumentException("Only main liquid stores have a cylindrical mode.", nameof(resource)),
    };
}
