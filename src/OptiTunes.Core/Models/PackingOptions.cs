namespace OptiTunes.Core.Models;

public sealed class PackingOptions
{
    /// <summary>Taux de support minimal (§7.3) pour une unité posée sur une autre.</summary>
    public double MinSupportRatio { get; set; } = 0.80;

    /// <summary>Les plaques exigent un support quasi intégral (§17).</summary>
    public double PlaqueSupportRatio { get; set; } = 0.95;

    /// <summary>Contrôle de l'accessibilité par la porte arrière selon l'ordre de livraison (§15).</summary>
    public bool RespectDeliveryOrder { get; set; } = true;

    /// <summary>Niveaux appliqués si gerbable mais niveaux max non renseignés (§18 : niveau minimal sûr).</summary>
    public int DefaultMaxLevelsWhenUnknown { get; set; } = 2;

    /// <summary>Teste plusieurs ordres de tri et conserve le meilleur plan.</summary>
    public bool MultiStrategy { get; set; } = true;

    /// <summary>
    /// Nombre maximal de camions (du même type) pour absorber le reliquat. 1 = un seul camion.
    /// Les unités impossibles par nature (dimensions, porte, données) ne déclenchent pas de camion supplémentaire.
    /// </summary>
    public int MaxVehicles { get; set; } = 1;

    /// <summary>
    /// Autorise à pivoter l'unité au sol (longueur en travers du camion) quand le fichier n'impose pas d'orientation.
    /// false = la LONGUEUR déclarée est toujours dans le sens de la longueur du camion.
    /// </summary>
    public bool AllowFloorRotation { get; set; } = true;

    /// <summary>
    /// Tubes gerbables couchés : regroupement en lits en quinconce (pas vertical 0,866 × D, §9.3)
    /// lorsque cela loge plus de tubes que la grille simple.
    /// </summary>
    public bool StaggerTubes { get; set; } = true;

    /// <summary>Débord (jeu) entre deux unités voisines au même niveau, en mm. Les unités gerbées restent en contact.</summary>
    public double GapBetweenUnits { get; set; }

    /// <summary>Débord le long des parois gauche et droite (valeur unique, appliquée des deux côtés), en mm.</summary>
    public double SideClearance { get; set; }

    /// <summary>Débord en hauteur : espace libre minimal entre le haut du chargement et le plafond, en mm.</summary>
    public double RoofClearance { get; set; }

    /// <summary>Gestion du rangement (par ordre = calcul historique).</summary>
    public StowageMode Stowage { get; set; } = StowageMode.Order;

    public bool HasClearances => GapBetweenUnits > 0 || SideClearance > 0 || RoofClearance > 0;

    /// <summary>Espace réellement chargeable : largeur moins deux débords latéraux, hauteur moins le débord plafond.</summary>
    public Vehicle UsableSpace(Vehicle v)
    {
        var usable = VehicleCatalog.Copy(v);
        usable.Width = Math.Max(0, v.Width - 2 * SideClearance);
        usable.Height = Math.Max(0, v.Height - RoofClearance);
        return usable;
    }

    public PackingOptions Clone() => (PackingOptions)MemberwiseClone();
}

public static class Geometry
{
    /// <summary>Tolérance de comparaison en mm.</summary>
    public const double Eps = 0.01;

    public static double Overlap(double aMin, double aMax, double bMin, double bMax) =>
        Math.Max(0, Math.Min(aMax, bMax) - Math.Max(aMin, bMin));

    public static bool Intersects(Placement a, Placement b) =>
        Overlap(a.X, a.MaxX, b.X, b.MaxX) > Eps &&
        Overlap(a.Y, a.MaxY, b.Y, b.MaxY) > Eps &&
        Overlap(a.Z, a.MaxZ, b.Z, b.MaxZ) > Eps;

    public static double ContactArea(Placement upper, Placement lower) =>
        Math.Abs(upper.Z - lower.MaxZ) > Eps
            ? 0
            : Overlap(upper.X, upper.MaxX, lower.X, lower.MaxX) * Overlap(upper.Y, upper.MaxY, lower.Y, lower.MaxY);
}
