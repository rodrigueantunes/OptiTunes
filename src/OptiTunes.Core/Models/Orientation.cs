namespace OptiTunes.Core.Models;

/// <summary>
/// Orientations 1..6 du §6.3. X = axe longitudinal (longueur véhicule), Y = largeur, Z = hauteur.
/// Les dimensions de base (A, B, C) correspondent à (L, l, H).
/// </summary>
[Flags]
public enum OrientationSet : byte
{
    None = 0,
    O1 = 1,
    O2 = 2,
    O3 = 4,
    O4 = 8,
    O5 = 16,
    O6 = 32,
    Upright = O1 | O2,
    All = O1 | O2 | O3 | O4 | O5 | O6
}

public static class Orientations
{
    // Index de la dimension de base portée par X, Y, Z pour chaque orientation.
    private static readonly int[][] Permutations =
    [
        [0, 1, 2],
        [0, 1, 2],
        [1, 0, 2],
        [0, 2, 1],
        [2, 0, 1],
        [1, 2, 0],
        [2, 1, 0]
    ];

    public static int[] Permutation(int orientation) => Permutations[orientation];

    public static (double DX, double DY, double DZ) Apply(int orientation, double a, double b, double c)
    {
        var dims = new[] { a, b, c };
        var p = Permutations[orientation];
        return (dims[p[0]], dims[p[1]], dims[p[2]]);
    }

    public static bool Contains(this OrientationSet set, int orientation) =>
        (set & ToFlag(orientation)) != 0;

    public static OrientationSet ToFlag(int orientation) => (OrientationSet)(1 << (orientation - 1));

    public static IEnumerable<int> Enumerate(this OrientationSet set)
    {
        for (var o = 1; o <= 6; o++)
        {
            if (set.Contains(o))
            {
                yield return o;
            }
        }
    }

    public static string Format(this OrientationSet set) => string.Join(",", set.Enumerate());

    /// <summary>
    /// Accepte "1,2", "12", "1|2|3", "TOUTES", "*", "DEBOUT", "FIXE".
    /// </summary>
    public static OrientationSet? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var t = text.Trim().ToUpperInvariant();
        switch (t)
        {
            case "*":
            case "TOUTES":
            case "TOUT":
            case "ALL":
                return OrientationSet.All;
            case "DEBOUT":
            case "UPRIGHT":
            case "ROTATION":
                return OrientationSet.Upright;
            case "FIXE":
            case "FIXED":
                return OrientationSet.O1;
        }

        var set = OrientationSet.None;
        foreach (var ch in t)
        {
            if (ch is >= '1' and <= '6')
            {
                set |= ToFlag(ch - '0');
            }
        }

        return set == OrientationSet.None ? null : set;
    }
}
