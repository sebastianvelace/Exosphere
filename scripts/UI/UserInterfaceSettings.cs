namespace Exosphere.Game;

using Godot;

public enum InterfaceLanguage { English, Spanish }

/// <summary>
/// How much of the in-flight HUD is drawn. All densities keep the attitude cluster
/// (engines — navball — data strip). <see cref="HudDensity.Minimal"/> is the default
/// (cluster + compact bottom band). <see cref="HudDensity.Full"/> adds top panels.
/// <see cref="HudDensity.Clean"/> drops chrome around the cluster only.
/// </summary>
public enum HudDensity { Full, Minimal, Clean }

public static class UserInterfaceSettings
{
    private const string Path = "user://interface.cfg";
    public static InterfaceLanguage Language { get; private set; } = InterfaceLanguage.English;
    public static bool ReducedMotion { get; private set; }
    public static float UiScale { get; private set; } = 1.0f;
    public static HudDensity HudDensity { get; private set; } = HudDensity.Minimal;

    public static void Load()
    {
        var config = new ConfigFile();
        if (config.Load(Path) != Error.Ok) return;
        Language = (InterfaceLanguage)(int)config.GetValue(
            "interface", "language", (int)InterfaceLanguage.English);
        ReducedMotion = (bool)config.GetValue("interface", "reduced_motion", false);
        UiScale = System.Math.Clamp(
            (float)config.GetValue("interface", "ui_scale", 1.0f), 1.0f, 1.5f);
        HudDensity = (HudDensity)System.Math.Clamp(
            (int)config.GetValue("interface", "hud_density", (int)HudDensity.Minimal),
            (int)HudDensity.Full,
            (int)HudDensity.Clean);
    }

    public static void SetLanguage(InterfaceLanguage language)
    {
        Language = language;
        Save();
    }

    public static void SetReducedMotion(bool value)
    {
        ReducedMotion = value;
        Save();
    }

    public static void SetUiScale(float value)
    {
        UiScale = System.Math.Clamp(value, 1.0f, 1.5f);
        Save();
    }

    public static void SetHudDensity(HudDensity value)
    {
        HudDensity = value;
        Save();
    }

    /// <summary>Advances Minimal → Full → Clean → Minimal and persists the result.</summary>
    public static HudDensity CycleHudDensity()
    {
        SetHudDensity(HudDensity switch
        {
            HudDensity.Minimal => HudDensity.Full,
            HudDensity.Full => HudDensity.Clean,
            _ => HudDensity.Minimal,
        });
        return HudDensity;
    }

    private static void Save()
    {
        var config = new ConfigFile();
        config.SetValue("interface", "language", (int)Language);
        config.SetValue("interface", "reduced_motion", ReducedMotion);
        config.SetValue("interface", "ui_scale", UiScale);
        config.SetValue("interface", "hud_density", (int)HudDensity);
        config.Save(Path);
    }
}

public static class UiText
{
    private static readonly Dictionary<string, (string en, string es)> Strings = new()
    {
        ["dossier"] = ("ORBITAL\nFLIGHT DOSSIER", "DOSSIER DE\nVUELO ORBITAL"),
        ["subtitle"] = ("Historical missions and unrestricted flight, governed by one physics model.",
            "Misiones históricas y vuelo libre, gobernados por un único modelo físico."),
        ["continue"] = ("CONTINUE", "CONTINUAR"),
        ["campaign"] = ("HISTORICAL CAMPAIGN", "CAMPAÑA HISTÓRICA"),
        ["sandbox"] = ("SANDBOX FLIGHT", "VUELO SANDBOX"),
        ["vab"] = ("VEHICLE ASSEMBLY", "ENSAMBLAJE DE VEHÍCULOS"),
        ["scenarios"] = ("SCENARIOS", "ESCENARIOS"),
        ["reentry"] = ("REENTRY TEST", "PRUEBA DE REENTRADA"),
        ["saves"] = ("SAVED FLIGHTS", "PARTIDAS GUARDADAS"),
        ["settings"] = ("SETTINGS", "AJUSTES"),
        ["quit"] = ("QUIT", "SALIR"),
        ["mission"] = ("SELECTED DOSSIER", "DOSSIER SELECCIONADO"),
        ["dossier_vehicle_title"] = ("FALCON 9 BLOCK 5\nPAYLOAD DEPLOYMENT",
            "FALCON 9 BLOCK 5\nDESPLIEGUE DE CARGA"),
        ["dossier_vehicle_value"] = ("F9 B5 / STANDARD FAIRING", "F9 B5 / COFIA ESTÁNDAR"),
        ["dossier_site_value"] = ("KENNEDY LC-39A", "KENNEDY LC-39A"),
        ["dossier_objective_value"] = ("200 KM PARKING ORBIT", "ÓRBITA DE ESTACIONAMIENTO 200 KM"),
        ["dossier_physics_value"] = ("FULL / ASSISTS AVAILABLE", "COMPLETA / ASISTENCIAS DISPONIBLES"),
        ["dossier_note"] = (
            "Published performance values remain distinct from simulator estimates. " +
            "Open Vehicle Assembly to inspect the dated preset and its sources.",
            "Los valores publicados se mantienen separados de las estimaciones del simulador. " +
            "Abre el ensamblaje para revisar el preset fechado y sus fuentes."),
        ["dossier_profile"] = ("PROFILE  F9-B5-2025-05", "PERFIL  F9-B5-2025-05"),
        ["flight_operations"] = ("FLIGHT OPERATIONS", "OPERACIONES DE VUELO"),
        ["footer_controls"] = ("ENTER  SELECT     ESC  BACK", "ENTER  SELECCIONAR     ESC  ATRÁS"),
        ["physics_ready"] = ("PHYSICS CORE READY", "NÚCLEO FÍSICO LISTO"),
        ["vehicle"] = ("VEHICLE", "VEHÍCULO"),
        ["site"] = ("LAUNCH SITE", "SITIO DE LANZAMIENTO"),
        ["objective"] = ("OBJECTIVE", "OBJETIVO"),
        ["physics"] = ("PHYSICS", "FÍSICA"),
        ["settings_title"] = ("INTERFACE SETTINGS", "AJUSTES DE INTERFAZ"),
        ["language"] = ("LANGUAGE", "IDIOMA"),
        ["motion"] = ("REDUCED MOTION", "MOVIMIENTO REDUCIDO"),
        ["scale"] = ("UI SCALE", "ESCALA DE INTERFAZ"),
        ["close"] = ("CLOSE", "CERRAR"),
        ["no_saves"] = ("No saved flights yet.", "Aún no hay partidas guardadas."),
        ["campaign_preview"] = ("Campaign sequence", "Secuencia de campaña"),
        ["scenario_title"] = ("SCENARIO LIBRARY", "BIBLIOTECA DE ESCENARIOS"),
    };

    public static string Get(string key)
    {
        var value = Strings[key];
        return UserInterfaceSettings.Language == InterfaceLanguage.Spanish
            ? value.es : value.en;
    }
}
