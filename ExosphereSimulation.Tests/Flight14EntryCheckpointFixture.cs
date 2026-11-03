namespace ExosphereSimulation.Tests;

using System.Security.Cryptography;
using System.Text.Json;
using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;

/// <summary>
/// One pad-to-entry Flight 14 flight per test process, shared by every downstream segment
/// (descent, flip, terminal burn). The checkpoint is cached on disk under a SHA-256 key of
/// the simulation assembly and the whole data directory, so any change to physics code or
/// data regenerates it; nothing stale can be reused.
///
/// While building it, the original run also continues for <see cref="ReferenceSeconds"/>
/// of descent control. <see cref="Flight14EntryCheckpointTests"/> requires a run resumed
/// from the checkpoint to reproduce that continuation exactly: if save/restore ever drops
/// state the segment tests depend on, that gate fails before any segment can be trusted.
/// </summary>
internal static class Flight14EntryCheckpointFixture
{
    public const double FrameSeconds = 1.0 / 60.0;
    public const double ReferenceSeconds = 60.0;

    public sealed record ShipState(double Time, Vector3d Position, Vector3d Velocity, Quaterniond Orientation,
        Vector3d AngularVelocity, double Propellant, double[] BodyFlapDeflections);

    public sealed record Entry(string Key, Flight14EntryCheckpoint Checkpoint, ShipState Reference);

    private sealed record CachedEntry(string Key, string Checkpoint, ShipStateDto Reference);

    /// <summary>Orientation is stored as (w, x, y, z), the <see cref="Quaterniond"/> constructor order.</summary>
    private sealed record ShipStateDto(double Time, double[] Position, double[] Velocity, double[] Orientation,
        double[] AngularVelocity, double Propellant, double[] BodyFlapDeflections);

    private static readonly Lazy<Entry> Shared = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    public static Entry Value => Shared.Value;

    public static string DataDirectory() => Path.Combine(FindRepoRoot().FullName, "data");

    /// <summary>A fresh, independent run at entry interface.</summary>
    public static Flight14LaunchDiagnostic Resume() =>
        Flight14LaunchDiagnostic.ResumeAtEntry(DataDirectory(), Value.Checkpoint);

    public static ShipState Sample(Flight14LaunchDiagnostic run) => new(
        run.Universe.CurrentTime, run.Ship.Position, run.Ship.Velocity, run.Ship.Orientation,
        run.Ship.AngularVelocity, run.Ship.Parts.Parts.Sum(p => p.LiquidFuel + p.Oxidizer),
        run.Ship.BodyFlapDeflectionsRad.ToArray());

    public static void FlyReference(Flight14LaunchDiagnostic run)
    {
        Flight14LaunchDiagnostic.AttachDescent(run, DataDirectory());
        int frames = (int)System.Math.Round(ReferenceSeconds / FrameSeconds);
        for (int i = 0; i < frames; i++) run.AdvanceFrame(FrameSeconds);
    }

    private static Entry Build()
    {
        string key = Fingerprint();
        string cacheFile = Path.Combine(Path.GetTempPath(), "exosphere-flight14-checkpoints", key + ".json");
        if (File.Exists(cacheFile))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<CachedEntry>(File.ReadAllText(cacheFile))!;
                if (cached.Key == key)
                    return new Entry(key, Flight14EntryCheckpoint.FromJson(cached.Checkpoint), FromDto(cached.Reference));
            }
            catch (JsonException)
            {
                // A truncated or foreign file is rebuilt below.
            }
        }

        var run = Flight14LaunchDiagnostic.CreateWithReturn(DataDirectory());
        var returning = run.ReturnController!;
        for (int i = 0; i < 11_020 * 60 && returning.Phase is not
            (Flight14ReturnPhase.EntryReached or Flight14ReturnPhase.Blocked); i++)
            run.AdvanceFrame(FrameSeconds);
        if (returning.Phase != Flight14ReturnPhase.EntryReached)
            throw new InvalidOperationException($"Flight 14 did not reach entry: {returning.BlockReason}");
        var checkpoint = run.CaptureEntryCheckpoint();
        FlyReference(run);
        var entry = new Entry(key, checkpoint, Sample(run));

        Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
        string temporary = cacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(
            new CachedEntry(key, checkpoint.ToJson(), ToDto(entry.Reference)), Flight14EntryCheckpoint.JsonOptions));
        File.Move(temporary, cacheFile, overwrite: true);
        return entry;
    }

    /// <summary>SHA-256 over the simulation assembly and every data file (path and bytes).</summary>
    private static string Fingerprint()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(File.ReadAllBytes(typeof(Universe).Assembly.Location));
        string data = DataDirectory();
        foreach (string file in Directory.GetFiles(data, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".import", StringComparison.Ordinal) && !f.EndsWith(".uid", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal))
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(data, file)));
            hash.AppendData(File.ReadAllBytes(file));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static ShipStateDto ToDto(ShipState s) => new(s.Time, [s.Position.X, s.Position.Y, s.Position.Z],
        [s.Velocity.X, s.Velocity.Y, s.Velocity.Z], [s.Orientation.W, s.Orientation.X, s.Orientation.Y, s.Orientation.Z],
        [s.AngularVelocity.X, s.AngularVelocity.Y, s.AngularVelocity.Z], s.Propellant, s.BodyFlapDeflections);

    private static ShipState FromDto(ShipStateDto d) => new(d.Time,
        new Vector3d(d.Position[0], d.Position[1], d.Position[2]),
        new Vector3d(d.Velocity[0], d.Velocity[1], d.Velocity[2]),
        new Quaterniond(d.Orientation[0], d.Orientation[1], d.Orientation[2], d.Orientation[3]),
        new Vector3d(d.AngularVelocity[0], d.AngularVelocity[1], d.AngularVelocity[2]),
        d.Propellant, d.BodyFlapDeflections);

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
