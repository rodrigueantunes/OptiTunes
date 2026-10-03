using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiTunes.App.Services;
using OptiTunes.Core.Models;

namespace OptiTunes.App.ViewModels;

/// <summary>Ligne éditable de la liste des véhicules, avec contrôle immédiat des valeurs.</summary>
public sealed partial class VehicleRowViewModel : ObservableObject
{
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Error), nameof(IsValid))] private string _name = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Error), nameof(IsValid), nameof(Summary))] private double _length;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Error), nameof(IsValid), nameof(Summary))] private double _width;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Error), nameof(IsValid), nameof(Summary))] private double _height;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Error), nameof(IsValid), nameof(Summary))] private double _payload;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Error), nameof(IsValid))] private double? _doorWidth;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Error), nameof(IsValid))] private double? _doorHeight;

    public static VehicleRowViewModel From(VehicleSetting v) => new()
    {
        Name = v.Name, Length = v.Length, Width = v.Width, Height = v.Height, Payload = v.Payload,
        DoorWidth = v.DoorWidth, DoorHeight = v.DoorHeight
    };

    public VehicleSetting ToSetting() => new()
    {
        Name = Name.Trim(), Length = Length, Width = Width, Height = Height, Payload = Payload,
        DoorWidth = DoorWidth is > 0 ? DoorWidth : null, DoorHeight = DoorHeight is > 0 ? DoorHeight : null
    };

    public string Summary => $"{Length / 1000:0.00} m · {Payload / 1000:0.#} t · {Length * Width * Height / 1e9:0.#} m³";

    /// <summary>Premier problème bloquant ou d'unité probable, vide si la ligne est correcte.</summary>
    public string Error
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return "Nom obligatoire";
            }

            if (Length <= 0 || Width <= 0 || Height <= 0 || Payload <= 0)
            {
                return "Dimensions et charge utile > 0 obligatoires";
            }

            if (Length < 1000 || Width < 1000 || Height < 1000)
            {
                return "Dimensions en mm attendues (< 1 m saisi)";
            }

            if (Length > 20000 || Width > 3000 || Height > 4000)
            {
                return "Dimensions hors gabarit routier";
            }

            if (Payload < 100)
            {
                return "Charge utile en kg attendue";
            }

            if (DoorWidth > Width || DoorHeight > Height)
            {
                return "Porte plus grande que l'intérieur";
            }

            return "";
        }
    }

    public bool IsValid => Error.Length == 0;
}

/// <summary>Écran Paramètres : liste des véhicules proposés et véhicule par défaut. Copie de travail, Enregistrer / Annuler.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private const string NoDefault = "(aucun)";
    private readonly ISettingsService _settings;

    public SettingsViewModel(ISettingsService settings)
    {
        _settings = settings;
        Vehicles.CollectionChanged += (_, e) =>
        {
            foreach (VehicleRowViewModel row in e.NewItems ?? Array.Empty<VehicleRowViewModel>())
            {
                row.PropertyChanged += (_, p) =>
                {
                    if (p.PropertyName == nameof(VehicleRowViewModel.Name))
                    {
                        RefreshDefaultChoices();
                    }
                };
            }
        };
        Reload();
    }

    /// <summary>Levé après un enregistrement réussi.</summary>
    public event Action? Saved;

    public ObservableCollection<VehicleRowViewModel> Vehicles { get; } = [];
    public ObservableCollection<string> DefaultChoices { get; } = [];

    [ObservableProperty] private VehicleRowViewModel? _selectedVehicle;
    [ObservableProperty] private string _defaultVehicle = NoDefault;
    [ObservableProperty] private string _message = "";

    public void Reload()
    {
        Vehicles.Clear();
        foreach (var v in _settings.Current.EffectiveVehicles())
        {
            Vehicles.Add(VehicleRowViewModel.From(v));
        }

        RefreshDefaultChoices();
        DefaultVehicle = _settings.Current.DefaultVehicle ?? NoDefault;
        SelectedVehicle = Vehicles.FirstOrDefault();
        Message = "";
    }

    private void RefreshDefaultChoices()
    {
        var current = DefaultVehicle;
        DefaultChoices.Clear();
        DefaultChoices.Add(NoDefault);
        foreach (var name in Vehicles.Select(v => v.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
        {
            DefaultChoices.Add(name);
        }

        DefaultVehicle = DefaultChoices.Contains(current) ? current : NoDefault;
    }

    [RelayCommand]
    private void Add()
    {
        var row = new VehicleRowViewModel
        {
            Name = UniqueName("Nouveau véhicule"), Length = 13600, Width = 2450, Height = 2700, Payload = 24000,
            DoorWidth = 2450, DoorHeight = 2650
        };
        Vehicles.Add(row);
        SelectedVehicle = row;
        RefreshDefaultChoices();
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (SelectedVehicle is not { } source)
        {
            return;
        }

        var copy = VehicleRowViewModel.From(source.ToSetting());
        copy.Name = UniqueName(source.Name + " (copie)");
        Vehicles.Insert(Vehicles.IndexOf(source) + 1, copy);
        SelectedVehicle = copy;
        RefreshDefaultChoices();
    }

    [RelayCommand]
    private void Remove()
    {
        if (SelectedVehicle is not { } row)
        {
            return;
        }

        var index = Vehicles.IndexOf(row);
        Vehicles.Remove(row);
        SelectedVehicle = Vehicles.ElementAtOrDefault(Math.Min(index, Vehicles.Count - 1));
        RefreshDefaultChoices();
    }

    [RelayCommand]
    private void MoveUp() => Move(-1);

    [RelayCommand]
    private void MoveDown() => Move(1);

    private void Move(int delta)
    {
        if (SelectedVehicle is not { } row)
        {
            return;
        }

        var index = Vehicles.IndexOf(row);
        var target = index + delta;
        if (target >= 0 && target < Vehicles.Count)
        {
            Vehicles.Move(index, target);
        }
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        Vehicles.Clear();
        foreach (var v in VehicleCatalog.Presets)
        {
            Vehicles.Add(VehicleRowViewModel.From(VehicleSetting.From(v)));
        }

        SelectedVehicle = Vehicles.FirstOrDefault();
        RefreshDefaultChoices();
        Message = "Liste par défaut rétablie (non enregistrée).";
    }

    [RelayCommand]
    private void Save()
    {
        var invalid = Vehicles.FirstOrDefault(v => !v.IsValid);
        if (invalid != null)
        {
            SelectedVehicle = invalid;
            Message = $"« {invalid.Name} » : {invalid.Error}.";
            return;
        }

        var duplicate = Vehicles.GroupBy(v => v.Name.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
        {
            Message = $"Nom en double : « {duplicate.Key} ».";
            return;
        }

        _settings.Current.Vehicles = Vehicles.Select(v => v.ToSetting()).ToList();
        _settings.Current.DefaultVehicle = DefaultVehicle == NoDefault ? null : DefaultVehicle;
        _settings.Save();
        RefreshDefaultChoices();
        Message = $"Enregistré : {Vehicles.Count} véhicule(s).";
        Saved?.Invoke();
    }

    /// <summary>Appelé quand une cellule « Nom » change, pour tenir à jour la liste du véhicule par défaut.</summary>
    public void NamesChanged() => RefreshDefaultChoices();

    private string UniqueName(string baseName)
    {
        var name = baseName;
        for (var i = 2; Vehicles.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)); i++)
        {
            name = $"{baseName} {i}";
        }

        return name;
    }
}
