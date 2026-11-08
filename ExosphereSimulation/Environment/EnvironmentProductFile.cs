namespace Exosphere.Simulation.Environment;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

/// <summary>
/// Reader of the environment product container written by <c>tools/envdata/envfile.py</c>:
/// magic <c>EXOENV01</c>, a little-endian u32 header length, a canonical JSON header and a
/// row-major float64 payload. The reader refuses a wrong magic or format version, a payload
/// whose SHA-256 differs from the header, or a payload whose size disagrees with its shape.
/// </summary>
public sealed class EnvironmentProductFile
{
    public const int FormatVersion = 1;
    private static readonly byte[] Magic = "EXOENV01"u8.ToArray();

    public string Product { get; }
    public IReadOnlyList<string> Columns { get; }
    public JsonElement Header { get; }
    private readonly double[] _values;

    public int Rows { get; }

    private EnvironmentProductFile(string product, IReadOnlyList<string> columns, JsonElement header,
        double[] values, int rows)
    {
        Product = product;
        Columns = columns;
        Header = header;
        _values = values;
        Rows = rows;
    }

    public double this[int row, int column] => _values[row * Columns.Count + column];

    public int ColumnIndex(string name)
    {
        for (int i = 0; i < Columns.Count; i++)
            if (Columns[i] == name) return i;
        throw new InvalidDataException($"Environment product '{Product}' has no column '{name}'.");
    }

    public static EnvironmentProductFile Load(string path) => Parse(File.ReadAllBytes(path), path);

    public static EnvironmentProductFile Parse(ReadOnlySpan<byte> blob, string label = "product")
    {
        if (blob.Length < 12 || !blob[..8].SequenceEqual(Magic))
            throw new InvalidDataException($"{label}: not an Exosphere environment product.");
        int headerLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(blob[8..12]));
        if (12 + headerLength > blob.Length)
            throw new InvalidDataException($"{label}: truncated header.");
        using var document = JsonDocument.Parse(blob.Slice(12, headerLength).ToArray());
        var header = document.RootElement.Clone();
        if (header.GetProperty("format_version").GetInt32() != FormatVersion)
            throw new InvalidDataException($"{label}: unsupported format version.");
        var payload = blob[(12 + headerLength)..];
        string digest = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        if (digest != header.GetProperty("payload_sha256").GetString())
            throw new InvalidDataException($"{label}: payload SHA-256 does not match its header.");
        var columns = header.GetProperty("columns").EnumerateArray().Select(c => c.GetString()!).ToArray();
        int rows = header.GetProperty("rows").GetInt32();
        if (payload.Length != 8L * columns.Length * rows)
            throw new InvalidDataException($"{label}: payload size does not match rows x columns.");
        var values = new double[columns.Length * rows];
        for (int i = 0; i < values.Length; i++)
            values[i] = BinaryPrimitives.ReadDoubleLittleEndian(payload.Slice(8 * i, 8));
        return new EnvironmentProductFile(header.GetProperty("product").GetString()!, columns, header, values, rows);
    }
}
