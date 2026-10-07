using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OptiTunes.Core.Export;

namespace OptiTunes.App.Views.Controls;

/// <summary>
/// Tableau de chiffres du détail du calcul : en-tête foncé, lignes alternées, nombres alignés à droite.
/// Construit en Grid (pas de DataGrid) pour ne pas capturer la molette de la page qui le contient.
/// </summary>
public sealed partial class DetailTableView : ContentControl
{
    public static readonly DependencyProperty TableProperty = DependencyProperty.Register(
        nameof(Table), typeof(DetailTable), typeof(DetailTableView), new PropertyMetadata(null, (d, _) => ((DetailTableView)d).Rebuild()));

    private static readonly Brush HeaderBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)));
    private static readonly Brush AltBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xF6, 0xF8, 0xFA)));
    private static readonly Brush LineBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE3, 0xE8, 0xEC)));

    public DetailTable? Table
    {
        get => (DetailTable?)GetValue(TableProperty);
        set => SetValue(TableProperty, value);
    }

    [GeneratedRegex(@"^[\d\s  ,.\-–%★▶×]+$")]
    private static partial Regex NumericCell();

    private void Rebuild()
    {
        if (Table is not { Rows.Count: > 0 } table)
        {
            Content = null;
            return;
        }

        var grid = new Grid();
        for (var c = 0; c < table.Headers.Count; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var r = 0; r <= table.Rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var band = new Border
            {
                Background = r == 0 ? HeaderBrush : r % 2 == 0 ? AltBrush : Brushes.White,
                BorderBrush = LineBrush,
                BorderThickness = new Thickness(0, 0, 0, r == 0 ? 0 : 1)
            };
            Grid.SetRow(band, r);
            Grid.SetColumnSpan(band, table.Headers.Count + 1);
            grid.Children.Add(band);
        }

        for (var c = 0; c < table.Headers.Count; c++)
        {
            Add(grid, 0, c, table.Headers[c], header: true, numeric: false);
        }

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            for (var c = 0; c < table.Headers.Count && c < row.Count; c++)
            {
                Add(grid, r + 1, c, row[c], header: false, numeric: c > 0 && NumericCell().IsMatch(row[c]));
            }
        }

        Content = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = grid,
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    private static void Add(Grid grid, int row, int column, string text, bool header, bool numeric)
    {
        var block = new TextBlock
        {
            Text = text,
            Padding = new Thickness(10, header ? 6 : 5, 10, header ? 6 : 5),
            FontSize = header ? 11.5 : 12.5,
            FontWeight = header || column == 0 ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = header ? Brushes.White : (Brush)Application.Current.FindResource("TextBrush"),
            TextAlignment = numeric ? TextAlignment.Right : TextAlignment.Left,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 320
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private static Brush Frozen(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
