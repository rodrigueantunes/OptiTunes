using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OptiTunes.App.Views.Controls;
using OptiTunes.Core.Models;

namespace OptiTunes.App.Services;

public interface IPrintService
{
    /// <summary>Imprime une feuille de chargement par camion (« Microsoft Print to PDF » pour un PDF).</summary>
    bool Print(LoadPlan plan, IReadOnlyDictionary<string, Color> colors);
}

public sealed class PrintService : IPrintService
{
    private static readonly Brush Dark = new SolidColorBrush(Color.FromRgb(0x34, 0x49, 0x5E));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));

    public bool Print(LoadPlan plan, IReadOnlyDictionary<string, Color> colors)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        var doc = BuildDocument(plan, colors, dialog.PrintableAreaWidth);
        dialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, $"OptiTunes – {plan.Groupage.Id}");
        return true;
    }

    public static FlowDocument BuildDocument(LoadPlan plan, IReadOnlyDictionary<string, Color> colors, double pageWidth)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10.5,
            PagePadding = new Thickness(36),
            PageWidth = pageWidth,
            ColumnWidth = pageWidth
        };
        var contentWidth = pageWidth - 72;

        foreach (var load in plan.Loads)
        {
            var title = new Paragraph(new Run($"Plan de chargement · {plan.Groupage.Id} · {load.Label} / {plan.Loads.Count}"))
            {
                FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Dark, Margin = new Thickness(0, 0, 0, 2),
                BreakPageBefore = load.Number > 1
            };
            doc.Blocks.Add(title);

            var v = load.Vehicle;
            var m = load.Metrics;
            doc.Blocks.Add(new Paragraph(new Run(
                $"{v.Id} · {v.Length:N0} × {v.Width:N0} × {v.Height:N0} mm · {v.MaxPayload:N0} kg" +
                (plan.Groupage.Date is { } d ? $" · {d:dd/MM/yyyy}" : "") +
                (plan.Groupage.Carrier != null ? $" · {plan.Groupage.Carrier}" : "") +
                $" · solution : {plan.Name}" +
                (plan.Options.HasClearances
                    ? $" · débords : unités {plan.Options.GapBetweenUnits:N0} / parois {plan.Options.SideClearance:N0} / plafond {plan.Options.RoofClearance:N0} mm"
                    : "")))
            { Foreground = Muted, Margin = new Thickness(0, 0, 0, 8) });

            doc.Blocks.Add(KpiTable(m));
            doc.Blocks.Add(Caption("Vue de dessus (cabine à gauche, porte à droite)"));
            doc.Blocks.Add(new BlockUIContainer(Render(load, colors, PlanViewMode.Top, contentWidth, contentWidth * 0.26, plan.Options)));
            doc.Blocks.Add(Caption("Vue de côté"));
            doc.Blocks.Add(new BlockUIContainer(Render(load, colors, PlanViewMode.Side, contentWidth, contentWidth * 0.26, plan.Options)));
            doc.Blocks.Add(Caption("Séquence de chargement (1 = premier chargé, au fond)"));
            doc.Blocks.Add(SequenceTable(load));
        }

        if (plan.Unloaded.Count > 0)
        {
            doc.Blocks.Add(new Paragraph(new Run("Reliquat non chargé"))
            {
                FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.Firebrick, BreakPageBefore = true
            });
            var table = NewTable([1.2, 2, 1, 4]);
            AddRow(table, true, "Ordre", "Article", "Articles", "Motif");
            foreach (var g in plan.Unloaded.GroupBy(u => (u.Unit.Order, u.Reason)))
            {
                AddRow(table, false, g.Key.Order.Id, g.Key.Order.Article, g.Sum(u => u.Unit.ItemCount).ToString("N0"), g.Key.Reason.Label());
            }

            doc.Blocks.Add(table);
        }

        return doc;
    }

    private static Paragraph Caption(string text) =>
        new(new Run(text)) { FontWeight = FontWeights.SemiBold, Foreground = Dark, Margin = new Thickness(0, 10, 0, 4) };

    private static Table KpiTable(PlanMetrics m)
    {
        var table = NewTable([1, 1, 1, 1, 1, 1]);
        AddRow(table, true, "Poids", "Volume", "Surface au sol", "ML réel", "Unités", "Articles");
        AddRow(table, false,
            $"{m.LoadedWeightKg:N0} kg ({m.WeightRate:0} %)",
            $"{m.LoadedVolumeM3:N2} m³ ({m.VolumeRate:0} %)",
            $"{m.FloorUsedM2:N2} m² ({m.FloorRate:0} %)",
            $"{m.LinearMetersReal:N2} m ({m.LinearRealRate:0} %)",
            m.UnitsPlaced.ToString("N0"),
            m.ItemsPlaced.ToString("N0"));
        return table;
    }

    private static Table SequenceTable(VehicleLoad load)
    {
        var table = NewTable([0.5, 1.4, 1.8, 1.4, 1.6, 0.6, 1.6, 1.6, 0.5, 0.9]);
        AddRow(table, true, "N°", "Ordre", "Article", "Commande", "Client", "Étapes", "Position X/Y/Z", "Encombrement", "Niv. (0 = sol)", "Poids kg");
        foreach (var p in load.Placements.OrderBy(p => p.Sequence))
        {
            var o = p.Unit.Order;
            AddRow(table, false, p.Sequence.ToString(), o.Id, o.Article, o.Reference, o.Customer ?? "",
                p.Unit.Departure > 0 || p.Unit.Arrival > 0 ? $"{p.Unit.Departure} → {p.Unit.Arrival}" : "",
                $"{p.X:0} / {p.Y:0} / {p.Z:0}", $"{p.DX:0}×{p.DY:0}×{p.DZ:0}", (p.Level - 1).ToString(), p.Unit.Weight.ToString("N1"));
        }

        return table;
    }

    private static Table NewTable(double[] widths)
    {
        var table = new Table { CellSpacing = 0, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0.5) };
        foreach (var w in widths)
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(w, GridUnitType.Star) });
        }

        table.RowGroups.Add(new TableRowGroup());
        return table;
    }

    private static void AddRow(Table table, bool header, params string[] cells)
    {
        var row = new TableRow { Background = header ? Dark : Brushes.White };
        foreach (var text in cells)
        {
            row.Cells.Add(new TableCell(new Paragraph(new Run(text))
            {
                Foreground = header ? Brushes.White : Brushes.Black,
                FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
                Margin = new Thickness(4, 2, 4, 2)
            })
            { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 0, 0, 0.5) });
        }

        table.RowGroups[0].Rows.Add(row);
    }

    private static Image Render(VehicleLoad load, IReadOnlyDictionary<string, Color> colors, PlanViewMode mode, double width, double height,
        PackingOptions options)
    {
        var view = new PlanView2D { Load = load, Mode = mode, Colors = colors, VisibleCount = int.MaxValue, Options = options };
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)(width * 2), (int)(height * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(view);
        bitmap.Freeze();
        return new Image { Source = bitmap, Width = width, Height = height };
    }
}
