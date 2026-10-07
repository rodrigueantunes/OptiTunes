using System.Diagnostics;
using OptiTunes.Core.Engine;
using OptiTunes.Core.Models;

namespace OptiTunes.Core.Validation;

public sealed record ScenarioResult(string Name, string Expected, string Obtained, bool Passed, TimeSpan Duration);

/// <summary>
/// Banc d'essai fonctionnel : jeux d'essais vérifiables à la main (§20, §24) + groupages aléatoires
/// systématiquement contrôlés par le validateur. Utilisé par les tests unitaires et par l'écran « Auto-test ».
/// </summary>
public static class SelfTestSuite
{
    public sealed record Scenario(string Name, string Expected, Func<Groupage> Build, Func<LoadPlan, string?> Assert,
        PackingOptions? Options = null);

    public static IReadOnlyList<Scenario> Scenarios { get; } = BuildScenarios();

    public static ScenarioResult Run(Scenario scenario)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var plan = new LoadOptimizer().Optimize(scenario.Build(), scenario.Options);
            var report = PlanValidator.Validate(plan);
            var failure = report.IsValid
                ? scenario.Assert(plan)
                : "Plan invalide : " + string.Join(" | ", report.AllErrors.Take(3));
            var obtained = $"{plan.Metrics.ItemsPlaced}/{plan.Metrics.ItemsTotal} chargé(s)" +
                           (plan.Loads.Count > 1 ? $" sur {plan.Loads.Count} camions" : "") +
                           (plan.Unloaded.Count > 0 ? $", reliquat : {plan.Unloaded[0].Reason.Label()}" : "");
            return new ScenarioResult(scenario.Name, scenario.Expected, failure ?? obtained, failure == null, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new ScenarioResult(scenario.Name, scenario.Expected, "Exception : " + ex.Message, false, sw.Elapsed);
        }
    }

    /// <summary>Groupage aléatoire reproductible : mélange de cartons, palettes, plaques, tubes et bobines.</summary>
    public static Groupage RandomGroupage(int seed)
    {
        var rnd = new Random(seed);
        var g = NewGroupage($"FUZZ-{seed}", 13600, 2450, 2700, rnd.Next(8, 25) * 1000, 2450, 2650);
        var lines = rnd.Next(3, 12);
        for (var i = 1; i <= lines; i++)
        {
            var type = (PhysicalType)rnd.Next(0, 7);
            var stackable = rnd.NextDouble() < 0.6;
            var o = new TransportOrder
            {
                Id = $"OT{i:00}",
                Kind = rnd.NextDouble() < 0.3 ? OrderKind.Cadence : OrderKind.CommandLine,
                OrderNumber = $"CDE{seed}",
                OrderLine = i.ToString(),
                Article = $"ART-{type}-{i}",
                Type = type,
                Quantity = rnd.Next(1, type == PhysicalType.Plaque ? 1500 : 30),
                Stop = rnd.Next(0, 4),
                Stackable = rnd.NextDouble() < 0.1 ? null : stackable,
                MaxLevels = stackable && rnd.NextDouble() < 0.8 ? rnd.Next(1, 5) : null,
                MaxLoadOnTop = stackable && rnd.NextDouble() < 0.7 ? rnd.Next(0, 800) : null,
                MustBeOnFloor = rnd.NextDouble() < 0.1,
                MustBeOnTop = rnd.NextDouble() < 0.08,
                Orientations = rnd.NextDouble() < 0.3 ? OrientationSet.All : null
            };

            switch (type)
            {
                case PhysicalType.Plaque:
                    o.Length = rnd.Next(600, 2600);
                    o.Width = rnd.Next(400, 1600);
                    o.Height = rnd.Next(2, 12);
                    o.UnitWeight = Math.Round(rnd.NextDouble() * 3 + 0.2, 2);
                    o.MaxPerPile = rnd.NextDouble() < 0.5 ? rnd.Next(50, 400) : null;
                    o.Orientations = OrientationSet.Upright;
                    break;
                case PhysicalType.Tube:
                    o.Length = rnd.Next(1000, 6000);
                    o.Diameter = rnd.Next(40, 400);
                    o.UnitWeight = rnd.Next(5, 120);
                    break;
                case PhysicalType.Roll:
                    o.Diameter = rnd.Next(400, 1500);
                    o.Width = rnd.Next(300, 2000);
                    o.UnitWeight = rnd.Next(100, 1500);
                    break;
                case PhysicalType.Pallet:
                    o.Length = 1200;
                    o.Width = rnd.NextDouble() < 0.5 ? 800 : 1000;
                    o.Height = rnd.Next(500, 2200);
                    o.UnitWeight = rnd.Next(100, 900);
                    break;
                default:
                    o.Length = rnd.Next(200, 2400);
                    o.Width = rnd.Next(200, 1600);
                    o.Height = rnd.Next(150, 1800);
                    o.UnitWeight = rnd.Next(2, 400);
                    break;
            }

            g.Orders.Add(o);
        }

        return g;
    }

    public static ScenarioResult RunFuzz(int seed)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Un groupage sur deux autorise la répartition sur 3 camions ; un sur trois a des débords.
            var rnd = new Random(seed * 7919);
            var options = new PackingOptions
            {
                MaxVehicles = seed % 2 == 0 ? 3 : 1,
                GapBetweenUnits = seed % 3 == 0 ? rnd.Next(0, 81) : 0,
                SideClearance = seed % 3 == 0 ? rnd.Next(0, 51) : 0,
                RoofClearance = seed % 3 == 0 ? rnd.Next(0, 201) : 0,
                Stowage = (StowageMode)rnd.Next(0, 6)
            };
            var groupage = RandomGroupage(seed);
            if (seed % 4 == 0)
            {
                foreach (var o in groupage.Orders)
                {
                    o.Steps(rnd.NextDouble() < 0.5 ? "CPF" : "OPF", rnd.Next(1, 3), rnd.Next(3, 6), $"CLIENT {rnd.Next(1, 4)}");
                }
            }

            var plan = new LoadOptimizer().Optimize(groupage, options);
            var report = PlanValidator.Validate(plan);
            var obtained = report.IsValid
                ? $"{plan.Metrics.ItemsPlaced}/{plan.Metrics.ItemsTotal} chargé(s), {plan.Placements.Count} unités, {plan.Loads.Count} camion(s), toutes contraintes respectées"
                : string.Join(" | ", report.AllErrors.Take(3));
            return new ScenarioResult($"Groupage aléatoire #{seed}", "Aucune contrainte violée, quantités conservées",
                obtained, report.IsValid, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new ScenarioResult($"Groupage aléatoire #{seed}", "Aucune contrainte violée", "Exception : " + ex.Message, false, sw.Elapsed);
        }
    }

    public static Groupage NewGroupage(string id, double l, double w, double h, double payload, double? doorW = null, double? doorH = null) =>
        new()
        {
            Id = id,
            Vehicle = new Vehicle
            {
                Id = "TEST", Length = l, Width = w, Height = h, MaxPayload = payload, DoorWidth = doorW, DoorHeight = doorH
            }
        };

    public static TransportOrder Box(string id, double l, double w, double h, double weight, int qty,
        bool stackable = false, int? levels = null, double? maxLoad = null, OrientationSet? orientations = null, int stop = 0) =>
        new()
        {
            Id = id, Article = id, Type = PhysicalType.Box, Quantity = qty, Length = l, Width = w, Height = h,
            UnitWeight = weight, Stackable = stackable, MaxLevels = levels, MaxLoadOnTop = maxLoad,
            Orientations = orientations ?? OrientationSet.Upright, Stop = stop
        };

    /// <summary>Renseigne la gestion des arrêts d'un ordre (magasin, étapes, client).</summary>
    public static TransportOrder Steps(this TransportOrder o, string warehouse, int departure, int arrival, string? customer = null)
    {
        o.Warehouse = warehouse;
        o.DepartureStep = departure;
        o.ArrivalStep = arrival;
        o.Customer = customer ?? o.Customer;
        return o;
    }

    private static string? NoBlocking(LoadPlan p) =>
        Expect(PlanValidator.Validate(p).Checks.First(c => c.Name.StartsWith("Accessibilité")).Status == CheckStatus.Ok,
            "une unité est bloquée à une étape");

    private static Groupage With(this Groupage g, params TransportOrder[] orders)
    {
        g.Orders.AddRange(orders);
        return g;
    }

    private static string? Expect(bool condition, string message) => condition ? null : message;

    private static string? Placed(LoadPlan plan, int expected) =>
        Expect(plan.Metrics.ItemsPlaced == expected, $"{plan.Metrics.ItemsPlaced} chargé(s) au lieu de {expected}");

    private static string? Reason(LoadPlan plan, RejectReason reason) =>
        Expect(plan.Unloaded.Count > 0 && plan.Unloaded.All(u => u.Reason == reason),
            $"motif attendu « {reason.Label()} », obtenu « {plan.Unloaded.FirstOrDefault()?.Reason.Label() ?? "aucun reliquat"} »");

    private static List<Scenario> BuildScenarios() =>
    [
        new("Référence unique gerbable",
            "10 cartons 1200×800×1000 Nmax 2 → tout chargé, piles de 2",
            () => NewGroupage("S01", 13600, 2450, 2700, 24000, 2450, 2650)
                .With(Box("CARTON", 1200, 800, 1000, 100, 10, true, 2, 500)),
            p => Placed(p, 10) ?? Expect(p.Placements.Max(x => x.Level) == 2, "piles de 2 attendues")),

        new("Limite exacte = véhicule",
            "Cube 1000 dans un véhicule 1000×1000×1000 → chargé",
            () => NewGroupage("S02", 1000, 1000, 1000, 500).With(Box("CUBE", 1000, 1000, 1000, 100, 1)),
            p => Placed(p, 1)),

        new("Dépassement d'un millimètre",
            "1001×1000×1000 dans 1000³ → refus dimensions",
            () => NewGroupage("S03", 1000, 1000, 1000, 500)
                .With(Box("CUBE+", 1001, 1000, 1000, 100, 1, orientations: OrientationSet.All)),
            p => Placed(p, 0) ?? Reason(p, RejectReason.TooLargeForVehicle)),

        new("Charge utile exactement atteinte",
            "10 × 100 kg pour Pmax 1000 kg → tout chargé",
            () => NewGroupage("S04", 13600, 2450, 2700, 1000).With(Box("LOURD", 500, 500, 500, 100, 10)),
            p => Placed(p, 10) ?? Expect(Math.Abs(p.Metrics.WeightRate - 100) < 0.01, "taux de charge 100 % attendu")),

        new("Dépassement d'un kilogramme",
            "10 × 100 kg + 1 kg pour Pmax 1000 kg → 1 refus poids",
            () => NewGroupage("S05", 13600, 2450, 2700, 1000)
                .With(Box("LOURD", 500, 500, 500, 100, 10), Box("PLUME", 300, 300, 300, 1, 1)),
            p => Placed(p, 10) ?? Reason(p, RejectReason.PayloadExceeded)),

        new("Non gerbables – plancher saturé",
            "8 caisses 1200×800 non gerbables, plancher 2400×2400 → 6 chargées au sol",
            () => NewGroupage("S06", 2400, 2400, 2700, 24000).With(Box("NG", 1200, 800, 1000, 50, 8)),
            p => Placed(p, 6) ?? Reason(p, RejectReason.FloorSaturatedNonStackable)
                 ?? Expect(p.Placements.All(x => x.OnFloor), "tout doit être au sol")),

        new("Empilement exactement Nmax",
            "4 caisses Nmax 3, hauteur 5000 → 3 empilées, 1 reliquat",
            () => NewGroupage("S07", 1200, 800, 5000, 24000).With(Box("N3", 1200, 800, 1000, 50, 4, true, 3, 10000)),
            p => Placed(p, 3) ?? Expect(p.Placements.Max(x => x.Level) == 3, "3 niveaux attendus")),

        new("Charge supportable atteinte",
            "3 × 100 kg, Psupport 200 kg → 3 empilées (200 kg sur la base)",
            () => NewGroupage("S08", 1200, 800, 5000, 24000).With(Box("PS", 1200, 800, 1000, 100, 3, true, 10, 200)),
            p => Placed(p, 3)),

        new("Charge supportable dépassée",
            "4 × 100 kg, Psupport 200 kg → la 4e refusée",
            () => NewGroupage("S09", 1200, 800, 5000, 24000).With(Box("PS", 1200, 800, 1000, 100, 4, true, 10, 200)),
            p => Placed(p, 3) ?? Expect(p.Placements.Max(x => x.LoadAbove) <= 200 + 1e-6, "charge > 200 kg")),

        new("Plaques avec reste de quantité",
            "1000 plaques ép. 5, 300/pile → 4 piles (300+300+300+100)",
            () => NewGroupage("S10", 13600, 2450, 2700, 24000, 2450, 2650).With(new TransportOrder
            {
                Id = "PLQ", Article = "PLAQUE-1600", Type = PhysicalType.Plaque, Quantity = 1000, Length = 1600, Width = 1200,
                Height = 5, UnitWeight = 2, Stackable = false, MaxPerPile = 300, Orientations = OrientationSet.Upright
            }),
            p => Placed(p, 1000) ?? Expect(p.Placements.Count == 4 && p.Placements.Count(x => x.Unit.ItemCount == 100) == 1,
                "4 piles dont une de 100 attendues")),

        new("Tubes gerbables en grille",
            "26 tubes Ø200 L6000 dans 6000×1000×1000 → grille 5×5 = 25",
            () => NewGroupage("S11", 6000, 1000, 1000, 24000).With(new TransportOrder
            {
                Id = "TUBE", Article = "TUBE-200", Type = PhysicalType.Tube, Quantity = 26, Length = 6000, Diameter = 200,
                UnitWeight = 30, Stackable = true, MaxLevels = 5, MaxLoadOnTop = 1000, Orientations = OrientationSet.O1
            }),
            p => Placed(p, 25)),

        new("Tubes non gerbables",
            "10 tubes Ø200 non gerbables → une seule rangée de 5",
            () => NewGroupage("S12", 6000, 1000, 1000, 24000).With(new TransportOrder
            {
                Id = "TUBE", Article = "TUBE-200", Type = PhysicalType.Tube, Quantity = 10, Length = 6000, Diameter = 200,
                UnitWeight = 30, Stackable = false, Orientations = OrientationSet.O1
            }),
            p => Placed(p, 5) ?? Expect(p.Placements.All(x => x.OnFloor), "tubes au sol uniquement")),

        new("Limité par le poids",
            "30 colis acier 1000 kg, Pmax 24 t → 24 chargés, motif poids",
            () => NewGroupage("S13", 13600, 2450, 2700, 24000, 2450, 2650)
                .With(Box("ACIER", 1000, 1000, 500, 1000, 30, true, 2, 1000)),
            p => Placed(p, 24) ?? Reason(p, RejectReason.PayloadExceeded)),

        new("Limité par le métrage linéaire",
            "40 palettes 1200×800 non gerbables en semi 13,60 m → ≥ 33 au sol",
            () => NewGroupage("S14", 13600, 2450, 2700, 30000, 2450, 2650).With(new TransportOrder
            {
                Id = "PAL", Article = "EUR", Type = PhysicalType.Pallet, Quantity = 40, Length = 1200, Width = 800, Height = 1500,
                UnitWeight = 300, Stackable = false, Orientations = OrientationSet.Upright
            }),
            p => Expect(p.Metrics.ItemsPlaced >= 33, $"{p.Metrics.ItemsPlaced} palettes seulement")
                 ?? Expect(p.Metrics.LinearMetersReal <= 13.6 + 1e-9, "ML réel > 13,60")
                 ?? Reason(p, RejectReason.FloorSaturatedNonStackable)),

        new("Limité par la hauteur",
            "2 caisses H1100 gerbables dans H2000 → 1 seule",
            () => NewGroupage("S15", 1200, 800, 2000, 24000).With(Box("HAUT", 1200, 800, 1100, 50, 2, true, 3, 500, OrientationSet.O1)),
            p => Placed(p, 1) ?? Reason(p, RejectReason.NoValidPosition)),

        new("Orientation unique incompatible",
            "2000×500×500 orientation 1 dans un véhicule de 1000 de long → refus orientation",
            () => NewGroupage("S16", 1000, 2500, 1000, 24000).With(Box("LONG", 2000, 500, 500, 50, 1, orientations: OrientationSet.O1)),
            p => Placed(p, 0) ?? Reason(p, RejectReason.NoAllowedOrientation)),

        new("Orientation alternative autorisée",
            "Même article avec orientations 1,2 → chargé en orientation 2",
            () => NewGroupage("S17", 1000, 2500, 1000, 24000).With(Box("LONG", 2000, 500, 500, 50, 1)),
            p => Placed(p, 1) ?? Expect(p.Placements[0].Orientation == 2, "orientation 2 attendue")),

        new("Produit très haut couché",
            "500×500×3000 toutes orientations, Hc 2700 → chargé couché",
            () => NewGroupage("S18", 13600, 2450, 2700, 24000)
                .With(Box("MAT", 500, 500, 3000, 80, 1, orientations: OrientationSet.All)),
            p => Placed(p, 1) ?? Expect(p.Placements[0].DZ <= 2700, "doit être couché")),

        new("Tient dedans mais pas par la porte",
            "1000×1800×1800 debout, porte 1500×1500 → refus porte",
            () => NewGroupage("S19", 3000, 2000, 2000, 24000, 1500, 1500).With(Box("GROS", 1000, 1800, 1800, 200, 1)),
            p => Placed(p, 0) ?? Reason(p, RejectReason.DoorPassage)),

        new("Ordre de livraison (3 arrêts)",
            "Arrêt 1 côté porte, arrêt 3 à l'avant, aucun blocage",
            () => NewGroupage("S20", 13600, 2450, 2700, 24000, 2450, 2650).With(
                Box("A1", 1200, 800, 1200, 200, 6, stop: 1),
                Box("A2", 1200, 1000, 1000, 150, 6, true, 2, 400, stop: 2),
                Box("A3", 800, 600, 900, 90, 10, true, 3, 300, stop: 3)),
            p => Placed(p, 22)
                 ?? Expect(PlanValidator.Validate(p).Checks.First(c => c.Name.StartsWith("Accessibilité")).Status == CheckStatus.Ok,
                     "un arrêt est bloqué")
                 ?? Expect(p.Placements.Where(x => x.Unit.Order.Stop == 3).Max(x => x.X) <
                           p.Placements.Where(x => x.Unit.Order.Stop == 1).Min(x => x.X) + 1, "arrêt 3 doit être devant")),

        new("Cartons de tailles différentes",
            "Mélange gerbable / non gerbable / au sommet → plan valide",
            () => NewGroupage("S21", 13600, 2450, 2700, 24000, 2450, 2650).With(
                Box("GRAND", 1200, 1000, 900, 120, 8, true, 3, 500),
                Box("MOYEN", 800, 600, 600, 40, 20, true, 4, 200),
                Box("PETIT", 400, 300, 300, 8, 40, true, 5, 60),
                Box("NG", 1000, 1000, 1200, 300, 4),
                new TransportOrder
                {
                    Id = "FRAGILE", Article = "FRAGILE", Type = PhysicalType.Box, Quantity = 6, Length = 600, Width = 400,
                    Height = 400, UnitWeight = 5, Stackable = true, MaxLevels = 4, MaxLoadOnTop = 0, MustBeOnTop = true,
                    Orientations = OrientationSet.Upright
                }),
            p => Placed(p, 78)),

        new("Bobine axe vertical",
            "Bobines Ø1200 × 1000, orientation par défaut axe vertical → au sol",
            () => NewGroupage("S22", 13600, 2450, 2700, 24000, 2450, 2650).With(new TransportOrder
            {
                Id = "BOB", Article = "BOBINE", Type = PhysicalType.Roll, Quantity = 6, Diameter = 1200, Width = 1000,
                UnitWeight = 800, Stackable = false
            }),
            p => Placed(p, 6) ?? Expect(p.Placements.All(x => x.CylinderAxis == 2), "axe vertical attendu")),

        new("Données indispensables manquantes",
            "Ordre sans poids → non chargé, motif données",
            () =>
            {
                var g = NewGroupage("S23", 13600, 2450, 2700, 24000);
                var o = Box("SANSPOIDS", 1000, 1000, 1000, 0, 2);
                o.BlockingErrors.Add("poids unitaire absent");
                return g.With(o);
            },
            p => Placed(p, 0) ?? Reason(p, RejectReason.InvalidData)),

        new("Multi-camions : reliquat plancher",
            "40 palettes non gerbables, 3 camions max → 2 camions, tout chargé",
            () => NewGroupage("S24", 13600, 2450, 2700, 30000, 2450, 2650).With(new TransportOrder
            {
                Id = "PAL", Article = "EUR", Type = PhysicalType.Pallet, Quantity = 40, Length = 1200, Width = 800, Height = 1500,
                UnitWeight = 300, Stackable = false
            }),
            p => Placed(p, 40) ?? Expect(p.Loads.Count == 2, $"{p.Loads.Count} camion(s) au lieu de 2")
                 ?? Expect(p.Loads[0].Placements.Count >= 33, "le camion 1 doit être rempli en premier"),
            new PackingOptions { MaxVehicles = 3 }),

        new("Multi-camions : reliquat poids",
            "30 colis de 1000 kg, Pmax 24 t → 24 + 6 sur 2 camions",
            () => NewGroupage("S25", 13600, 2450, 2700, 24000, 2450, 2650)
                .With(Box("ACIER", 1000, 1000, 500, 1000, 30, true, 2, 1000)),
            p => Placed(p, 30) ?? Expect(p.Loads.Count == 2 && p.Loads[0].Placements.Count == 24, "répartition 24 + 6 attendue")
                 ?? Expect(p.Loads.All(l => l.Metrics.LoadedWeightKg <= 24000), "charge utile dépassée"),
            new PackingOptions { MaxVehicles = 3 }),

        new("Multi-camions : impossible par nature",
            "Caisse trop haute pour la porte + 40 palettes → pas de camion de plus pour la caisse",
            () => NewGroupage("S26", 13600, 2450, 2700, 30000, 2450, 2500).With(
                Box("GROS", 1000, 1000, 2600, 200, 1),
                new TransportOrder
                {
                    Id = "PAL", Article = "EUR", Type = PhysicalType.Pallet, Quantity = 40, Length = 1200, Width = 800, Height = 1500,
                    UnitWeight = 300, Stackable = false
                }),
            p => Placed(p, 40) ?? Expect(p.Loads.Count == 2, $"{p.Loads.Count} camion(s) au lieu de 2") ?? Reason(p, RejectReason.DoorPassage),
            new PackingOptions { MaxVehicles = 5 }),

        new("Multi-camions : maximum atteint",
            "100 palettes non gerbables, 2 camions max → reliquat motivé",
            () => NewGroupage("S27", 13600, 2450, 2700, 30000, 2450, 2650).With(new TransportOrder
            {
                Id = "PAL", Article = "EUR", Type = PhysicalType.Pallet, Quantity = 100, Length = 1200, Width = 800, Height = 1500,
                UnitWeight = 200, Stackable = false
            }),
            p => Expect(p.Loads.Count == 2, "2 camions attendus") ?? Expect(p.Metrics.ItemsRemaining > 0, "reliquat attendu")
                 ?? Expect(p.Issues.Any(i => i.Code == "CAMIONS_MAX"), "alerte maximum de camions attendue"),
            new PackingOptions { MaxVehicles = 2 }),

        // ---------- Matrice par famille physique (résultats calculés à la main) ----------

        new("BOX · grille exacte 3 niveaux",
            "Cartons 600×400×400 Nmax 3 dans 2400×1200×1200 : 12 au sol × 3 = 36 (maximum géométrique)",
            () => NewGroupage("F01", 2400, 1200, 1200, 24000).With(Box("C40", 600, 400, 400, 10, 40, true, 3, 1000)),
            p => Placed(p, 36) ?? Expect(p.Placements.Max(x => x.Level) == 3, "3 niveaux attendus")),

        new("PALETTE · EUR gerbées 2 niveaux",
            "70 palettes 1200×800×1200 Nmax 2 en semi : ≥ 66 (33 à 34 au sol × 2)",
            () => NewGroupage("F02", 13600, 2450, 2700, 30000, 2450, 2650).With(new TransportOrder
            {
                Id = "EUR", Article = "EUR", Type = PhysicalType.Pallet, Quantity = 70, Length = 1200, Width = 800, Height = 1200,
                UnitWeight = 300, Stackable = true, MaxLevels = 2, MaxLoadOnTop = 400
            }),
            p => Expect(p.Metrics.ItemsPlaced >= 66, $"{p.Metrics.ItemsPlaced} palettes seulement")
                 ?? Expect(p.Placements.All(x => x.Level <= 2), "plus de 2 niveaux")),

        new("PLAQUE · piles gerbées",
            "1 500 plaques ép. 5, 100/pile, Nmax 3, véhicule 3200×1200×1600 : 2 piles au sol × 3 = 600 plaques",
            () => NewGroupage("F03", 3200, 1200, 1600, 24000).With(new TransportOrder
            {
                Id = "PLQ", Article = "PLQ", Type = PhysicalType.Plaque, Quantity = 1500, Length = 1600, Width = 1200, Height = 5,
                UnitWeight = 2, Stackable = true, MaxLevels = 3, MaxLoadOnTop = 2000, MaxPerPile = 100
            }),
            p => Placed(p, 600) ?? Expect(p.Placements.Count == 6, "6 piles attendues")),

        new("TUBE · aucune caisse sur des tubes couchés",
            "5 tubes Ø200 couvrent le plancher 6000×1000 : les caisses ne peuvent pas être posées dessus",
            () => NewGroupage("F04", 6000, 1000, 1000, 24000).With(
                new TransportOrder
                {
                    Id = "TUB", Article = "TUBE", Type = PhysicalType.Tube, Quantity = 5, Length = 6000, Diameter = 200,
                    UnitWeight = 40, Stackable = true, MaxLevels = 2, MaxLoadOnTop = 500, Orientations = OrientationSet.O1, MustBeOnFloor = true
                },
                Box("CAISSE", 1000, 1000, 300, 50, 2, true, 2, 500)),
            p => Expect(p.Placements.All(x => x.Supports.All(s => s.Support.CylinderAxis is not (0 or 1) || x.Unit.Shape == UnitShape.Cylinder)),
                     "une caisse repose sur un tube couché")
                 ?? Expect(p.Placements.Count(x => x.Unit.Order.Id == "TUB") == 5, "les 5 tubes doivent être chargés")),

        new("TUBE · quinconce plus dense que la grille",
            "30 tubes Ø200 L6000, véhicule 6000×1100×1100 : grille 25, quinconce 6 rangées × 5 = 30",
            () => NewGroupage("F05", 6000, 1100, 1100, 24000).With(new TransportOrder
            {
                Id = "TUB", Article = "TUBE", Type = PhysicalType.Tube, Quantity = 30, Length = 6000, Diameter = 200,
                UnitWeight = 30, Stackable = true, MaxLevels = 10, MaxLoadOnTop = 1000, Orientations = OrientationSet.O1
            }),
            p => Placed(p, 30) ?? Expect(p.Placements.Any(x => x.Unit.Shape == UnitShape.Staggered), "lit en quinconce attendu")
                 ?? Expect(p.Placements.All(x => x.MaxZ <= 1100), "hauteur dépassée")),

        new("TUBE · grille conservée si plus dense",
            "Ø200 dans 6000×1000×1000, Nmax 5 : grille 25 ≥ quinconce 23 → tubes individuels",
            () => NewGroupage("F06", 6000, 1000, 1000, 24000).With(new TransportOrder
            {
                Id = "TUB", Article = "TUBE", Type = PhysicalType.Tube, Quantity = 25, Length = 6000, Diameter = 200,
                UnitWeight = 30, Stackable = true, MaxLevels = 5, MaxLoadOnTop = 1000, Orientations = OrientationSet.O1
            }),
            p => Placed(p, 25) ?? Expect(p.Placements.All(x => x.Unit.Shape == UnitShape.Cylinder), "pas de lit en quinconce attendu")),

        new("TUBE · quinconce limité par la charge supportable",
            "Tubes 100 kg, 200 kg supportables : 3 rangées maximum",
            () => NewGroupage("F07", 6000, 1100, 2000, 24000).With(new TransportOrder
            {
                Id = "TUB", Article = "TUBE", Type = PhysicalType.Tube, Quantity = 15, Length = 6000, Diameter = 200,
                UnitWeight = 100, Stackable = true, MaxLevels = 10, MaxLoadOnTop = 200, Orientations = OrientationSet.O1
            }),
            p => Placed(p, 15) ?? Expect(p.Placements.All(x => x.MaxZ <= 600 + 1e-6), "plus de 3 rangées de tubes")),

        new("BOBINE · couchée, rien dessus",
            "Bobines Ø1000 couchées + cartons : rien n'est posé sur une bobine couchée",
            () => NewGroupage("F08", 2000, 1000, 2000, 24000).With(
                new TransportOrder
                {
                    Id = "BOB", Article = "BOBINE", Type = PhysicalType.Roll, Quantity = 3, Diameter = 1000, Width = 1000,
                    UnitWeight = 600, Stackable = true, MaxLevels = 2, MaxLoadOnTop = 2000, Orientations = OrientationSet.O3
                },
                Box("CARTON", 500, 500, 500, 10, 4, true, 3, 100)),
            p => Expect(p.Placements.All(x => x.Supports.All(s => s.Support.Unit.Order.Type != PhysicalType.Roll)),
                     "une unité est posée sur une bobine couchée")
                 ?? Expect(p.Placements.Where(x => x.Unit.Order.Id == "BOB").All(x => x.OnFloor), "bobine couchée hors du plancher")
                 ?? Expect(p.Metrics.ItemsRemaining <= 2, "au plus 2 articles en reliquat attendus")),

        new("BOBINE · axe vertical gerbée",
            "4 bobines Ø1000 debout, Nmax 2, véhicule 2000×1000×2000 : 2 au sol × 2 niveaux",
            () => NewGroupage("F09", 2000, 1000, 2000, 24000).With(new TransportOrder
            {
                Id = "BOB", Article = "BOBINE", Type = PhysicalType.Roll, Quantity = 4, Diameter = 1000, Width = 800,
                UnitWeight = 500, Stackable = true, MaxLevels = 2, MaxLoadOnTop = 1000
            }),
            p => Placed(p, 4) ?? Expect(p.Placements.Max(x => x.Level) == 2, "2 niveaux attendus")),

        new("FAISCEAU · bloc indivisible",
            "4 faisceaux 5000×600×400 Nmax 3 dans 5000×1200×1300 : 2 de front × 3 niveaux, les 4 chargés",
            () => NewGroupage("F10", 5000, 1200, 1300, 24000).With(new TransportOrder
            {
                Id = "FSC", Article = "FAISCEAU", Type = PhysicalType.Bundle, Quantity = 4, Length = 5000, Width = 600, Height = 400,
                UnitWeight = 380, Stackable = true, MaxLevels = 3, MaxLoadOnTop = 1200
            }),
            p => Placed(p, 4) ?? Expect(p.Placements.All(x => x.Level <= 3), "plus de 3 niveaux")),

        new("PALETTE · jamais couchée",
            "Palette 1200×800×2800 dans une hauteur de 2700 : refus (seule une rotation au sol est permise)",
            () => NewGroupage("F11", 13600, 2450, 2700, 24000).With(new TransportOrder
            {
                Id = "PAL", Article = "HAUTE", Type = PhysicalType.Pallet, Quantity = 1, Length = 1200, Width = 800, Height = 2800,
                UnitWeight = 300, Stackable = false
            }),
            p => Placed(p, 0) ?? Reason(p, RejectReason.NoAllowedOrientation)),

        new("CUSTOM · enveloppe parallélépipédique",
            "2 unités spéciales 900×900×900 non gerbables : chargées au sol",
            () => NewGroupage("F12", 2000, 1000, 1000, 24000).With(new TransportOrder
            {
                Id = "SPE", Article = "MACHINE", Type = PhysicalType.Custom, Quantity = 2, Length = 900, Width = 900, Height = 900,
                UnitWeight = 400, Stackable = false
            }),
            p => Placed(p, 2) ?? Expect(p.Placements.All(x => x.OnFloor), "au sol attendu")),

        new("Rotation au sol interdite",
            "2 palettes 1730×1370 : 3,46 m dans la longueur (rotation interdite), 2,74 m en travers (autorisée)",
            () => NewGroupage("F13", 13600, 2450, 2400, 25000).With(new TransportOrder
            {
                Id = "PAL", Article = "PS00124", Type = PhysicalType.Pallet, Quantity = 2, Length = 1730, Width = 1370, Height = 1220,
                UnitWeight = 300, Stackable = false
            }),
            p => Expect(Math.Abs(p.Metrics.LinearMetersReal - 3.46) < 1e-6, $"ML réel {p.Metrics.LinearMetersReal:0.00} m au lieu de 3,46 m"),
            new PackingOptions { AllowFloorRotation = false }),

        // ---------- Débords ----------

        new("Débord entre unités",
            "Caisses 500×500 dans 2000×1000, débord 100 mm : 3 caisses au lieu de 8",
            () => NewGroupage("D01", 2000, 1000, 1000, 24000).With(Box("C50", 500, 500, 500, 20, 10)),
            p => Placed(p, 3) ?? Expect(p.Placements.Count(x => x.X < 1) == 1, "une seule caisse par rangée attendue"),
            new PackingOptions { GapBetweenUnits = 100 }),

        new("Débord parois latérales",
            "Semi 2450 de large, débord parois 25 mm : 2 palettes de 1200 tiennent encore de front (34), toutes à 25 mm des parois",
            () => NewGroupage("D02", 13600, 2450, 2700, 30000, 2450, 2650).With(new TransportOrder
            {
                Id = "EUR", Article = "EUR", Type = PhysicalType.Pallet, Quantity = 40, Length = 1200, Width = 800, Height = 1500,
                UnitWeight = 300, Stackable = false
            }),
            p => Placed(p, 34) ?? Expect(p.Placements.All(x => x.Y >= 25 - 1e-6 && x.MaxY <= 2425 + 1e-6), "palette trop près d'une paroi"),
            new PackingOptions { SideClearance = 25 }),

        new("Débord parois : une palette de front en moins",
            "Débord parois 30 mm : 2 × 1200 = 2400 > 2390 utiles, moins de 34 palettes",
            () => NewGroupage("D03", 13600, 2450, 2700, 30000, 2450, 2650).With(new TransportOrder
            {
                Id = "EUR", Article = "EUR", Type = PhysicalType.Pallet, Quantity = 40, Length = 1200, Width = 800, Height = 1500,
                UnitWeight = 300, Stackable = false
            }),
            p => Expect(p.Metrics.ItemsPlaced is >= 22 and < 34, $"{p.Metrics.ItemsPlaced} palettes (attendu entre 22 et 33)"),
            new PackingOptions { SideClearance = 30 }),

        new("Débord plafond",
            "Palettes de 1300 gerbables, hauteur 2700, débord plafond 150 mm : 2600 > 2550, plus de gerbage",
            () => NewGroupage("D04", 1200, 800, 2700, 24000).With(Box("P13", 1200, 800, 1300, 200, 2, true, 2, 1000, OrientationSet.O1)),
            p => Placed(p, 1) ?? Expect(p.Placements.All(x => x.MaxZ <= 2550 + 1e-6), "plafond non respecté"),
            new PackingOptions { RoofClearance = 150 }),

        new("Débords nuls = comportement inchangé",
            "Même chargement sans débord : les 2 palettes de 1300 se gerbent (2600 ≤ 2700)",
            () => NewGroupage("D05", 1200, 800, 2700, 24000).With(Box("P13", 1200, 800, 1300, 200, 2, true, 2, 1000, OrientationSet.O1)),
            p => Placed(p, 2)),

        // ---------- Gestion des arrêts ----------

        new("Arrêts · itinéraire CPF / OPF",
            "26090007 : -1 et -3 chargés CPF (étape 1) livrés étape 4, -2 chargé OPF (étape 2) livré étape 3 → -2 côté porte",
            () => NewGroupage("A01", 13600, 2450, 2400, 25000).With(
                Box("26090007-1", 1200, 800, 1100, 250, 6).Steps("CPF", 1, 4, "C00022-002"),
                Box("26090007-2", 1200, 1000, 1200, 300, 4).Steps("OPF", 2, 3, "C00720-001"),
                Box("26090007-3", 1200, 800, 1100, 250, 4).Steps("CPF", 1, 4, "C00022-002")),
            p => Placed(p, 14) ?? NoBlocking(p)
                 ?? Expect(p.Placements.Where(x => x.Unit.Order.Id == "26090007-2").Min(x => x.X) >=
                           p.Placements.Where(x => x.Unit.Order.Id != "26090007-2").Max(x => x.MaxX) - 1e-6,
                     "les palettes OPF (livrées en premier, chargées en dernier) doivent être côté porte"),
            new PackingOptions { Stowage = StowageMode.Stops }),

        new("Arrêts · itinéraire impossible sur une seule file",
            "Plancher d'une palette de large : A (1 → 3) puis B (2 → 4) se bloquent forcément → B en reliquat (contrainte stricte)",
            () => NewGroupage("A02", 6000, 800, 2400, 25000).With(
                Box("A", 1200, 800, 1000, 200, 1, orientations: OrientationSet.O1).Steps("CPF", 1, 3),
                Box("B", 1200, 800, 1000, 200, 1, orientations: OrientationSet.O1).Steps("OPF", 2, 4)),
            p => Placed(p, 1) ?? Reason(p, RejectReason.StowageBlocked),
            new PackingOptions { Stowage = StowageMode.Stops }),

        new("Arrêts · itinéraire impossible → camion supplémentaire",
            "Même cas avec 2 camions autorisés : B part dans le camion 2",
            () => NewGroupage("A03", 6000, 800, 2400, 25000).With(
                Box("A", 1200, 800, 1000, 200, 1, orientations: OrientationSet.O1).Steps("CPF", 1, 3),
                Box("B", 1200, 800, 1000, 200, 1, orientations: OrientationSet.O1).Steps("OPF", 2, 4)),
            p => Placed(p, 2) ?? Expect(p.Loads.Count == 2, "2 camions attendus"),
            new PackingOptions { Stowage = StowageMode.Stops, MaxVehicles = 2 }),

        new("Arrêts · gerbage dans l'ordre de livraison",
            "Palettes gerbables livrées aux étapes 3 et 4 : celle de l'étape 4 au sol, celle de l'étape 3 dessus",
            () => NewGroupage("A04", 1200, 800, 2400, 25000).With(
                Box("BAS", 1200, 800, 1000, 200, 1, true, 2, 1000, OrientationSet.O1).Steps("CPF", 1, 3),
                Box("HAUT", 1200, 800, 1000, 200, 1, true, 2, 1000, OrientationSet.O1).Steps("CPF", 1, 4)),
            p => Placed(p, 2) ?? NoBlocking(p)
                 ?? Expect(p.Placements.Single(x => x.Unit.Order.Id == "BAS").Z > 0, "la palette livrée en premier doit être au-dessus"),
            new PackingOptions { Stowage = StowageMode.Stops }),

        new("Rangement compact",
            "Même cas qu'A02 en rangement compact : pas de contrainte d'accès, les 2 palettes sont chargées",
            () => NewGroupage("A05", 6000, 800, 2400, 25000).With(
                Box("A", 1200, 800, 1000, 200, 1, orientations: OrientationSet.O1).Steps("CPF", 1, 3),
                Box("B", 1200, 800, 1000, 200, 1, orientations: OrientationSet.O1).Steps("OPF", 2, 4)),
            p => Placed(p, 2) ?? Expect(p.Loads.Count == 1, "1 camion attendu"),
            new PackingOptions { Stowage = StowageMode.Compact }),

        new("Rangement par client",
            "3 clients : chaque client en zone continue de l'avant vers la porte, sans gerbage entre clients",
            () => NewGroupage("A06", 13600, 2450, 2700, 25000).With(
                Box("C1-A", 1200, 800, 1000, 150, 4, true, 2, 500).Steps("", 0, 0, "CLIENT 1"),
                Box("C2-A", 1200, 800, 1000, 150, 5, true, 2, 500).Steps("", 0, 0, "CLIENT 2"),
                Box("C1-B", 800, 600, 600, 40, 6, true, 3, 200).Steps("", 0, 0, "CLIENT 1"),
                Box("C3-A", 1200, 1000, 1200, 200, 3, true, 2, 600).Steps("", 0, 0, "CLIENT 3")),
            p => Placed(p, 18)
                 ?? Expect(PlanValidator.Validate(p).Checks.First(c => c.Name == "Rangement par zone").Status == CheckStatus.Ok, "zones mélangées")
                 ?? Expect(p.Placements.Where(x => x.Unit.Order.Customer == "CLIENT 1").Max(x => x.X) <
                           p.Placements.Where(x => x.Unit.Order.Customer == "CLIENT 3").Min(x => x.MaxX), "client 1 doit être devant le client 3"),
            new PackingOptions { Stowage = StowageMode.Customer })
    ];
}
