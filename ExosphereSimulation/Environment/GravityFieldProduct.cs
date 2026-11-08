namespace Exosphere.Simulation.Environment;

/// <summary>
/// Loads a spherical-harmonic field product (<c>data/environment/gravity/*.bin</c>, environment
/// plan E3.3) at a chosen degree. The product's GM and reference radius travel with it.
/// </summary>
public static class GravityFieldProduct
{
    public static SphericalHarmonicGravity Load(string path, int degree)
    {
        var product = EnvironmentProductFile.Load(path);
        int productDegree = product.Header.GetProperty("degree").GetInt32();
        if (degree < 2 || degree > productDegree)
            throw new ArgumentOutOfRangeException(nameof(degree), $"{product.Product} holds degree 2..{productDegree}.");
        int count = SphericalHarmonicGravity.Index(degree, degree) + 1;
        var c = new double[count];
        var s = new double[count];
        int nColumn = product.ColumnIndex("n"), mColumn = product.ColumnIndex("m");
        int cColumn = product.ColumnIndex("c"), sColumn = product.ColumnIndex("s");
        for (int row = 0; row < product.Rows; row++)
        {
            int n = (int)product[row, nColumn], m = (int)product[row, mColumn];
            if (n > degree) break;
            int index = SphericalHarmonicGravity.Index(n, m);
            if (index != row) throw new InvalidDataException($"{product.Product}: rows are not in (n, m) order at {row}.");
            c[index] = product[row, cColumn];
            s[index] = product[row, sColumn];
        }
        return new SphericalHarmonicGravity($"{product.Product}-d{degree}",
            product.Header.GetProperty("gm_m3s2").GetDouble(),
            product.Header.GetProperty("reference_radius_m").GetDouble(), degree, c, s);
    }
}
