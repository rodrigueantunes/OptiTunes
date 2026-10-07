using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using OptiTunes.App.Services;
using OptiTunes.App.ViewModels;

namespace OptiTunes.App.Views;

/// <summary>
/// Code-behind limité à ce qui est propre à la vue : caméra 3D, tests d'intersection souris (clic / survol),
/// position de l'infobulle 3D et glisser-déposer de fichier.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(new DialogService(), new PrintService(), new SettingsService(Environment.GetEnvironmentVariable("OPTITUNES_SETTINGS")));
        DataContext = _vm;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += (_, _) =>
        {
            // « Ouvrir avec » / glisser sur l'exe : le fichier est passé en argument.
            var file = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(File.Exists);
            if (file != null)
            {
                _vm.LoadFile(file);
            }
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.CurrentLoad):
                Dispatcher.BeginInvoke(SetIsoCamera, DispatcherPriority.Background);
                break;
            case nameof(MainViewModel.HoverText):
                HoverCard.Visibility = _vm.HoverText == null ? Visibility.Collapsed : Visibility.Visible;
                break;
        }
    }

    private GeometryModel3D? HitModel(Point position)
    {
        GeometryModel3D? hit = null;
        VisualTreeHelper.HitTest(Viewport.Viewport, null, result =>
        {
            if (result is RayMeshGeometry3DHitTestResult mesh && mesh.ModelHit is GeometryModel3D model)
            {
                hit = model;
                return HitTestResultBehavior.Stop;
            }

            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(position));
        return hit;
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _vm.SelectFromModel(HitModel(e.GetPosition(Viewport.Viewport)));

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.RightButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed)
        {
            _vm.HoverFromModel(null);
            return;
        }

        var position = e.GetPosition(Viewport.Viewport);
        _vm.HoverFromModel(HitModel(position));
        if (_vm.HoverText == null)
        {
            return;
        }

        // Infobulle à droite du curseur, recadrée pour rester dans la zone 3D.
        HoverCard.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = HoverCard.DesiredSize;
        var host = Viewport.ActualWidth > 0 ? new Size(Viewport.ActualWidth, Viewport.ActualHeight) : RenderSize;
        var x = position.X + 18 + size.Width > host.Width ? position.X - size.Width - 12 : position.X + 18;
        var y = Math.Min(Math.Max(4, position.Y - 10), host.Height - size.Height - 4);
        Canvas.SetLeft(HoverCard, Math.Max(4, x));
        Canvas.SetTop(HoverCard, Math.Max(4, y));
    }

    private void Viewport_MouseLeave(object sender, MouseEventArgs e) => _vm.HoverFromModel(null);

    /// <summary>Ctrl+F : place le curseur dans la recherche (focus = affaire de vue).</summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            DropOverlay.Visibility = Visibility.Visible;
        }
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        // DragLeave est aussi levé en passant d'un contrôle enfant à l'autre : on vérifie la position.
        var position = e.GetPosition(this);
        if (position.X <= 0 || position.Y <= 0 || position.X >= ActualWidth || position.Y >= ActualHeight)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files && File.Exists(files[0]))
        {
            _vm.LoadFile(files[0]);
        }
    }

    private (double L, double W, double H) VehicleSize =>
        _vm.Vehicle is { } v ? (v.Length / 1000, v.Width / 1000, v.Height / 1000) : (13.6, 2.45, 2.7);

    private void SetCamera(Point3D position, Point3D target, Vector3D up)
    {
        if (Viewport.Camera is not { } camera)
        {
            return;
        }

        camera.Position = position;
        camera.LookDirection = target - position;
        camera.UpDirection = up;
    }

    private void SetIsoCamera()
    {
        var (l, w, h) = VehicleSize;
        var target = new Point3D(l / 2, w / 2, h / 3);
        var d = Math.Max(l, 6);
        SetCamera(new Point3D(l * 1.05, w / 2 - d * 0.9, h + d * 0.55), target, new Vector3D(0, 0, 1));
        Viewport.ZoomExtents(300);
    }

    private void CameraIso_Click(object sender, RoutedEventArgs e) => SetIsoCamera();

    private void CameraTop_Click(object sender, RoutedEventArgs e)
    {
        var (l, w, _) = VehicleSize;
        SetCamera(new Point3D(l / 2, w / 2, l * 1.2), new Point3D(l / 2, w / 2, 0), new Vector3D(0, 1, 0));
        Viewport.ZoomExtents(300);
    }

    private void CameraSide_Click(object sender, RoutedEventArgs e)
    {
        var (l, w, h) = VehicleSize;
        SetCamera(new Point3D(l / 2, -l * 1.2, h / 2), new Point3D(l / 2, w / 2, h / 2), new Vector3D(0, 0, 1));
        Viewport.ZoomExtents(300);
    }

    private void CameraRear_Click(object sender, RoutedEventArgs e)
    {
        var (l, w, h) = VehicleSize;
        SetCamera(new Point3D(l + 8, w / 2, h * 0.6), new Point3D(l / 2, w / 2, h / 2), new Vector3D(0, 0, 1));
        Viewport.ZoomExtents(300);
    }

    private void ZoomExtents_Click(object sender, RoutedEventArgs e) => Viewport.ZoomExtents(300);

    /// <summary>Sommaire du détail du calcul : amène l'étape choisie en haut de la page.</summary>
    private void CalculationToc_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CalculationToc.SelectedItem is not { } section ||
            CalculationItems.ItemContainerGenerator.ContainerFromItem(section) is not FrameworkElement element)
        {
            return;
        }

        var top = element.TransformToAncestor(CalculationScroll).Transform(new Point(0, 0)).Y;
        CalculationScroll.ScrollToVerticalOffset(CalculationScroll.VerticalOffset + top);
    }
}
