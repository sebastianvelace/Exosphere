namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Navigation;

/// <summary>
/// Calcula nodos de transferencia: Lambert geocéntrico para la Luna y Hohmann
/// heliocéntrico para destinos planetarios.
/// Se instancia como hijo de MapViewController y se registra como singleton.
/// </summary>
public partial class TransferPlanner : Node
{
    private const double LunarCoastSeconds =
        66.0 * 3600.0 + 17.0 * 60.0 + 43.3;
    private const double LunarTargetAltitude = 100_000.0;
    private const double MaximumPracticalTliDeltaV = 4_500.0;

    public static TransferPlanner? Instance { get; private set; }

    public string? TargetBodyId  { get; private set; }
    public ManeuverNode? CurrentNode { get; private set; }
    public LunarTransferPlan? LunarPlan { get; private set; }
    public EncounterResult? EarthFrameEncounter { get; private set; }
    public OrbitalElements? CurrentTransferOrbit { get; private set; }
    public string? LastPlanningError { get; private set; }

    /// <summary>
    /// Última predicción de encuentro con el destino (entrada a SOI o máxima aproximación),
    /// en marco HELIOCÉNTRICO. Se recalcula en <see cref="PlanTransfer"/> y al ajustar el Δv.
    /// Encounter prediction for the selected target — heliocentric (Sun-relative) frame.
    /// </summary>
    public EncounterResult? Encounter { get; private set; }

    public override void _Ready() => Instance = this;

    /// <summary>
    /// Calcula una transferencia Hohmann desde la órbita actual hacia el cuerpo destino.
    /// Retorna null si no hay suficiente información.
    /// </summary>
    public ManeuverNode? PlanTransfer(string targetBodyId)
    {
        TargetBodyId = targetBodyId;
        CurrentNode  = null;
        LunarPlan    = null;
        EarthFrameEncounter = null;
        CurrentTransferOrbit = null;
        LastPlanningError = null;
        Encounter = null;

        var bridge   = SimulationBridge.Instance;
        var vessel   = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (vessel == null || universe == null) return null;

        var targetBody = universe.GetBody(targetBodyId);
        var sunBody    = universe.GetBody("sun");
        if (targetBody == null || sunBody == null) return null;

        if (targetBodyId == "moon")
            return PlanLunarTransfer(vessel, universe, targetBody, sunBody);

        // Posiciones heliocentricas
        var r1Vec = vessel.Position     - sunBody.Position;   // vessel desde el Sol
        var r2Vec = targetBody.Position - sunBody.Position;   // destino desde el Sol

        double r1 = r1Vec.Magnitude;
        double r2 = r2Vec.Magnitude;

        if (r1 < 1000.0 || r2 < 1000.0) return null;

        double mu  = sunBody.GM;
        if (mu <= 0.0) return null;

        HohmannTransferPlan plan;
        try { plan = HohmannTransfer.Compute(mu, r1, r2); }
        catch (ArgumentException) { return null; }

        // Momento del burn = ahora
        double burnTime = universe.CurrentTime;

        // Dirección prograde heliocéntrica del vessel
        var helioVel = vessel.Velocity - sunBody.Velocity;
        var prograde = helioVel.Magnitude > 0.1
            ? helioVel.Normalized
            : r1Vec.Cross(Vector3d.Up).Normalized;

        CurrentNode = new ManeuverNode
        {
            BurnTime     = burnTime,
            DeltaV       = prograde * plan.FirstBurnDeltaV,
            DvMagnitude  = System.Math.Abs(plan.FirstBurnDeltaV),
            TargetBodyId = targetBodyId,
            TimeOfFlight = plan.TimeOfFlight,
            SecondBurnDv = plan.SecondBurnDeltaV,
            TransferSma  = plan.TransferSemiMajorAxis,
            RequiredPhaseAngle = plan.RequiredPhaseAngle,
            ReferenceBodyId = "sun",
            TransferKind = TransferKind.HeliocentricHohmann,
            BurnLabel = "DV1",
            ArrivalBurnLabel = "DV2",
        };

        GD.Print($"[TransferPlanner] Hohmann to {targetBodyId}: Δv1={plan.FirstBurnDeltaV:F0} m/s, " +
                 $"Δv2={plan.SecondBurnDeltaV:F0} m/s, ToF={plan.TimeOfFlight / 86400.0:F1} days, " +
                 $"phase={plan.RequiredPhaseAngle * MathUtils.RAD_TO_DEG:F1} deg");

        PredictEncounter();
        return CurrentNode;
    }

    private ManeuverNode? PlanLunarTransfer(
        Vessel vessel,
        Universe universe,
        CelestialBody moon,
        CelestialBody sun)
    {
        var moonEphemeris = moon.OrbitalElements;
        var earth = moonEphemeris is null
            ? null
            : universe.GetBody(moonEphemeris.ReferenceBodyId);
        if (earth == null || moonEphemeris == null || earth.Id != "earth")
            return FailLunarPlan("Moon ephemeris is not Earth-relative.");

        var dominant = universe.GetDominantBody(vessel.Position);
        if (dominant.Id != earth.Id)
            return FailLunarPlan("TLI requires an Earth parking orbit.");

        Vector3d relPosition = vessel.Position - earth.Position;
        Vector3d relVelocity = vessel.Velocity - earth.Velocity;
        OrbitalElements parkingOrbit = OrbitalElements.FromStateVector(
            relPosition,
            relVelocity,
            earth.GM,
            earth.Id,
            universe.CurrentTime);

        if (parkingOrbit.IsHyperbolic ||
            parkingOrbit.IsRadial ||
            parkingOrbit.Periapsis <= earth.Radius + 80_000.0)
        {
            return FailLunarPlan(
                "TLI requires a stable Earth orbit with periapsis above 80 km.");
        }

        LunarTransferPlan plan;
        try
        {
            plan = LunarTransferPlanner.Compute(
                earth.GM,
                moon.GM,
                moon.Radius,
                moon.SphereOfInfluence,
                parkingOrbit,
                moonEphemeris,
                universe.CurrentTime,
                LunarCoastSeconds,
                LunarTargetAltitude,
                windowSamples: 90);
        }
        catch (ArgumentException exception)
        {
            return FailLunarPlan(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return FailLunarPlan(exception.Message);
        }

        if (plan.InjectionDeltaVMag > MaximumPracticalTliDeltaV)
        {
            return FailLunarPlan(
                $"No practical TLI window from this orbital plane " +
                $"({plan.InjectionDeltaVMag / 1000.0:F2} km/s required).");
        }

        LunarPlan = plan;
        EarthFrameEncounter = plan.Encounter;
        CurrentTransferOrbit = plan.EarthTransferOrbit;
        CurrentNode = new ManeuverNode
        {
            BurnTime = plan.BurnTime,
            DeltaV = plan.InjectionDeltaV,
            DvMagnitude = plan.InjectionDeltaVMag,
            TargetBodyId = moon.Id,
            TimeOfFlight = plan.TimeOfFlight,
            SecondBurnDv = plan.EstimatedCircularInsertionDeltaV,
            TransferSma = plan.EarthTransferOrbit.SemiMajorAxis,
            ReferenceBodyId = earth.Id,
            TransferKind = TransferKind.LunarLambert,
            BurnLabel = "TLI",
            ArrivalBurnLabel = "LOI",
            PlannedDeparturePosition = plan.DeparturePosition,
            PlannedPreBurnVelocity = plan.PreBurnVelocity,
            TargetPeriapsisAltitude =
                plan.TargetPeriluneRadius - moon.Radius,
            PredictedPeriapsisAltitude =
                plan.PredictedLunarPeriapsisRadius - moon.Radius,
            BPlaneAimRadius = plan.BPlaneAimRadius,
        };

        Encounter = ToHeliocentricEncounter(
            plan.Encounter, earth, universe, sun);

        GD.Print(
            $"[TransferPlanner] Lunar Lambert: TLI={plan.InjectionDeltaVMag:F0} m/s, " +
            $"LOI≈{plan.EstimatedCircularInsertionDeltaV:F0} m/s, " +
            $"T-burn={(plan.BurnTime - universe.CurrentTime) / 60.0:F1} min, " +
            $"ToF={plan.TimeOfFlight / 86400.0:F2} d, " +
            $"Pe={CurrentNode.PredictedPeriapsisAltitude / 1000.0:F0} km");

        return CurrentNode;
    }

    private ManeuverNode? FailLunarPlan(string message)
    {
        LastPlanningError = message;
        GD.PushWarning($"[TransferPlanner] Lunar plan unavailable: {message}");
        return null;
    }

    /// <summary>
    /// Recalcula la predicción de encuentro para el nodo actual usando el factor de ajuste
    /// vigente. Propaga la cónica heliocéntrica POST-burn y la posición del destino hacia
    /// adelante para hallar la entrada a su SOI o el punto de máxima aproximación.
    /// Recomputes the encounter for the current node (call after Δv adjustments).
    /// </summary>
    public void PredictEncounter()
    {
        Encounter = null;
        var node = CurrentNode;
        if (node?.TargetBodyId == null) return;

        var universe = SimulationBridge.Instance?.Universe;
        var vessel   = SimulationBridge.Instance?.ActiveVessel;
        var sun      = universe?.GetBody("sun");
        var target   = universe?.GetBody(node.TargetBodyId);
        if (universe == null || vessel == null || sun == null || target == null) return;

        if (node.TransferKind == TransferKind.LunarLambert)
        {
            PredictLunarEncounter(node, universe, target, sun);
            return;
        }

        if (sun.GM <= 0.0) return;

        // Estado heliocéntrico post-burn: aplicamos el Δv (con su factor de ajuste) a la
        // velocidad actual del vessel respecto al Sol, en el instante del burn (= ahora).
        var relPos    = vessel.Position - sun.Position;
        var helioVel  = vessel.Velocity - sun.Velocity;
        // node.DeltaV ya codifica el SIGNO de la maniobra (retrógrado para Venus, etc.);
        // dvDir apunta en la dirección correcta y DvMagnitude es la magnitud positiva.
        var dvDir     = node.DeltaV.Magnitude > 1e-6 ? node.DeltaV.Normalized : Vector3d.Zero;
        var postVel   = helioVel + dvDir * (node.DvMagnitude * node.DvAdjustFactor);
        if (relPos.Magnitude < 1000.0) return;

        var postOrbit = OrbitalElements.FromStateVector(relPos, postVel, sun.GM, "sun", node.BurnTime);
        if (postOrbit.IsRadial) return;

        // Ventana de búsqueda: hasta ~1.3× el tiempo de vuelo Hohmann (margen para fases no ideales).
        double window = System.Math.Max(node.TimeOfFlight * 1.3, 3600.0);

        // Posición del destino RELATIVA AL SOL en t (compone padre+luna si es un satélite).
        Vector3d TargetRel(double t) => HelioPositionAt(target, t, universe, sun) - sun.Position;

        Encounter = TrajectoryPrediction.FindEncounter(
            postOrbit, sun.GM, TargetRel,
            targetSoiRadius: target.SphereOfInfluence,
            startTime: node.BurnTime, searchWindow: window);

        if (Encounter is { } enc)
        {
            string tag = enc.HasEncounter ? "SOI entry" : "closest approach";
            GD.Print($"[TransferPlanner] {tag}: dist={enc.ClosestApproachDistance / 1e6:F0} Mm, " +
                     $"ETA={(enc.TimeOfClosestApproach - node.BurnTime) / 86400.0:F1} d");
        }
    }

    private void PredictLunarEncounter(
        ManeuverNode node,
        Universe universe,
        CelestialBody moon,
        CelestialBody sun)
    {
        var moonEphemeris = moon.OrbitalElements;
        var earth = universe.GetBody(node.ReferenceBodyId);
        if (earth == null || moonEphemeris == null || earth.GM <= 0.0)
            return;

        Vector3d dvDirection = node.DeltaV.Magnitude > 1e-6
            ? node.DeltaV.Normalized
            : Vector3d.Zero;
        Vector3d postBurnVelocity =
            node.PlannedPreBurnVelocity
            + dvDirection * (node.DvMagnitude * node.DvAdjustFactor);
        OrbitalElements transferOrbit = OrbitalElements.FromStateVector(
            node.PlannedDeparturePosition,
            postBurnVelocity,
            earth.GM,
            earth.Id,
            node.BurnTime);
        CurrentTransferOrbit = transferOrbit;

        LunarEncounterAnalysis analysis = LunarTransferPlanner.AnalyzeEncounter(
            earth.GM,
            moon.GM,
            moon.Radius,
            moon.SphereOfInfluence,
            transferOrbit,
            moonEphemeris,
            node.BurnTime,
            System.Math.Max(node.TimeOfFlight * 1.12, 3600.0));

        Encounter = ToHeliocentricEncounter(
            analysis.Encounter, earth, universe, sun);
        EarthFrameEncounter = analysis.Encounter;
        node.ArrivalImpact = analysis.IsImpact;
        node.PredictedPeriapsisAltitude =
            analysis.PredictedPeriapsisRadius - moon.Radius;
        node.SecondBurnDv =
            analysis.EstimatedCircularInsertionDeltaV;
    }

    /// <summary>
    /// Posición INERCIAL (mundo) de <paramref name="body"/> en el tiempo <paramref name="t"/>,
    /// propagando su cónica Kepler y componiendo recursivamente la de su cuerpo padre.
    /// Bodies sin elementos (el Sol) quedan en su posición actual.
    /// </summary>
    private static Vector3d HelioPositionAt(CelestialBody body, double t, Universe universe, CelestialBody sun)
    {
        var oe = body.OrbitalElements;
        if (oe == null) return body.Position;   // raíz (Sol) — fija

        var parent = universe.GetBody(oe.ReferenceBodyId);
        if (parent == null) return body.Position;

        var (relPos, _) = oe.GetStateAtTime(t, parent.GM);
        Vector3d parentPos = parent.Id == sun.Id ? sun.Position
                                                 : HelioPositionAt(parent, t, universe, sun);
        return parentPos + relPos;
    }

    private static EncounterResult ToHeliocentricEncounter(
        EncounterResult encounter,
        CelestialBody earth,
        Universe universe,
        CelestialBody sun)
    {
        double markerTime = encounter.HasEncounter
            ? encounter.TimeOfSoiEntry
            : encounter.TimeOfClosestApproach;
        Vector3d earthHeliocentric =
            HelioPositionAt(earth, markerTime, universe, sun) - sun.Position;
        return encounter with
        {
            EncounterPosition =
                earthHeliocentric + encounter.EncounterPosition,
        };
    }

    public void ClearNode()
    {
        CurrentNode  = null;
        TargetBodyId = null;
        Encounter    = null;
        LunarPlan    = null;
        EarthFrameEncounter = null;
        CurrentTransferOrbit = null;
        LastPlanningError = null;
    }
}

public enum TransferKind
{
    HeliocentricHohmann,
    LunarLambert,
}

/// <summary>Representa un nodo de maniobra de transferencia calculado.</summary>
public class ManeuverNode
{
    /// <summary>Tiempo de simulación (s) al que iniciar el burn.</summary>
    public double   BurnTime       { get; set; }

    /// <summary>Vector Δv en espacio mundo (m/s).</summary>
    public Vector3d DeltaV         { get; set; }

    /// <summary>Magnitud |Δv| (m/s).</summary>
    public double   DvMagnitude    { get; set; }

    /// <summary>ID del cuerpo destino.</summary>
    public string?  TargetBodyId   { get; set; }

    /// <summary>Tiempo de vuelo hasta la llegada (s).</summary>
    public double   TimeOfFlight   { get; set; }

    /// <summary>Δv del segundo burn (circularización en destino) (m/s).</summary>
    public double   SecondBurnDv   { get; set; }

    /// <summary>Semi-eje mayor de la elipse de transferencia (m).</summary>
    public double   TransferSma    { get; set; }

    /// <summary>Required coplanar target lead angle at departure (rad), [0, 2pi).</summary>
    public double   RequiredPhaseAngle { get; set; }

    /// <summary>Factor de ajuste manual del usuario (±10% por pasos de 5%).</summary>
    public double   DvAdjustFactor { get; set; } = 1.0;

    public string ReferenceBodyId { get; set; } = "sun";
    public TransferKind TransferKind { get; set; }
    public string BurnLabel { get; set; } = "DV1";
    public string ArrivalBurnLabel { get; set; } = "DV2";
    public Vector3d PlannedDeparturePosition { get; set; }
    public Vector3d PlannedPreBurnVelocity { get; set; }
    public double TargetPeriapsisAltitude { get; set; }
    public double PredictedPeriapsisAltitude { get; set; }
    public double BPlaneAimRadius { get; set; }
    public bool ArrivalImpact { get; set; }
}
