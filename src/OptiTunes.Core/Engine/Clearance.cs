using OptiTunes.Core.Models;

namespace OptiTunes.Core.Engine;

/// <summary>Règles de débord (jeux de chargement), communes au moteur et au validateur.</summary>
public static class Clearance
{
    /// <summary>
    /// Vrai si l'emprise (x, y, dx, dy) est à moins de <paramref name="gap"/> de <paramref name="other"/> :
    /// les deux emprises agrandies du jeu se chevauchent (côte à côte, bout à bout ou en coin).
    /// </summary>
    public static bool TooClose(double x, double y, double dx, double dy, Placement other, double gap) =>
        Geometry.Overlap(x, x + dx + gap, other.X, other.MaxX + gap) > Geometry.Eps &&
        Geometry.Overlap(y, y + dy + gap, other.Y, other.MaxY + gap) > Geometry.Eps;
}
