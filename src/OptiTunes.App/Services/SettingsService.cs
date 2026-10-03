using System.IO;
using System.Text.Json;
using OptiTunes.Core.Models;

namespace OptiTunes.App.Services;

/// <summary>Véhicule de la liste paramétrable (dimensions intérieures utiles en mm, charge utile en kg).</summary>
public sealed class VehicleSetting
{
    public string Name { get; set; } = "";
    public double Length { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Payload { get; set; }
    public double? DoorWidth { get; set; }
    public double? DoorHeight { get; set; }

    public Vehicle ToVehicle() => new()
    {
        Id = Name, Type = Name, Length = Length, Width = Width, Height = Height, MaxPayload = Payload,
        DoorWidth = DoorWidth, DoorHeight = DoorHeight
    };

    public static VehicleSetting From(Vehicle v) => new()
    {
        Name = v.Id, Length = v.Length, Width = v.Width, Height = v.Height, Payload = v.MaxPayload,
        DoorWidth = v.DoorWidth, DoorHeight = v.DoorHeight
    };
}

public sealed class AppSettings
{
    public List<string> RecentFiles { get; set; } = [];
    public bool AutoReload { get; set; } = true;

    /// <summary>Dernière version dont les nouveautés ont été affichées.</summary>
    public string? LastSeenVersion { get; set; }

    /// <summary>Liste des véhicules proposés (null = catalogue par défaut).</summary>
    public List<VehicleSetting>? Vehicles { get; set; }

    /// <summary>Véhicule appliqué quand le fichier n'en définit pas (null = aucun).</summary>
    public string? DefaultVehicle { get; set; }

    // Réglages de calcul mémorisés
    public int MinSupportPercent { get; set; } = 80;
    public bool RespectDeliveryOrder { get; set; } = true;
    public bool MultiStrategy { get; set; } = true;
    public bool MultiVehicle { get; set; } = true;
    public int MaxVehicles { get; set; } = 5;
    public bool AllowFloorRotation { get; set; } = true;
    public bool StaggerTubes { get; set; } = true;

    // Débords (mm)
    public double GapBetweenUnits { get; set; }
    public double SideClearance { get; set; }
    public double RoofClearance { get; set; }

    public List<VehicleSetting> EffectiveVehicles() =>
        Vehicles ?? VehicleCatalog.Presets.Select(VehicleSetting.From).ToList();
}

public interface ISettingsService
{
    AppSettings Current { get; }
    void Save();
    void AddRecent(string path);
}

/// <summary>Préférences locales (%APPDATA%\OptiTunes\settings.json).</summary>
public sealed class SettingsService : ISettingsService
{
    private const int MaxRecent = 8;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsService(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OptiTunes", "settings.json")
            : path;
        Current = Load();
        var unique = Current.RecentFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Current.RecentFiles.Clear();
        Current.RecentFiles.AddRange(unique);
    }

    public AppSettings Current { get; }

    public void AddRecent(string path)
    {
        var full = Path.GetFullPath(path);
        Current.RecentFiles.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        Current.RecentFiles.Insert(0, full);
        if (Current.RecentFiles.Count > MaxRecent)
        {
            Current.RecentFiles.RemoveRange(MaxRecent, Current.RecentFiles.Count - MaxRecent);
        }

        Save();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, Json));
        }
        catch (IOException)
        {
            // Préférences non critiques : on ignore un disque indisponible.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private AppSettings Load()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings() : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }
}

/// <summary>
/// Surveille le fichier importé : à chaque enregistrement (Excel, éditeur…), déclenche un rechargement après
/// un court délai, le temps que l'écriture se termine.
/// </summary>
public sealed class FileWatcher : IDisposable
{
    private readonly Action _changed;
    private FileSystemWatcher? _watcher;
    private string? _fileName;

    public FileWatcher(Action changed) => _changed = changed;

    public void Watch(string? path)
    {
        Stop();
        if (path == null || Path.GetDirectoryName(Path.GetFullPath(path)) is not { } folder || !Directory.Exists(folder))
        {
            return;
        }

        _fileName = Path.GetFileName(path);
        _watcher = new FileSystemWatcher(folder)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnEvent;
        _watcher.Created += OnEvent;
        _watcher.Renamed += (_, e) =>
        {
            if (string.Equals(e.Name, _fileName, StringComparison.OrdinalIgnoreCase))
            {
                _changed();
            }
        };
    }

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        if (string.Equals(e.Name, _fileName, StringComparison.OrdinalIgnoreCase))
        {
            _changed();
        }
    }

    public void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    public void Dispose() => Stop();
}
