using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using OptiTunes.App.Services;
using OptiTunes.Core.Engine;
using OptiTunes.Core.Models;
using OptiTunes.Core.Validation;

namespace OptiTunes.App.ViewModels;

/// <summary>
/// Ligne de la grille des ordres de transport, enrichie du résultat de chargement.
/// « Charger » décoché = ordre exclu du calcul (simulation).
/// </summary>
public sealed class OrderRowViewModel(TransportOrder order, LoadPlan? plan, Color color, bool included = true,
    Action<OrderRowViewModel, bool>? onIncludedChanged = null)
{
    private bool _included = included;

    public TransportOrder Order { get; } = order;

    public bool IsIncluded
    {
        get => _included;
        set
        {
            if (_included == value)
            {
                return;
            }

            _included = value;
            onIncludedChanged?.Invoke(this, value);
        }
    }
    public SolidColorBrush Brush { get; } = Frozen(new SolidColorBrush(color));

    public string Id => Order.Id;
    public string Kind => Order.Kind.Label();
    public string Reference => Order.Reference;
    public string? Customer => Order.Customer;
    public string Stop => Order.Stop > 0 ? Order.Stop.ToString() : "–";
    public string Warehouse => string.IsNullOrWhiteSpace(Order.Warehouse) ? "–" : Order.Warehouse!;
    public string Departure => Order.DepartureStep > 0 ? Order.DepartureStep.ToString() : "–";
    public string Arrival => Order.ArrivalStep > 0 ? Order.ArrivalStep.ToString() : "–";
    public string Article => Order.Article;
    public string? Designation => Order.Designation;
    public string Type => Order.TypeKnown ? Order.Type.Label() : "?";
    public int Quantity => Order.Quantity;
    public string Dimensions => Order.DimensionsText;
    public double? UnitWeight => Order.UnitWeight >= 0 ? Order.UnitWeight : null;
    public double? TotalWeight => Order.UnitWeight >= 0 ? Order.UnitWeight * Order.Quantity : null;

    private OrderLinearMeters? Ml => plan?.LinearMetersByOrder.GetValueOrDefault(Order);

    /// <summary>ML brut par rangées (besoin théorique).</summary>
    public double? MlRows => Ml?.MlRows;

    /// <summary>ML équivalent surface (besoin théorique).</summary>
    public double? MlEquivalent => Ml?.MlEquivalent;

    /// <summary>ML équivalent réellement occupé au sol dans le plan.</summary>
    public double? MlPlan => plan?.PlanLinearMeters(Order);

    public string MlDetail => Ml is { } m
        ? $"{m.Stacks} pile(s) de {m.Levels} niveau(x) · {m.PerRow} par rangée · {m.Rows} rangée(s)"
        : "";

    public string MlTooltip
    {
        get
        {
            if (Ml is not { } m)
            {
                return "Métrage non calculable (données ou dimensions).";
            }

            static string F(double? v) => v is { } x ? $"{x:N2} m" : "impossible";
            return string.Join(Environment.NewLine,
                $"ML plancher retenu : {m.MlRows:N2} m ({MlDetail})",
                $"  • longueur dans le sens du camion : {F(m.MlLengthwise)}",
                $"  • longueur en travers du camion : {F(m.MlCrosswise)}",
                $"ML équivalent surface (§13.2) : {m.MlEquivalent:N2} m",
                $"ML occupé dans le plan : {MlPlan:N2} m");
        }
    }

    public string Stacking => Order.Stackable switch
    {
        true => Order.MaxLevels is { } n ? $"Oui · {n - 1} gerbage{(n - 1 > 1 ? "s" : "")}" : "Oui · ? gerbage",
        false => "Non",
        null => "Non (inconnu)"
    } + (Order.MustBeOnFloor ? " · sol" : "") + (Order.MustBeOnTop ? " · sommet" : "");

    public string Vehicles => plan == null || !_included ? "" : string.Join(", ", plan.VehiclesOf(Order));
    public string Loaded => _included ? (plan?.PlacedItems(Order) ?? 0).ToString("N0") : "–";
    public string Remaining => _included ? (plan?.RemainingItems(Order) ?? Order.Quantity).ToString("N0") : "–";
    private int RemainingCount => plan?.RemainingItems(Order) ?? Order.Quantity;
    public bool IsComplete => _included && RemainingCount == 0 && Order.IsValid;

    /// <summary>Pastille : vert chargé, orange partiel, rouge non chargé / bloqué, gris exclu.</summary>
    public object? StatusLevel => !_included ? null
        : !Order.IsValid ? CheckStatus.Error
        : RemainingCount == 0 ? CheckStatus.Ok
        : (plan?.PlacedItems(Order) ?? 0) > 0 ? CheckStatus.Warning : CheckStatus.Error;

    public string Status
    {
        get
        {
            if (!_included)
            {
                return "Exclu du calcul (simulation)";
            }

            if (!Order.IsValid)
            {
                return "Bloqué : " + string.Join(", ", Order.BlockingErrors);
            }

            if (plan == null)
            {
                return "";
            }

            if (RemainingCount == 0)
            {
                return plan.VehiclesOf(Order).Count > 1 ? "Chargé (réparti sur plusieurs camions)" : "Chargé";
            }

            var reasons = plan.Unloaded.Where(u => u.Unit.Order == Order).Select(u => u.Reason.Label()).Distinct();
            return "Reliquat : " + string.Join(" ; ", reasons);
        }
    }

    private static SolidColorBrush Frozen(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}

/// <summary>Ligne de la grille des unités placées.</summary>
public sealed class PlacementRowViewModel(Placement placement, Color color)
{
    public Placement Placement { get; } = placement;
    public SolidColorBrush Brush { get; } = new(color);

    public int Sequence => Placement.Sequence;
    public string UnitId => Placement.Unit.Id;
    public string OrderId => Placement.Unit.Order.Id;
    public string Article => Placement.Unit.Order.Article;
    public string? Customer => Placement.Unit.Order.Customer;
    public string Type => Placement.Unit.Order.Type.Label();
    public int Items => Placement.Unit.ItemCount;
    public double X => Placement.X;
    public double Y => Placement.Y;
    public double Z => Placement.Z;
    public string Size => $"{Placement.DX:0} × {Placement.DY:0} × {Placement.DZ:0}";
    /// <summary>Niveau de gerbage : 0 = au sol, 1 = première unité gerbée, etc.</summary>
    public int Level => Placement.Level - 1;
    public double Weight => Placement.Unit.Weight;
    public string Load => Placement.Unit.CanReceive
        ? $"{Placement.LoadAbove:N1} / {Placement.Unit.MaxLoadOnTop:N1}{(Placement.Unit.MaxLoadAssumed ? " (estimée)" : "")}"
        : "–";
    public string Supports => Placement.OnFloor ? "Plancher" : string.Join(", ", Placement.Supports.Select(s => s.Support.Unit.Id));
    /// <summary>Étapes retenues par le rangement (chargement → livraison).</summary>
    public string Stop => Placement.Unit.Departure > 0 || Placement.Unit.Arrival > 0
        ? $"{(Placement.Unit.Departure > 0 ? Placement.Unit.Departure.ToString() : "–")} → {(Placement.Unit.Arrival > 0 ? Placement.Unit.Arrival.ToString() : "–")}"
        : "–";

    public string? Zone => Placement.Unit.GroupKey;
}

/// <summary>Étape du détail du calcul, avec ses blocs repliables (ouverts par défaut).</summary>
public sealed class DetailSectionViewModel(OptiTunes.Core.Export.DetailSection section)
{
    public OptiTunes.Core.Export.DetailSection Section { get; } = section;
    public int Number => Section.Number;
    public string Title => Section.Title;
    public string Purpose => Section.Purpose;
    public IReadOnlyList<OptiTunes.Core.Export.DetailLine> Intro => Section.Intro;
    public IReadOnlyList<DetailBlockViewModel> Blocks { get; } = section.Blocks.Select(b => new DetailBlockViewModel(b)).ToList();
    public OptiTunes.Core.Export.DetailTable? Table => Section.Table;
    public bool HasTable => Section.HasTable;
}

public sealed partial class DetailBlockViewModel(OptiTunes.Core.Export.DetailBlock block) : ObservableObject
{
    public string Title => block.Title;
    public IReadOnlyList<OptiTunes.Core.Export.DetailLine> Lines => block.Lines;

    [ObservableProperty] private bool _isExpanded = true;
}

public sealed record IssueRow(string Source, IssueSeverity Severity, string Label, string Message, int? Line);

public sealed class RecentFileViewModel(string path)
{
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
    public bool Exists => System.IO.File.Exists(Path);
}

public sealed record VehicleChoice(string Label, Vehicle? Vehicle)
{
    public override string ToString() => Label;
}

public sealed record ColorModeChoice(ColorMode Mode, string Label)
{
    public override string ToString() => Label;
}

public sealed record StowageChoice(StowageMode Mode, string Label, string Description)
{
    public override string ToString() => Label;
}

/// <summary>Une solution de chargement proposée (recommandée ou alternative).</summary>
public sealed class SolutionViewModel(LoadPlan plan, ValidationReport report)
{
    public LoadPlan Plan { get; } = plan;
    public ValidationReport Report { get; } = report;

    public string Title => Plan.IsRecommended ? $"★ Recommandée · {Plan.Name}" : Plan.Name;
    public bool IsRecommended => Plan.IsRecommended;
    public bool IsValid => Report.IsValid;

    public string Summary =>
        $"{Plan.Loads.Count} camion{(Plan.Loads.Count > 1 ? "s" : "")} · {Plan.Metrics.ItemsPlaced:N0}/{Plan.Metrics.ItemsTotal:N0} art. · " +
        $"ML {Plan.Metrics.LinearMetersReal:N2} m" + (Plan.Metrics.ItemsRemaining > 0 ? $" · reliquat {Plan.Metrics.ItemsRemaining:N0}" : "");

    // Colonnes de l'onglet « Solutions »
    public int Trucks => Plan.Loads.Count;
    public string Items => $"{Plan.Metrics.ItemsPlaced:N0} / {Plan.Metrics.ItemsTotal:N0}";
    public int Remaining => Plan.Metrics.ItemsRemaining;
    public double MlReal => Plan.Metrics.LinearMetersReal;
    public double MaxLoadMl => Plan.Loads.Count == 0 ? 0 : Plan.Loads.Max(l => l.Metrics.LinearMetersReal);
    public double WeightRate => Plan.Metrics.WeightRate;
    public int SplitOrders => Plan.Groupage.Orders.Count(o => Plan.VehiclesOf(o).Count > 1);
    public int IncompleteOrders => Plan.Groupage.Orders.Count(o => Plan.RemainingItems(o) > 0);
    public string Conformity => !Report.IsValid ? "Non conforme" : Report.Warnings > 0 ? $"Conforme · {Report.Warnings} attention" : "Conforme";
    public CheckStatus StatusLevel => !Report.IsValid ? CheckStatus.Error : Report.Warnings > 0 ? CheckStatus.Warning : CheckStatus.Ok;

    public override string ToString() => $"{Title} – {Summary}";
}

/// <summary>Un camion de la solution affichée.</summary>
public sealed class LoadViewModel(VehicleLoad load)
{
    public VehicleLoad Load { get; } = load;
    public string Label => Load.Label;
    public string Summary => $"{Load.Metrics.LinearRealRate:0} %";

    public string Details => string.Join(Environment.NewLine,
        $"{Load.Placements.Count} unités · {Load.Metrics.ItemsPlaced:N0} articles",
        $"ML réel {Load.Metrics.LinearMetersReal:N2} m ({Load.Metrics.LinearRealRate:0} %)",
        $"Poids {Load.Metrics.LoadedWeightKg:N0} kg ({Load.Metrics.WeightRate:0} %)");
}

/// <summary>Entrée de légende cliquable (met le groupe en évidence dans les vues).</summary>
public sealed partial class LegendItemViewModel(LegendEntry entry) : ObservableObject
{
    public LegendEntry Entry { get; } = entry;
    public string Label => Entry.Label;
    public SolidColorBrush Brush { get; } = new(entry.Color);

    [ObservableProperty] private bool _isActive;
}
