namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

public static partial class DistributedAerodynamics
{
    // Eight representable steps absorb only arithmetic at an identical station;
    // this is a floating-point comparison, not a physical geometry tolerance.
    private static bool SameStation(double a, double b)
    {
        double x = System.Math.Abs(a), y = System.Math.Abs(b);
        double ulp = System.Math.Max(double.BitIncrement(x) - x, double.BitIncrement(y) - y);
        return System.Math.Abs(a - b) <= 8.0 * ulp;
    }

    private static bool TryBuildOverlappingCoaxialEnvelope(PartGraph graph,
        IReadOnlyDictionary<Part, Vector3d> positions, List<AeroElement> elements)
    {
        var hull = new List<(Part Part, Vector3d Centre, double Bottom, double Top, double Nose)>();
        var bounds = new List<double>();
        foreach (var part in graph.Parts)
        {
            if (graph.IsEnclosedPayload(part)) continue;
            var d = part.Definition;
            if (!(d.LengthM > 0.0) || !(d.DiameterM > 0.0)
                || !positions.TryGetValue(part, out var centre) || centre.X != 0.0 || centre.Z != 0.0)
                return false;
            double bottom = centre.Y - d.LengthM * 0.5, top = centre.Y + d.LengthM * 0.5;
            double nose = System.Math.Clamp(d.NoseLengthM, 0.0, d.LengthM);
            hull.Add((part, centre, bottom, top, nose));
            bounds.Add(bottom); bounds.Add(top); bounds.Add(top - nose);
        }
        bool overlap = false;
        for (int i = 0; i < hull.Count; i++)
            for (int j = i + 1; j < hull.Count; j++)
            {
                var a = hull[i]; var b = hull[j];
                double overlapTop = System.Math.Min(a.Top, b.Top), overlapBottom = System.Math.Max(a.Bottom, b.Bottom);
                if (overlapTop <= overlapBottom || SameStation(overlapTop, overlapBottom)) continue;
                overlap = true;
                // The existing aggregate ogive supplies no partial-area profile. Keep
                // that approximation unchanged until an exposed nose can be resolved.
                if ((a.Nose > 0.0 && b.Top > a.Top - a.Nose)
                    || (b.Nose > 0.0 && a.Top > b.Top - b.Nose)) return false;
            }
        if (!overlap) return false;

        var stations = new List<double>();
        foreach (double station in bounds.OrderBy(y => y))
            if (stations.Count == 0 || !SameStation(station, stations[^1])) stations.Add(station);
        var cuts = stations.ToArray();
        double previousArea = 0.0;
        for (int i = 0; i + 1 < cuts.Length; i++)
        {
            double mid = cuts[i] + (cuts[i + 1] - cuts[i]) * 0.5;
            int owner = -1;
            for (int j = 0; j < hull.Count; j++)
                if (mid > hull[j].Bottom && mid < hull[j].Top
                    && (owner < 0 || hull[j].Part.Definition.DiameterM > hull[owner].Part.Definition.DiameterM))
                    owner = j;
            double area = owner >= 0 ? SectionArea(hull[owner].Part.Definition.DiameterM) : 0.0;
            // Internal diameter steps follow the exposed section, including across
            // a buried engine whose mass centre is below its logical interface.
            if (i > 0 && System.Math.Abs(previousArea - area) >= 1e-9)
                elements.Add(new AeroElement(new Vector3d(0.0, cuts[i], 0.0), 0.0, previousArea - area));
            previousArea = area;
            if (owner < 0) continue;
            var span = hull[owner];
            double diameter = span.Part.Definition.DiameterM;
            if (mid < span.Top - span.Nose)
                elements.Add(new AeroElement(new Vector3d(0.0, mid, 0.0),
                    diameter * (cuts[i + 1] - cuts[i]), 0.0, diameter));
        }
        foreach (var span in hull)
            if (span.Nose > 0.0)
                elements.Add(new AeroElement(new Vector3d(0.0, span.Top - 0.6 * span.Nose, 0.0),
                    OgivePlanformFraction * span.Part.Definition.DiameterM * span.Nose, 0.0,
                    span.Part.Definition.DiameterM));
        if (hull.Count > 0)
        {
            var lead = hull.OrderByDescending(s => s.Top).ThenByDescending(s => s.Part.Definition.DiameterM).First();
            elements.Add(new AeroElement(new Vector3d(0.0,
                lead.Top - OgiveCenterOfPressureFraction * lead.Nose, 0.0),
                0.0, SectionArea(lead.Part.Definition.DiameterM)));
        }
        return true;
    }
}
