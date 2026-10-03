using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OptiTunes.App.Services;
using OptiTunes.Core.Models;

namespace OptiTunes.App.Views.Controls;

public enum PlanViewMode
{
    Top,
    Side,
    Rear
}

/// <summary>
/// Vue 2D cotée d'un camion : dessus (X × Y), côté (X × Z) ou arrière depuis la porte (Y × Z).
/// Cabine à gauche, porte à droite. Clic = sélection, survol = infobulle.
/// </summary>
public sealed class PlanView2D : FrameworkElement
{
    public static readonly DependencyProperty LoadProperty = DependencyProperty.Register(
        nameof(Load), typeof(VehicleLoad), typeof(PlanView2D), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(PlanViewMode), typeof(PlanView2D), new FrameworkPropertyMetadata(PlanViewMode.Top, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ColorsProperty = DependencyProperty.Register(
        nameof(Colors), typeof(IReadOnlyDictionary<string, Color>), typeof(PlanView2D), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighlightOrderIdsProperty = DependencyProperty.Register(
        nameof(HighlightOrderIds), typeof(IReadOnlySet<string>), typeof(PlanView2D), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty VisibleCountProperty = DependencyProperty.Register(
        nameof(VisibleCount), typeof(int), typeof(PlanView2D), new FrameworkPropertyMetadata(int.MaxValue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(PackingOptions), typeof(PlanView2D), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Options du calcul : les débords sont tracés comme zones interdites.</summary>
    public PackingOptions? Options
    {
        get => (PackingOptions?)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    private static readonly Brush ClearanceFill = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0xF3, 0x9C, 0x12)));
    private static readonly Pen ClearancePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)), 1) { DashStyle = DashStyles.Dash });

    public static readonly DependencyProperty SelectedUnitIdProperty = DependencyProperty.Register(
        nameof(SelectedUnitId), typeof(string), typeof(PlanView2D),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private readonly List<(Rect Rect, Placement Placement)> _hitAreas = [];
    private Placement? _hovered;

    public VehicleLoad? Load
    {
        get => (VehicleLoad?)GetValue(LoadProperty);
        set => SetValue(LoadProperty, value);
    }

    public PlanViewMode Mode
    {
        get => (PlanViewMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public IReadOnlyDictionary<string, Color>? Colors
    {
        get => (IReadOnlyDictionary<string, Color>?)GetValue(ColorsProperty);
        set => SetValue(ColorsProperty, value);
    }

    public IReadOnlySet<string>? HighlightOrderIds
    {
        get => (IReadOnlySet<string>?)GetValue(HighlightOrderIdsProperty);
        set => SetValue(HighlightOrderIdsProperty, value);
    }

    public int VisibleCount
    {
        get => (int)GetValue(VisibleCountProperty);
        set => SetValue(VisibleCountProperty, value);
    }

    public string? SelectedUnitId
    {
        get => (string?)GetValue(SelectedUnitIdProperty);
        set => SetValue(SelectedUnitIdProperty, value);
    }

    private static readonly Brush VehicleFill = Frozen(new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xF7)));
    private static readonly Pen VehiclePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x34, 0x49, 0x5E)), 2));
    private static readonly Pen GridPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0xE4, 0xE6)), 1));
    private static readonly Pen DoorPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)), 5));
    private static readonly Pen SelectionPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xC3, 0x00)), 3));
    private static readonly Pen HoverPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x1B, 0x4F, 0x72)), 2));
    private static readonly Pen MlPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)), 1.5) { DashStyle = DashStyles.Dash });
    private static readonly Brush CabBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x34, 0x49, 0x5E)));
    private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D)));
    private static readonly Brush DarkText = Frozen(new SolidColorBrush(Color.FromRgb(0x1B, 0x26, 0x31)));
    private static readonly Typeface Font = new("Segoe UI");
    private static readonly Typeface FontBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    protected override void OnRender(DrawingContext dc)
    {
        _hitAreas.Clear();
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        var load = Load;
        if (load == null || ActualWidth < 50 || ActualHeight < 50)
        {
            return;
        }

        var v = load.Vehicle;
        var (worldW, worldH) = Mode switch
        {
            PlanViewMode.Top => (v.Length, v.Width),
            PlanViewMode.Side => (v.Length, v.Height),
            _ => (v.Width, v.Height)
        };

        const double marginLeft = 50, marginRight = 20, marginTop = 26, marginBottom = 32;
        var cab = Mode == PlanViewMode.Rear ? 0 : 36;
        var scale = Math.Min((ActualWidth - marginLeft - marginRight - cab) / worldW,
                             (ActualHeight - marginTop - marginBottom) / worldH);
        if (scale <= 0)
        {
            return;
        }

        var ox = marginLeft + cab;
        var oy = marginTop;
        var flipY = Mode != PlanViewMode.Top;

        Rect ToScreen(double u0, double v0, double du, double dv)
        {
            var x = ox + u0 * scale;
            var y = flipY ? oy + (worldH - v0 - dv) * scale : oy + v0 * scale;
            return new Rect(x, y, Math.Max(1, du * scale), Math.Max(1, dv * scale));
        }

        var vehicleRect = ToScreen(0, 0, worldW, worldH);
        dc.DrawRectangle(VehicleFill, null, vehicleRect);

        // Zones de débord (interdites) : bandes le long des parois et sous le plafond.
        if (Options is { } o)
        {
            void Band(double u0, double v0, double du, double dv)
            {
                if (du > 0 && dv > 0)
                {
                    dc.DrawRectangle(ClearanceFill, ClearancePen, ToScreen(u0, v0, du, dv));
                }
            }

            var side = o.SideClearance;
            var roof = o.RoofClearance;
            switch (Mode)
            {
                case PlanViewMode.Top:
                    Band(0, 0, worldW, side);
                    Band(0, worldH - side, worldW, side);
                    break;
                case PlanViewMode.Side:
                    Band(0, worldH - roof, worldW, roof);
                    break;
                default:
                    Band(0, 0, side, worldH);
                    Band(worldW - side, 0, side, worldH);
                    Band(side, worldH - roof, worldW - 2 * side, roof);
                    break;
            }
        }

        var step = Mode == PlanViewMode.Rear ? 500 : 1000;
        for (double u = step; u < worldW; u += step)
        {
            var x = ox + u * scale;
            dc.DrawLine(GridPen, new Point(x, vehicleRect.Top), new Point(x, vehicleRect.Bottom));
            DrawText(dc, Mode == PlanViewMode.Rear ? $"{u:0}" : $"{u / 1000:0}", new Point(x, vehicleRect.Bottom + 4), 11, LabelBrush, center: true);
        }

        DrawText(dc, Mode == PlanViewMode.Rear ? "Y (mm)" : "X (m)", new Point(vehicleRect.Right - 34, vehicleRect.Bottom + 17), 11, LabelBrush);
        DrawText(dc, $"{worldH:0}", new Point(4, vehicleRect.Top), 11, LabelBrush);

        if (Mode != PlanViewMode.Rear)
        {
            var cabRect = new Rect(marginLeft, vehicleRect.Top + (flipY ? vehicleRect.Height * 0.15 : vehicleRect.Height * 0.08),
                cab - 6, vehicleRect.Height * (flipY ? 0.85 : 0.84));
            dc.DrawRoundedRectangle(CabBrush, null, cabRect, 6, 6);
            DrawText(dc, "AV", new Point(cabRect.Left + cabRect.Width / 2, cabRect.Top + cabRect.Height / 2 - 8), 11, Brushes.White, center: true, bold: true);
            var doorH = Mode == PlanViewMode.Top ? v.DoorWidth ?? v.Width : v.DoorHeight ?? v.Height;
            var door = ToScreen(worldW, Mode == PlanViewMode.Top ? (v.Width - doorH) / 2 : 0, 0, doorH);
            dc.DrawLine(DoorPen, door.TopLeft, door.BottomLeft);
        }

        var placements = load.Placements.Where(p => p.Sequence <= VisibleCount);
        placements = Mode switch
        {
            PlanViewMode.Top => placements.OrderBy(p => p.MaxZ).ThenBy(p => p.Z),
            PlanViewMode.Side => placements.OrderByDescending(p => p.MaxY),
            _ => placements.OrderBy(p => p.MaxX)
        };

        var colors = Colors;
        var highlight = HighlightOrderIds;
        Rect? selectedRect = null;
        Rect? hoveredRect = null;

        foreach (var p in placements)
        {
            var r = Mode switch
            {
                PlanViewMode.Top => ToScreen(p.X, p.Y, p.DX, p.DY),
                PlanViewMode.Side => ToScreen(p.X, p.Z, p.DX, p.DZ),
                _ => ToScreen(p.Y, p.Z, p.DY, p.DZ)
            };

            var color = colors != null && colors.TryGetValue(p.Unit.Order.Id, out var c) ? c : System.Windows.Media.Colors.SteelBlue;
            var faded = highlight != null && !highlight.Contains(p.Unit.Order.Id);
            if (faded)
            {
                color = OrderPalette.Fade(color);
            }

            var fill = new SolidColorBrush(color);
            fill.Freeze();
            var pen = new Pen(new SolidColorBrush(OrderPalette.Darken(color)), 1);
            pen.Freeze();

            if (p.Unit.Shape == UnitShape.Cylinder && IsAxisPerpendicularToView(p))
            {
                dc.DrawEllipse(fill, pen, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
            }
            else if (p.Unit.Shape == UnitShape.Staggered && IsAxisPerpendicularToView(p))
            {
                var radius = p.Unit.TubeDiameter / 2 * scale;
                foreach (var (cx, cy, cz) in p.TubeCentersWorld())
                {
                    var (u, w) = Mode == PlanViewMode.Side ? (cx, cz) : (cy, cz);
                    var center = ToScreen(u, w, 0, 0).TopLeft;
                    dc.DrawEllipse(fill, pen, center, radius, radius);
                }
            }
            else
            {
                dc.DrawRectangle(fill, pen, r);
            }

            if (!faded && r.Width > 40 && r.Height > 16)
            {
                var label = Mode == PlanViewMode.Top && p.Level > 1 ? $"{p.Unit.Order.Article} ×{p.Level}" : p.Unit.Order.Article;
                DrawText(dc, label, new Point(r.X + 3, r.Y + 2), 10.5, DarkText, maxWidth: r.Width - 6);
            }

            _hitAreas.Add((r, p));
            if (p.Unit.Id == SelectedUnitId)
            {
                selectedRect = r;
            }

            if (p == _hovered)
            {
                hoveredRect = r;
            }
        }

        dc.DrawRectangle(null, VehiclePen, vehicleRect);

        if (hoveredRect is { } hr)
        {
            dc.DrawRectangle(null, HoverPen, hr);
        }

        if (selectedRect is { } sr)
        {
            dc.DrawRectangle(null, SelectionPen, sr);
        }

        var m = load.Metrics;
        if (Mode != PlanViewMode.Rear && m.LinearMetersReal > 0)
        {
            var x = ox + load.Placements.Where(p => p.OnFloor).Select(p => p.MaxX).DefaultIfEmpty(0).Max() * scale;
            dc.DrawLine(MlPen, new Point(x, vehicleRect.Top - 6), new Point(x, vehicleRect.Bottom));
            var label = $"ML réel {m.LinearMetersReal:0.00} m";
            var atLeft = x + 100 > ActualWidth;
            DrawText(dc, label, new Point(atLeft ? x - 98 : x + 4, vehicleRect.Top - 21), 11, MlPen.Brush, bold: true);
        }

        if (m.LoadedWeightKg > 0)
        {
            var (cu, cv) = Mode switch
            {
                PlanViewMode.Top => (m.CgX, m.CgY),
                PlanViewMode.Side => (m.CgX, m.CgZ),
                _ => (m.CgY, m.CgZ)
            };
            var cg = ToScreen(cu, cv, 0, 0);
            var cgPen = new Pen(Brushes.Black, 2);
            dc.DrawEllipse(Brushes.White, cgPen, cg.TopLeft, 6, 6);
            dc.DrawLine(cgPen, new Point(cg.X - 6, cg.Y), new Point(cg.X + 6, cg.Y));
            dc.DrawLine(cgPen, new Point(cg.X, cg.Y - 6), new Point(cg.X, cg.Y + 6));
        }
    }

    private bool IsAxisPerpendicularToView(Placement p) => Mode switch
    {
        PlanViewMode.Top => p.CylinderAxis == 2,
        PlanViewMode.Side => p.CylinderAxis == 1,
        _ => p.CylinderAxis == 0
    };

    private void DrawText(DrawingContext dc, string text, Point at, double size, Brush brush, bool center = false,
        double maxWidth = 0, bool bold = false)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold ? FontBold : Font, size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (maxWidth > 0)
        {
            ft.MaxTextWidth = maxWidth;
            ft.MaxLineCount = 1;
            ft.Trimming = TextTrimming.CharacterEllipsis;
        }

        dc.DrawText(ft, center ? new Point(at.X - ft.Width / 2, at.Y) : at);
    }

    private Placement? HitTest(Point pos)
    {
        for (var i = _hitAreas.Count - 1; i >= 0; i--)
        {
            if (_hitAreas[i].Rect.Contains(pos))
            {
                return _hitAreas[i].Placement;
            }
        }

        return null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (HitTest(e.GetPosition(this)) is { } p)
        {
            SelectedUnitId = p.Unit.Id;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTest(e.GetPosition(this));
        if (hit == _hovered)
        {
            return;
        }

        _hovered = hit;
        Cursor = hit != null ? Cursors.Hand : null;
        ToolTip = hit == null ? null : UnitInfo.Describe(hit);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = null;
        ToolTip = null;
        InvalidateVisual();
    }
}
