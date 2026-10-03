using OptiTunes.Core.Models;

namespace OptiTunes.Core.Engine;

/// <summary>
/// Transforme les ordres (articles commerciaux) en unités physiques transportables (§1.2) :
/// cartons, piles de plaques (§8.2), tubes (§9), bobines (§10.3)...
/// Les unités impossibles par nature (trop grandes, porte, poids) sont écartées avec leur motif.
/// </summary>
public static class UnitBuilder
{
    public sealed record Result(List<PhysicalUnit> Units, List<UnloadedUnit> Rejected, List<Issue> Issues);

    public static Result Build(Groupage groupage, PackingOptions options)
    {
        var units = new List<PhysicalUnit>();
        var rejected = new List<UnloadedUnit>();
        var issues = new List<Issue>();

        // Débords déduits : piles, lits de tubes et contrôles de dimensions se font dans l'espace chargeable.
        var v = options.UsableSpace(groupage.Vehicle);

        foreach (var order in groupage.Orders)
        {
            var orderUnits = Expand(order, v, options, issues);

            if (!order.IsValid)
            {
                rejected.AddRange(orderUnits.Select(u =>
                    new UnloadedUnit(u, RejectReason.InvalidData, string.Join(" ; ", order.BlockingErrors))));
                continue;
            }

            if (orderUnits.Count == 0)
            {
                continue;
            }

            var sample = orderUnits[0];
            var reason = StaticReject(sample, v, out var detail);
            if (reason != null)
            {
                rejected.AddRange(orderUnits.Select(u => new UnloadedUnit(u, reason.Value, detail)));
                continue;
            }

            if (order.Type is PhysicalType.Tube or PhysicalType.Roll && orderUnits.Count > 0)
            {
                var axis = OrientedAxisIsHorizontal(sample);
                if (axis)
                {
                    issues.Add(new Issue(IssueSeverity.Info, "CALAGE",
                        $"Ordre {order.Id} : unités cylindriques couchées – prévoir calage / berceau anti-roulement.",
                        order.SourceLine, order.Id));
                }
            }

            units.AddRange(orderUnits);
        }

        // Étapes et zones de rangement selon le mode choisi (y compris pour les unités écartées, pour le rapport).
        Stowage.Apply(units.Concat(rejected.Select(r => r.Unit)).ToList(), options.Stowage);
        return new Result(units, rejected, issues);
    }

    private static List<PhysicalUnit> Expand(TransportOrder o, Vehicle v, PackingOptions options, List<Issue> issues)
    {
        var list = new List<PhysicalUnit>();
        if (o.Quantity <= 0)
        {
            return list;
        }

        var stackable = o.Stackable == true;
        var maxLevels = !stackable
            ? 1
            : Math.Max(1, o.MaxLevels ?? options.DefaultMaxLevelsWhenUnknown);

        var orientations = o.Orientations ?? (options.AllowFloorRotation ? DefaultOrientations(o.Type) : OrientationSet.O1);
        var supportRatio = o.Type == PhysicalType.Plaque ? options.PlaqueSupportRatio : options.MinSupportRatio;

        if (o.Type == PhysicalType.Plaque)
        {
            var thickness = o.Height;
            var usableHeight = Math.Min(v.Height, v.DoorHeight is > 0 ? v.DoorHeight.Value : v.Height);
            var perHeight = thickness > 0 ? (int)Math.Floor(usableHeight / thickness + 1e-9) : 0;
            var perPile = o.MaxPerPile is > 0 ? Math.Min(perHeight, o.MaxPerPile.Value) : perHeight;

            if (perPile <= 0 || !o.IsValid)
            {
                // Pile impossible : une seule unité porteuse de la quantité, rejetée ensuite.
                list.Add(Make(o, 1, o.Quantity, o.Length, o.Width, o.Height * o.Quantity, o.UnitWeight * o.Quantity));
                return list;
            }

            var remaining = o.Quantity;
            var index = 1;
            while (remaining > 0)
            {
                var q = Math.Min(perPile, remaining);
                list.Add(Make(o, index++, q, o.Length, o.Width, q * thickness, q * o.UnitWeight));
                remaining -= q;
            }

            if (list.Count > 1)
            {
                issues.Add(new Issue(IssueSeverity.Info, "PILES",
                    $"Ordre {o.Id} : {o.Quantity} plaques réparties en {list.Count} piles de {perPile} max " +
                    $"(hauteur pile {perPile * thickness:0} mm).", o.SourceLine, o.Id));
            }

            return list;
        }

        if (o.Type == PhysicalType.Tube && options.StaggerTubes && o.IsValid && StaggeredBeds(o, v, maxLevels, orientations) is { } beds)
        {
            var bedIndex = 1;
            foreach (var (count, width, height, centers) in beds)
            {
                list.Add(new PhysicalUnit
                {
                    Id = $"{o.Id}-L{bedIndex:00}",
                    Order = o,
                    Index = bedIndex++,
                    ItemCount = count,
                    Shape = UnitShape.Staggered,
                    AxisBaseIndex = 0,
                    A = o.Length,
                    B = width,
                    C = height,
                    Weight = count * o.UnitWeight,
                    Stackable = false,
                    MaxLevels = 1,
                    MaxLoadOnTop = 0,
                    Orientations = orientations & OrientationSet.Upright,
                    MustBeOnFloor = o.MustBeOnFloor,
                    RequiredSupportRatio = supportRatio,
                    TubeDiameter = o.Diameter,
                    TubeCenters = centers
                });
            }

            issues.Add(new Issue(IssueSeverity.Info, "QUINCONCE",
                $"Ordre {o.Id} : {o.Quantity} tubes Ø{o.Diameter:0} empilés en quinconce (pas vertical {o.Diameter * StaggerPitch:0} mm) " +
                $"en {beds.Count} lit(s) – plus dense que la grille simple. Calage latéral obligatoire.", o.SourceLine, o.Id));
            return list;
        }

        for (var i = 1; i <= o.Quantity; i++)
        {
            list.Add(o.Type switch
            {
                PhysicalType.Tube => Make(o, i, 1, o.Length, o.Diameter, o.Diameter, o.UnitWeight, UnitShape.Cylinder, 0),
                PhysicalType.Roll => Make(o, i, 1, o.Diameter, o.Diameter, o.Width, o.UnitWeight, UnitShape.Cylinder, 2),
                _ => Make(o, i, 1, o.Length, o.Width, o.Height, o.UnitWeight)
            });
        }

        return list;

        PhysicalUnit Make(TransportOrder order, int index, int count, double a, double b, double c, double weight,
            UnitShape shape = UnitShape.Box, int axis = -1)
        {
            var loadAssumed = stackable && order.MaxLoadOnTop == null;
            var maxLoad = !stackable
                ? 0
                : order.MaxLoadOnTop ?? (maxLevels - 1) * weight;

            return new PhysicalUnit
            {
                Id = $"{order.Id}-{index:000}",
                Order = order,
                Index = index,
                ItemCount = count,
                Shape = shape,
                AxisBaseIndex = axis,
                A = a,
                B = b,
                C = c,
                Weight = weight,
                Stackable = stackable,
                // « Au sommet » non gerbable : peut être posé en haut d'une pile mais ne reçoit rien.
                MaxLevels = order.MustBeOnTop && !stackable ? 99 : maxLevels,
                MaxLoadOnTop = maxLoad,
                MaxLoadAssumed = loadAssumed,
                Orientations = orientations,
                MustBeOnFloor = order.MustBeOnFloor,
                MustBeOnTop = order.MustBeOnTop,
                RequiredSupportRatio = supportRatio
            };
        }
    }

    /// <summary>Pas vertical entre deux rangées de tubes en quinconce : √3 / 2 ≈ 0,866 (§9.3).</summary>
    public const double StaggerPitch = 0.8660254037844386;

    /// <summary>
    /// Lits de tubes en quinconce (largeur = largeur utile, hauteur limitée par la hauteur utile, Nmax et la charge
    /// supportable). null si la grille simple loge autant ou plus par section, ou si les tubes ne peuvent pas être couchés.
    /// </summary>
    public static List<(int Count, double Width, double Height, IReadOnlyList<(double B, double C)> Centers)>? StaggeredBeds(
        TransportOrder o, Vehicle v, int maxLevels, OrientationSet orientations)
    {
        var d = o.Diameter;
        if (o.Stackable != true || maxLevels < 2 || d <= 0 || !orientations.Contains(1) || o.Length > v.Length + Geometry.Eps)
        {
            return null;
        }

        var width = Math.Min(v.Width, v.DoorWidth is > 0 ? v.DoorWidth.Value : v.Width);
        var height = Math.Min(v.Height, v.DoorHeight is > 0 ? v.DoorHeight.Value : v.Height);
        var rowsByLoad = o.MaxLoadOnTop is { } load && o.UnitWeight > 0
            ? 1 + (int)Math.Floor(load / o.UnitWeight + 1e-9)
            : maxLevels;
        var rowCap = Math.Min(maxLevels, rowsByLoad);
        if (rowCap < 2)
        {
            return null;
        }

        var k = (int)Math.Floor(width / d + 1e-9);
        var gridRows = Math.Min(rowCap, (int)Math.Floor(height / d + 1e-9));
        var pitch = d * StaggerPitch;
        var staggerRows = Math.Min(rowCap, (int)Math.Floor((height - d) / pitch + 1e-9) + 1);
        var full = width + Geometry.Eps >= k * d + d / 2;
        int PerRow(int r) => full || r % 2 == 0 ? k : k - 1;
        var staggerCapacity = Enumerable.Range(0, Math.Max(0, staggerRows)).Sum(PerRow);
        if (k < 2 || staggerRows < 2 || staggerCapacity <= k * gridRows)
        {
            return null;
        }

        var beds = new List<(int, double, double, IReadOnlyList<(double, double)>)>();
        var remaining = o.Quantity;
        while (remaining > 0)
        {
            var centers = new List<(double B, double C)>();
            for (var r = 0; r < staggerRows && centers.Count < remaining; r++)
            {
                var offset = r % 2 == 1 ? d / 2 : 0;
                for (var i = 0; i < PerRow(r) && centers.Count < remaining; i++)
                {
                    centers.Add((d / 2 + offset + i * d, d / 2 + r * pitch));
                }
            }

            var bedWidth = centers.Max(c => c.B) + d / 2;
            var bedHeight = centers.Max(c => c.C) + d / 2;
            beds.Add((centers.Count, bedWidth, bedHeight, centers));
            remaining -= centers.Count;
        }

        return beds;
    }

    public static OrientationSet DefaultOrientations(PhysicalType type) => type switch
    {
        PhysicalType.Roll => OrientationSet.O1,
        _ => OrientationSet.Upright
    };

    /// <summary>Contrôles indépendants de la position : dimensions, orientation, porte, poids unitaire.</summary>
    public static RejectReason? StaticReject(PhysicalUnit u, Vehicle v, out string? detail)
    {
        detail = null;
        if (u.Weight > v.MaxPayload + Geometry.Eps)
        {
            detail = $"{u.Weight:0.#} kg > {v.MaxPayload:0} kg";
            return RejectReason.PayloadExceeded;
        }

        bool Fits(int o)
        {
            var (dx, dy, dz) = Orientations.Apply(o, u.A, u.B, u.C);
            return dx <= v.Length + Geometry.Eps && dy <= v.Width + Geometry.Eps && dz <= v.Height + Geometry.Eps;
        }

        var allowedFitting = u.Orientations.Enumerate().Where(Fits).ToList();
        if (allowedFitting.Count == 0)
        {
            var anyFits = OrientationSet.All.Enumerate().Any(Fits);
            detail = $"{u.A:0} × {u.B:0} × {u.C:0} mm, orientations {u.Orientations.Format()}";
            return anyFits ? RejectReason.NoAllowedOrientation : RejectReason.TooLargeForVehicle;
        }

        if (v.HasDoor && !allowedFitting.Any(o => PassesDoor(u, o, v)))
        {
            detail = $"ouverture {v.DoorWidth:0} × {v.DoorHeight:0} mm";
            return RejectReason.DoorPassage;
        }

        return null;
    }

    public static bool PassesDoor(PhysicalUnit u, int orientation, Vehicle v)
    {
        if (!v.HasDoor)
        {
            return true;
        }

        var (_, dy, dz) = Orientations.Apply(orientation, u.A, u.B, u.C);
        return dy <= v.DoorWidth!.Value + Geometry.Eps && dz <= v.DoorHeight!.Value + Geometry.Eps;
    }

    private static bool OrientedAxisIsHorizontal(PhysicalUnit u) =>
        u.Orientations.Enumerate().Any(o => Orientations.Permutation(o)[2] != u.AxisBaseIndex);
}
