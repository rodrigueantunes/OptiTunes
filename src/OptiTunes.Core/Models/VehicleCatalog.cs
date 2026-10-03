namespace OptiTunes.Core.Models;

/// <summary>Véhicules types pour simuler un autre camion que celui du groupage (dimensions intérieures utiles).</summary>
public static class VehicleCatalog
{
    public static IReadOnlyList<Vehicle> Presets { get; } =
    [
        new() { Id = "Semi-remorque 13,60 m", Length = 13600, Width = 2450, Height = 2700, MaxPayload = 24000, DoorWidth = 2450, DoorHeight = 2650 },
        new() { Id = "Semi méga 13,60 m", Length = 13600, Width = 2450, Height = 3000, MaxPayload = 24000, DoorWidth = 2450, DoorHeight = 2950 },
        new() { Id = "Porteur 19 t – 9,60 m", Length = 9600, Width = 2450, Height = 2600, MaxPayload = 9500, DoorWidth = 2450, DoorHeight = 2500 },
        new() { Id = "Porteur 12 t – 7,20 m", Length = 7200, Width = 2450, Height = 2400, MaxPayload = 5500, DoorWidth = 2400, DoorHeight = 2300 },
        new() { Id = "Fourgon 20 m³", Length = 4300, Width = 2100, Height = 2200, MaxPayload = 1100, DoorWidth = 1900, DoorHeight = 2050 }
    ];

    public static Vehicle Copy(Vehicle v) => new()
    {
        Id = v.Id, Type = v.Type ?? v.Id, Length = v.Length, Width = v.Width, Height = v.Height,
        MaxPayload = v.MaxPayload, DoorWidth = v.DoorWidth, DoorHeight = v.DoorHeight
    };
}
