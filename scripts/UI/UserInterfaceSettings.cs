namespace Exosphere.Game;

using Godot;

public enum InterfaceLanguage { English, Spanish }

/// <summary>
/// Exterior Minimal uses the broadcast band with live engine boards and navball.
/// Full adds diagnostic panels. Clean retains the expanded attitude cluster.
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
        ["flight14_home"] = ("From Starbase to orbit. Explore the mission, or take command of your own flight.", "De Starbase a la órbita. Explora la misión o toma el mando de tu propio vuelo."),
        ["flight14_details"] = ("EXPLORE FLIGHT 14  →", "EXPLORAR VUELO 14  →"),
        ["flight14_label"] = ("FEATURED MISSION", "MISIÓN DESTACADA"),
        ["flight14_card_title"] = ("THE ORBITAL MISSION", "LA MISIÓN ORBITAL"),
        ["in_development"] = ("IN DEVELOPMENT", "EN DESARROLLO"),
        ["phase_ascent"] = ("01   Ascent & orbital insertion", "01   Ascenso e inserción orbital"),
        ["phase_deploy"] = ("02   Satellite deployment", "02   Despliegue de satélites"),
        ["phase_return"] = ("03   Deorbit, entry & splashdown", "03   Desorbitado, reentrada y amerizaje"),
        ["flight14_pending"] = ("The full Flight 14 mission is in development. Explore existing Starship configurations in Free Flight while the mission is being built.", "La misión completa del vuelo 14 está en desarrollo. Puedes explorar las configuraciones actuales de Starship en vuelo libre."),
        ["reference_only"] = ("MISSION BRIEFING / REFERENCE", "INFORMACIÓN DE MISIÓN / REFERENCIA"),
        ["flight14_scope"] = ("The target is a complete mission from Starbase: ascent, orbit, satellite deployment and return. Free Flight currently offers the dated Flight 7 and Flight 12 presets.", "El objetivo es una misión completa desde Starbase: ascenso, órbita, despliegue y retorno. El vuelo libre ofrece actualmente los presets fechados de los vuelos 7 y 12."),
        ["flight14_launch_pending"] = ("LAUNCH FLIGHT 14 / NOT AVAILABLE", "INICIAR VUELO 14 / NO DISPONIBLE"),
        ["choose_existing_vehicle"] = ("FREE FLIGHT / CHOOSE A VEHICLE", "VUELO LIBRE / ELEGIR VEHÍCULO"),
        ["free_flight"] = ("FREE FLIGHT", "VUELO LIBRE"),
        ["vehicle_selection"] = ("SELECT YOUR VEHICLE", "SELECCIONA TU VEHÍCULO"),
        ["free_flight_description"] = ("Choose a launch vehicle, review its dated configuration, then launch from its home pad.", "Elige un cohete, revisa su configuración fechada y despega desde su base."),
        ["configuration_date"] = ("CONFIGURATION", "CONFIGURACIÓN"),
        ["manual_flight_note"] = ("Manual flight, unrestricted objectives. Flight assistance remains available in the flight controls. Historical mission events are enabled only in Campaign.", "Vuelo manual con objetivos libres. Las asistencias están disponibles en los controles de vuelo. Los eventos de misiones históricas se activan en la campaña."),
        ["launch_vehicle"] = ("LAUNCH VEHICLE  →", "LANZAR VEHÍCULO  →"),
        ["change_vehicle"] = ("CHANGE VEHICLE", "CAMBIAR VEHÍCULO"),
        ["mission_planned"] = ("This mission is planned. Its playable definition and vehicle are not available yet.", "Esta misión está planificada. Su definición jugable y vehículo aún no están disponibles."),
        ["apollo11_partial"] = ("Partial mission: spacecraft extraction, docking and lunar orbit are available. Powered lunar descent and the return sequence are still in development.", "Misión parcial: extracción del módulo lunar, acoplamiento y órbita lunar disponibles. El descenso propulsado y la secuencia de retorno siguen en desarrollo."),
        ["mission_objectives"] = ("OBJECTIVES", "OBJETIVOS"),
        ["mission_limits"] = ("FLIGHT LIMITS", "LÍMITES DE VUELO"),
        ["mission_prerequisites"] = ("Complete first", "Completa primero"),
        ["mission_date"] = ("HISTORICAL DATE", "FECHA HISTÓRICA"),
        ["mission_status"] = ("STATUS", "ESTADO"),
        ["start_mission"] = ("START MISSION  →", "INICIAR MISIÓN  →"),
        ["back_to_missions"] = ("BACK TO MISSIONS", "VOLVER A MISIONES"),
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
