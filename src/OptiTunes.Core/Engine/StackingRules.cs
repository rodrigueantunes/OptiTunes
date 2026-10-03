using OptiTunes.Core.Models;

namespace OptiTunes.Core.Engine;

/// <summary>
/// Règles physiques de superposition (§7, §10.3, §16.3, §17), communes au moteur et au validateur.
/// Les supports eux-mêmes sont recalculés indépendamment de part et d'autre.
/// </summary>
public static class StackingRules
{
    /// <summary>Axe du cylindre dans le repère véhicule (0 = X, 1 = Y, 2 = Z), -1 si l'unité n'est pas cylindrique.</summary>
    public static int CylinderAxis(PhysicalUnit unit, int orientation)
    {
        if (unit.Shape == UnitShape.Box || unit.AxisBaseIndex < 0)
        {
            return -1;
        }

        return Array.IndexOf(Orientations.Permutation(orientation), unit.AxisBaseIndex);
    }

    public static bool IsLying(PhysicalUnit unit, int orientation)
    {
        var axis = CylinderAxis(unit, orientation);
        return axis is 0 or 1;
    }

    /// <summary>
    /// Motif d'interdiction si <paramref name="upper"/> (pose <paramref name="orientation"/>, emprise X/Y) repose sur
    /// <paramref name="lower"/>, sinon null.
    /// </summary>
    public static string? Forbidden(PhysicalUnit upper, int orientation, double x, double y, double dx, double dy, Placement lower)
    {
        if (!lower.Unit.Stackable || lower.Unit.MustBeOnTop || lower.Unit.MaxLevels <= 1 || lower.Unit.MaxLoadOnTop <= 0)
        {
            return $"{lower.Unit.Id} ne peut rien recevoir";
        }

        if (lower.Unit.Shape == UnitShape.Staggered)
        {
            return $"{lower.Unit.Id} : lit de tubes en quinconce, rien ne peut être posé dessus";
        }

        var lowerAxis = CylinderAxis(lower.Unit, lower.Orientation);
        if (lowerAxis is not (0 or 1))
        {
            return null;
        }

        // Support cylindrique couché : contact linéaire, roulement possible.
        if (lower.Unit.Order.Type == PhysicalType.Roll)
        {
            return $"{lower.Unit.Id} : bobine couchée, rien dessus sans berceau";
        }

        var upperAxis = CylinderAxis(upper, orientation);
        if (upper.Shape != UnitShape.Cylinder || upperAxis != lowerAxis)
        {
            return $"{lower.Unit.Id} : tube couché, seul un tube de même axe peut être posé dessus (berceau requis sinon)";
        }

        // Empilage en grille : même diamètre, exactement à l'aplomb.
        var (cross, crossSize) = lowerAxis == 0 ? (y, dy) : (x, dx);
        var (lowerCross, lowerCrossSize) = lowerAxis == 0 ? (lower.Y, lower.DY) : (lower.X, lower.DX);
        if (Math.Abs(cross - lowerCross) > Geometry.Eps || Math.Abs(crossSize - lowerCrossSize) > Geometry.Eps)
        {
            return $"{lower.Unit.Id} : tube non aligné sur le tube inférieur";
        }

        return null;
    }

    /// <summary>
    /// Le centre de l'emprise doit se trouver dans l'enveloppe des surfaces de contact (pas de basculement, §16.3).
    /// </summary>
    public static bool CenterOverSupports(double x, double y, double dx, double dy, IEnumerable<Placement> supports)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var any = false;
        foreach (var q in supports)
        {
            var x0 = Math.Max(x, q.X);
            var x1 = Math.Min(x + dx, q.MaxX);
            var y0 = Math.Max(y, q.Y);
            var y1 = Math.Min(y + dy, q.MaxY);
            if (x1 - x0 <= Geometry.Eps || y1 - y0 <= Geometry.Eps)
            {
                continue;
            }

            any = true;
            minX = Math.Min(minX, x0);
            minY = Math.Min(minY, y0);
            maxX = Math.Max(maxX, x1);
            maxY = Math.Max(maxY, y1);
        }

        var cx = x + dx / 2;
        var cy = y + dy / 2;
        return any && cx >= minX - Geometry.Eps && cx <= maxX + Geometry.Eps && cy >= minY - Geometry.Eps && cy <= maxY + Geometry.Eps;
    }
}
