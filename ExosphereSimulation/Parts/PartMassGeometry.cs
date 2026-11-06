namespace Exosphere.Simulation.Parts;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

/// <summary>Which inventory a mass component of a part holds.</summary>
public enum PartMassComponentKind { Dry, LiquidFuel, Oxidizer, SolidFuel, Monopropellant, Header }

/// <summary>
/// The shape a part's stored mass takes along the part axis (+Y, the thrust direction).
/// <list type="bullet">
/// <item>A declared tank holds a settled liquid column: under axial acceleration the liquid
/// lies on the tank's aft end, so its centroid sits at <c>bottom + h/2</c> with
/// <c>h = m/λ</c> (λ = ρ·A, kg per metre of column). The column is a uniform solid cylinder of
/// the tank radius; end domes are replaced by the cylinder of equal volume.</item>
/// <item>A point holds a header tank's propellant at its station.</item>
/// <item>Otherwise the mass fills the part's own cylinder uniformly at a fixed centroid.</item>
/// </list>
/// Free surfaces, slosh and unsettled (coasting) liquid are not modelled here (F5).
/// </summary>
public readonly struct StoreShape
{
    /// <summary>Centroid along the part axis, from the part centre (m), of a fixed shape.</summary>
    public double FixedCenterYM { get; }
    /// <summary>Settled-column aft end, from the part centre (m); used when <see cref="LinearDensityKgPerM"/> &gt; 0.</summary>
    public double ColumnBottomYM { get; }
    /// <summary>Column mass per unit length λ = ρ·π·R² (kg/m); zero for a fixed shape.</summary>
    public double LinearDensityKgPerM { get; }
    public double RadiusM { get; }
    /// <summary>Fixed-shape central length (m); a point has zero length and radius.</summary>
    public double LengthM { get; }

    private StoreShape(double fixedCenter, double bottom, double lambda, double radius, double length)
    {
        FixedCenterYM = fixedCenter;
        ColumnBottomYM = bottom;
        LinearDensityKgPerM = lambda;
        RadiusM = radius;
        LengthM = length;
    }

    public bool IsColumn => LinearDensityKgPerM > 0.0;

    public static StoreShape UniformCylinder(double centerYM, double lengthM, double radiusM) =>
        new(centerYM, 0.0, 0.0, radiusM, lengthM);

    public static StoreShape Point(double centerYM) => new(centerYM, 0.0, 0.0, 0.0, 0.0);

    public static StoreShape SettledColumn(double bottomYM, double radiusM, double densityKgPerM3) =>
        new(0.0, bottomYM, densityKgPerM3 * System.Math.PI * radiusM * radiusM, radiusM, 0.0);

    /// <summary>Centroid along the part axis for mass <paramref name="massKg"/>.</summary>
    public double CenterYM(double massKg) =>
        IsColumn ? ColumnBottomYM + 0.5 * massKg / LinearDensityKgPerM : FixedCenterYM;

    /// <summary>d(centroid)/d(mass), m/kg.</summary>
    public double CenterYPerKg => IsColumn ? 0.5 / LinearDensityKgPerM : 0.0;

    /// <summary>Central transverse inertia per kilogram (m²) at mass <paramref name="massKg"/>.</summary>
    public double TransverseSpecific(double massKg)
    {
        double length = IsColumn ? massKg / LinearDensityKgPerM : LengthM;
        return (3.0 * RadiusM * RadiusM + length * length) / 12.0;
    }

    /// <summary>d(transverse specific inertia)/d(mass), m²/kg (h·dh/dm/6).</summary>
    public double TransverseSpecificPerKg(double massKg) =>
        IsColumn ? massKg / (LinearDensityKgPerM * LinearDensityKgPerM) / 6.0 : 0.0;

    public double AxialSpecific => 0.5 * RadiusM * RadiusM;
}

/// <summary>One mass component of a part at its present load.</summary>
public readonly record struct PartMassComponent(PartMassComponentKind Kind, int HeaderIndex,
    double MassKg, StoreShape Shape)
{
    public double CenterYM => Shape.CenterYM(MassKg);
}

/// <summary>
/// The single mass decomposition every consumer shares (composite mass properties, the part's
/// own centre, and the continuous candidate sampler of F4): dry structure at its declared
/// centre with its declared or uniform-cylinder gyration, each main resource as a declared
/// settled tank column or the part's uniform cylinder, and header tanks as point masses.
/// Each component keeps its own centroid, so the parallel-axis spread between dry structure
/// and propellant is never lost.
/// </summary>
public static class PartMassGeometry
{
    /// <summary>The part's own cylinder (length, radius); zero falls back to the caller's defaults.</summary>
    public static (double Length, double Radius) Envelope(PartDefinition definition,
        double fallbackLength, double fallbackDiameter)
    {
        double length = definition.LengthM > 0.0 ? definition.LengthM : fallbackLength;
        double diameter = definition.DiameterM > 0.0 ? definition.DiameterM : fallbackDiameter;
        return (length, diameter * 0.5);
    }

    /// <summary>Shape of one main resource store of a part.</summary>
    public static StoreShape MainStoreShape(PartDefinition definition, PartMassComponentKind kind,
        double fallbackLength, double fallbackDiameter)
    {
        var tank = definition.FindPropellantTank(kind);
        if (tank != null) return StoreShape.SettledColumn(tank.BottomYM, tank.RadiusM, tank.DensityKgPerM3);
        var (length, radius) = Envelope(definition, fallbackLength, fallbackDiameter);
        return StoreShape.UniformCylinder(0.0, length, radius);
    }

    /// <summary>Shape of the dry structure: declared gyration radii or the part's uniform cylinder.</summary>
    public static StoreShape DryShape(PartDefinition definition, double fallbackLength, double fallbackDiameter)
    {
        if (definition.DryGyration is { } gyration)
        {
            // A uniform cylinder with the declared gyration: axial k² = R²/2 and transverse
            // k² = (3R² + L²)/12, solved for the equivalent R and L.
            double radiusSquared = 2.0 * gyration.AxialM * gyration.AxialM;
            double lengthSquared = 12.0 * gyration.TransverseM * gyration.TransverseM - 3.0 * radiusSquared;
            return StoreShape.UniformCylinder(definition.DryMassCenterYM,
                System.Math.Sqrt(System.Math.Max(0.0, lengthSquared)), System.Math.Sqrt(radiusSquared));
        }
        var (length, radius) = Envelope(definition, fallbackLength, fallbackDiameter);
        return StoreShape.UniformCylinder(definition.DryMassCenterYM, length, radius);
    }

    /// <summary>Appends this part's components at their present loads (zero masses included).</summary>
    public static void AppendComponents(Part part, double fallbackLength, double fallbackDiameter,
        List<PartMassComponent> into)
    {
        var definition = part.Definition;
        into.Add(new(PartMassComponentKind.Dry, -1, part.EffectiveMassDry,
            DryShape(definition, fallbackLength, fallbackDiameter)));
        AppendMain(PartMassComponentKind.LiquidFuel, part.LiquidFuel);
        AppendMain(PartMassComponentKind.Oxidizer, part.Oxidizer);
        AppendMain(PartMassComponentKind.SolidFuel, part.SolidFuel);
        AppendMain(PartMassComponentKind.Monopropellant, part.Monopropellant);
        var headers = definition.HeaderTanks;
        for (int i = 0; i < part.HeaderPropellantKg.Length; i++)
        {
            // An undeclared saved slot has no station: it stays at the part centre.
            double station = i < headers.Count ? headers[i].CenterYM : 0.0;
            into.Add(new(PartMassComponentKind.Header, i, part.HeaderPropellantKg[i], StoreShape.Point(station)));
        }

        void AppendMain(PartMassComponentKind kind, double mass) =>
            into.Add(new(kind, -1, mass, MainStoreShape(definition, kind, fallbackLength, fallbackDiameter)));
    }

    /// <summary>The part's own centroid along its axis, from its centre (m).</summary>
    public static double CenterOffsetYM(Part part)
    {
        var definition = part.Definition;
        double dry = part.EffectiveMassDry;
        double mass = dry, moment = dry * definition.DryMassCenterYM;
        Add(PartMassComponentKind.LiquidFuel, part.LiquidFuel);
        Add(PartMassComponentKind.Oxidizer, part.Oxidizer);
        Add(PartMassComponentKind.SolidFuel, part.SolidFuel);
        Add(PartMassComponentKind.Monopropellant, part.Monopropellant);
        var headers = definition.HeaderTanks;
        for (int i = 0; i < part.HeaderPropellantKg.Length; i++)
        {
            double kg = part.HeaderPropellantKg[i];
            mass += kg;
            moment += kg * (i < headers.Count ? headers[i].CenterYM : 0.0);
        }
        return mass > 0.0 ? moment / mass : 0.0;

        void Add(PartMassComponentKind kind, double kg)
        {
            if (kg <= 0.0) return;
            mass += kg;
            // Only a declared tank moves a resource off the part centre.
            var tank = definition.FindPropellantTank(kind);
            moment += kg * (tank == null ? 0.0
                : StoreShape.SettledColumn(tank.BottomYM, tank.RadiusM, tank.DensityKgPerM3).CenterYM(kg));
        }
    }

    /// <summary>Central inertia per kilogram of a component, axis +Y, as a body tensor.</summary>
    public static Matrix3x3d SpecificInertia(in PartMassComponent component)
    {
        double transverse = component.Shape.TransverseSpecific(component.MassKg);
        double axial = component.Shape.AxialSpecific;
        return new Matrix3x3d(transverse, 0, 0, 0, axial, 0, 0, 0, transverse);
    }
}
