using OptiTunes.Core.Models;

namespace OptiTunes.Core.Engine;

/// <summary>
/// Besoin en métrage linéaire d'un ordre, calculé à partir des seules données du groupage (§13),
/// avant tout placement : piles possibles (niveaux, hauteur, charge supportable), puis rangées sur la largeur.
/// </summary>
/// <param name="Stacks">Nombre de piles au sol.</param>
/// <param name="Levels">Niveaux par pile retenus.</param>
/// <param name="PerRow">Piles côte à côte sur la largeur du véhicule.</param>
/// <param name="Rows">Rangées le long du véhicule.</param>
/// <param name="FootprintM2">Emprise au sol totale (m²).</param>
/// <param name="MlRows">ML brut par rangées (m) : rangées × longueur au sol (§13.1).</param>
/// <param name="MlEquivalent">ML équivalent par surface (m) : emprise / largeur utile (§13.2).</param>
/// <param name="MlLengthwise">ML rangées avec la longueur déclarée dans le sens du camion (null si impossible).</param>
/// <param name="MlCrosswise">ML rangées avec la longueur déclarée en travers du camion (null si impossible ou interdit).</param>
public sealed record OrderLinearMeters(
    int Stacks,
    int Levels,
    int PerRow,
    int Rows,
    double FootprintM2,
    double MlRows,
    double MlEquivalent,
    double? MlLengthwise = null,
    double? MlCrosswise = null,
    int BestOrientation = 1,
    int CapacityPerVehicle = 0);

public static class LinearMeterCalculator
{
    /// <summary>Unités physiques d'un même ordre. null si aucune orientation ne tient dans le véhicule.</summary>
    /// <param name="gap">Débord entre unités voisines (mm) : ajouté entre deux piles d'une rangée et entre deux rangées.</param>
    public static OrderLinearMeters? ForUnits(IReadOnlyList<PhysicalUnit> units, Vehicle v, double gap = 0)
    {
        if (units.Count == 0 || v.Width <= 0)
        {
            return null;
        }

        // Unité de référence = la plus haute (pile complète pour les plaques).
        var u = units.OrderByDescending(x => x.C).First();
        OrderLinearMeters? best = null;
        OrderLinearMeters? packing = null;
        double? lengthwise = null;
        double? crosswise = null;

        foreach (var o in u.Orientations.Enumerate())
        {
            var (dx, dy, dz) = Orientations.Apply(o, u.A, u.B, u.C);
            if (dx > v.Length + Geometry.Eps || dy > v.Width + Geometry.Eps || dz > v.Height + Geometry.Eps)
            {
                continue;
            }

            var levels = 1;
            if (u.CanReceive)
            {
                levels = Math.Min(u.MaxLevels, (int)Math.Floor(v.Height / dz + 1e-9));
                if (u.Weight > 0)
                {
                    levels = Math.Min(levels, 1 + (int)Math.Floor(u.MaxLoadOnTop / u.Weight + 1e-9));
                }

                levels = Math.Max(1, levels);
            }

            var stacks = (int)Math.Ceiling(units.Count / (double)levels);
            var perRow = Math.Max(1, (int)Math.Floor((v.Width + gap) / (dy + gap) + 1e-9));
            var rows = (int)Math.Ceiling(stacks / (double)perRow);
            var candidate = new OrderLinearMeters(
                stacks,
                levels,
                perRow,
                rows,
                stacks * dx * dy / 1e6,
                (rows * dx + Math.Max(0, rows - 1) * gap) / 1000.0,
                stacks * dx * dy / v.Width / 1000.0,
                BestOrientation: o,
                CapacityPerVehicle: perRow * (int)Math.Floor((v.Length + gap) / (dx + gap) + 1e-9) * levels);

            if (o == 1)
            {
                lengthwise = candidate.MlRows;
            }
            else if (o == 2)
            {
                crosswise = candidate.MlRows;
            }

            if (best == null ||
                candidate.MlRows < best.MlRows - 1e-9 ||
                (Math.Abs(candidate.MlRows - best.MlRows) < 1e-9 && candidate.MlEquivalent < best.MlEquivalent))
            {
                best = candidate;
            }

            // Sens de pose pour le remplissage : si tout ne tient pas dans un camion, la plus grande capacité prime.
            if (packing == null || BetterForPacking(candidate, packing, units.Count))
            {
                packing = candidate;
            }
        }

        return best == null
            ? null
            : best with
            {
                MlLengthwise = lengthwise, MlCrosswise = crosswise,
                BestOrientation = packing!.BestOrientation, CapacityPerVehicle = packing.CapacityPerVehicle
            };
    }

    private static bool BetterForPacking(OrderLinearMeters a, OrderLinearMeters b, int count)
    {
        var aFits = a.CapacityPerVehicle >= count;
        var bFits = b.CapacityPerVehicle >= count;
        if (aFits != bFits)
        {
            return aFits;
        }

        if (!aFits && a.CapacityPerVehicle != b.CapacityPerVehicle)
        {
            return a.CapacityPerVehicle > b.CapacityPerVehicle;
        }

        return a.MlRows < b.MlRows - 1e-9 || (Math.Abs(a.MlRows - b.MlRows) < 1e-9 && a.MlEquivalent < b.MlEquivalent);
    }
}
