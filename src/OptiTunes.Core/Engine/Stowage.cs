using OptiTunes.Core.Models;

namespace OptiTunes.Core.Engine;

/// <summary>
/// Gestion des arrêts et du rangement : étapes de chargement / déchargement de chaque unité, zones de rangement
/// (client, magasin, commande) et règle d'accessibilité par la porte arrière. Commun au moteur et au validateur.
/// </summary>
public static class Stowage
{
    public static string Label(this StowageMode mode) => mode switch
    {
        StowageMode.Stops => "Par arrêts (départ / arrivée)",
        StowageMode.Customer => "Par client",
        StowageMode.Warehouse => "Par magasin de départ",
        StowageMode.Command => "Par commande",
        StowageMode.Compact => "Compact (sans contrainte d'accès)",
        _ => "Par ordre (calcul actuel)"
    };

    public static bool IsGrouped(this StowageMode mode) =>
        mode is StowageMode.Customer or StowageMode.Warehouse or StowageMode.Command;

    /// <summary>Le groupage porte-t-il des étapes (MAGASIN / DEPART / ARRIVEE) ?</summary>
    public static bool HasItinerary(Groupage g) =>
        g.Orders.Any(o => o.DepartureStep > 0 || o.ArrivalStep > 0 || !string.IsNullOrWhiteSpace(o.Warehouse));

    /// <summary>Rangement proposé par défaut : par arrêts si le fichier en donne, sinon le calcul actuel.</summary>
    public static StowageMode DefaultFor(Groupage g) => HasItinerary(g) ? StowageMode.Stops : StowageMode.Order;

    /// <summary>
    /// Renseigne sur chaque unité ses étapes et sa zone de rangement selon le mode.
    /// Mode « Ordre » : seule la colonne ARRET compte (comportement historique). Mode « Compact » : aucune contrainte.
    /// </summary>
    public static void Apply(IReadOnlyCollection<PhysicalUnit> units, StowageMode mode)
    {
        foreach (var u in units)
        {
            var o = u.Order;
            (u.Departure, u.Arrival) = mode switch
            {
                StowageMode.Compact => (0, 0),
                StowageMode.Order => (0, o.Stop),
                _ => (o.DepartureStep, o.EffectiveArrival)
            };
            u.GroupKey = mode switch
            {
                StowageMode.Customer => string.IsNullOrWhiteSpace(o.Customer) ? "(client non renseigné)" : o.Customer!.Trim(),
                StowageMode.Warehouse => string.IsNullOrWhiteSpace(o.Warehouse) ? "(magasin non renseigné)" : o.Warehouse!.Trim(),
                StowageMode.Command => string.IsNullOrWhiteSpace(o.OrderNumber) ? o.Id : o.OrderNumber!.Trim(),
                _ => null
            };
            u.GroupRank = -1;
        }

        if (!mode.IsGrouped())
        {
            return;
        }

        // Zones de l'avant vers la porte : chargées d'abord, puis livrées en dernier, puis ordre alphabétique.
        var ranks = units
            .GroupBy(u => u.GroupKey!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Min(u => u.Departure))
            .ThenByDescending(g => g.Max(u => u.Arrival))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select((g, index) => (g.Key, index))
            .ToDictionary(x => x.Key, x => x.index, StringComparer.OrdinalIgnoreCase);

        foreach (var u in units)
        {
            u.GroupRank = ranks[u.GroupKey!];
        }
    }

    /// <summary>
    /// Vrai si <paramref name="blocker"/>, placé entre <paramref name="front"/> et la porte (ou posé dessus),
    /// empêche de charger ou de décharger <paramref name="front"/> sans le déplacer.
    /// Étape 0 = inconnue : chargé au départ, aucune contrainte de déchargement.
    /// </summary>
    public static bool Blocks(PhysicalUnit front, PhysicalUnit blocker)
    {
        int fl = front.Departure, fu = front.Arrival, bl = blocker.Departure, bu = blocker.Arrival;

        // Déchargement de « front » : « blocker » est encore à bord (déchargé plus tard, chargé avant).
        if (fu > 0 && bu > fu && bl < fu)
        {
            return true;
        }

        // Chargement de « front » : « blocker » est déjà à bord (chargé avant, pas encore déchargé).
        return fl > 0 && bl < fl && (bu == 0 || bu > fl);
    }

    /// <summary>Vrai si l'unité d'une zone suivante se trouve entièrement devant une zone précédente, ou posée sur une autre zone.</summary>
    public static bool ZoneConflict(PhysicalUnit unit, double x, double maxX, Placement other) =>
        unit.GroupRank >= 0 && other.Unit.GroupRank >= 0 && other.Unit.GroupRank != unit.GroupRank &&
        (unit.GroupRank > other.Unit.GroupRank
            ? other.X >= maxX - Geometry.Eps       // zone suivante devant une zone précédente
            : x >= other.MaxX - Geometry.Eps);     // zone précédente derrière une zone suivante

    public static string Describe(PhysicalUnit u) =>
        u.Departure > 0 || u.Arrival > 0
            ? $"étapes {(u.Departure > 0 ? u.Departure.ToString() : "départ")} → {(u.Arrival > 0 ? u.Arrival.ToString() : "?")}"
            : "sans étape";
}
