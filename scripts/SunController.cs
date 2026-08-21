namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Systems;

/// <summary>
/// Keeps scene lighting consistent with the Sun's true position each frame.
///
/// The simulation tracks the Sun as a real body, but the scene
/// <see cref="DirectionalLight3D"/> and the planet shaders' <c>sun_dir</c> uniforms
/// were authored at fixed values, so the Earth's daylit hemisphere, terminator and
/// night-side city lights never tracked the actual Sun.
///
/// This node computes the unit direction TOWARD the Sun in render/world space and:
///   • orients the scene directional light so its forward (−Z) points away from the
///     Sun (i.e. light travels FROM the Sun toward the scene), and
///   • pushes <c>sun_dir</c> into every planet material that exposes it (Earth first).
///
/// Render space is only translated relative to sim space (floating origin), never
/// rotated, so a sim-space direction equals a render-space direction. The Sun is so
/// distant that (sun − vessel) is essentially the Earth→Sun direction.
/// </summary>
[GlobalClass]
public partial class SunController : Node
{
    public readonly record struct SolarGeometrySnapshot(
        string AtmosphereBodyId,
        float Visibility,
        float AtmosphericVisibility,
        bool OccluderEnabled,
        Vector3 OccluderDirection,
        float OccluderAngularRadius);

    public static SunController? Instance { get; private set; }
    public static float SolarVisibility { get; private set; } = 1f;
    /// <summary>True geometric solar elevation at the active body's surface, in degrees.</summary>
    public static double SolarElevationDegrees { get; private set; } = double.NaN;
    /// <summary>Standard civil-lighting bucket derived from solar elevation.</summary>
    public static string SolarPhase { get; private set; } = "UNKNOWN";
    /// <summary>Simulation epoch used for the last solar-cycle sample.</summary>
    public static double SolarSimulationTime { get; private set; } = double.NaN;

    // Visual acceptance captures may request a deterministic daylight angle without
    // teleporting the physical Sun body.  The override is presentation-only: eclipse
    // geometry, gravity and ephemeris state continue to use the real body positions.
    private double? _visualSunElevationOverrideDegrees;
    // Presentation-only fixture for deterministic daylight captures. It never changes
    // Sun/Moon positions, ephemeris, gravity or the physical eclipse calculation.
    private bool _visualClearSolarOcclusion;

    /// <summary>Requested presentation-only solar elevation, or null for physical time.</summary>
    public double? VisualSunElevationOverrideDegrees => _visualSunElevationOverrideDegrees;

    /// <summary>
    /// Clears only the presentation-side solar-disc occlusion for a visual capture.
    /// Physical positions and eclipse state remain authoritative in the simulation.
    /// </summary>
    public void SetVisualClearSolarOcclusion(bool clear)
    {
        _visualClearSolarOcclusion = clear;
        _lastFedVisibility = -1f;
        _solarGeometryReady = false;
    }

    /// <summary>
    /// Applies a bounded solar-elevation override to render consumers.  This does not
    /// change the simulation's Sun position or any force calculation.
    /// </summary>
    public void SetVisualSunElevationOverride(double? elevationDegrees)
    {
        _visualSunElevationOverrideDegrees = elevationDegrees.HasValue
            ? System.Math.Clamp(elevationDegrees.Value, -90.0, 90.0)
            : null;
        _lastSunDir = Vector3.Zero;
        _lastFedVisibility = -1f;
    }

    /// <summary>Returns the physical or presentation-overridden direction toward the Sun.</summary>
    public Vector3d GetVisualSunDirection(
        CelestialBody body, Vector3d observer, Vector3d physicalDirection)
    {
        if (!_visualSunElevationOverrideDegrees.HasValue)
            return physicalDirection;

        Vector3d up = body.GetGeodeticUp(observer);
        Vector3d horizontal = physicalDirection - up * physicalDirection.Dot(up);
        if (horizontal.MagnitudeSquared < 1e-12)
            horizontal = body.GetEastDirection(observer);
        horizontal = horizontal.Normalized;
        double elevation = _visualSunElevationOverrideDegrees.Value
            * System.Math.PI / 180.0;
        return (horizontal * System.Math.Cos(elevation)
            + up * System.Math.Sin(elevation)).Normalized;
    }

    // Lighting is presentation-only.  Twenty updates per second are enough to keep a
    // moving terminator/eclipsing body visually locked while avoiding a tree walk and
    // several material writes on every render frame.
    private const double VisualUpdatePeriodSeconds = 1.0 / 20.0;
    private double _visualUpdateTimer;
    private double _solarTelemetryTimer;
    private Vector3 _lastSunDir;
    private float _lastFedVisibility = -1f;
    private Universe? _cachedUniverse;
    private CelestialBody? _cachedSun;
    private readonly List<ShaderMaterial> _planetMaterials = new();
    private Node3D? _planetsNode;
    private MeshInstance3D? _groundMesh;
    private ShaderMaterial? _groundMaterial;

    // Cached node lookups — re-found lazily if they go null (e.g. scene rebuild).
    private DirectionalLight3D? _light;
    private ShaderMaterial?     _earthMat;
    private int _planetNodeCount = -1;
    private bool _solarGeometryReady;
    private bool _solarGeometryTelemetryPublished;
    private SolarGeometrySnapshot _solarGeometrySnapshot;

    public override void _Ready()
    {
        Instance = this;
    }

    public override void _ExitTree()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
        _solarGeometryReady = false;
        _solarGeometryTelemetryPublished = false;
    }

    public bool TryGetCachedSolarGeometry(
        string atmosphereBodyId,
        out SolarGeometrySnapshot snapshot)
    {
        if (!_solarGeometryReady
            || !string.Equals(
                _solarGeometrySnapshot.AtmosphereBodyId,
                atmosphereBodyId,
                StringComparison.OrdinalIgnoreCase))
        {
            snapshot = default;
            return false;
        }

        snapshot = _solarGeometrySnapshot;
        return true;
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (!ReferenceEquals(_cachedUniverse, universe))
        {
            _cachedUniverse = universe;
            _cachedSun = universe?.GetBody("sun");
            _planetMaterials.Clear();
            _planetsNode = null;
            _groundMesh = null;
            _groundMaterial = null;
            _earthMat = null;
            _planetNodeCount = -1;
            _lastSunDir = Vector3.Zero;
            _lastFedVisibility = -1f;
            _visualUpdateTimer = 0.0;
            _solarTelemetryTimer = 0.0;
            _solarGeometryReady = false;
            _solarGeometryTelemetryPublished = false;
            SolarVisibility = 1f;
            SolarElevationDegrees = double.NaN;
            SolarPhase = "UNKNOWN";
            SolarSimulationTime = double.NaN;
        }

        var sun = _cachedSun;
        if (vessel == null || universe == null || sun == null) return;

        var atmosphereBody = universe.GetDominantBody(vessel.Position);
        Vector3d physicalDir = (sun.Position - vessel.Position).Normalized;
        Vector3d simDir = atmosphereBody == null
            ? physicalDir
            : GetVisualSunDirection(atmosphereBody, vessel.Position, physicalDir);
        var renderDir = new Vector3((float)simDir.X, (float)simDir.Y, (float)simDir.Z);
        _visualUpdateTimer -= System.Math.Max(0.0, delta);
        if (_visualUpdateTimer > 0.0) return;
        _visualUpdateTimer = VisualUpdatePeriodSeconds;

        UpdateSolarCycle(universe.CurrentTime, vessel, atmosphereBody, renderDir);

        bool sunDirectionChanged = _lastSunDir == Vector3.Zero
            || _lastSunDir.DistanceSquaredTo(renderDir) > 1e-10f;
        bool materialsNeedRefresh = _earthMat == null
            || _planetsNode == null
            || !IsInstanceValid(_planetsNode)
            || (_planetsNode != null && _planetNodeCount != _planetsNode.GetChildCount());
        if (sunDirectionChanged)
        {
            OrientLight(renderDir);
            _lastSunDir = renderDir;
        }
        if (sunDirectionChanged || materialsNeedRefresh)
            FeedSunDir(renderDir);

        string atmosphereBodyId = atmosphereBody?.Id ?? string.Empty;
        double sunDistance = (sun.Position - vessel.Position).Magnitude;
        // When a presentation elevation override is active, occultation must use the
        // same sun the sky and the directional light are aiming at. Otherwise the
        // physical night-side Earth extinguishes a visually risen sun (J2000 Starbase
        // is astronomical twilight; play daylight is a 28° override).
        Vector3d sunSamplePosition = _visualSunElevationOverrideDegrees.HasValue
            ? vessel.Position + simDir * sunDistance
            : sun.Position;
        double visibility = 1.0;
        double atmosphericVisibility = 1.0;
        CelestialBody? bestOccluder = null;
        double lowestVisibility = 1.0;
        foreach (var body in universe.Bodies)
        {
            if (body.Id == "sun") continue;
            double bodyVisibility = MissionGeometry.LimbDarkenedSolarDiscVisibility(
                vessel.Position, body.Position, body.Radius, sunSamplePosition, sun.Radius);
            visibility = System.Math.Min(visibility, bodyVisibility);
            if (body.Id != atmosphereBodyId)
                atmosphericVisibility = System.Math.Min(atmosphericVisibility, bodyVisibility);
            if (bodyVisibility < lowestVisibility)
            {
                lowestVisibility = bodyVisibility;
                bestOccluder = body;
            }
        }
        bool occluderEnabled = bestOccluder != null && lowestVisibility < 0.999999;
        Vector3 occluderDirection = Vector3.Zero;
        float occluderAngularRadius = 0.0f;
        if (occluderEnabled)
        {
            var direction = (bestOccluder!.Position - vessel.Position).Normalized;
            double distance = (bestOccluder.Position - vessel.Position).Magnitude;
            occluderDirection = new Vector3(
                (float)direction.X, (float)direction.Y, (float)direction.Z);
            occluderAngularRadius = (float)MissionGeometry.ApparentAngularRadius(
                bestOccluder.Radius, distance);
        }
        if (_visualClearSolarOcclusion)
        {
            visibility = 1.0;
            atmosphericVisibility = 1.0;
            lowestVisibility = 1.0;
            bestOccluder = null;
            occluderEnabled = false;
            occluderDirection = Vector3.Zero;
            occluderAngularRadius = 0.0f;
        }
        _solarGeometrySnapshot = new SolarGeometrySnapshot(
            atmosphereBodyId,
            (float)visibility,
            (float)atmosphericVisibility,
            occluderEnabled,
            occluderDirection,
            occluderAngularRadius);
        _solarGeometryReady = true;
        if (!_solarGeometryTelemetryPublished)
        {
            GD.Print(
                $"PERF_SOLAR_GEOMETRY mode=shared cadenceHz=20 skyConsumerHz=12 "
                + $"body={atmosphereBodyId} shared=True");
            _solarGeometryTelemetryPublished = true;
        }
        float visibilityValue = (float)visibility;
        if (System.Math.Abs(visibilityValue - _lastFedVisibility) > 1e-4f)
        {
            SolarVisibility = visibilityValue;
            FeedSolarVisibility(visibilityValue);
            _lastFedVisibility = visibilityValue;
        }
    }

    private void UpdateSolarCycle(
        double simulationTime,
        Vessel vessel,
        CelestialBody? atmosphereBody,
        Vector3 sunDirection)
    {
        if (atmosphereBody == null)
        {
            SolarElevationDegrees = double.NaN;
            SolarPhase = "UNKNOWN";
            SolarSimulationTime = simulationTime;
            return;
        }

        Vector3d up = (vessel.Position - atmosphereBody.Position).Normalized;
        double elevation = System.Math.Asin(System.Math.Clamp(
            up.Dot(new Vector3d(sunDirection.X, sunDirection.Y, sunDirection.Z)), -1.0, 1.0))
            * 180.0 / System.Math.PI;
        SolarElevationDegrees = elevation;
        SolarPhase = ClassifySolarPhase(elevation);
        SolarSimulationTime = simulationTime;

        _solarTelemetryTimer -= VisualUpdatePeriodSeconds;
        if (_solarTelemetryTimer > 0.0) return;
        _solarTelemetryTimer = 1.0;
        GD.Print(
            $"PERF_SOLAR_CYCLE time={simulationTime:F2} body={atmosphereBody.Id} "
            + $"elevationDeg={elevation:F3} phase={SolarPhase} "
            + $"timeScale={_cachedUniverse?.TimeScale ?? 0.0:F3} "
            + $"solarVisibility={SolarVisibility:F3}");
    }

    /// <summary>
    /// Maps geometric solar elevation to the standard twilight bands used by
    /// observers: civil, nautical and astronomical twilight. The thresholds are
    /// presentation labels only; the shader continues to use the continuous angle.
    /// </summary>
    public static string ClassifySolarPhase(double elevationDegrees) =>
        elevationDegrees >= -0.833 ? "DAY"
        : elevationDegrees >= -6.0 ? "CIVIL_TWILIGHT"
        : elevationDegrees >= -12.0 ? "NAUTICAL_TWILIGHT"
        : elevationDegrees >= -18.0 ? "ASTRONOMICAL_TWILIGHT"
        : "NIGHT";

    /// <summary>
    /// Aims the directional light so it emits FROM the Sun toward the scene: a
    /// DirectionalLight3D shines along its forward/−Z axis, so to light the
    /// sun-facing side we need forward == −sunDir, i.e. −basis.z == sunDir.
    /// Energy and other light settings are left untouched.
    /// </summary>
    private void OrientLight(Vector3 sunDir)
    {
        if (_light == null || !IsInstanceValid(_light))
            _light = GetTree().Root.FindChild("DirectionalLight3D", true, false) as DirectionalLight3D;
        if (_light == null) return;

        // Look toward −sunDir (the travel direction of the light). Pick an "up" that
        // isn't parallel to the look direction to keep the basis well-conditioned.
        Vector3 up = Mathf.Abs(sunDir.Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up;
        var origin = _light.GlobalPosition;
        _light.LookAtFromPosition(origin, origin - sunDir, up);
    }

    /// <summary>
    /// Pushes <c>sun_dir</c> into the Earth material (priority) and any other body
    /// material that exposes the same uniform, so every shader-driven terminator and
    /// night-side city-light field lines up with the real Sun.
    /// </summary>
    private void FeedSunDir(Vector3 sunDir)
    {
        if (_earthMat == null)
            _earthMat = FindBodyMaterial("Earth_mesh");
        _earthMat?.SetShaderParameter("sun_dir", sunDir);

        RefreshPlanetMaterials();
        foreach (var material in _planetMaterials)
            material.SetShaderParameter("sun_dir", sunDir);
    }

    private void FeedSolarVisibility(float visibility)
    {
        if (_earthMat == null)
            _earthMat = FindBodyMaterial("Earth_mesh");
        _earthMat?.SetShaderParameter("solar_visibility", visibility);

        // Generic body shaders use the same solar-disc visibility for Mars,
        // Venus and other scaled-space bodies. Without this propagation their
        // surface remains in full direct-light mode while the sky/exposure is
        // correctly in eclipse or planetary night.
        RefreshPlanetMaterials();
        foreach (var material in _planetMaterials)
            material.SetShaderParameter("solar_visibility", visibility);

        // The low-altitude tangent patch is a separate unshaded material.  It uses the
        // same parameter so a synthetic or real eclipse cannot leave the local terrain
        // at full direct-light strength while the atmosphere is in shadow.
        if (_groundMesh == null || !IsInstanceValid(_groundMesh))
        {
            _groundMesh = GetTree().Root.FindChild("EarthGround", true, false) as MeshInstance3D;
            _groundMaterial = _groundMesh == null
                ? null
                : (_groundMesh.GetSurfaceOverrideMaterial(0) ?? _groundMesh.GetActiveMaterial(0))
                    as ShaderMaterial;
        }
        _groundMaterial?.SetShaderParameter("solar_visibility", visibility);
    }

    private void RefreshPlanetMaterials()
    {
        if (_planetsNode == null || !IsInstanceValid(_planetsNode))
        {
            _planetsNode = GetTree().Root.FindChild("Planets", true, false) as Node3D;
            _planetMaterials.Clear();
            _planetNodeCount = _planetsNode?.GetChildCount() ?? -1;
            if (_planetsNode == null) return;

            foreach (var child in _planetsNode.GetChildren())
            {
                if (child is not MeshInstance3D mesh || mesh.Name == "Earth_mesh") continue;
                // Body shaders (planet_body / earth_surface) all declare `sun_dir`.
                // Keep only valid ShaderMaterial references so this traversal happens
                // once per scene/universe instead of once per render frame.
                if ((mesh.GetSurfaceOverrideMaterial(0) ?? mesh.GetActiveMaterial(0))
                    is ShaderMaterial material)
                    _planetMaterials.Add(material);
            }
        }
        else if (_planetNodeCount != _planetsNode.GetChildCount())
        {
            // Lazy body presentation can add Mars/Venus after the first frame.
            // Rebuild this small cache only when the Planets child set changes;
            // do not walk the scene tree on every lighting sample.
            _planetMaterials.Clear();
            _planetNodeCount = _planetsNode.GetChildCount();
            foreach (var child in _planetsNode.GetChildren())
            {
                if (child is not MeshInstance3D mesh || mesh.Name == "Earth_mesh") continue;
                if ((mesh.GetSurfaceOverrideMaterial(0) ?? mesh.GetActiveMaterial(0))
                    is ShaderMaterial material)
                    _planetMaterials.Add(material);
            }
        }
    }

    private ShaderMaterial? FindBodyMaterial(string meshName)
    {
        var mesh = GetTree().Root.FindChild(meshName, true, false) as MeshInstance3D;
        if (mesh == null) return null;
        return (mesh.GetSurfaceOverrideMaterial(0) ?? mesh.GetActiveMaterial(0)) as ShaderMaterial;
    }
}
