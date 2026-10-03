namespace OptiTunes.Core.Models;

/// <summary>Indicateurs d'occupation (§13, §14, §16.1, §19.1), pour un camion ou pour tout le groupage.</summary>
public sealed class PlanMetrics
{
    /// <summary>Nombre de camions couverts par ces indicateurs (capacités multipliées d'autant).</summary>
    public int VehicleCount { get; init; } = 1;

    /// <summary>Minimum théorique de camions : MAX(poids, volume, ML équivalent) / capacité d'un camion.</summary>
    public int EstimatedVehicles { get; init; }

    public double VehicleVolumeM3 { get; init; }
    public double VehicleFloorM2 { get; init; }
    public double VehicleLinearMeters { get; init; }
    public double VehiclePayloadKg { get; init; }

    public double LoadedWeightKg { get; init; }
    public double LoadedVolumeM3 { get; init; }
    public double FloorUsedM2 { get; init; }
    public double LinearMetersEquivalent { get; init; }
    public double LinearMetersReal { get; init; }

    /// <summary>Σ ML brut par rangées, chaque ordre chargé séparément (majorant : §13.6, pas de partage de rangée).</summary>
    public double LinearMetersRequiredRows { get; init; }

    /// <summary>Besoin théorique total MLtotal = Σ ML équivalent (§13.3) : indicateur de référence.</summary>
    public double LinearMetersRequiredEquivalent { get; init; }

    public double LinearRequiredRate { get; init; }
    public double MaxLoadHeight { get; init; }

    public double WeightRate { get; init; }
    public double VolumeRate { get; init; }
    public double FloorRate { get; init; }
    public double LinearRate { get; init; }
    public double LinearRealRate { get; init; }

    public int UnitsPlaced { get; init; }
    public int UnitsTotal { get; init; }
    public int ItemsPlaced { get; init; }
    public int ItemsTotal { get; init; }
    public int ItemsRemaining => ItemsTotal - ItemsPlaced;

    public double CgX { get; init; }
    public double CgY { get; init; }
    public double CgZ { get; init; }

    public string LimitingFactor { get; init; } = "";

    /// <summary>Indicateurs globaux du groupage, tous camions confondus.</summary>
    public static PlanMetrics Compute(LoadPlan plan)
    {
        var count = Math.Max(1, plan.Loads.Count);
        var perLoad = plan.Loads.Select(l => l.Metrics).ToList();
        return Build(plan, plan.Vehicle, plan.Placements, count,
            mlReal: perLoad.Sum(m => m.LinearMetersReal),
            limiting: GlobalLimiting(plan));
    }

    /// <summary>Indicateurs d'un camion.</summary>
    public static PlanMetrics Compute(LoadPlan plan, VehicleLoad load)
    {
        var floor = load.Placements.Where(p => p.OnFloor).ToList();
        var mlReal = floor.Count == 0 ? 0 : (floor.Max(p => p.MaxX) - floor.Min(p => p.X)) / 1000.0;
        var metrics = Build(plan, load.Vehicle, load.Placements, 1, mlReal, "");
        return metrics.With(LoadLimiting(plan, load, metrics));
    }

    private static PlanMetrics Build(LoadPlan plan, Vehicle v, IReadOnlyList<Placement> placed, int count, double mlReal, string limiting)
    {
        var floor = placed.Where(p => p.OnFloor).ToList();
        var weight = placed.Sum(p => p.Unit.Weight);
        var volume = placed.Sum(p => p.DX * p.DY * p.DZ) / 1e9;
        var floorArea = floor.Sum(p => p.FootprintArea) / 1e6;
        var mlEq = v.Width > 0 ? floor.Sum(p => p.FootprintArea) / v.Width / 1000.0 : 0;

        double cgx = 0, cgy = 0, cgz = 0;
        if (weight > 0)
        {
            cgx = placed.Sum(p => p.Unit.Weight * (p.X + p.DX / 2)) / weight;
            cgy = placed.Sum(p => p.Unit.Weight * (p.Y + p.DY / 2)) / weight;
            cgz = placed.Sum(p => p.Unit.Weight * (p.Z + p.DZ / 2)) / weight;
        }

        var requiredEq = plan.LinearMetersByOrder.Values.Sum(m => m.MlEquivalent);

        return new PlanMetrics
        {
            VehicleCount = count,
            EstimatedVehicles = Estimate(plan),
            VehicleVolumeM3 = v.VolumeM3 * count,
            VehicleFloorM2 = v.FloorAreaM2 * count,
            VehicleLinearMeters = v.LinearMeters * count,
            VehiclePayloadKg = v.MaxPayload * count,
            LoadedWeightKg = weight,
            LoadedVolumeM3 = volume,
            FloorUsedM2 = floorArea,
            LinearMetersEquivalent = mlEq,
            LinearMetersReal = mlReal,
            LinearMetersRequiredRows = plan.LinearMetersByOrder.Values.Sum(m => m.MlRows),
            LinearMetersRequiredEquivalent = requiredEq,
            LinearRequiredRate = Rate(requiredEq, v.LinearMeters),
            MaxLoadHeight = placed.Count == 0 ? 0 : placed.Max(p => p.MaxZ),
            WeightRate = Rate(weight, v.MaxPayload * count),
            VolumeRate = Rate(volume, v.VolumeM3 * count),
            FloorRate = Rate(floorArea, v.FloorAreaM2 * count),
            LinearRate = Rate(mlEq, v.LinearMeters * count),
            LinearRealRate = Rate(mlReal, v.LinearMeters * count),
            UnitsPlaced = placed.Count,
            UnitsTotal = plan.Placements.Count + plan.Unloaded.Count,
            ItemsPlaced = placed.Sum(p => p.Unit.ItemCount),
            ItemsTotal = plan.Groupage.Orders.Sum(o => o.Quantity),
            CgX = cgx,
            CgY = cgy,
            CgZ = cgz,
            LimitingFactor = limiting
        };
    }

    private PlanMetrics With(string limiting) => new()
    {
        VehicleCount = VehicleCount, EstimatedVehicles = EstimatedVehicles,
        VehicleVolumeM3 = VehicleVolumeM3, VehicleFloorM2 = VehicleFloorM2, VehicleLinearMeters = VehicleLinearMeters,
        VehiclePayloadKg = VehiclePayloadKg, LoadedWeightKg = LoadedWeightKg, LoadedVolumeM3 = LoadedVolumeM3,
        FloorUsedM2 = FloorUsedM2, LinearMetersEquivalent = LinearMetersEquivalent, LinearMetersReal = LinearMetersReal,
        LinearMetersRequiredRows = LinearMetersRequiredRows, LinearMetersRequiredEquivalent = LinearMetersRequiredEquivalent,
        LinearRequiredRate = LinearRequiredRate, MaxLoadHeight = MaxLoadHeight, WeightRate = WeightRate,
        VolumeRate = VolumeRate, FloorRate = FloorRate, LinearRate = LinearRate, LinearRealRate = LinearRealRate,
        UnitsPlaced = UnitsPlaced, UnitsTotal = UnitsTotal, ItemsPlaced = ItemsPlaced, ItemsTotal = ItemsTotal,
        CgX = CgX, CgY = CgY, CgZ = CgZ, LimitingFactor = limiting
    };

    /// <summary>Borne basse du nombre de camions : chaque ressource doit tenir dans N camions.</summary>
    private static int Estimate(LoadPlan plan)
    {
        var v = plan.Vehicle;
        if (v.MaxPayload <= 0 || v.VolumeM3 <= 0 || v.LinearMeters <= 0)
        {
            return 0;
        }

        var units = plan.Placements.Select(p => p.Unit)
            .Concat(plan.Unloaded.Where(u => IsSpaceReason(u.Reason)).Select(u => u.Unit))
            .ToList();
        var weight = units.Sum(u => u.Weight) / v.MaxPayload;
        var volume = units.Sum(u => u.Volume) / 1e9 / v.VolumeM3;
        var ml = plan.LinearMetersByOrder.Values.Sum(m => m.MlEquivalent) / v.LinearMeters;
        return Math.Max(1, (int)Math.Ceiling(Math.Max(weight, Math.Max(volume, ml)) - 1e-9));
    }

    public static bool IsSpaceReason(RejectReason reason) =>
        reason is RejectReason.PayloadExceeded or RejectReason.FloorSaturatedNonStackable or RejectReason.NoValidPosition
            or RejectReason.StowageBlocked;

    private static double Rate(double value, double capacity) => capacity > 0 ? value / capacity * 100 : 0;

    private static string GlobalLimiting(LoadPlan plan)
    {
        if (plan.Unloaded.Count == 0)
        {
            return plan.Loads.Count > 1 ? $"Aucune – tout est chargé sur {plan.Loads.Count} camions" : "Aucune – tout est chargé";
        }

        var main = plan.Unloaded
            .GroupBy(u => u.Reason)
            .OrderByDescending(g => g.Sum(u => u.Unit.ItemCount))
            .First().Key;

        return IsSpaceReason(main) && plan.Loads.Count >= plan.Options.MaxVehicles && plan.Options.MaxVehicles > 1
            ? $"Nombre maximal de camions atteint ({plan.Options.MaxVehicles})"
            : main.Label();
    }

    /// <summary>Ce qui a « fermé » ce camion : la ressource la plus consommée.</summary>
    private static string LoadLimiting(LoadPlan plan, VehicleLoad load, PlanMetrics m)
    {
        var isLast = load == plan.Loads[^1];
        if (isLast && !plan.Unloaded.Any(u => IsSpaceReason(u.Reason)))
        {
            return "Aucune – reste de la place";
        }

        if (m.WeightRate >= 95)
        {
            return $"Poids (charge utile {m.WeightRate:0} %)";
        }

        if (m.LinearRealRate >= 92 || m.FloorRate >= 85)
        {
            return $"Plancher / métrage linéaire ({m.LinearRealRate:0} % réel)";
        }

        return m.VolumeRate >= 80 ? $"Volume ({m.VolumeRate:0} %)" : "Hauteur / gerbage / ordre de livraison";
    }
}
