namespace OptiTunes.Core.Models;

/// <summary>Véhicule (§4). Dimensions en mm, masses en kg.</summary>
public sealed class Vehicle
{
    public string Id { get; set; } = "";
    public string? Type { get; set; }
    public double Length { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double MaxPayload { get; set; }
    public double? DoorWidth { get; set; }
    public double? DoorHeight { get; set; }

    public bool HasDoor => DoorWidth is > 0 && DoorHeight is > 0;

    /// <summary>Vc en m³.</summary>
    public double VolumeM3 => Length * Width * Height / 1e9;

    /// <summary>Sc en m².</summary>
    public double FloorAreaM2 => Length * Width / 1e6;

    /// <summary>MLc en mètres.</summary>
    public double LinearMeters => Length / 1000.0;

    public override string ToString() =>
        $"{Id} – {Length:0} × {Width:0} × {Height:0} mm – {MaxPayload:0} kg";
}

/// <summary>Groupage exporté : un véhicule + des ordres de transport.</summary>
public sealed class Groupage
{
    public string Id { get; set; } = "";
    public DateTime? Date { get; set; }
    public string? Carrier { get; set; }
    public Vehicle Vehicle { get; set; } = new();
    public List<TransportOrder> Orders { get; } = [];
    public int SourceLine { get; set; }

    /// <summary>Anomalies bloquantes sur le véhicule : aucun calcul possible.</summary>
    public List<string> BlockingErrors { get; } = [];

    public bool IsComputable => BlockingErrors.Count == 0;

    /// <summary>Même groupage (mêmes ordres) sur un autre véhicule, pour simulation.</summary>
    public Groupage WithVehicle(Vehicle vehicle)
    {
        var copy = new Groupage
        {
            Id = Id, Date = Date, Carrier = Carrier, SourceLine = SourceLine, Vehicle = VehicleCatalog.Copy(vehicle)
        };
        copy.Orders.AddRange(Orders);
        return copy;
    }

    /// <summary>Même groupage limité aux ordres retenus (simulation « et si on retire cet ordre ? »).</summary>
    public Groupage WithoutOrders(IReadOnlySet<string> excludedIds)
    {
        var copy = new Groupage { Id = Id, Date = Date, Carrier = Carrier, SourceLine = SourceLine, Vehicle = Vehicle };
        copy.BlockingErrors.AddRange(BlockingErrors);
        copy.Orders.AddRange(Orders.Where(o => !excludedIds.Contains(o.Id)));
        return copy;
    }

    public override string ToString() => $"{Id} ({Orders.Count} ordres)";
}

/// <summary>
/// Ordre de transport : ligne de commande ou ligne de cadencement portant un article.
/// Height = épaisseur unitaire pour les plaques. Diameter pour tubes / bobines.
/// </summary>
public sealed class TransportOrder
{
    public string Id { get; set; } = "";
    public OrderKind Kind { get; set; }
    public string? OrderNumber { get; set; }
    public string? OrderLine { get; set; }
    public string? CadenceNumber { get; set; }
    public string? Customer { get; set; }

    /// <summary>Séquence de livraison (1 = premier arrêt). 0 = non renseigné.</summary>
    public int Stop { get; set; }

    /// <summary>Magasin de départ (gestion des arrêts, facultatif).</summary>
    public string? Warehouse { get; set; }

    /// <summary>Étape de l'itinéraire où l'ordre est chargé (0 = au départ du camion).</summary>
    public int DepartureStep { get; set; }

    /// <summary>Étape de l'itinéraire où l'ordre est livré (0 = non renseignée).</summary>
    public int ArrivalStep { get; set; }

    /// <summary>Étape de livraison : ARRIVEE, sinon ARRET.</summary>
    public int EffectiveArrival => ArrivalStep > 0 ? ArrivalStep : Stop;

    public string Article { get; set; } = "";
    public string? Designation { get; set; }
    public PhysicalType Type { get; set; }

    /// <summary>false si le type physique du fichier n'a pas été reconnu.</summary>
    public bool TypeKnown { get; set; } = true;

    public int Quantity { get; set; }

    public double Length { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Diameter { get; set; }
    public double UnitWeight { get; set; }

    /// <summary>null = non renseigné (traité comme non gerbable par sécurité).</summary>
    public bool? Stackable { get; set; }
    public int? MaxLevels { get; set; }

    /// <summary>Charge maximale supportable sur l'unité (kg). null = inconnue, 0 = ne peut rien recevoir.</summary>
    public double? MaxLoadOnTop { get; set; }

    public OrientationSet? Orientations { get; set; }
    public int? MaxPerPile { get; set; }
    public bool MustBeOnFloor { get; set; }
    public bool MustBeOnTop { get; set; }

    public int SourceLine { get; set; }

    /// <summary>Anomalies bloquantes détectées à l'import : l'ordre n'est pas chargé.</summary>
    public List<string> BlockingErrors { get; } = [];

    public bool IsValid => BlockingErrors.Count == 0;

    public string Reference => string.Join("/", new[] { OrderNumber, OrderLine, Kind == OrderKind.Cadence ? CadenceNumber : null }
        .Where(s => !string.IsNullOrEmpty(s)));

    public string DimensionsText => Type switch
    {
        PhysicalType.Tube => $"L{Length:0} × Ø{Diameter:0}",
        PhysicalType.Roll => $"Ø{Diameter:0} × {Width:0}",
        PhysicalType.Plaque => $"{Length:0} × {Width:0} × ép. {Height:0.##}",
        _ => $"{Length:0} × {Width:0} × {Height:0}"
    };
}

public sealed record Issue(
    IssueSeverity Severity,
    string Code,
    string Message,
    int? SourceLine = null,
    string? OrderId = null)
{
    public string SeverityLabel => Severity switch
    {
        IssueSeverity.Error => "Bloquant",
        IssueSeverity.Warning => "Avertissement",
        _ => "Info"
    };
}
