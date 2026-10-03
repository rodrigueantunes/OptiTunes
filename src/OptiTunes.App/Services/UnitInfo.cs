using OptiTunes.Core.Models;

namespace OptiTunes.App.Services;

/// <summary>Texte d'information d'une unité, commun aux infobulles 2D / 3D et au panneau de sélection.</summary>
public static class UnitInfo
{
    public static string Title(Placement p) => $"{p.Unit.Order.Article}  ·  {p.Unit.Id}";

    public static string Describe(Placement p)
    {
        var u = p.Unit;
        var o = u.Order;
        var lines = new List<string>
        {
            Title(p),
            $"{o.Kind.Label()} {o.Reference}{(string.IsNullOrEmpty(o.Customer) ? "" : $" · {o.Customer}")}{(o.Stop > 0 ? $" · arrêt {o.Stop}" : "")}",
        };

        if (!string.IsNullOrEmpty(o.Designation))
        {
            lines.Add(o.Designation!);
        }

        if (o.DepartureStep > 0 || o.ArrivalStep > 0 || !string.IsNullOrWhiteSpace(o.Warehouse))
        {
            lines.Add($"Magasin {(string.IsNullOrWhiteSpace(o.Warehouse) ? "–" : o.Warehouse)} · " +
                      $"chargé étape {(o.DepartureStep > 0 ? o.DepartureStep.ToString() : "départ")} · " +
                      $"livré étape {(o.EffectiveArrival > 0 ? o.EffectiveArrival.ToString() : "–")}");
        }

        lines.Add($"Camion {p.VehicleNumber} · chargé en n° {p.Sequence} · " +
                  (p.Level == 1 ? "niveau 0 (sol)" : $"niveau {p.Level - 1} (gerbée)"));
        lines.Add($"Position X {p.X:N0} · Y {p.Y:N0} · Z {p.Z:N0} mm");
        lines.Add($"Encombrement {p.DX:N0} × {p.DY:N0} × {p.DZ:N0} mm");
        if (u.Shape == UnitShape.Staggered)
        {
            lines.Add($"Lit de {u.ItemCount} tubes Ø{u.TubeDiameter:N0} en quinconce (calage latéral obligatoire)");
        }

        lines.Add($"Poids {u.Weight:N1} kg{(u.ItemCount > 1 ? $" · {u.ItemCount} articles" : "")}");
        lines.Add(u.CanReceive
            ? $"Charge dessus {p.LoadAbove:N1} / {u.MaxLoadOnTop:N1} kg{(u.MaxLoadAssumed ? " (estimée)" : "")}"
            : u.MustBeOnTop ? "Au sommet : ne reçoit rien" : "Non gerbable : ne reçoit rien");
        lines.Add(p.OnFloor ? "Posé au plancher" : $"Posé sur {string.Join(", ", p.Supports.Select(s => s.Support.Unit.Id))}");
        return string.Join(Environment.NewLine, lines);
    }
}
