namespace OptiTunes.Core.Models;

/// <summary>
/// Unité physique réellement positionnée (§1.2) : carton, pile de plaques, tube, palette...
/// Dimensions de base A × B × C (mm) avant orientation.
/// </summary>
public sealed class PhysicalUnit
{
    public required string Id { get; init; }
    public required TransportOrder Order { get; init; }
    public int Index { get; init; }

    /// <summary>Nombre d'articles contenus (plaques dans la pile, sinon 1).</summary>
    public int ItemCount { get; init; } = 1;

    public UnitShape Shape { get; init; }

    /// <summary>Index (0..2) de la dimension de base portée par l'axe du cylindre, -1 sinon.</summary>
    public int AxisBaseIndex { get; init; } = -1;

    public double A { get; init; }
    public double B { get; init; }
    public double C { get; init; }
    public double Weight { get; init; }

    public bool Stackable { get; init; }
    public int MaxLevels { get; init; }
    public double MaxLoadOnTop { get; init; }
    public bool MaxLoadAssumed { get; init; }
    public OrientationSet Orientations { get; init; }
    public bool MustBeOnFloor { get; init; }
    public bool MustBeOnTop { get; init; }
    public double RequiredSupportRatio { get; init; }

    /// <summary>Lit en quinconce : diamètre des tubes et centres (B, C) dans le repère de base, axe = A.</summary>
    public double TubeDiameter { get; init; }

    public IReadOnlyList<(double B, double C)> TubeCenters { get; init; } = [];

    /// <summary>Étape de chargement retenue par le rangement (0 = au départ).</summary>
    public int Departure { get; set; }

    /// <summary>Étape de déchargement retenue par le rangement (0 = sans contrainte).</summary>
    public int Arrival { get; set; }

    /// <summary>Zone de rangement (client, magasin, commande) et son rang de l'avant vers la porte ; -1 = sans zone.</summary>
    public string? GroupKey { get; set; }

    public int GroupRank { get; set; } = -1;

    public double Volume => A * B * C;

    /// <summary>Peut être posée sur une autre unité.</summary>
    public bool CanBeElevated => !MustBeOnFloor && (Stackable || MustBeOnTop);

    /// <summary>Peut recevoir une unité au-dessus.</summary>
    public bool CanReceive => Stackable && !MustBeOnTop && MaxLevels > 1 && MaxLoadOnTop > 0;

    /// <summary>Clé identifiant les unités strictement interchangeables pour le moteur.</summary>
    public string ShapeKey =>
        $"{A}|{B}|{C}|{Weight}|{(int)Orientations}|{Stackable}|{MaxLevels}|{MaxLoadOnTop}|{MustBeOnFloor}|{MustBeOnTop}|{Departure}|{Arrival}|{GroupRank}|{RequiredSupportRatio}";
}

public readonly record struct SupportLink(Placement Support, double ContactArea);

public sealed class Placement
{
    public required PhysicalUnit Unit { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double DX { get; init; }
    public double DY { get; init; }
    public double DZ { get; init; }
    public int Orientation { get; init; }

    /// <summary>Ordre de chargement dans son camion (1 = premier chargé, au fond du véhicule).</summary>
    public int Sequence { get; set; }

    /// <summary>Numéro du camion portant l'unité.</summary>
    public int VehicleNumber { get; set; } = 1;

    public int Level { get; set; } = 1;
    public int LevelCap { get; set; }
    public double LoadAbove { get; set; }
    public List<SupportLink> Supports { get; } = [];

    /// <summary>Centres (axe au milieu) des tubes d'un lit en quinconce, dans le repère véhicule.</summary>
    public IEnumerable<(double X, double Y, double Z)> TubeCentersWorld()
    {
        var perm = Orientations.Permutation(Orientation);
        var aAxis = Array.IndexOf(perm, 0);
        var bAxis = Array.IndexOf(perm, 1);
        var cAxis = Array.IndexOf(perm, 2);
        double[] min = [X, Y, Z];
        double[] size = [DX, DY, DZ];
        foreach (var (b, c) in Unit.TubeCenters)
        {
            var w = new double[3];
            w[aAxis] = min[aAxis] + size[aAxis] / 2;
            w[bAxis] = min[bAxis] + b;
            w[cAxis] = min[cAxis] + c;
            yield return (w[0], w[1], w[2]);
        }
    }

    public double MaxX => X + DX;
    public double MaxY => Y + DY;
    public double MaxZ => Z + DZ;
    public double FootprintArea => DX * DY;
    public bool OnFloor => Z < Geometry.Eps;

    /// <summary>Axe du (des) cylindre(s) dans le repère véhicule (0 = X, 1 = Y, 2 = Z), -1 pour une boîte.</summary>
    public int CylinderAxis => Engine.StackingRules.CylinderAxis(Unit, Orientation);
}

public sealed record StrategyOutcome(string Name, int Units, int Items, double VolumeM3, double LengthUsedM, int CompleteOrders);

public sealed record UnloadedUnit(PhysicalUnit Unit, RejectReason Reason, string? Detail = null);

/// <summary>Chargement d'un camion du groupage.</summary>
public sealed class VehicleLoad
{
    /// <summary>Numéro du camion (1 = véhicule du groupage, 2+ = camions supplémentaires pour le reliquat).</summary>
    public int Number { get; init; }

    public required Vehicle Vehicle { get; init; }
    public List<Placement> Placements { get; } = [];
    public PlanMetrics Metrics { get; set; } = new();
    public string Strategy { get; set; } = "";
    public List<StrategyOutcome> StrategyLog { get; } = [];

    public string Label => $"Camion {Number}";
}

/// <summary>Résultat d'un calcul de groupage : un ou plusieurs camions + reliquat final.</summary>
public sealed class LoadPlan
{
    public required Groupage Groupage { get; init; }
    public Vehicle Vehicle => Groupage.Vehicle;

    /// <summary>Nom de la solution (« Recommandée » ou stratégie appliquée à tous les camions).</summary>
    public string Name { get; init; } = "";

    public bool IsRecommended { get; set; }
    public List<VehicleLoad> Loads { get; } = [];
    public List<UnloadedUnit> Unloaded { get; } = [];
    public List<Issue> Issues { get; } = [];
    public TimeSpan Duration { get; set; }

    /// <summary>Indicateurs globaux (capacités = nombre de camions × capacité d'un camion).</summary>
    public PlanMetrics Metrics { get; set; } = new();

    public PackingOptions Options { get; set; } = new();

    /// <summary>Toutes les unités placées, tous camions confondus.</summary>
    public IReadOnlyList<Placement> Placements => Loads.SelectMany(l => l.Placements).ToList();

    public string Strategy => string.Join(" | ", Loads.Select(l => l.Strategy).Distinct());

    /// <summary>Besoin théorique en métrage linéaire par ordre (§13), calculé sur les données du groupage.</summary>
    public Dictionary<TransportOrder, Engine.OrderLinearMeters> LinearMetersByOrder { get; } = [];

    /// <summary>ML équivalent occupé dans le plan (tous camions) par les unités au sol d'un ordre.</summary>
    public double PlanLinearMeters(TransportOrder order) =>
        Vehicle.Width <= 0
            ? 0
            : Placements.Where(p => p.Unit.Order == order && p.OnFloor).Sum(p => p.FootprintArea) / Vehicle.Width / 1000.0;

    public int PlacedItems(TransportOrder order) =>
        Placements.Where(p => p.Unit.Order == order).Sum(p => p.Unit.ItemCount);

    public int RemainingItems(TransportOrder order) =>
        Unloaded.Where(u => u.Unit.Order == order).Sum(u => u.Unit.ItemCount);

    /// <summary>Numéros des camions transportant l'ordre.</summary>
    public IReadOnlyList<int> VehiclesOf(TransportOrder order) =>
        Loads.Where(l => l.Placements.Any(p => p.Unit.Order == order)).Select(l => l.Number).ToList();
}
