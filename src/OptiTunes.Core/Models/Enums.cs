namespace OptiTunes.Core.Models;

/// <summary>Familles physiques de l'analyse fonctionnelle (§2).</summary>
public enum PhysicalType
{
    Box,
    Plaque,
    Tube,
    Bundle,
    Pallet,
    Roll,
    Custom
}

/// <summary>Nature de l'ordre de transport exporté dans le groupage.</summary>
public enum OrderKind
{
    /// <summary>Ligne de commande.</summary>
    CommandLine,

    /// <summary>Ligne de cadencement d'une ligne de commande.</summary>
    Cadence
}

public enum UnitShape
{
    Box,
    Cylinder,

    /// <summary>Lit de tubes identiques empilés en quinconce (§9.3), manipulé comme un bloc.</summary>
    Staggered
}

/// <summary>Gestion du rangement dans le camion.</summary>
public enum StowageMode
{
    /// <summary>Calcul historique : optimisation, accessibilité selon la colonne ARRET.</summary>
    Order,

    /// <summary>Par arrêts : étapes de départ (chargement) et d'arrivée (livraison) de chaque ordre.</summary>
    Stops,

    /// <summary>Zones continues par client.</summary>
    Customer,

    /// <summary>Zones continues par magasin de départ.</summary>
    Warehouse,

    /// <summary>Zones continues par commande (lignes et cadences ensemble).</summary>
    Command,

    /// <summary>Densité maximale, sans contrainte d'accès.</summary>
    Compact
}

public enum IssueSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>Motifs d'impossibilité (§19.3).</summary>
public enum RejectReason
{
    InvalidData,
    TooLargeForVehicle,
    NoAllowedOrientation,
    DoorPassage,
    PayloadExceeded,
    FloorSaturatedNonStackable,
    NoValidPosition,

    /// <summary>Place disponible, mais l'itinéraire ou les zones de rangement interdisent toute position.</summary>
    StowageBlocked
}

public static class EnumLabels
{
    public static string Label(this PhysicalType type) => type switch
    {
        PhysicalType.Box => "BOX",
        PhysicalType.Plaque => "PLAQUE",
        PhysicalType.Tube => "TUBE",
        PhysicalType.Bundle => "FAISCEAU",
        PhysicalType.Pallet => "PALETTE",
        PhysicalType.Roll => "BOBINE",
        _ => "CUSTOM"
    };

    public static string Label(this OrderKind kind) => kind == OrderKind.Cadence ? "CAD" : "CDE";

    public static string Label(this RejectReason reason) => reason switch
    {
        RejectReason.InvalidData => "Données indispensables manquantes ou invalides",
        RejectReason.TooLargeForVehicle => "Dimensions supérieures au véhicule",
        RejectReason.NoAllowedOrientation => "Aucune orientation autorisée compatible",
        RejectReason.DoorPassage => "Passage par la porte impossible",
        RejectReason.PayloadExceeded => "Charge utile dépassée",
        RejectReason.FloorSaturatedNonStackable => "Produit non gerbable et plancher saturé",
        RejectReason.StowageBlocked => "Accessibilité impossible avec l'itinéraire / le rangement (contrainte stricte)",
        _ => "Aucune position valide restante (plancher / hauteur / gerbage saturés)"
    };
}
