namespace Exosphere.Game;

using Godot;

/// <summary>
/// Non-indexed triangle accumulator for one material. Geometry is authored in metres in the
/// part's local frame (y up, origin at the part centre) and scaled to render units by the
/// owning MeshInstance3D, so builders can use real dimensions directly.
/// Winding is derived from the supplied normals, so every primitive renders front-facing
/// without hand-ordering vertices (Godot treats clockwise triangles as front faces).
/// </summary>
internal sealed class HistGeo
{
    public readonly List<Vector3> Vertices = new();
    public readonly List<Vector3> Normals = new();
    public readonly List<Vector2> Uvs = new();

    public bool IsEmpty => Vertices.Count == 0;

    public void Tri(Vector3 a, Vector3 b, Vector3 c,
        Vector3 na, Vector3 nb, Vector3 nc,
        Vector2 ua, Vector2 ub, Vector2 uc)
    {
        Vector3 geometric = (b - a).Cross(c - a);
        if (geometric.LengthSquared() < 1e-14f) return;
        if (geometric.Dot(na + nb + nc) > 0f)
        {
            (b, c) = (c, b);
            (nb, nc) = (nc, nb);
            (ub, uc) = (uc, ub);
        }
        Vertices.Add(a); Vertices.Add(b); Vertices.Add(c);
        Normals.Add(na); Normals.Add(nb); Normals.Add(nc);
        Uvs.Add(ua); Uvs.Add(ub); Uvs.Add(uc);
    }

    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
        Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd,
        Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
    {
        Tri(a, b, c, na, nb, nc, ua, ub, uc);
        Tri(a, c, d, na, nc, nd, ua, uc, ud);
    }

    public void FlatQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
        => Quad(a, b, c, d, n, n, n, n,
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));

    /// <summary>
    /// Surface of revolution around the local Y axis through <paramref name="center"/>.
    /// The profile is (radius, y) in metres; the outward side is to the right of travel,
    /// so a bottom-to-top profile faces outward and a top-to-bottom one faces inward.
    /// Repeated profile points create a hard crease. U spans the swept angle as a fraction
    /// of a full turn; V is cumulative profile length in metres (materials set the scale).
    /// </summary>
    public void Lathe(IReadOnlyList<Vector2> profile, int segments,
        float a0 = 0f, float a1 = Mathf.Tau, Vector3 center = default,
        bool flatAround = false, float smoothDeg = 42f)
    {
        int n = profile.Count;
        if (n < 2 || segments < 1) return;
        var segNormal = new Vector2[n - 1];
        var valid = new bool[n - 1];
        var vAt = new float[n];
        for (int i = 0; i < n - 1; i++)
        {
            Vector2 d = profile[i + 1] - profile[i];
            float len = d.Length();
            vAt[i + 1] = vAt[i] + len;
            valid[i] = len > 1e-6f;
            segNormal[i] = valid[i] ? new Vector2(d.Y, -d.X) / len : Vector2.Zero;
        }
        float cosSmooth = Mathf.Cos(Mathf.DegToRad(smoothDeg));
        Vector2 NormalAt(int seg, bool end)
        {
            int neighbour = end ? seg + 1 : seg - 1;
            if (neighbour < 0 || neighbour >= n - 1 || !valid[neighbour])
                return segNormal[seg];
            if (segNormal[seg].Dot(segNormal[neighbour]) < cosSmooth)
                return segNormal[seg];
            return (segNormal[seg] + segNormal[neighbour]).Normalized();
        }

        for (int i = 0; i < n - 1; i++)
        {
            if (!valid[i]) continue;
            Vector2 p0 = profile[i], p1 = profile[i + 1];
            Vector2 n0 = NormalAt(i, false), n1 = NormalAt(i, true);
            for (int s = 0; s < segments; s++)
            {
                float t0 = a0 + (a1 - a0) * s / segments;
                float t1 = a0 + (a1 - a0) * (s + 1) / segments;
                float c0 = Mathf.Cos(t0), s0 = Mathf.Sin(t0);
                float c1 = Mathf.Cos(t1), s1 = Mathf.Sin(t1);
                float cm = Mathf.Cos((t0 + t1) * 0.5f), sm = Mathf.Sin((t0 + t1) * 0.5f);
                Vector3 P(Vector2 p, float c, float sn) => center + new Vector3(p.X * c, p.Y, p.X * sn);
                Vector3 N(Vector2 q, float c, float sn) =>
                    flatAround ? new Vector3(q.X * cm, q.Y, q.X * sm).Normalized()
                               : new Vector3(q.X * c, q.Y, q.X * sn).Normalized();
                float u0 = (t0 - a0) / Mathf.Tau, u1 = (t1 - a0) / Mathf.Tau;
                Quad(P(p0, c0, s0), P(p0, c1, s1), P(p1, c1, s1), P(p1, c0, s0),
                    N(n0, c0, s0), N(n0, c1, s1), N(n1, c1, s1), N(n1, c0, s0),
                    new Vector2(u0, vAt[i]), new Vector2(u1, vAt[i]),
                    new Vector2(u1, vAt[i + 1]), new Vector2(u0, vAt[i + 1]));
            }
        }
    }

    /// <summary>Open (or capped) frustum between two arbitrary points.</summary>
    public void Frustum(Vector3 a, Vector3 b, float ra, float rb, int segments = 10,
        bool capA = false, bool capB = false)
    {
        Vector3 axis = b - a;
        float length = axis.Length();
        if (length < 1e-6f) return;
        Vector3 up = axis / length;
        Vector3 helper = Mathf.Abs(up.Y) < 0.95f ? Vector3.Up : Vector3.Right;
        Vector3 x = up.Cross(helper).Normalized();
        Vector3 z = x.Cross(up).Normalized();
        float slope = (ra - rb) / length;
        for (int s = 0; s < segments; s++)
        {
            float t0 = Mathf.Tau * s / segments, t1 = Mathf.Tau * (s + 1) / segments;
            Vector3 d0 = x * Mathf.Cos(t0) + z * Mathf.Sin(t0);
            Vector3 d1 = x * Mathf.Cos(t1) + z * Mathf.Sin(t1);
            Vector3 n0 = (d0 + up * slope).Normalized();
            Vector3 n1 = (d1 + up * slope).Normalized();
            float u0 = (float)s / segments, u1 = (float)(s + 1) / segments;
            Quad(a + d0 * ra, a + d1 * ra, b + d1 * rb, b + d0 * rb,
                n0, n1, n1, n0,
                new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, length), new Vector2(u0, length));
            if (capA && ra > 0f)
                Tri(a, a + d0 * ra, a + d1 * ra, -up, -up, -up, Vector2.Zero, Vector2.Zero, Vector2.Zero);
            if (capB && rb > 0f)
                Tri(b, b + d1 * rb, b + d0 * rb, up, up, up, Vector2.Zero, Vector2.Zero, Vector2.Zero);
        }
    }

    public void Tube(Vector3 a, Vector3 b, float radius, int segments = 6)
        => Frustum(a, b, radius, radius, segments);

    /// <summary>Box with edges along the basis columns, centred on <paramref name="center"/>.</summary>
    public void Box(Vector3 center, Basis basis, Vector3 size)
    {
        Vector3 ax = basis.Column0.Normalized() * size.X * 0.5f;
        Vector3 ay = basis.Column1.Normalized() * size.Y * 0.5f;
        Vector3 az = basis.Column2.Normalized() * size.Z * 0.5f;
        void Face(Vector3 normalAxis, Vector3 u, Vector3 v)
        {
            Vector3 c = center + normalAxis;
            Vector3 n = normalAxis.Normalized();
            FlatQuad(c - u - v, c + u - v, c + u + v, c - u + v, n);
        }
        Face(ax, ay, az); Face(-ax, ay, az);
        Face(ay, ax, az); Face(-ay, ax, az);
        Face(az, ax, ay); Face(-az, ax, ay);
    }

    public void Box(Vector3 center, Vector3 size) => Box(center, Basis.Identity, size);

    /// <summary>Convex planar outline extruded symmetrically by <paramref name="thickness"/>.</summary>
    public void Slab(IReadOnlyList<Vector3> outline, Vector3 normal, float thickness)
    {
        int count = outline.Count;
        if (count < 3) return;
        Vector3 n = normal.Normalized();
        Vector3 half = n * thickness * 0.5f;
        Vector3 centroid = Vector3.Zero;
        foreach (var p in outline) centroid += p;
        centroid /= count;
        for (int i = 1; i < count - 1; i++)
        {
            Tri(outline[0] + half, outline[i] + half, outline[i + 1] + half, n, n, n,
                Vector2.Zero, Vector2.Right, Vector2.One);
            Tri(outline[0] - half, outline[i] - half, outline[i + 1] - half, -n, -n, -n,
                Vector2.Zero, Vector2.Right, Vector2.One);
        }
        for (int i = 0; i < count; i++)
        {
            Vector3 p0 = outline[i], p1 = outline[(i + 1) % count];
            Vector3 side = (p1 - p0).Cross(n).Normalized();
            if (side.Dot((p0 + p1) * 0.5f - centroid) < 0f) side = -side;
            FlatQuad(p0 - half, p1 - half, p1 + half, p0 + half, side);
        }
    }

    /// <summary>Torus around the Y axis.</summary>
    public void Torus(Vector3 center, float majorRadius, float minorRadius,
        int segments = 40, int sides = 8)
    {
        var profile = new List<Vector2>(sides + 1);
        for (int i = 0; i <= sides; i++)
        {
            float a = -Mathf.Pi * 0.5f + Mathf.Tau * i / sides;
            profile.Add(new Vector2(
                majorRadius + minorRadius * Mathf.Cos(a), minorRadius * Mathf.Sin(a)));
        }
        Lathe(profile, segments, center: center, smoothDeg: 80f);
    }

    /// <summary>Dish (paraboloid shell, both faces) opening along +axis.</summary>
    public void Dish(Vector3 center, Vector3 axis, float radius, float depth, int segments = 16)
    {
        Vector3 up = axis.Normalized();
        Vector3 helper = Mathf.Abs(up.Y) < 0.95f ? Vector3.Up : Vector3.Right;
        Vector3 x = up.Cross(helper).Normalized();
        Vector3 z = x.Cross(up).Normalized();
        const int rings = 4;
        for (int r = 0; r < rings; r++)
        {
            float f0 = (float)r / rings, f1 = (float)(r + 1) / rings;
            for (int s = 0; s < segments; s++)
            {
                float t0 = Mathf.Tau * s / segments, t1 = Mathf.Tau * (s + 1) / segments;
                Vector3 P(float f, float t) => center
                    + (x * Mathf.Cos(t) + z * Mathf.Sin(t)) * (radius * f)
                    + up * (depth * f * f);
                Vector3 Nr(float f, float t) => (up - (x * Mathf.Cos(t) + z * Mathf.Sin(t))
                    * (2f * depth * f / radius)).Normalized();
                Vector3 a = P(f0, t0), b = P(f0, t1), c = P(f1, t1), d = P(f1, t0);
                Quad(a, b, c, d, Nr(f0, t0), Nr(f0, t1), Nr(f1, t1), Nr(f1, t0),
                    Vector2.Zero, Vector2.Right, Vector2.One, Vector2.Down);
                Quad(a, b, c, d, -Nr(f0, t0), -Nr(f0, t1), -Nr(f1, t1), -Nr(f1, t0),
                    Vector2.Zero, Vector2.Right, Vector2.One, Vector2.Down);
            }
        }
    }

    /// <summary>
    /// Box lying on a body of revolution at azimuth <paramref name="theta"/>: X along the
    /// circumference, Y along the local generator (tilted by <paramref name="slope"/>, the
    /// surface's radial change per metre of height), Z radially outward.
    /// </summary>
    public void SurfaceBox(float theta, float radius, float y, float width, float height,
        float depth, float slope = 0f)
    {
        Vector3 radial = new(Mathf.Cos(theta), 0f, Mathf.Sin(theta));
        Vector3 tangent = new(-Mathf.Sin(theta), 0f, Mathf.Cos(theta));
        Vector3 along = (Vector3.Up + radial * slope).Normalized();
        Vector3 outward = tangent.Cross(along).Normalized();
        if (outward.Dot(radial) < 0f) outward = -outward;
        Vector3 center = radial * radius + Vector3.Up * y + outward * (depth * 0.5f);
        Box(center, new Basis(tangent, along, outward), new Vector3(width, height, depth));
    }

    public ArrayMesh? AppendTo(ArrayMesh? mesh, Material material)
    {
        if (IsEmpty) return mesh;
        mesh ??= new ArrayMesh();
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = Vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = Normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = Uvs.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
        return mesh;
    }
}

/// <summary>One geometry batch per material, merged into a single multi-surface mesh.</summary>
internal sealed class HistMeshKit
{
    private readonly Dictionary<Material, HistGeo> _batches = new();
    private readonly List<Material> _order = new();

    public HistGeo this[Material material]
    {
        get
        {
            if (!_batches.TryGetValue(material, out var geo))
            {
                geo = new HistGeo();
                _batches[material] = geo;
                _order.Add(material);
            }
            return geo;
        }
    }

    public ArrayMesh? Build()
    {
        ArrayMesh? mesh = null;
        foreach (var material in _order)
            mesh = _batches[material].AppendTo(mesh, material);
        return mesh;
    }
}

/// <summary>Shared, lazily generated PBR materials and procedural detail textures.</summary>
internal static class HistMaterials
{
    private static readonly Dictionary<string, StandardMaterial3D> Cache = new();
    private static readonly Dictionary<string, Texture2D> Textures = new();

    public static StandardMaterial3D Get(string key, Color albedo, float metallic,
        float roughness, string? normal = null, float normalScale = 1f,
        Vector2? uvScale = null, string? albedoTexture = null, bool doubleSided = false,
        float emission = 0f)
    {
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var material = new StandardMaterial3D
        {
            AlbedoColor = albedo,
            Metallic = metallic,
            MetallicSpecular = 0.5f,
            Roughness = roughness,
            CullMode = doubleSided
                ? BaseMaterial3D.CullModeEnum.Disabled
                : BaseMaterial3D.CullModeEnum.Back,
        };
        if (normal != null)
        {
            material.NormalEnabled = true;
            material.NormalTexture = Texture(normal);
            material.NormalScale = normalScale;
        }
        if (albedoTexture != null)
            material.AlbedoTexture = Texture(albedoTexture);
        if (uvScale is Vector2 scale)
            material.Uv1Scale = new Vector3(scale.X, scale.Y, 1f);
        if (emission > 0f)
        {
            // Bright metals read black against the flight scene's flat ambient term; a
            // faint self-tint keeps them legible without the glow of a light source.
            material.EmissionEnabled = true;
            material.Emission = albedo;
            material.EmissionEnergyMultiplier = emission;
        }
        Cache[key] = material;
        return material;
    }

    private static Texture2D Texture(string key)
    {
        if (Textures.TryGetValue(key, out var cached)) return cached;
        Texture2D texture = key switch
        {
            "ribs" => NormalFromHeight(64, 8, (u, _) =>
                0.5f + 0.5f * Mathf.Cos(Mathf.Tau * u), 3.0f),
            "ribs_sharp" => NormalFromHeight(64, 8, (u, _) =>
                Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.Pi * u)), 0.45f), 4.0f),
            "shingles" => NormalFromHeight(128, 128, (u, v) =>
            {
                // Four longitudinal beads per shingle plus the stepped overlap of the
                // next shingle row (Mercury/Gemini René 41 and beryllium shingles).
                float bead = 0.5f + 0.5f * Mathf.Cos(Mathf.Tau * 4f * u);
                float step = v - Mathf.Floor(v);
                return bead * 0.35f + step * 0.65f;
            }, 4.0f),
            "crinkle" => NoiseNormal(17, 0.09f, 7.0f),
            "panels" => PanelLines(256, 0.62f, 2),
            "panels_fine" => PanelLines(256, 0.80f, 1),
            "foil_albedo" => NoiseAlbedo(23, 0.05f, 0.72f),
            _ => throw new ArgumentException($"Unknown historical texture '{key}'"),
        };
        Textures[key] = texture;
        return texture;
    }

    private static ImageTexture NormalFromHeight(int width, int height,
        Func<float, float, float> heightAt, float strength)
    {
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgb8);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float h = Mathf.Clamp(heightAt((x + 0.5f) / width, (y + 0.5f) / height), 0f, 1f);
                image.SetPixel(x, y, new Color(h, h, h));
            }
        image.BumpMapToNormalMap(strength);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture NoiseNormal(int seed, float frequency, float strength)
    {
        var noise = new FastNoiseLite
        {
            Seed = seed,
            NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular,
            Frequency = frequency,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = 3,
        };
        var image = noise.GetSeamlessImage(256, 256);
        image.Convert(Image.Format.Rgb8);
        image.BumpMapToNormalMap(strength);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture NoiseAlbedo(int seed, float frequency, float floor)
    {
        var noise = new FastNoiseLite
        {
            Seed = seed,
            NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular,
            Frequency = frequency,
        };
        var source = noise.GetSeamlessImage(256, 256);
        var image = Image.CreateEmpty(256, 256, false, Image.Format.Rgb8);
        for (int y = 0; y < 256; y++)
            for (int x = 0; x < 256; x++)
            {
                float v = floor + (1f - floor) * source.GetPixel(x, y).R;
                image.SetPixel(x, y, new Color(v, v, v));
            }
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture PanelLines(int size, float lineValue, int lineWidth)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool line = x < lineWidth || y < lineWidth;
                float v = line ? lineValue : 1f;
                image.SetPixel(x, y, new Color(v, v, v));
            }
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }
}
