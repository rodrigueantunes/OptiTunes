using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiTunes.App.Services;
using OptiTunes.Core.Engine;
using OptiTunes.Core.Export;
using OptiTunes.Core.Import;
using OptiTunes.Core.Models;
using OptiTunes.Core.Validation;

namespace OptiTunes.App.ViewModels;

public sealed record SampleFile(string Name, string Path);

public partial class MainViewModel : ObservableObject
{
    private const string CsvFilter = "Fichiers à plat (*.csv;*.txt)|*.csv;*.txt|Tous les fichiers (*.*)|*.*";

    private readonly IDialogService _dialogs;
    private readonly IPrintService _printer;
    private readonly ISettingsService _settings;
    private readonly FileWatcher _watcher;
    private readonly DispatcherTimer _reloadDelay = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private string? _currentPath;
    private readonly HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase);
    private readonly FlatFileImporter _importer = new();
    private readonly LoadOptimizer _optimizer = new();
    private readonly DispatcherTimer _animation = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Model3DGroup _sceneRoot = new();
    private readonly Model3DGroup _unitsGroup = new();
    private List<(Placement Placement, GeometryModel3D Model)> _unitModels = [];
    private SceneResult? _scene;
    private List<Issue> _importIssues = [];
    private string? _hoverId;
    private bool _syncingSelection;
    private int _optimizeVersion;

    public MainViewModel(IDialogService dialogs, IPrintService printer, ISettingsService settings)
    {
        _dialogs = dialogs;
        _printer = printer;
        _settings = settings;
        _autoReload = settings.Current.AutoReload;
        var saved = settings.Current;
        _minSupportPercent = saved.MinSupportPercent;
        _respectDeliveryOrder = saved.RespectDeliveryOrder;
        _multiStrategy = saved.MultiStrategy;
        _multiVehicle = saved.MultiVehicle;
        _maxVehicles = saved.MaxVehicles;
        _allowFloorRotation = saved.AllowFloorRotation;
        _staggerTubes = saved.StaggerTubes;
        _gapBetweenUnits = saved.GapBetweenUnits;
        _sideClearance = saved.SideClearance;
        _roofClearance = saved.RoofClearance;
        Settings = new SettingsViewModel(settings);
        Settings.Saved += () =>
        {
            RebuildVehicleChoices();
            ShowToast("Paramètres enregistrés : liste des véhicules mise à jour.", IssueSeverity.Info);
            _ = OptimizeAsync();
        };
        var dispatcher = Dispatcher.CurrentDispatcher;
        _watcher = new FileWatcher(() => dispatcher.BeginInvoke(() =>
        {
            _reloadDelay.Stop();
            _reloadDelay.Start();
        }));
        _reloadDelay.Tick += (_, _) =>
        {
            _reloadDelay.Stop();
            if (AutoReload && _currentPath != null && File.Exists(_currentPath))
            {
                LoadFile(_currentPath, reload: true);
            }
        };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };
        RefreshRecent();
        _animation.Tick += (_, _) =>
        {
            if (VisibleCount >= MaxVisible)
            {
                _animation.Stop();
                IsAnimating = false;
                return;
            }

            VisibleCount++;
        };
        _searchDelay.Tick += (_, _) =>
        {
            _searchDelay.Stop();
            ApplySearch();
        };

        var samples = Path.Combine(AppContext.BaseDirectory, "Samples");
        if (Directory.Exists(samples))
        {
            foreach (var file in Directory.GetFiles(samples, "*.csv").OrderBy(f => f))
            {
                Samples.Add(new SampleFile(Path.GetFileNameWithoutExtension(file).Replace('_', ' '), file));
            }
        }

        _selectedVehicleChoice = new VehicleChoice("Véhicule du fichier", null);
        RebuildVehicleChoices();
        _selectedColorMode = ColorModes[0];
        _isReleaseNotesOpen = settings.Current.LastSeenVersion != VersionNumber;
    }

    // ---------- Nouveautés ----------

    public string VersionNumber => AppVersion.TrimStart('v');
    public IReadOnlyList<ReleaseNote> ReleaseNotesList => ReleaseNotes.All;

    [ObservableProperty] private bool _isReleaseNotesOpen;

    [RelayCommand]
    private void ShowReleaseNotes() => IsReleaseNotesOpen = true;

    [RelayCommand]
    private void CloseReleaseNotes()
    {
        IsReleaseNotesOpen = false;
        _settings.Current.LastSeenVersion = VersionNumber;
        _settings.Save();
    }

    /// <summary>Échap : ferme ce qui est ouvert, sinon efface la mise en évidence.</summary>
    [RelayCommand]
    private void Escape()
    {
        if (IsReleaseNotesOpen)
        {
            CloseReleaseNotes();
        }
        else if (IsFormatHelpOpen)
        {
            IsFormatHelpOpen = false;
        }
        else if (IsSettingsOpen)
        {
            IsSettingsOpen = false;
        }
        else
        {
            ClearHighlight();
        }
    }

    // ---------- Simulation : ordres exclus ----------

    public bool HasExclusions => _excluded.Count > 0;

    public string ExclusionText => _excluded.Count == 0
        ? ""
        : $"Simulation : {_excluded.Count} ordre(s) exclu(s) du calcul";

    private void OnOrderIncludedChanged(OrderRowViewModel row, bool included)
    {
        if (included)
        {
            _excluded.Remove(row.Id);
        }
        else
        {
            _excluded.Add(row.Id);
        }

        OnPropertyChanged(nameof(HasExclusions));
        OnPropertyChanged(nameof(ExclusionText));
        _ = OptimizeAsync();
    }

    [RelayCommand]
    private void IncludeAll()
    {
        if (_excluded.Count == 0)
        {
            return;
        }

        _excluded.Clear();
        OnPropertyChanged(nameof(HasExclusions));
        OnPropertyChanged(nameof(ExclusionText));
        _ = OptimizeAsync();
    }

    public SettingsViewModel Settings { get; }

    [ObservableProperty] private bool _isSettingsOpen;

    [RelayCommand]
    private void OpenSettings()
    {
        Settings.Reload();
        IsSettingsOpen = true;
    }

    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    /// <summary>« Véhicule du fichier » + la liste paramétrée, en conservant la sélection par nom.</summary>
    private void RebuildVehicleChoices()
    {
        var current = SelectedVehicleChoice?.Label;
        VehicleChoices.Clear();
        VehicleChoices.Add(new VehicleChoice("Véhicule du fichier", null));
        foreach (var v in _settings.Current.EffectiveVehicles())
        {
            VehicleChoices.Add(new VehicleChoice(v.Name, v.ToVehicle()));
        }

        var match = VehicleChoices.FirstOrDefault(c => c.Label == current) ?? VehicleChoices[0];
        if (!Equals(match, SelectedVehicleChoice))
        {
            _selectedVehicleChoice = match;
            OnPropertyChanged(nameof(SelectedVehicleChoice));
        }
    }

    private Vehicle? DefaultVehicle =>
        _settings.Current.DefaultVehicle is { } name
            ? _settings.Current.EffectiveVehicles().FirstOrDefault(v => v.Name == name)?.ToVehicle()
            : null;

    private void SaveOptions()
    {
        var s = _settings.Current;
        s.MinSupportPercent = MinSupportPercent;
        s.RespectDeliveryOrder = RespectDeliveryOrder;
        s.MultiStrategy = MultiStrategy;
        s.MultiVehicle = MultiVehicle;
        s.MaxVehicles = MaxVehicles;
        s.AllowFloorRotation = AllowFloorRotation;
        s.StaggerTubes = StaggerTubes;
        s.GapBetweenUnits = GapBetweenUnits;
        s.SideClearance = SideClearance;
        s.RoofClearance = RoofClearance;
        _settings.Save();
    }

    public string AppVersion { get; } =
        "v" + (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               .Split('+')[0] ?? "0.0.1");

    public ObservableCollection<SampleFile> Samples { get; } = [];
    public ObservableCollection<Groupage> Groupages { get; } = [];
    public ObservableCollection<VehicleChoice> VehicleChoices { get; } = [];

    public IReadOnlyList<ColorModeChoice> ColorModes { get; } =
    [
        new(ColorMode.Ordre, "Couleur par ordre"),
        new(ColorMode.Client, "Couleur par client"),
        new(ColorMode.Arret, "Couleur par étape de livraison"),
        new(ColorMode.Magasin, "Couleur par magasin de départ")
    ];

    public ObservableCollection<SolutionViewModel> Solutions { get; } = [];
    public ObservableCollection<LoadViewModel> Loads { get; } = [];
    public ObservableCollection<OrderRowViewModel> Orders { get; } = [];
    public ObservableCollection<PlacementRowViewModel> Units { get; } = [];
    public ObservableCollection<IssueRow> Issues { get; } = [];
    public ObservableCollection<ValidationCheck> Checks { get; } = [];
    public ObservableCollection<ScenarioResult> SelfTests { get; } = [];
    public ObservableCollection<StrategyOutcome> Strategies { get; } = [];
    public ObservableCollection<LegendItemViewModel> Legend { get; } = [];
    public ObservableCollection<RecentFileViewModel> Recent { get; } = [];
    public ObservableCollection<IssueRow> ImportIssues { get; } = [];
    public ObservableCollection<ColumnMapping> ImportColumns { get; } = [];

    [ObservableProperty] private string _sourceName = "Aucun fichier";
    [ObservableProperty] private Groupage? _selectedGroupage;
    [ObservableProperty] private VehicleChoice _selectedVehicleChoice;
    [ObservableProperty] private ColorModeChoice _selectedColorMode;
    [ObservableProperty] private SolutionViewModel? _selectedSolution;
    [ObservableProperty] private LoadViewModel? _selectedLoad;
    [ObservableProperty] private OrderRowViewModel? _selectedOrder;
    [ObservableProperty] private IReadOnlyDictionary<string, Color> _orderColors = new Dictionary<string, Color>();
    [ObservableProperty] private IReadOnlySet<string>? _highlightOrderIds;
    [ObservableProperty] private Model3D? _scene3D;
    [ObservableProperty] private int _visibleCount;
    [ObservableProperty] private int _maxVisible;
    [ObservableProperty] private bool _isAnimating;
    [ObservableProperty] private PlacementRowViewModel? _selectedUnit;
    [ObservableProperty] private string? _selectedUnitId;
    [ObservableProperty] private string? _hoverText;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Importez un groupage (bouton ou glisser-déposer du fichier) ou ouvrez un exemple.";
    [ObservableProperty] private string _selfTestSummary = "Non exécuté";
    [ObservableProperty] private int _minSupportPercent = 80;
    [ObservableProperty] private bool _respectDeliveryOrder = true;
    [ObservableProperty] private bool _multiStrategy = true;
    [ObservableProperty] private bool _multiVehicle = true;
    [ObservableProperty] private int _maxVehicles = 5;
    [ObservableProperty] private bool _allowFloorRotation = true;
    [ObservableProperty] private bool _staggerTubes = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ClearanceText))] private double _gapBetweenUnits;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ClearanceText))] private double _sideClearance;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ClearanceText))] private double _roofClearance;

    /// <summary>Rappel des débords dans l'en-tête (vide si aucun).</summary>
    // ---------- Gestion des arrêts / rangement ----------

    public IReadOnlyList<StowageChoice> StowageChoices { get; } =
    [
        new(StowageMode.Order, "Par ordre", "Calcul actuel : optimisation, accessibilité selon la colonne ARRET. Par défaut sans étapes."),
        new(StowageMode.Stops, "Par arrêts", "Étapes DEPART / ARRIVEE : chargé tôt au fond, livré tôt côté porte, aucune palette bloquée. Par défaut avec étapes."),
        new(StowageMode.Customer, "Par client", "Une zone continue par client (colonne CLIENT), de l'avant vers la porte ; pas de gerbage entre clients."),
        new(StowageMode.Warehouse, "Par magasin", "Une zone continue par magasin de départ (colonne MAGASIN)."),
        new(StowageMode.Command, "Par commande", "Lignes et cadences d'une même commande ensemble."),
        new(StowageMode.Compact, "Compact", "Densité maximale, sans contrainte d'accès (pour comparer).")
    ];

    [ObservableProperty] private StowageChoice? _selectedStowage;
    private bool _keepStowage;

    partial void OnSelectedStowageChanged(StowageChoice? value)
    {
        if (!_keepStowage)
        {
            _ = OptimizeAsync();
        }
    }

    /// <summary>Résumé de l'itinéraire : départs (magasin) puis livraisons (clients).</summary>
    public string ItineraryText
    {
        get
        {
            if (SelectedGroupage is not { } g || !Stowage.HasItinerary(g))
            {
                return "";
            }

            var departures = g.Orders.Where(o => o.DepartureStep > 0 || !string.IsNullOrWhiteSpace(o.Warehouse))
                .GroupBy(o => o.DepartureStep).OrderBy(x => x.Key)
                .Select(x => $"{(x.Key > 0 ? x.Key.ToString() : "départ")} {string.Join("/", x.Select(o => o.Warehouse).Where(w => !string.IsNullOrWhiteSpace(w)).Distinct())}");
            var arrivals = g.Orders.Where(o => o.EffectiveArrival > 0)
                .GroupBy(o => o.EffectiveArrival).OrderBy(x => x.Key)
                .Select(x => $"{x.Key} {string.Join("/", x.Select(o => o.Customer).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct())}");
            return $"Itinéraire · chargements : {string.Join(", ", departures)} · livraisons : {string.Join(", ", arrivals)}";
        }
    }

    public bool HasItinerary => ItineraryText.Length > 0;

    public string ClearanceText => GapBetweenUnits <= 0 && SideClearance <= 0 && RoofClearance <= 0
        ? ""
        : $"Débords : entre unités {GapBetweenUnits:N0} mm · parois {SideClearance:N0} mm · plafond {RoofClearance:N0} mm";

    public bool HasClearances => ClearanceText.Length > 0;
    [ObservableProperty] private bool _autoReload;
    [ObservableProperty] private bool _hasFile;
    [ObservableProperty] private ImportResult? _lastImport;
    [ObservableProperty] private IssueRow? _selectedImportIssue;
    [ObservableProperty] private string? _toastText;
    [ObservableProperty] private IssueSeverity _toastKind;
    [ObservableProperty] private bool _isToastVisible;
    [ObservableProperty] private DateTime? _lastImportTime;

    public bool HasRecent => Recent.Count > 0;

    public string ImportSummary => LastImport is not { } r
        ? ""
        : $"{r.Source} · {r.Format} · {r.Encoding} · séparateur « {(r.Separator == '\t' ? "tabulation" : r.Separator.ToString())} »";

    public string ImportCounts => LastImport is not { } r
        ? ""
        : $"{r.LinesRead} ligne(s) lue(s) · {r.Groupages.Count} groupage(s) · {r.OrderCount} ordre(s) dont {r.BlockedOrders} bloqué(s)";

    public int ImportErrors => LastImport?.Issues.Count(i => i.Severity == IssueSeverity.Error) ?? 0;
    public int ImportWarnings => LastImport?.Issues.Count(i => i.Severity == IssueSeverity.Warning) ?? 0;
    public int ImportInfos => LastImport?.Issues.Count(i => i.Severity == IssueSeverity.Info) ?? 0;
    public string ImportTimeText => LastImportTime is { } t ? $"Importé à {t:HH:mm:ss}" : "";

    public string SelectedRawLine => SelectedImportIssue?.Line is { } line && LastImport != null
        ? $"Ligne {line} : {LastImport.RawLine(line)}"
        : "Sélectionnez une alerte pour voir la ligne du fichier.";

    public LoadPlan? Plan => SelectedSolution?.Plan;
    public ValidationReport? Report => SelectedSolution?.Report;
    public bool HasPlan => Plan != null;
    public bool HasSeveralLoads => Loads.Count > 1;
    public bool HasSeveralSolutions => Solutions.Count > 1;

    /// <summary>Indicateurs de tout le groupage (tous camions).</summary>
    public PlanMetrics? Metrics => Plan?.Metrics;

    /// <summary>Indicateurs du camion affiché.</summary>
    public PlanMetrics? LoadMetrics => SelectedLoad?.Load.Metrics;

    public VehicleLoad? CurrentLoad => SelectedLoad?.Load;
    public Vehicle? Vehicle => Plan?.Vehicle ?? SelectedGroupage?.Vehicle;

    public string VehicleText => Vehicle is { } v
        ? $"{v.Id} · {v.Length:N0} × {v.Width:N0} × {v.Height:N0} mm · {v.MaxPayload:N0} kg · porte " +
          (v.HasDoor ? $"{v.DoorWidth:N0} × {v.DoorHeight:N0} mm" : "non renseignée")
        : "";

    public string GroupageText => SelectedGroupage is { } g
        ? $"{g.Id}{(g.Date is { } d ? $" · {d:dd/MM/yyyy}" : "")}{(g.Carrier != null ? $" · {g.Carrier}" : "")} · {g.Orders.Count} ordre(s)"
        : "Aucun groupage";

    public string VehiclesKpi => Plan == null ? "–" : $"{Plan.Loads.Count}";
    public string VehiclesKpiDetail => Plan == null ? "" : $"minimum théorique : {Plan.Metrics.EstimatedVehicles}";
    public string ItemsKpi => Metrics == null ? "–" : $"{Metrics.ItemsPlaced:N0} / {Metrics.ItemsTotal:N0}";
    public string ItemsKpiDetail => Metrics == null ? "" : Metrics.ItemsRemaining == 0 ? "tout est chargé" : $"reliquat : {Metrics.ItemsRemaining:N0}";
    public string WeightKpi => Metrics == null ? "–" : $"{Metrics.LoadedWeightKg:N0} kg";
    public string MlKpi => Metrics == null ? "–" : $"{Metrics.LinearMetersReal:N2} m";
    public string MlKpiDetail => Metrics == null ? "" : $"besoin équivalent {Metrics.LinearMetersRequiredEquivalent:N2} m";

    public string ValidationText => Report == null
        ? ""
        : Report.IsValid
            ? Report.Warnings == 0 ? "Plan conforme" : $"Plan conforme · {Report.Warnings} point(s) d'attention"
            : $"PLAN NON CONFORME · {Report.Errors} contrôle(s) en échec";

    public CheckStatus ValidationStatus => Report == null ? CheckStatus.Warning
        : !Report.IsValid ? CheckStatus.Error
        : Report.Warnings > 0 ? CheckStatus.Warning : CheckStatus.Ok;

    public string StepText => $"{VisibleCount} / {MaxVisible}";

    public string SelectedUnitText => SelectedUnit is { } u ? UnitInfo.Describe(u.Placement) : "Cliquez une unité dans une vue ou dans le plan.";

    // ---------- Import ----------

    [RelayCommand]
    private void Import()
    {
        var path = _dialogs.OpenFile("Importer un groupage", CsvFilter);
        if (path != null)
        {
            LoadFile(path);
        }
    }

    [RelayCommand]
    private void OpenSample(SampleFile? sample)
    {
        if (sample != null)
        {
            LoadFile(sample.Path);
        }
    }

    [RelayCommand]
    private void OpenRecent(RecentFileViewModel? recent)
    {
        if (recent == null)
        {
            return;
        }

        if (!File.Exists(recent.Path))
        {
            ShowToast($"Fichier introuvable : {recent.Path}", IssueSeverity.Error);
            _settings.Current.RecentFiles.Remove(recent.Path);
            _settings.Save();
            RefreshRecent();
            return;
        }

        LoadFile(recent.Path);
    }

    [RelayCommand]
    private void Reload()
    {
        if (_currentPath != null)
        {
            LoadFile(_currentPath, reload: true);
        }
    }

    partial void OnAutoReloadChanged(bool value)
    {
        _settings.Current.AutoReload = value;
        _settings.Save();
    }

    /// <summary>Importe un fichier ; en rechargement, conserve le groupage affiché s'il existe toujours.</summary>
    public void LoadFile(string path, bool reload = false)
    {
        ImportResult result;
        try
        {
            result = _importer.ImportFile(path);
        }
        catch (IOException) when (reload)
        {
            // Fichier encore en cours d'écriture : nouvel essai un peu plus tard.
            _reloadDelay.Start();
            return;
        }
        catch (Exception ex)
        {
            ShowToast($"Lecture impossible : {ex.Message}", IssueSeverity.Error);
            return;
        }

        var previousGroupage = reload ? SelectedGroupage?.Id : null;
        _keepStowage = reload;
        if (!reload && _excluded.Count > 0)
        {
            _excluded.Clear();
            OnPropertyChanged(nameof(HasExclusions));
            OnPropertyChanged(nameof(ExclusionText));
        }
        _currentPath = path;
        _watcher.Watch(path);
        if (!reload)
        {
            _settings.AddRecent(path);
            RefreshRecent();
        }

        SourceName = Path.GetFileName(path);
        HasFile = true;
        _importIssues = result.Issues;
        LastImport = result;
        LastImportTime = DateTime.Now;
        ImportIssues.Clear();
        foreach (var i in result.Issues.OrderByDescending(i => i.Severity).ThenBy(i => i.SourceLine))
        {
            ImportIssues.Add(new IssueRow("Import", i.Severity, i.SeverityLabel, i.Message, i.SourceLine));
        }

        ImportColumns.Clear();
        foreach (var c in result.Columns)
        {
            ImportColumns.Add(c);
        }

        foreach (var name in new[] { nameof(ImportSummary), nameof(ImportCounts), nameof(ImportErrors), nameof(ImportWarnings), nameof(ImportInfos), nameof(ImportTimeText) })
        {
            OnPropertyChanged(name);
        }

        Groupages.Clear();
        foreach (var g in result.Groupages)
        {
            Groupages.Add(g);
        }

        Status = $"{SourceName} : {result.LinesRead} lignes, {result.Groupages.Count} groupage(s), " +
                 $"{result.Issues.Count(i => i.Severity == IssueSeverity.Error)} anomalie(s) bloquante(s)";
        SelectedGroupage = Groupages.FirstOrDefault(g => g.Id == previousGroupage) ?? Groupages.FirstOrDefault();
        if (SelectedGroupage == null)
        {
            ClearPlan();
        }

        var errors = ImportErrors;
        var warnings = ImportWarnings;
        var noVehicle = result.Groupages.Any(g => !g.IsComputable) && SelectedVehicleChoice.Vehicle == null;
        var message = (reload ? "Fichier modifié : rechargé · " : "Import réussi · ") +
                      $"{result.Groupages.Count} groupage(s), {result.OrderCount} ordre(s)" +
                      (errors + warnings > 0 ? $" · {errors} bloquant(s), {warnings} avertissement(s)" : "") +
                      (noVehicle ? " · choisissez un véhicule" : "");
        ShowToast(message, errors > 0 || noVehicle ? IssueSeverity.Error : warnings > 0 ? IssueSeverity.Warning : IssueSeverity.Info);
    }

    private void RefreshRecent()
    {
        Recent.Clear();
        foreach (var path in _settings.Current.RecentFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Recent.Add(new RecentFileViewModel(path));
        }

        OnPropertyChanged(nameof(HasRecent));
    }

    partial void OnSelectedImportIssueChanged(IssueRow? value) => OnPropertyChanged(nameof(SelectedRawLine));

    public void ShowToast(string text, IssueSeverity kind)
    {
        ToastText = text;
        ToastKind = kind;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    [RelayCommand]
    private void CloseToast() => IsToastVisible = false;

    partial void OnSelectedGroupageChanged(Groupage? value)
    {
        // Rangement par défaut selon le fichier (par arrêts s'il porte des étapes), conservé lors d'un rechargement.
        if (value != null && (!_keepStowage || SelectedStowage == null))
        {
            _keepStowage = true;
            SelectedStowage = StowageChoices.First(c => c.Mode == Stowage.DefaultFor(value));
        }

        _keepStowage = false;
        OnPropertyChanged(nameof(ItineraryText));
        OnPropertyChanged(nameof(HasItinerary));
        OnPropertyChanged(nameof(GroupageText));
        if (value != null)
        {
            RebuildPalette();
            _ = OptimizeAsync();
        }
    }

    partial void OnSelectedVehicleChoiceChanged(VehicleChoice value) => _ = OptimizeAsync();
    partial void OnMinSupportPercentChanged(int value) => OptionChanged();
    partial void OnRespectDeliveryOrderChanged(bool value) => OptionChanged();
    partial void OnMultiStrategyChanged(bool value) => OptionChanged();
    partial void OnMultiVehicleChanged(bool value) => OptionChanged();
    partial void OnMaxVehiclesChanged(int value) => OptionChanged();
    partial void OnAllowFloorRotationChanged(bool value) => OptionChanged();
    partial void OnStaggerTubesChanged(bool value) => OptionChanged();
    partial void OnGapBetweenUnitsChanged(double value) => ClearanceChanged(value, v => GapBetweenUnits = v);
    partial void OnSideClearanceChanged(double value) => ClearanceChanged(value, v => SideClearance = v);
    partial void OnRoofClearanceChanged(double value) => ClearanceChanged(value, v => RoofClearance = v);

    /// <summary>Débord négatif ou absurde ramené dans [0 ; 1000] mm, puis recalcul.</summary>
    private void ClearanceChanged(double value, Action<double> set)
    {
        var clamped = Math.Clamp(double.IsFinite(value) ? Math.Round(value) : 0, 0, 1000);
        if (clamped != value)
        {
            set(clamped);
            return;
        }

        OnPropertyChanged(nameof(HasClearances));
        OptionChanged();
    }

    private void OptionChanged()
    {
        SaveOptions();
        _ = OptimizeAsync();
    }

    // ---------- Calcul ----------

    [RelayCommand]
    private async Task OptimizeAsync()
    {
        var source = SelectedGroupage;
        if (source == null)
        {
            return;
        }

        // Ordres exclus par la simulation, puis véhicule : choisi dans la liste, sinon celui du fichier,
        // sinon le véhicule par défaut des paramètres.
        source = _excluded.Count > 0 ? source.WithoutOrders(_excluded) : source;
        if (source.Orders.Count == 0)
        {
            ShowToast("Tous les ordres sont exclus : rien à calculer.", IssueSeverity.Warning);
            return;
        }

        var groupage = SelectedVehicleChoice?.Vehicle is { } preset
            ? source.WithVehicle(preset)
            : !source.IsComputable && DefaultVehicle is { } fallback
                ? source.WithVehicle(fallback)
                : source;
        if (!ReferenceEquals(groupage, source) && SelectedVehicleChoice?.Vehicle == null)
        {
            ShowToast($"Véhicule absent du fichier : véhicule par défaut « {groupage.Vehicle.Id} » appliqué.", IssueSeverity.Warning);
        }
        var version = ++_optimizeVersion;
        var options = new PackingOptions
        {
            MinSupportRatio = MinSupportPercent / 100.0,
            RespectDeliveryOrder = RespectDeliveryOrder,
            MultiStrategy = MultiStrategy,
            MaxVehicles = MultiVehicle ? Math.Max(1, MaxVehicles) : 1,
            Stowage = SelectedStowage?.Mode ?? StowageMode.Order,
            AllowFloorRotation = AllowFloorRotation,
            StaggerTubes = StaggerTubes,
            GapBetweenUnits = GapBetweenUnits,
            SideClearance = SideClearance,
            RoofClearance = RoofClearance
        };

        IsBusy = true;
        Status = $"Calcul des solutions pour {groupage.Id}…";
        try
        {
            var solutions = await Task.Run(() => _optimizer.OptimizeSolutions(groupage, options)
                .Select(p => new SolutionViewModel(p, PlanValidator.Validate(p)))
                .ToList());

            if (version != _optimizeVersion)
            {
                return;
            }

            Solutions.Clear();
            foreach (var s in solutions)
            {
                Solutions.Add(s);
            }

            OnPropertyChanged(nameof(HasSeveralSolutions));
            SelectedSolution = Solutions.FirstOrDefault();

            var best = solutions[0].Plan;
            Status = $"{groupage.Id} : {best.Metrics.ItemsPlaced:N0}/{best.Metrics.ItemsTotal:N0} article(s) chargé(s) sur " +
                     $"{best.Loads.Count} camion(s) · {solutions.Count} solution(s) proposée(s) · calcul {best.Duration.TotalMilliseconds:N0} ms";
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"Erreur d'optimisation : {ex.Message}");
        }
        finally
        {
            if (version == _optimizeVersion)
            {
                IsBusy = false;
            }
        }
    }

    partial void OnSelectedSolutionChanged(SolutionViewModel? value)
    {
        if (value != null)
        {
            ApplySolution(value);
        }
    }

    private void ApplySolution(SolutionViewModel solution)
    {
        var plan = solution.Plan;
        _animation.Stop();
        IsAnimating = false;

        RebuildOrderRows();

        Checks.Clear();
        foreach (var c in solution.Report.Checks)
        {
            Checks.Add(c);
        }

        Issues.Clear();
        foreach (var i in _importIssues.Where(i => i.OrderId == null || plan.Groupage.Orders.Any(o => o.Id == i.OrderId)))
        {
            Issues.Add(new IssueRow("Import", i.Severity, i.SeverityLabel, i.Message, i.SourceLine));
        }

        foreach (var i in plan.Issues)
        {
            Issues.Add(new IssueRow("Calcul", i.Severity, i.SeverityLabel, i.Message, i.SourceLine));
        }

        foreach (var g in plan.Unloaded.GroupBy(u => (u.Unit.Order.Id, u.Reason)))
        {
            var detail = g.First().Detail;
            Issues.Add(new IssueRow("Reliquat", IssueSeverity.Warning, "Reliquat",
                $"Ordre {g.Key.Id} : {g.Sum(u => u.Unit.ItemCount):N0} article(s) non chargé(s) – {g.Key.Reason.Label()}" +
                (detail != null ? $" ({detail})" : ""), null));
        }

        Loads.Clear();
        foreach (var load in plan.Loads)
        {
            Loads.Add(new LoadViewModel(load));
        }

        SetCalculation(CalculationDetails.Build(plan, solution.Report, Solutions.Select(x => x.Plan).ToList()));

        SelectedLoad = Loads.FirstOrDefault();

        foreach (var name in new[]
                 {
                     nameof(Plan), nameof(Report), nameof(HasPlan), nameof(HasSeveralLoads), nameof(Metrics), nameof(Vehicle),
                     nameof(VehicleText), nameof(ValidationText), nameof(ValidationStatus), nameof(VehiclesKpi),
                     nameof(VehiclesKpiDetail), nameof(ItemsKpi), nameof(ItemsKpiDetail), nameof(WeightKpi), nameof(MlKpi),
                     nameof(MlKpiDetail)
                 })
        {
            OnPropertyChanged(name);
        }
    }

    private void RebuildOrderRows()
    {
        Orders.Clear();
        if (SelectedGroupage == null)
        {
            return;
        }

        foreach (var o in SelectedGroupage.Orders)
        {
            var plan = Plan?.Groupage.Orders.Contains(o) == true ? Plan : null;
            Orders.Add(new OrderRowViewModel(o, plan ?? Plan, OrderColors.GetValueOrDefault(o.Id, Colors.SteelBlue),
                !_excluded.Contains(o.Id), OnOrderIncludedChanged));
        }
    }

    partial void OnSelectedLoadChanged(LoadViewModel? value)
    {
        SelectedUnit = null;
        Units.Clear();
        Strategies.Clear();
        if (value != null)
        {
            foreach (var p in value.Load.Placements.OrderBy(p => p.Sequence))
            {
                Units.Add(new PlacementRowViewModel(p, OrderColors.GetValueOrDefault(p.Unit.Order.Id, Colors.SteelBlue)));
            }

            foreach (var s in value.Load.StrategyLog)
            {
                Strategies.Add(s);
            }
        }

        BuildScene();
        OnPropertyChanged(nameof(LoadMetrics));
        OnPropertyChanged(nameof(CurrentLoad));
    }

    private void ClearPlan()
    {
        Solutions.Clear();
        SelectedSolution = null;
        SetCalculation([]);
        Loads.Clear();
        Orders.Clear();
        Units.Clear();
        Checks.Clear();
        Issues.Clear();
        Strategies.Clear();
        _unitsGroup.Children.Clear();
        _unitModels = [];
        OnPropertyChanged(nameof(HasPlan));
        OnPropertyChanged(nameof(Metrics));
        OnPropertyChanged(nameof(LoadMetrics));
    }

    // ---------- Couleurs, légende, mise en évidence ----------

    partial void OnSelectedColorModeChanged(ColorModeChoice value)
    {
        RebuildPalette();
        RebuildOrderRows();
        OnSelectedLoadChanged(SelectedLoad);
    }

    private void RebuildPalette()
    {
        if (SelectedGroupage == null)
        {
            return;
        }

        var (legend, colors) = OrderPalette.Build(SelectedGroupage, SelectedColorMode.Mode);
        OrderColors = colors;
        Legend.Clear();
        foreach (var entry in legend)
        {
            Legend.Add(new LegendItemViewModel(entry));
        }

        HighlightOrderIds = null;
    }

    [RelayCommand]
    private void ToggleLegend(LegendItemViewModel? item)
    {
        if (item == null)
        {
            return;
        }

        var activate = !item.IsActive;
        foreach (var l in Legend)
        {
            l.IsActive = false;
        }

        item.IsActive = activate;
        HighlightOrderIds = activate ? item.Entry.OrderIds : null;
    }

    [RelayCommand]
    private void ClearHighlight()
    {
        foreach (var l in Legend)
        {
            l.IsActive = false;
        }

        SearchText = "";
        HighlightOrderIds = null;
    }

    partial void OnSelectedOrderChanged(OrderRowViewModel? value)
    {
        if (value == null || Plan == null)
        {
            return;
        }

        HighlightOrderIds = new HashSet<string> { value.Id };
        var truck = Plan.VehiclesOf(value.Order).FirstOrDefault();
        if (truck > 0 && SelectedLoad?.Load.Number != truck)
        {
            SelectedLoad = Loads.FirstOrDefault(l => l.Load.Number == truck);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDelay.Stop();
        _searchDelay.Start();
    }

    private void ApplySearch()
    {
        var text = SearchText.Trim();
        if (text.Length == 0 || SelectedGroupage == null)
        {
            HighlightOrderIds = null;
            return;
        }

        bool Match(string? s) => s != null && s.Contains(text, StringComparison.OrdinalIgnoreCase);
        HighlightOrderIds = SelectedGroupage.Orders
            .Where(o => Match(o.Id) || Match(o.Article) || Match(o.Customer) || Match(o.OrderNumber) || Match(o.Designation))
            .Select(o => o.Id)
            .ToHashSet();
    }

    partial void OnHighlightOrderIdsChanged(IReadOnlySet<string>? value) => RefreshMaterials();

    // ---------- Scène 3D ----------

    private void BuildScene()
    {
        _sceneRoot.Children.Clear();
        _unitsGroup.Children.Clear();
        _unitModels = [];
        _scene = null;
        _hoverId = null;
        HoverText = null;

        if (SelectedLoad == null)
        {
            Scene3D = null;
            MaxVisible = 0;
            return;
        }

        // La géométrie est construite une fois ; l'animation ne fait qu'ajouter / retirer des modèles.
        _scene = Scene3DBuilder.Build(SelectedLoad.Load, int.MaxValue, Plan?.Options);
        foreach (var child in _scene.Root.Children.Where(c => c is not GeometryModel3D g || !_scene.ByModel.ContainsKey(g)))
        {
            _sceneRoot.Children.Add(child);
        }

        _unitModels = _scene.ByModel.Select(kv => (kv.Value, kv.Key)).OrderBy(x => x.Value.Sequence).ToList();
        _sceneRoot.Children.Add(_unitsGroup);
        RefreshMaterials();

        MaxVisible = _unitModels.Count;
        VisibleCount = MaxVisible;
        ApplyVisibility();
        Scene3D = _sceneRoot;
    }

    partial void OnVisibleCountChanged(int value)
    {
        OnPropertyChanged(nameof(StepText));
        ApplyVisibility();
    }

    partial void OnMaxVisibleChanged(int value) => OnPropertyChanged(nameof(StepText));

    private void ApplyVisibility()
    {
        var target = Math.Clamp(VisibleCount, 0, _unitModels.Count);
        while (_unitsGroup.Children.Count > target)
        {
            _unitsGroup.Children.RemoveAt(_unitsGroup.Children.Count - 1);
        }

        while (_unitsGroup.Children.Count < target)
        {
            _unitsGroup.Children.Add(_unitModels[_unitsGroup.Children.Count].Model);
        }
    }

    private Material MaterialFor(Placement p)
    {
        if (p.Unit.Id == SelectedUnitId)
        {
            return Scene3DBuilder.Selected;
        }

        if (p.Unit.Id == _hoverId)
        {
            return Scene3DBuilder.Hover;
        }

        var color = OrderPalette.Shade(OrderColors.GetValueOrDefault(p.Unit.Order.Id, Colors.SteelBlue), p.Unit.Index);
        if (HighlightOrderIds != null && !HighlightOrderIds.Contains(p.Unit.Order.Id))
        {
            color = OrderPalette.Fade(color);
        }

        return Scene3DBuilder.Solid(color);
    }

    private void RefreshMaterials()
    {
        foreach (var (placement, model) in _unitModels)
        {
            var material = MaterialFor(placement);
            model.Material = material;
            model.BackMaterial = material;
        }
    }

    private void RefreshMaterial(string? unitId)
    {
        if (unitId == null || _scene == null || !_scene.ByUnit.TryGetValue(unitId, out var model))
        {
            return;
        }

        var material = MaterialFor(_scene.ByModel[model]);
        model.Material = material;
        model.BackMaterial = material;
    }

    /// <summary>Appelé par la vue 3D après un test d'intersection au clic.</summary>
    public void SelectFromModel(GeometryModel3D? model)
    {
        if (model != null && _scene != null && _scene.ByModel.TryGetValue(model, out var placement))
        {
            SelectedUnitId = placement.Unit.Id;
        }
    }

    /// <summary>Appelé par la vue 3D au survol : met l'unité en surbrillance et prépare l'infobulle.</summary>
    public void HoverFromModel(GeometryModel3D? model)
    {
        Placement? placement = null;
        if (model != null && _scene != null)
        {
            _scene.ByModel.TryGetValue(model, out placement);
        }

        var id = placement?.Unit.Id;
        if (id == _hoverId)
        {
            return;
        }

        var previous = _hoverId;
        _hoverId = id;
        RefreshMaterial(previous);
        RefreshMaterial(id);
        HoverText = placement == null ? null : UnitInfo.Describe(placement);
    }

    partial void OnSelectedUnitIdChanged(string? oldValue, string? newValue)
    {
        RefreshMaterial(oldValue);
        RefreshMaterial(newValue);
        if (_syncingSelection)
        {
            return;
        }

        _syncingSelection = true;
        SelectedUnit = Units.FirstOrDefault(u => u.UnitId == newValue);
        _syncingSelection = false;
        OnPropertyChanged(nameof(SelectedUnitText));
    }

    partial void OnSelectedUnitChanged(PlacementRowViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedUnitText));
        if (_syncingSelection)
        {
            return;
        }

        _syncingSelection = true;
        SelectedUnitId = value?.UnitId;
        _syncingSelection = false;
    }

    [RelayCommand]
    private void ToggleAnimation()
    {
        if (CurrentLoad == null)
        {
            return;
        }

        if (IsAnimating)
        {
            _animation.Stop();
            IsAnimating = false;
            return;
        }

        if (VisibleCount >= MaxVisible)
        {
            VisibleCount = 0;
        }

        IsAnimating = true;
        _animation.Start();
    }

    // ---------- Sorties ----------

    [RelayCommand]
    private void Export()
    {
        if (Plan == null)
        {
            return;
        }

        var path = _dialogs.SaveFile("Exporter le plan de chargement", CsvFilter, $"Plan_{Plan.Groupage.Id}.csv");
        if (path == null)
        {
            return;
        }

        File.WriteAllText(path, PlanExporter.ToCsv(Plan), new UTF8Encoding(true));
        Status = $"Plan exporté : {path}";
    }

    // ---------- Détail du calcul ----------

    /// <summary>Explication pas à pas de la solution affichée, avec les chiffres (onglet « Détail du calcul »).</summary>
    public IReadOnlyList<DetailSection> CalculationSections { get; private set; } = [];

    /// <summary>Étapes affichées, blocs par ordre repliables (ouverts par défaut).</summary>
    public IReadOnlyList<DetailSectionViewModel> CalculationView { get; private set; } = [];

    public bool HasCalculation => CalculationSections.Count > 0;

    [ObservableProperty] private DetailSectionViewModel? _selectedCalculationSection;

    private void SetCalculation(IReadOnlyList<DetailSection> sections)
    {
        CalculationSections = sections;
        CalculationView = sections.Select(s => new DetailSectionViewModel(s)).ToList();
        OnPropertyChanged(nameof(CalculationSections));
        OnPropertyChanged(nameof(CalculationView));
        OnPropertyChanged(nameof(HasCalculation));
    }

    [RelayCommand]
    private void CopyCalculation()
    {
        if (!HasCalculation)
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(CalculationDetails.ToText(CalculationSections));
            ShowToast("Détail du calcul copié : il peut être collé dans un courriel ou un document.", IssueSeverity.Info);
        }
        catch (Exception ex)
        {
            ShowToast($"Copie impossible : {ex.Message}", IssueSeverity.Error);
        }
    }

    /// <summary>Modèle d'import complet (toutes les colonnes, 1 groupage + 2 ordres d'exemple).</summary>
    [RelayCommand]
    private void ExportTemplate()
    {
        var path = _dialogs.SaveFile("Exporter le modèle CSV", CsvFilter, "OptiTunes_modele_import.csv");
        if (path == null)
        {
            return;
        }

        try
        {
            File.WriteAllText(path, ImportFormat.TemplateCsv(), new UTF8Encoding(true));
            Status = $"Modèle exporté : {path}";
            ShowToast("Modèle CSV exporté : 1 groupage et 2 ordres d'exemple, toutes les colonnes.", IssueSeverity.Info);
        }
        catch (Exception ex)
        {
            ShowToast($"Export impossible : {ex.Message}", IssueSeverity.Error);
        }
    }

    // ---------- Aide sur le format ----------

    public IReadOnlyList<FieldDoc> GroupageFieldDocs => ImportFormat.GroupageFields;
    public IReadOnlyList<FieldDoc> OrderFieldDocs => ImportFormat.OrderFields;

    [ObservableProperty] private bool _isFormatHelpOpen;

    [RelayCommand]
    private void ShowFormatHelp() => IsFormatHelpOpen = true;

    [RelayCommand]
    private void CloseFormatHelp() => IsFormatHelpOpen = false;

    [RelayCommand]
    private void Print()
    {
        if (Plan == null)
        {
            return;
        }

        if (_printer.Print(Plan, OrderColors))
        {
            Status = $"Plan de chargement envoyé à l'impression ({Plan.Loads.Count} camion(s)).";
        }
    }

    [RelayCommand]
    private async Task RunSelfTestAsync()
    {
        SelfTests.Clear();
        SelfTestSummary = "Exécution…";
        IsBusy = true;
        try
        {
            var results = await Task.Run(() =>
                SelfTestSuite.Scenarios.Select(SelfTestSuite.Run)
                    .Concat(Enumerable.Range(1, 100).AsParallel().AsOrdered().Select(SelfTestSuite.RunFuzz))
                    .ToList());

            foreach (var r in results)
            {
                SelfTests.Add(r);
            }

            var ok = results.Count(r => r.Passed);
            SelfTestSummary = ok == results.Count
                ? $"{ok}/{results.Count} réussis – moteur conforme"
                : $"{results.Count - ok} échec(s) sur {results.Count}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
