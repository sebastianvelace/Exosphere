namespace Exosphere.Game;

using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;

public static class SaveSystem
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented     = true,
        AllowTrailingCommas = true
    };

    private static string SaveDirectory =>
        System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            ".local", "share", "Exosphere", "saves");

    // Guardar estado actual en un slot
    public static void SaveGame(string slotName = "quicksave")
    {
        var bridge = SimulationBridge.Instance;
        if (bridge?.Universe == null) return;

        System.IO.Directory.CreateDirectory(SaveDirectory);
        var state = SerializeUniverse(bridge.Universe);
        var path  = System.IO.Path.Combine(SaveDirectory, $"{slotName}.json");
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(state, _opts));
        GD.Print($"[SaveSystem] Saved to {path}");
    }

    // Cargar estado desde un slot
    public static bool LoadGame(string slotName = "quicksave")
    {
        var path = System.IO.Path.Combine(SaveDirectory, $"{slotName}.json");
        if (!System.IO.File.Exists(path)) return false;

        var bridge = SimulationBridge.Instance;
        if (bridge == null) return false;

        var text  = System.IO.File.ReadAllText(path);
        var state = JsonSerializer.Deserialize<UniverseSaveState>(text, _opts);
        if (state == null) return false;

        RestoreUniverse(bridge.Universe, state);
        GD.Print($"[SaveSystem] Loaded from {path}");
        return true;
    }

    public static string[] ListSaveSlots()
    {
        if (!System.IO.Directory.Exists(SaveDirectory)) return [];
        return System.IO.Directory.GetFiles(SaveDirectory, "*.json")
            .Select(p => System.IO.Path.GetFileNameWithoutExtension(p))
            .ToArray();
    }

    // ── Serialización ──────────────────────────────────────────────────────

    private static UniverseSaveState SerializeUniverse(Universe u)
    {
        var state = new UniverseSaveState
        {
            CurrentTime    = u.CurrentTime,
            ActiveVesselId = u.ActiveVessel?.Id
        };

        foreach (var vessel in u.Vessels)
        {
            state.Vessels.Add(new VesselSaveState
            {
                Id              = vessel.Id,
                Name            = vessel.Name,
                PositionX       = vessel.Position.X,
                PositionY       = vessel.Position.Y,
                PositionZ       = vessel.Position.Z,
                VelocityX       = vessel.Velocity.X,
                VelocityY       = vessel.Velocity.Y,
                VelocityZ       = vessel.Velocity.Z,
                OrientationW    = vessel.Orientation.W,
                OrientationX    = vessel.Orientation.X,
                OrientationY    = vessel.Orientation.Y,
                OrientationZ    = vessel.Orientation.Z,
                IsOnRails       = vessel.IsOnRails,
                ReferenceBodyId = vessel.ReferenceBodyId ?? ""
            });
        }

        return state;
    }

    private static void RestoreUniverse(Universe u, UniverseSaveState state)
    {
        u.Vessels
            .ToList()
            .ForEach(v => u.RemoveVessel(v));

        // Nota: cuerpos celestes no se restauran del save — siempre se recalculan desde data/
        // Solo restauramos vessels y tiempo.
        // CurrentTime tiene private set en Universe; se expone en una iteración posterior.
        // u.CurrentTime = state.CurrentTime;

        foreach (var vs in state.Vessels)
        {
            var vessel = new Vessel
            {
                Name            = vs.Name,
                Position        = new Vector3d(vs.PositionX, vs.PositionY, vs.PositionZ),
                Velocity        = new Vector3d(vs.VelocityX, vs.VelocityY, vs.VelocityZ),
                Orientation     = new Quaterniond(vs.OrientationW, vs.OrientationX, vs.OrientationY, vs.OrientationZ),
                IsOnRails       = vs.IsOnRails,
                ReferenceBodyId = vs.ReferenceBodyId
            };
            u.AddVessel(vessel);

            if (vs.Id == state.ActiveVesselId)
                u.ActiveVessel = vessel;
        }
    }

    // ── DTOs ───────────────────────────────────────────────────────────────

    private class UniverseSaveState
    {
        public double CurrentTime    { get; set; }
        public string? ActiveVesselId { get; set; }
        public List<VesselSaveState> Vessels { get; set; } = new();
    }

    private class VesselSaveState
    {
        public string Id   { get; set; } = "";
        public string Name { get; set; } = "";

        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public double PositionZ { get; set; }

        public double VelocityX { get; set; }
        public double VelocityY { get; set; }
        public double VelocityZ { get; set; }

        public double OrientationW { get; set; }
        public double OrientationX { get; set; }
        public double OrientationY { get; set; }
        public double OrientationZ { get; set; }

        public bool   IsOnRails       { get; set; }
        public string ReferenceBodyId { get; set; } = "";
    }
}
