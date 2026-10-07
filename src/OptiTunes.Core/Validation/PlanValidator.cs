using OptiTunes.Core.Engine;
using OptiTunes.Core.Models;

namespace OptiTunes.Core.Validation;

public enum CheckStatus
{
    Ok,
    Warning,
    Error
}

public sealed class ValidationCheck
{
    public required string Name { get; init; }
    public required string Rule { get; init; }
    public CheckStatus Status { get; set; } = CheckStatus.Ok;
    public List<string> Violations { get; } = [];
    public int Checked { get; set; }

    public string Summary => Violations.Count == 0
        ? $"{Checked} contrôle(s) – conforme"
        : $"{Violations.Count} anomalie(s) sur {Checked}";
}

public sealed class ValidationReport
{
    public List<ValidationCheck> Checks { get; } = [];
    public bool IsValid => Checks.All(c => c.Status != CheckStatus.Error);
    public int Errors => Checks.Count(c => c.Status == CheckStatus.Error);
    public int Warnings => Checks.Count(c => c.Status == CheckStatus.Warning);

    public IEnumerable<string> AllErrors =>
        Checks.Where(c => c.Status == CheckStatus.Error).SelectMany(c => c.Violations.Select(v => $"[{c.Name}] {v}"));
}

/// <summary>
/// Contrôle indépendant du plan final, camion par camion : recalcule supports, niveaux et charges sans réutiliser
/// les données internes du moteur. « Un plan optimisé ne doit jamais violer une contrainte obligatoire » (§24).
/// </summary>
public static class PlanValidator
{
    private const int MaxDetails = 50;

    public static ValidationReport Validate(LoadPlan plan)
    {
        var report = new ValidationReport();
        var options = plan.Options;

        var conservation = Add(report, "Conservation des quantités", "Quantité chargée (tous camions) + reliquat = quantité de l'ordre");
        var inclusion = Add(report, "Inclusion dans le véhicule", "X + DX ≤ Lc ; Y + DY ≤ lc ; Z + DZ ≤ Hc");
        var orientation = Add(report, "Pose de l'unité", "Dimensions placées = rotation autorisée des dimensions de l'unité");
        var collision = Add(report, "Absence de collision", "Aucun chevauchement de volumes entre deux unités d'un même camion");
        var support = Add(report, "Support et stabilité", "Au sol, ou contact ≥ taux minimal et centre de l'unité au-dessus de ses appuis");
        var stacking = Add(report, "Gerbabilité", "Rien sur un non gerbable, un « au sommet », une bobine couchée ; sur un tube couché, seulement un tube aligné");
        var levels = Add(report, "Niveaux de gerbage", "Niveau de gerbage (0 = sol) ≤ NIVEAUX_MAX de chaque unité de la pile");
        var load = Add(report, "Charge supportable", "Σ poids au-dessus ≤ charge max supportable");
        var payload = Add(report, "Charge utile de chaque camion", "Pchargement ≤ Pmax");
        var door = Add(report, "Passage par la porte", "Section DY × DZ ≤ ouverture");
        var justified = Add(report, "Reliquat justifié", "Toute unité non chargée a un motif");
        var delivery = Add(report, "Accessibilité par arrêt",
            "À chaque étape, l'unité chargée ou déchargée n'est ni masquée côté porte ni recouverte par une unité encore à bord");
        var zones = Add(report, "Rangement par zone", $"{options.Stowage.Label()} : zones continues de l'avant vers la porte, pas de gerbage entre zones");
        var cg = Add(report, "Centre de gravité", "CG transversal proche de l'axe, CG vertical bas");
        var clearances = Add(report, "Débords",
            $"Parois gauche/droite ≥ {options.SideClearance:0} mm, plafond ≥ {options.RoofClearance:0} mm, entre unités voisines ≥ {options.GapBetweenUnits:0} mm");
        var beds = Add(report, "Tubes en quinconce", "Tubes dans le lit, sans chevauchement, chacun posé sur la rangée inférieure, charge du bas ≤ supportable");

        // Conservation (global, tous camions)
        foreach (var order in plan.Groupage.Orders)
        {
            conservation.Checked++;
            var loaded = plan.PlacedItems(order);
            var remaining = plan.RemainingItems(order);
            if (loaded + remaining != order.Quantity)
            {
                Fail(conservation, $"Ordre {order.Id} : {loaded} chargé(s) + {remaining} reliquat ≠ {order.Quantity}");
            }
        }

        var ids = plan.Placements.Select(p => p.Unit.Id).Concat(plan.Unloaded.Select(u => u.Unit.Id)).ToList();
        if (ids.Count != ids.Distinct().Count())
        {
            Fail(conservation, "Une même unité physique apparaît plusieurs fois dans le plan.");
        }

        foreach (var u in plan.Unloaded)
        {
            justified.Checked++;
            if (!Enum.IsDefined(u.Reason))
            {
                Fail(justified, $"{u.Unit.Id} non chargée sans motif");
            }
        }

        var multi = plan.Loads.Count > 1;
        foreach (var vehicleLoad in plan.Loads)
        {
            var prefix = multi ? $"{vehicleLoad.Label} : " : "";
            CheckLoad(vehicleLoad, prefix);
        }

        if (!plan.Vehicle.HasDoor)
        {
            door.Status = CheckStatus.Warning;
            door.Violations.Add("Dimensions d'ouverture non renseignées : passage non vérifié.");
        }

        if (!options.RespectDeliveryOrder && delivery.Violations.Count > 0)
        {
            delivery.Violations.Insert(0, "Contrôle de l'ordre de livraison désactivé dans les options.");
        }

        return report;

        void CheckLoad(VehicleLoad vl, string prefix)
        {
            var v = vl.Vehicle;
            var placed = vl.Placements;

            if (options.HasClearances)
            {
                foreach (var p in placed)
                {
                    clearances.Checked++;
                    if (p.Y < options.SideClearance - Geometry.Eps || p.MaxY > v.Width - options.SideClearance + Geometry.Eps)
                    {
                        Fail(clearances, $"{prefix}{p.Unit.Id} à moins de {options.SideClearance:0} mm d'une paroi latérale");
                    }

                    if (p.MaxZ > v.Height - options.RoofClearance + Geometry.Eps)
                    {
                        Fail(clearances, $"{prefix}{p.Unit.Id} à {v.Height - p.MaxZ:0} mm du plafond (< {options.RoofClearance:0} mm)");
                    }
                }

                if (options.GapBetweenUnits > 0)
                {
                    var sorted = placed.OrderBy(p => p.X).ToList();
                    for (var i = 0; i < sorted.Count; i++)
                    {
                        for (var j = i + 1; j < sorted.Count && sorted[j].X < sorted[i].MaxX + options.GapBetweenUnits - Geometry.Eps; j++)
                        {
                            var (a, b) = (sorted[i], sorted[j]);
                            if (Geometry.Overlap(a.Z, a.MaxZ, b.Z, b.MaxZ) > Geometry.Eps &&
                                Clearance.TooClose(a.X, a.Y, a.DX, a.DY, b, options.GapBetweenUnits))
                            {
                                Fail(clearances, $"{prefix}{a.Unit.Id} et {b.Unit.Id} à moins de {options.GapBetweenUnits:0} mm l'un de l'autre");
                            }
                        }
                    }
                }
            }

            foreach (var p in placed.Where(p => p.Unit.Shape == UnitShape.Staggered))
            {
                beds.Checked++;
                if (CheckBed(p.Unit) is { } problem)
                {
                    Fail(beds, $"{prefix}{p.Unit.Id} : {problem}");
                }
            }

            foreach (var p in placed)
            {
                inclusion.Checked++;
                if (p.X < -Geometry.Eps || p.Y < -Geometry.Eps || p.Z < -Geometry.Eps ||
                    p.MaxX > v.Length + Geometry.Eps || p.MaxY > v.Width + Geometry.Eps || p.MaxZ > v.Height + Geometry.Eps)
                {
                    Fail(inclusion, $"{prefix}{p.Unit.Id} dépasse : fin ({p.MaxX:0}, {p.MaxY:0}, {p.MaxZ:0})");
                }

                orientation.Checked++;
                var (dx, dy, dz) = Orientations.Apply(p.Orientation, p.Unit.A, p.Unit.B, p.Unit.C);
                if (!p.Unit.Orientations.Contains(p.Orientation))
                {
                    Fail(orientation, $"{prefix}{p.Unit.Id} : pose {p.Orientation} non autorisée");
                }
                else if (Math.Abs(dx - p.DX) > Geometry.Eps || Math.Abs(dy - p.DY) > Geometry.Eps || Math.Abs(dz - p.DZ) > Geometry.Eps)
                {
                    Fail(orientation, $"{prefix}{p.Unit.Id} : dimensions incohérentes avec la pose");
                }

                if (v.HasDoor)
                {
                    door.Checked++;
                    if (!UnitBuilder.PassesDoor(p.Unit, p.Orientation, v))
                    {
                        Fail(door, $"{prefix}{p.Unit.Id} : section {p.DY:0} × {p.DZ:0} > porte {v.DoorWidth:0} × {v.DoorHeight:0}");
                    }
                }
            }

            // Collisions – balayage sur X
            var byX = placed.OrderBy(p => p.X).ToList();
            for (var i = 0; i < byX.Count; i++)
            {
                collision.Checked++;
                for (var j = i + 1; j < byX.Count && byX[j].X < byX[i].MaxX - Geometry.Eps; j++)
                {
                    if (Geometry.Intersects(byX[i], byX[j]))
                    {
                        Fail(collision, $"{prefix}{byX[i].Unit.Id} chevauche {byX[j].Unit.Id}");
                    }
                }
            }

            // Recalcul indépendant des supports
            var supports = placed.ToDictionary(p => p, p => placed
                .Where(q => q != p && Geometry.ContactArea(p, q) > Geometry.Eps)
                .Select(q => (Below: q, Area: Geometry.ContactArea(p, q)))
                .ToList());

            foreach (var p in placed)
            {
                support.Checked++;
                stacking.Checked++;
                if (!p.OnFloor)
                {
                    var ratio = supports[p].Sum(s => s.Area) / p.FootprintArea;
                    if (ratio < p.Unit.RequiredSupportRatio - 1e-6)
                    {
                        Fail(support, $"{prefix}{p.Unit.Id} : taux de support {ratio:P0} < {p.Unit.RequiredSupportRatio:P0}");
                    }

                    if (!p.Unit.CanBeElevated)
                    {
                        Fail(stacking, $"{prefix}{p.Unit.Id} ({(p.Unit.MustBeOnFloor ? "au sol obligatoire" : "non gerbable")}) posé à Z = {p.Z:0}");
                    }

                    if (!StackingRules.CenterOverSupports(p.X, p.Y, p.DX, p.DY, supports[p].Select(s => s.Below)))
                    {
                        Fail(support, $"{prefix}{p.Unit.Id} : centre de l'unité hors de ses appuis (basculement)");
                    }
                }

                foreach (var (below, _) in supports[p])
                {
                    if (StackingRules.Forbidden(p.Unit, p.Orientation, p.X, p.Y, p.DX, p.DY, below) is { } why)
                    {
                        Fail(stacking, $"{prefix}{p.Unit.Id} posé sur {below.Unit.Id} : {why}");
                    }
                }
            }

            var level = new Dictionary<Placement, int>();
            var cap = new Dictionary<Placement, int>();
            foreach (var p in placed.OrderBy(p => p.Z))
            {
                levels.Checked++;
                var below = supports[p];
                level[p] = below.Count == 0 ? 1 : 1 + below.Max(s => level[s.Below]);
                cap[p] = below.Count == 0 ? p.Unit.MaxLevels : Math.Min(p.Unit.MaxLevels, below.Min(s => cap[s.Below]));
                if (level[p] > cap[p])
                {
                    Fail(levels, $"{prefix}{p.Unit.Id} au niveau de gerbage {level[p] - 1} > {cap[p] - 1} autorisé(s)");
                }
            }

            // Charge supportable – propagation du haut vers le bas
            var loadAbove = placed.ToDictionary(p => p, _ => 0.0);
            foreach (var p in placed.OrderByDescending(p => p.Z))
            {
                var below = supports[p];
                var total = below.Sum(s => s.Area);
                foreach (var (b, area) in below)
                {
                    loadAbove[b] += (p.Unit.Weight + loadAbove[p]) * area / total;
                }
            }

            foreach (var p in placed)
            {
                load.Checked++;
                if (loadAbove[p] > p.Unit.MaxLoadOnTop + 1e-3)
                {
                    Fail(load, $"{prefix}{p.Unit.Id} porte {loadAbove[p]:0.#} kg > {p.Unit.MaxLoadOnTop:0.#} kg");
                }
            }

            payload.Checked++;
            var weight = placed.Sum(p => p.Unit.Weight);
            if (weight > v.MaxPayload + Geometry.Eps)
            {
                Fail(payload, $"{prefix}{weight:0.#} kg > {v.MaxPayload:0} kg");
            }

            // Accessibilité : chargement et déchargement à chaque étape par la porte arrière.
            var strict = options.RespectDeliveryOrder && options.Stowage != StowageMode.Compact;
            foreach (var p in placed.Where(p => p.Unit.Departure > 0 || p.Unit.Arrival > 0))
            {
                delivery.Checked++;
                foreach (var q in placed)
                {
                    if (q == p)
                    {
                        continue;
                    }

                    var behind = q.X >= p.MaxX - Geometry.Eps &&
                                 Geometry.Overlap(p.Y, p.MaxY, q.Y, q.MaxY) > Geometry.Eps &&
                                 Geometry.Overlap(p.Z, p.MaxZ, q.Z, q.MaxZ) > Geometry.Eps;
                    var onTop = supports[q].Any(s => s.Below == p);
                    if ((behind || onTop) && Stowage.Blocks(p.Unit, q.Unit))
                    {
                        var message = $"{prefix}{p.Unit.Id} ({Stowage.Describe(p.Unit)}) bloqué par {q.Unit.Id} ({Stowage.Describe(q.Unit)})";
                        if (strict)
                        {
                            Fail(delivery, message);
                        }
                        else
                        {
                            Warn(delivery, message);
                        }

                        break;
                    }
                }
            }

            // Zones de rangement (client, magasin, commande) : continues de l'avant vers la porte, pas de gerbage entre zones.
            if (options.Stowage.IsGrouped())
            {
                foreach (var p in placed)
                {
                    zones.Checked++;
                    foreach (var q in placed.Where(q => q != p && Stowage.ZoneConflict(p.Unit, p.X, p.MaxX, q)))
                    {
                        Fail(zones, $"{prefix}{p.Unit.Id} (zone « {p.Unit.GroupKey} ») mal rangé par rapport à {q.Unit.Id} (zone « {q.Unit.GroupKey} »)");
                        break;
                    }

                    foreach (var (below, _) in supports[p].Where(s => !string.Equals(s.Below.Unit.GroupKey, p.Unit.GroupKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        Fail(zones, $"{prefix}{p.Unit.Id} (zone « {p.Unit.GroupKey} ») gerbé sur {below.Unit.Id} (zone « {below.Unit.GroupKey} »)");
                    }
                }
            }

            if (weight > 0)
            {
                cg.Checked++;
                var cgy = placed.Sum(p => p.Unit.Weight * (p.Y + p.DY / 2)) / weight;
                var offset = Math.Abs(cgy - v.Width / 2) / v.Width;
                if (offset > 0.10)
                {
                    Warn(cg, $"{prefix}CG transversal décalé de {offset:P0} de la largeur (Y = {cgy:0} mm)");
                }

                var cgz = placed.Sum(p => p.Unit.Weight * (p.Z + p.DZ / 2)) / weight;
                if (cgz > v.Height * 0.6)
                {
                    Warn(cg, $"{prefix}CG haut : Z = {cgz:0} mm (> 60 % de la hauteur utile)");
                }
            }
        }
    }

    /// <summary>Géométrie interne d'un lit de tubes en quinconce (repère de base : B largeur, C hauteur).</summary>
    private static string? CheckBed(PhysicalUnit u)
    {
        var d = u.TubeDiameter;
        var c = u.TubeCenters;
        if (d <= 0 || c.Count != u.ItemCount)
        {
            return $"{c.Count} tubes décrits pour {u.ItemCount} articles";
        }

        foreach (var (b, h) in c)
        {
            if (b - d / 2 < -Geometry.Eps || b + d / 2 > u.B + Geometry.Eps || h - d / 2 < -Geometry.Eps || h + d / 2 > u.C + Geometry.Eps)
            {
                return "tube hors de l'enveloppe du lit";
            }
        }

        for (var i = 0; i < c.Count; i++)
        {
            for (var j = i + 1; j < c.Count; j++)
            {
                var dist = Math.Sqrt(Math.Pow(c[i].B - c[j].B, 2) + Math.Pow(c[i].C - c[j].C, 2));
                if (dist < d - 0.5)
                {
                    return $"tubes qui se chevauchent (entraxe {dist:0} mm < Ø{d:0})";
                }
            }

            if (c[i].C > d / 2 + Geometry.Eps &&
                !c.Any(o => o.C < c[i].C - Geometry.Eps && Math.Abs(Math.Sqrt(Math.Pow(c[i].B - o.B, 2) + Math.Pow(c[i].C - o.C, 2)) - d) < 0.5))
            {
                return "tube sans appui sur la rangée inférieure";
            }
        }

        var rows = c.Select(t => Math.Round(t.C, 1)).Distinct().Count();
        var perTube = u.Weight / u.ItemCount;
        var bottomLoad = (rows - 1) * perTube;
        var allowed = u.Order.MaxLoadOnTop ?? (Math.Max(1, u.Order.MaxLevels ?? 2) - 1) * perTube;
        if (bottomLoad > allowed + 1e-3 || rows > Math.Max(1, u.Order.MaxLevels ?? 2))
        {
            return $"{rows} rangées : charge sur un tube du bas {bottomLoad:0.#} kg > {allowed:0.#} kg ou niveaux dépassés";
        }

        return null;
    }

    private static ValidationCheck Add(ValidationReport report, string name, string rule)
    {
        var check = new ValidationCheck { Name = name, Rule = rule };
        report.Checks.Add(check);
        return check;
    }

    private static void Fail(ValidationCheck check, string message)
    {
        check.Status = CheckStatus.Error;
        if (check.Violations.Count < MaxDetails)
        {
            check.Violations.Add(message);
        }
    }

    private static void Warn(ValidationCheck check, string message)
    {
        if (check.Status == CheckStatus.Ok)
        {
            check.Status = CheckStatus.Warning;
        }

        if (check.Violations.Count < MaxDetails)
        {
            check.Violations.Add(message);
        }
    }
}
