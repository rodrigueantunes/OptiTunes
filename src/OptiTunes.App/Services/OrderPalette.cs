using System.Windows.Media;
using OptiTunes.Core.Models;

namespace OptiTunes.App.Services;

public enum ColorMode
{
    Ordre,
    Client,
    Arret,
    Magasin
}

public sealed record LegendEntry(string Label, Color Color, IReadOnlySet<string> OrderIds);

/// <summary>Couleurs stables partagées par les vues 2D et 3D, par ordre, par client ou par arrêt.</summary>
public static class OrderPalette
{
    private static readonly Color[] Colors =
    [
        Color.FromRgb(0x5D, 0xAD, 0xE2), Color.FromRgb(0x58, 0xD6, 0x8D), Color.FromRgb(0xF5, 0xB0, 0x41),
        Color.FromRgb(0xAF, 0x7A, 0xC5), Color.FromRgb(0xEC, 0x70, 0x63), Color.FromRgb(0x48, 0xC9, 0xB0),
        Color.FromRgb(0xF7, 0xDC, 0x6F), Color.FromRgb(0x85, 0x92, 0x9E), Color.FromRgb(0xE5, 0x98, 0xB8),
        Color.FromRgb(0x2E, 0x86, 0xC1), Color.FromRgb(0xA9, 0xCC, 0xE3), Color.FromRgb(0xDC, 0x76, 0x33),
        Color.FromRgb(0x73, 0xC6, 0xB6), Color.FromRgb(0xC3, 0x9B, 0xD3), Color.FromRgb(0x9A, 0x7D, 0x0A),
        Color.FromRgb(0x1A, 0xBC, 0x9C)
    ];

    /// <summary>Légende (une entrée par groupe) et couleur de chaque ordre.</summary>
    public static (IReadOnlyList<LegendEntry> Legend, IReadOnlyDictionary<string, Color> ByOrder) Build(Groupage groupage, ColorMode mode)
    {
        var groups = groupage.Orders
            .GroupBy(o => mode switch
            {
                ColorMode.Client => string.IsNullOrWhiteSpace(o.Customer) ? "Client non renseigné" : o.Customer!,
                ColorMode.Arret => o.EffectiveArrival > 0 ? $"Livraison étape {o.EffectiveArrival}" : "Étape non renseignée",
                ColorMode.Magasin => string.IsNullOrWhiteSpace(o.Warehouse) ? "Magasin non renseigné" : $"Magasin {o.Warehouse}",
                _ => $"{o.Id} · {o.Article}"
            })
            .OrderBy(g => mode == ColorMode.Arret ? g.First().EffectiveArrival : 0)
            .ToList();

        var legend = new List<LegendEntry>();
        var byOrder = new Dictionary<string, Color>();
        foreach (var g in groups)
        {
            var color = Colors[legend.Count % Colors.Length];
            var ids = g.Select(o => o.Id).ToHashSet();
            legend.Add(new LegendEntry(g.Key, color, ids));
            foreach (var id in ids)
            {
                byOrder.TryAdd(id, color);
            }
        }

        return (legend, byOrder);
    }

    public static Color Darken(Color c, double factor = 0.7) =>
        Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

    /// <summary>Nuance alternée (± 8 %) pour distinguer deux unités voisines du même ordre.</summary>
    public static Color Shade(Color c, int index)
    {
        var step = index % 3 - 1;
        if (step == 0)
        {
            return c;
        }

        return step > 0
            ? Color.FromRgb((byte)(c.R + (255 - c.R) * 0.16), (byte)(c.G + (255 - c.G) * 0.16), (byte)(c.B + (255 - c.B) * 0.16))
            : Darken(c, 0.88);
    }

    /// <summary>Couleur estompée (unités hors mise en évidence).</summary>
    public static Color Fade(Color c) =>
        Color.FromRgb((byte)(c.R + (0xEE - c.R) * 0.8), (byte)(c.G + (0xEE - c.G) * 0.8), (byte)(c.B + (0xEE - c.B) * 0.8));
}
