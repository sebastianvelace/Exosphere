namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

/// <summary>
/// One aerodynamic element of an axisymmetric body, in the vessel frame (skirt-datum origin).
/// <see cref="CrossflowAreaM2"/> is a planform area loaded by viscous crossflow;
/// <see cref="SlenderAreaChangeM2"/> is a signed cross-section change ΔA (positive where the
/// body widens going aft from its nose) loaded by slender-body potential flow.
/// </summary>
public readonly record struct AeroElement(Vector3d PositionLocal, double CrossflowAreaM2, double SlenderAreaChangeM2,
    double DiameterM = 0.0);

/// <summary>Aerodynamic force (world) and moment about the centre of mass (body frame).</summary>
public readonly record struct AeroWrench(Vector3d ForceWorld, Vector3d TorqueBody);

/// <summary>
/// Distributed aerodynamics of a slender launch vehicle, evaluated element by element at the
/// local flow velocity v + ω×r, so static stability and rate damping come from one model:
///
///   crossflow (Allen–Perkins viscous crossflow):  N = ½ρ|v⊥|² · η·C_dn · A_planform
///   slender body (Munk):                          N = ½ρ|v|²  · 2ΔA · sin α cos α
///   axial:                                        A = ½ρ|v|²  · C_A · A_max · cos²α
///
/// Normal forces act at each element against its local crossflow; the axial force acts on
/// the axis and carries no moment. η·C_dn follows the measured crossflow Mach and Reynolds
/// numbers of each element and the body's fineness (<see cref="CrossflowDrag"/>, Jorgensen
/// NASA TR R-474); C_A (declared per vehicle, 0.6 default) carries the aggregate model's
/// transonic Mach rise.
/// </summary>
public static partial class DistributedAerodynamics
{
    /// <summary>Planform fraction of a tangent ogive (≈ 2/3 of its D·L rectangle).</summary>
    public const double OgivePlanformFraction = 2.0 / 3.0;

    /// <summary>Slender-body centre of pressure of a tangent ogive, from its tip (Barrowman).</summary>
    public const double OgiveCenterOfPressureFraction = 0.466;

    /// <summary>Two parts are coaxial when their axes are within this fraction of a diameter.</summary>
    private const double CoaxialToleranceFraction = 0.1;

    public static void BuildElements(PartGraph graph, IReadOnlyDictionary<Part, Vector3d> positions,
        List<AeroElement> elements)
    {
        if (TryBuildOverlappingCoaxialEnvelope(graph, positions, elements)) return;
        double lead = double.NegativeInfinity;
        Part? leadPart = null;
        foreach (var part in graph.Parts)
        {
            if (graph.IsEnclosedPayload(part) || !positions.TryGetValue(part, out var centre)) continue;
            double length = part.Definition.LengthM, diameter = part.Definition.DiameterM;
            if (length <= 0.0 || diameter <= 0.0) continue;

            double nose = System.Math.Clamp(part.Definition.NoseLengthM, 0.0, length);
            double top = centre.Y + 0.5 * length;
            double bottom = centre.Y - 0.5 * length;
            double cylinder = length - nose;
            if (cylinder > 0.0)
                elements.Add(new AeroElement(new Vector3d(centre.X, bottom + 0.5 * cylinder, centre.Z),
                    diameter * cylinder, 0.0, diameter));
            if (nose > 0.0)
                elements.Add(new AeroElement(new Vector3d(centre.X, top - 0.6 * nose, centre.Z),
                    OgivePlanformFraction * diameter * nose, 0.0, diameter));

            if (IsOnAxis(centre, diameter) && top > lead)
            {
                lead = top;
                leadPart = part;
            }
        }

        // The leading nose turns the flow from zero area to the full section (Munk: the
        // normal-force slope is 2·A_base whatever the nose shape; its shape sets where it acts).
        if (leadPart != null && positions.TryGetValue(leadPart, out var noseCentre))
        {
            var definition = leadPart.Definition;
            double nose = System.Math.Clamp(definition.NoseLengthM, 0.0, definition.LengthM);
            double top = noseCentre.Y + 0.5 * definition.LengthM;
            elements.Add(new AeroElement(new Vector3d(0.0, top - OgiveCenterOfPressureFraction * nose, 0.0),
                0.0, SectionArea(definition.DiameterM)));
        }

        // Every diameter step between stacked coaxial parts: a flare (wider aft) adds
        // stabilising normal force at the joint, a boat-tail (narrower aft) removes it.
        foreach (var joint in graph.Joints)
        {
            if (graph.IsEnclosedPayload(joint.Parent) || graph.IsEnclosedPayload(joint.Child)) continue;
            if (!positions.TryGetValue(joint.Parent, out var a) || !positions.TryGetValue(joint.Child, out var b))
                continue;
            var (upper, upperCentre, lower, lowerCentre) = a.Y >= b.Y
                ? (joint.Parent, a, joint.Child, b)
                : (joint.Child, b, joint.Parent, a);
            if (!IsOnAxis(upperCentre, upper.Definition.DiameterM) || !IsOnAxis(lowerCentre, lower.Definition.DiameterM))
                continue;
            if (upper.Definition.DiameterM <= 0.0 || lower.Definition.DiameterM <= 0.0) continue;
            double change = SectionArea(lower.Definition.DiameterM) - SectionArea(upper.Definition.DiameterM);
            if (System.Math.Abs(change) < 1e-9) continue;
            double junction = lowerCentre.Y + 0.5 * lower.Definition.LengthM;
            elements.Add(new AeroElement(new Vector3d(0.0, junction, 0.0), 0.0, change));
        }
    }

    /// <summary>
    /// Force and moment on the body for one candidate state. <paramref name="airVelocityAtCenterOfMass"/>
    /// is the centre of mass's velocity relative to the local atmosphere.
    /// </summary>
    public static AeroWrench Evaluate(
        IReadOnlyList<AeroElement> elements,
        Quaterniond orientation,
        Vector3d angularVelocityWorld,
        Vector3d centerOfMassLocal,
        Vector3d airVelocityAtCenterOfMass,
        double density,
        double temperature,
        double maximumDiameter,
        double axialDragCoefficient,
        double soundSpeedConstant = AerodynamicsModel.EarthAirSoundSpeedConstant,
        double dynamicViscosity = double.NaN)
    {
        double speed = airVelocityAtCenterOfMass.Magnitude;
        if (density <= 0.0 || !double.IsFinite(speed)) return default;
        double machMultiplier = AerodynamicsModel.GetMachDragMultiplier(
            AerodynamicsModel.ComputeMach(speed, temperature, soundSpeedConstant));
        var axis = orientation.Rotate(Vector3d.Up).Normalized;
        double soundSpeed = System.Math.Sqrt(soundSpeedConstant * System.Math.Max(1.0, temperature));
        double viscosity = double.IsFinite(dynamicViscosity) && dynamicViscosity > 0.0
            ? dynamicViscosity
            : EarthAirViscosity.GetDynamicViscosityAtTemperature(temperature);
        double fineness = Fineness(elements, maximumDiameter);

        var force = Vector3d.Zero;
        var torque = Vector3d.Zero;
        if (speed > 1e-6)
        {
            double cosine = axis.Dot(airVelocityAtCenterOfMass) / speed;
            double axialMagnitude = 0.5 * density * speed * speed * System.Math.Max(0.0, axialDragCoefficient)
                * SectionArea(maximumDiameter) * cosine * cosine * machMultiplier;
            force += axis * (-System.Math.Sign(cosine) * axialMagnitude);
        }

        for (int i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            var arm = orientation.Rotate(element.PositionLocal - centerOfMassLocal);
            var local = airVelocityAtCenterOfMass + angularVelocityWorld.Cross(arm);
            double localSpeedSquared = local.MagnitudeSquared;
            if (localSpeedSquared < 1e-12) continue;
            var crossflow = local - axis * axis.Dot(local);
            double crossflowSpeed = crossflow.Magnitude;
            if (crossflowSpeed < 1e-9) continue;
            var against = crossflow / -crossflowSpeed;

            double normal = 0.0;
            if (element.CrossflowAreaM2 > 0.0)
            {
                double diameter = element.DiameterM > 0.0 ? element.DiameterM : maximumDiameter;
                double coefficient = CrossflowDrag.Coefficient(crossflowSpeed / soundSpeed,
                    density * crossflowSpeed * diameter / viscosity, fineness);
                normal += 0.5 * density * crossflowSpeed * crossflowSpeed * coefficient * element.CrossflowAreaM2;
            }
            if (element.SlenderAreaChangeM2 != 0.0)
            {
                // ½ρv²·2ΔA·sinα·cosα, with sinα·cosα = |v⊥|·(a·v)/v²: signed so a nose
                // trailing the flow (tail-first flight) correctly reverses its contribution.
                normal += density * element.SlenderAreaChangeM2 * crossflowSpeed * axis.Dot(local);
            }
            var elementForce = against * normal;
            force += elementForce;
            torque += arm.Cross(elementForce);
        }
        return new AeroWrench(force, orientation.Inverse().Rotate(torque));
    }

    /// <summary>Body length/diameter from its crossflow planform (Σ d·l over the largest d²).</summary>
    public static double Fineness(IReadOnlyList<AeroElement> elements, double maximumDiameter)
    {
        if (!(maximumDiameter > 0.0)) return double.NaN;
        double planform = 0.0;
        for (int i = 0; i < elements.Count; i++) planform += elements[i].CrossflowAreaM2;
        return planform / (maximumDiameter * maximumDiameter);
    }

    /// <summary>Earth air (USSA-1976 Sutherland constants) when a caller passes no viscosity.</summary>
    private static readonly AtmosphereModel EarthAirViscosity = new();

    private static double SectionArea(double diameter) => System.Math.PI * 0.25 * diameter * diameter;

    private static bool IsOnAxis(Vector3d centre, double diameter) =>
        System.Math.Sqrt(centre.X * centre.X + centre.Z * centre.Z)
            <= CoaxialToleranceFraction * System.Math.Max(diameter, 1e-3);
}
