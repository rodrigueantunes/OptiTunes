using System.Globalization;
using System.Text;
using OptiTunes.Core.Engine;
using OptiTunes.Core.Models;
using OptiTunes.Core.Validation;

namespace OptiTunes.Core.Export;

/// <summary>Nature d'une ligne d'explication (mise en forme dans l'application).</summary>
public enum DetailKind
{
    /// <summary>Phrase d'explication.</summary>
    Text,

    /// <summary>Sous-titre (un ordre, les totaux…).</summary>
    Heading,

    /// <summary>Calcul posé avec ses chiffres : « libellé = formule = résultat ».</summary>
    Formula,

    /// <summary>Résultat retenu à l'issue de l'étape.</summary>
    Result,

    /// <summary>Précision secondaire.</summary>
    Note,

    /// <summary>Point d'attention (donnée manquante, reliquat…).</summary>
    Warning,

    /// <summary>Anomalie (contrôle en échec).</summary>
    Error
}

public sealed record DetailLine(string Text, DetailKind Kind = DetailKind.Text);

/// <summary>Tableau de chiffres d'une section (une ligne par ordre, par camion…).</summary>
public sealed record DetailTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>Étape du calcul : numéro, titre, à quoi elle sert, lignes de calcul et tableau éventuel.</summary>
public sealed record DetailSection(int Number, string Title, string Purpose, IReadOnlyList<DetailLine> Lines, DetailTable? Table = null)
{
    public string Header => Number > 0 ? $"{Number}. {Title}" : Title;
    public bool HasTable => Table is { Rows.Count: > 0 };

    /// <summary>Lignes avant le premier sous-titre (toujours visibles).</summary>
    public IReadOnlyList<DetailLine> Intro => Lines.TakeWhile(l => l.Kind != DetailKind.Heading).ToList();

    /// <summary>Blocs repliables : un par sous-titre (un ordre, les totaux…), avec les lignes qui le suivent.</summary>
    public IReadOnlyList<DetailBlock> Blocks
    {
        get
        {
            var blocks = new List<DetailBlock>();
            string? title = null;
            var lines = new List<DetailLine>();
            foreach (var line in Lines.SkipWhile(l => l.Kind != DetailKind.Heading))
            {
                if (line.Kind == DetailKind.Heading)
                {
                    if (title != null)
                    {
                        blocks.Add(new DetailBlock(title, lines));
                    }

                    title = line.Text;
                    lines = [];
                }
                else
                {
                    lines.Add(line);
                }
            }

            if (title != null)
            {
                blocks.Add(new DetailBlock(title, lines));
            }

            return blocks;
        }
    }
}

/// <summary>Bloc repliable d'une section : sous-titre et ses lignes de calcul.</summary>
public sealed record DetailBlock(string Title, IReadOnlyList<DetailLine> Lines);

/// <summary>
/// Détail complet du calcul d'un plan, en français et avec les chiffres du groupage : véhicule et espace chargeable,
/// ordres, unités de chargement, niveaux de gerbage, métrage linéaire théorique, rangement, placement, choix de la
/// solution, résultat de chaque camion, reliquat, contrôles et lexique. Chaque étape suit le moteur pas à pas.
/// </summary>
public static class CalculationDetails
{
    // Séparateur de milliers : espace insécable « normale » (l'espace fine se lit mal à l'écran).
    private static readonly CultureInfo Fr = FrenchCulture();

    private static CultureInfo FrenchCulture()
    {
        var c = (CultureInfo)CultureInfo.GetCultureInfo("fr-FR").Clone();
        c.NumberFormat.NumberGroupSeparator = "\u00A0";
        return c;
    }

    private static string Mm(double v) => v.ToString("#,0", Fr);
    private static string Kg(double v) => v.ToString("#,0.##", Fr);
    private static string M(double v) => v.ToString("#,0.00", Fr);
    private static string M2(double v) => v.ToString("#,0.00", Fr);
    private static string M3(double v) => v.ToString("#,0.00", Fr);
    private static string Pct(double v) => v.ToString("0.#", Fr) + " %";
    private static string N(int v) => v.ToString("#,0", Fr);
    private static string R(double v) => v.ToString("#,0.###", Fr);

    private const int MaxTerms = 12;

    /// <param name="plan">Solution affichée.</param>
    /// <param name="report">Contrôle indépendant de cette solution (null : recalculé).</param>
    /// <param name="solutions">Toutes les solutions proposées, de la meilleure à la moins bonne (comparaison).</param>
    public static List<DetailSection> Build(LoadPlan plan, ValidationReport? report = null, IReadOnlyList<LoadPlan>? solutions = null)
    {
        report ??= PlanValidator.Validate(plan);
        var ctx = new Context(plan);
        var sections = new List<DetailSection> { Summary(ctx, report) };
        var number = 1;
        void Add(DetailSection? s)
        {
            if (s != null)
            {
                sections.Add(s with { Number = number++ });
            }
        }

        Add(VehicleSection(ctx));
        Add(OrdersSection(ctx));
        Add(UnitsSection(ctx));
        Add(LevelsSection(ctx));
        Add(LinearMetersSection(ctx));
        Add(StowageSection(ctx));
        Add(PlacementSection(ctx));
        Add(SolutionChoiceSection(ctx, solutions));
        foreach (var load in plan.Loads)
        {
            Add(LoadSection(ctx, load));
        }

        Add(UnloadedSection(ctx));
        Add(ChecksSection(report));
        Add(RecapSection(ctx));
        Add(Glossary());
        return sections;
    }

    /// <summary>Texte brut (copie dans le presse-papiers, courriel).</summary>
    public static string ToText(IEnumerable<DetailSection> sections)
    {
        var sb = new StringBuilder();
        foreach (var s in sections)
        {
            sb.AppendLine(s.Header.ToUpper(Fr));
            sb.AppendLine(s.Purpose);
            foreach (var l in s.Lines)
            {
                var prefix = l.Kind switch
                {
                    DetailKind.Formula => "    ",
                    DetailKind.Heading => "  # ",
                    DetailKind.Result => "  => ",
                    DetailKind.Warning => "  ! ",
                    DetailKind.Error => "  X ",
                    DetailKind.Note => "    (",
                    _ => "  - "
                };
                sb.AppendLine(prefix + l.Text + (l.Kind == DetailKind.Note ? ")" : ""));
            }

            if (s.Table is { Rows.Count: > 0 } t)
            {
                sb.AppendLine("  " + string.Join(" | ", t.Headers));
                foreach (var row in t.Rows)
                {
                    sb.AppendLine("  " + string.Join(" | ", row));
                }
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------ Contexte

    /// <summary>Données communes : espace chargeable, unités par ordre, besoin en métrage.</summary>
    private sealed class Context
    {
        public Context(LoadPlan plan)
        {
            Plan = plan;
            Options = plan.Options;
            Vehicle = plan.Vehicle;
            Usable = Options.UsableSpace(Vehicle);
            Gap = Math.Max(0, Options.GapBetweenUnits);
            UnitsByOrder = plan.Placements.Select(p => p.Unit)
                .Concat(plan.Unloaded.Select(u => u.Unit))
                .GroupBy(u => u.Order)
                .ToDictionary(g => g.Key, g => g.OrderBy(u => u.Index).ToList());
        }

        public LoadPlan Plan { get; }
        public PackingOptions Options { get; }
        public Vehicle Vehicle { get; }
        public Vehicle Usable { get; }
        public double Gap { get; }
        public Dictionary<TransportOrder, List<PhysicalUnit>> UnitsByOrder { get; }
        public IReadOnlyList<TransportOrder> Orders => Plan.Groupage.Orders;

        public List<PhysicalUnit> UnitsOf(TransportOrder o) => UnitsByOrder.GetValueOrDefault(o) ?? [];
    }

    private sealed class Lines : List<DetailLine>
    {
        public void T(string s) => Add(new(s));
        public void H(string s) => Add(new(s, DetailKind.Heading));
        public void F(string s) => Add(new(s, DetailKind.Formula));
        public void R(string s) => Add(new(s, DetailKind.Result));
        public void N(string s) => Add(new(s, DetailKind.Note));
        public void W(string s) => Add(new(s, DetailKind.Warning));
        public void E(string s) => Add(new(s, DetailKind.Error));
    }

    /// <summary>« a + b + c = total » ; au-delà de <see cref="MaxTerms"/> termes, seul le total.</summary>
    private static string Sum(IReadOnlyList<string> terms, string total) =>
        terms.Count == 0 ? total
        : terms.Count == 1 ? total
        : terms.Count <= MaxTerms ? $"{string.Join(" + ", terms)} = {total}"
        : $"{string.Join(" + ", terms.Take(MaxTerms))} + … ({N(terms.Count)} termes) = {total}";

    // ------------------------------------------------------------------ 0. En résumé

    private static DetailSection Summary(Context ctx, ValidationReport report)
    {
        var p = ctx.Plan;
        var m = p.Metrics;
        var g = p.Groupage;
        var l = new Lines();
        var weight = g.Orders.Where(o => o.UnitWeight > 0).Sum(o => o.Quantity * o.UnitWeight);
        l.T($"Groupage {g.Id} : {N(g.Orders.Count)} ordre(s) de transport, {N(m.ItemsTotal)} article(s), {Kg(weight)} kg à charger " +
            $"dans un véhicule {VehicleName(ctx.Vehicle)} de {Mm(ctx.Vehicle.Length)} × {Mm(ctx.Vehicle.Width)} × {Mm(ctx.Vehicle.Height)} mm.");
        l.T($"Solution affichée : « {p.Name} »{(p.IsRecommended ? ", la solution recommandée" : "")} ; rangement « {p.Options.Stowage.Label()} ».");
        if (m.ItemsRemaining == 0)
        {
            l.R($"Tout est chargé : {N(m.ItemsPlaced)} article(s) sur {N(p.Loads.Count)} camion(s).");
        }
        else
        {
            l.W($"{N(m.ItemsPlaced)} article(s) chargé(s) sur {N(m.ItemsTotal)} ; reliquat de {N(m.ItemsRemaining)} article(s). Raison principale : {m.LimitingFactor}.");
        }

        l.R($"Métrage linéaire réel occupé : {M(m.LinearMetersReal)} m sur {M(m.VehicleLinearMeters)} m disponibles ({Pct(m.LinearRealRate)}) ; " +
            $"besoin théorique {M(m.LinearMetersRequiredEquivalent)} m.");
        l.T($"Poids chargé : {Kg(m.LoadedWeightKg)} kg ({Pct(m.WeightRate)} de la charge utile) ; minimum théorique : {N(m.EstimatedVehicles)} camion(s).");
        if (report.IsValid)
        {
            l.R(report.Warnings == 0
                ? $"Contrôle indépendant : plan conforme ({N(report.Checks.Count)} règles vérifiées)."
                : $"Contrôle indépendant : plan conforme, {N(report.Warnings)} point(s) d'attention.");
        }
        else
        {
            l.E($"Contrôle indépendant : {N(report.Errors)} règle(s) en échec — voir la section « Contrôles du plan ».");
        }

        l.N("Les sections suivantes reprennent chaque étape dans l'ordre où OptiTunes la réalise, avec les valeurs de ce groupage. " +
            "Dimensions en mm, poids en kg, métrages en mètres.");
        return new(0, "En résumé", "Ce qu'il faut retenir du calcul, en quelques lignes.", l);
    }

    private static string VehicleName(Vehicle v) =>
        string.Join(" ", new[] { v.Id, v.Type }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()).Trim() is { Length: > 0 } s ? s : "(sans nom)";

    // ------------------------------------------------------------------ 1. Véhicule

    private static DetailSection VehicleSection(Context ctx)
    {
        var v = ctx.Vehicle;
        var u = ctx.Usable;
        var o = ctx.Options;
        var l = new Lines();
        l.T($"Véhicule {VehicleName(v)} : intérieur utile {Mm(v.Length)} mm de long, {Mm(v.Width)} mm de large, {Mm(v.Height)} mm de haut ; " +
            $"charge utile {Kg(v.MaxPayload)} kg.");
        if (v.HasDoor)
        {
            l.T($"Ouverture arrière {Mm(v.DoorWidth!.Value)} × {Mm(v.DoorHeight!.Value)} mm : chaque unité doit pouvoir passer (largeur et hauteur de sa section).");
        }
        else
        {
            l.W("Ouverture arrière non renseignée : le passage des unités par la porte n'est pas contrôlé.");
        }

        l.F($"Largeur chargeable = largeur − 2 × débord parois = {Mm(v.Width)} − 2 × {Mm(o.SideClearance)} = {Mm(u.Width)} mm");
        l.F($"Hauteur chargeable = hauteur − débord plafond = {Mm(v.Height)} − {Mm(o.RoofClearance)} = {Mm(u.Height)} mm");
        l.T($"Longueur chargeable = {Mm(v.Length)} mm, de la cabine (X = 0) à la porte.");
        l.T(ctx.Gap > 0
            ? $"Jeu entre unités voisines : {Mm(ctx.Gap)} mm, bout à bout et côte à côte (pas entre unités gerbées, qui restent en contact)."
            : "Jeu entre unités voisines : 0 mm (unités jointives).");
        l.F($"Métrage linéaire du camion = longueur / 1 000 = {Mm(v.Length)} / 1 000 = {M(v.LinearMeters)} m");
        l.F($"Surface au sol = {Mm(v.Length)} × {Mm(v.Width)} / 1 000 000 = {M2(v.FloorAreaM2)} m²");
        l.F($"Volume = {Mm(v.Length)} × {Mm(v.Width)} × {Mm(v.Height)} / 10⁹ = {M3(v.VolumeM3)} m³");
        if (o.HasClearances)
        {
            l.N("Les débords réduisent l'espace où le moteur peut poser les unités ; les taux d'occupation restent exprimés par rapport au camion entier.");
        }

        return new(0, "Le véhicule et l'espace chargeable",
            "OptiTunes part des dimensions intérieures du camion, puis retire les débords demandés pour obtenir l'espace réellement chargeable.", l);
    }

    // ------------------------------------------------------------------ 2. Ordres

    private static DetailSection OrdersSection(Context ctx)
    {
        var l = new Lines();
        var rows = new List<IReadOnlyList<string>>();
        var weightTerms = new List<string>();
        double weight = 0, volume = 0;
        foreach (var o in ctx.Orders)
        {
            var w = o.UnitWeight > 0 ? o.Quantity * o.UnitWeight : 0;
            weight += w;
            volume += o.Quantity * ArticleVolume(o) / 1e9;
            if (o.UnitWeight > 0)
            {
                weightTerms.Add($"{N(o.Quantity)} × {Kg(o.UnitWeight)}");
            }

            rows.Add([
                o.Id, o.Article, o.TypeKnown ? o.Type.Label() : "?", N(o.Quantity), o.DimensionsText,
                o.UnitWeight >= 0 ? Kg(o.UnitWeight) : "?", Kg(w), StackingText(o),
                o.Stackable == true ? o.MaxLoadOnTop is { } ml ? Kg(ml) + " kg" : "non renseignée" : "–",
                StepsText(o)
            ]);
        }

        l.F($"Poids total = Σ quantité × poids unitaire = {Sum(weightTerms, Kg(weight) + " kg")}");
        l.F($"Volume total des articles = Σ quantité × longueur × largeur × hauteur = {M3(volume)} m³");
        foreach (var o in ctx.Orders.Where(o => !o.IsValid))
        {
            l.W($"Ordre {o.Id} non chargeable : {string.Join(" ; ", o.BlockingErrors)}.");
        }

        l.N("Gerbage : « 1 gerbage » = une unité peut être posée sur celle du sol (colonne NIVEAUX_MAX). " +
            "Charge sur le dessus non renseignée = le poids de ses propres gerbages (hypothèse prudente).");
        return new(0, "Les ordres à charger", "Les données lues dans le fichier pour chaque ordre de transport (ligne de commande ou de cadencement).", l,
            new DetailTable(["Ordre", "Article", "Type", "Qté", "Dimensions mm", "Poids unitaire kg", "Poids total kg", "Gerbage", "Charge sur le dessus", "Étapes"], rows));
    }

    private static double ArticleVolume(TransportOrder o) => o.Type switch
    {
        PhysicalType.Tube => o.Length * o.Diameter * o.Diameter,
        PhysicalType.Roll => o.Diameter * o.Diameter * o.Width,
        _ => o.Length * o.Width * o.Height
    };

    private static string StackingText(TransportOrder o) => o.Stackable switch
    {
        true => (o.MaxLevels is { } n ? $"Oui, {N(n - 1)} gerbage(s)" : "Oui, nombre non renseigné") +
                (o.MustBeOnTop ? ", au sommet" : "") + (o.MustBeOnFloor ? ", au sol" : ""),
        false => "Non" + (o.MustBeOnTop ? ", au sommet" : "") + (o.MustBeOnFloor ? ", au sol" : ""),
        null => "Non renseigné (non)"
    };

    private static string StepsText(TransportOrder o)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(o.Warehouse))
        {
            parts.Add(o.Warehouse!.Trim());
        }

        if (o.DepartureStep > 0 || o.ArrivalStep > 0)
        {
            parts.Add($"{(o.DepartureStep > 0 ? o.DepartureStep.ToString() : "départ")} → {(o.ArrivalStep > 0 ? o.ArrivalStep.ToString() : "?")}");
        }
        else if (o.Stop > 0)
        {
            parts.Add($"arrêt {o.Stop}");
        }

        return parts.Count == 0 ? "–" : string.Join(" ", parts);
    }

    // ------------------------------------------------------------------ 3. Unités de chargement

    private static DetailSection UnitsSection(Context ctx)
    {
        var l = new Lines();
        var rows = new List<IReadOnlyList<string>>();
        foreach (var o in ctx.Orders)
        {
            var units = ctx.UnitsOf(o);
            if (units.Count == 0 || !o.IsValid)
            {
                continue;
            }

            var u = units.OrderByDescending(x => x.ItemCount).First();
            switch (o.Type)
            {
                case PhysicalType.Plaque when o.IsValid && o.Height > 0:
                {
                    var door = ctx.Usable.DoorHeight is > 0 ? ctx.Usable.DoorHeight.Value : ctx.Usable.Height;
                    var height = Math.Min(ctx.Usable.Height, door);
                    var perHeight = (int)Math.Floor(height / o.Height + 1e-9);
                    var perPile = o.MaxPerPile is > 0 ? Math.Min(perHeight, o.MaxPerPile.Value) : perHeight;
                    l.F($"Ordre {o.Id} : plaques par pile = ⌊hauteur disponible / épaisseur⌋ = ⌊{Mm(height)} / {R(o.Height)}⌋ = {N(perHeight)}" +
                        (o.MaxPerPile is > 0 ? $", limité à QTE_MAX_PILE = {N(o.MaxPerPile.Value)} → {N(perPile)}" : ""));
                    l.F($"Ordre {o.Id} : piles = ⌈{N(o.Quantity)} plaques / {N(perPile)}⌉ = {N(units.Count)} pile(s) ; " +
                        $"pile pleine = {N(u.ItemCount)} × {R(o.Height)} mm = {Mm(u.C)} mm et {N(u.ItemCount)} × {Kg(o.UnitWeight)} kg = {Kg(u.Weight)} kg");
                    break;
                }
                case PhysicalType.Tube when u.Shape == UnitShape.Staggered:
                {
                    var pitch = o.Diameter * UnitBuilder.StaggerPitch;
                    l.F($"Ordre {o.Id} : {N(o.Quantity)} tubes Ø{Mm(o.Diameter)} regroupés en {N(units.Count)} lit(s) en quinconce ; " +
                        $"pas vertical = 0,866 × {Mm(o.Diameter)} = {Mm(pitch)} mm ; lit de {N(u.ItemCount)} tubes, {Mm(u.A)} × {Mm(u.B)} × {Mm(u.C)} mm, {Kg(u.Weight)} kg");
                    l.N($"Quinconce retenue parce qu'elle loge plus de tubes par section que des rangées alignées (chaque rangée posée dans les creux de la précédente).");
                    break;
                }
                case PhysicalType.Tube:
                    l.T($"Ordre {o.Id} : {N(o.Quantity)} tubes = {N(units.Count)} unité(s) cylindrique(s) couchée(s), {Mm(o.Length)} mm × Ø{Mm(o.Diameter)}, {Kg(o.UnitWeight)} kg chacune.");
                    break;
                case PhysicalType.Roll:
                    l.T($"Ordre {o.Id} : {N(o.Quantity)} bobine(s) Ø{Mm(o.Diameter)} × laize {Mm(o.Width)} mm, posée(s) axe vertical, {Kg(o.UnitWeight)} kg chacune.");
                    break;
                default:
                    l.T($"Ordre {o.Id} : {N(o.Quantity)} article(s) = {N(units.Count)} unité(s) de {Mm(u.A)} × {Mm(u.B)} × {Mm(u.C)} mm, {Kg(u.Weight)} kg chacune.");
                    break;
            }

            rows.Add([
                o.Id, o.Type.Label(), N(units.Count), N(units.Sum(x => x.ItemCount)),
                $"{Mm(u.A)} × {Mm(u.B)} × {Mm(u.C)}", Kg(u.Weight), Pct(u.RequiredSupportRatio * 100)
            ]);
        }

        foreach (var g in ctx.Plan.Unloaded.Where(x => x.Reason is RejectReason.InvalidData or RejectReason.TooLargeForVehicle
                         or RejectReason.NoAllowedOrientation or RejectReason.DoorPassage && !ctx.Plan.Placements.Any(p => p.Unit.Order == x.Unit.Order))
                     .GroupBy(x => x.Unit.Order))
        {
            var first = g.First();
            l.W($"Ordre {g.Key.Id} écarté avant le placement : {first.Reason.Label()}{(first.Detail != null ? $" ({first.Detail})" : "")}.");
        }

        l.N(ctx.Options.AllowFloorRotation
            ? "Pose : chaque unité reste à plat ; elle peut pivoter au sol (longueur dans le sens du camion ou en travers), jamais sur le flanc."
            : "Pose : rotation au sol désactivée, la LONGUEUR du fichier reste dans le sens de la longueur du camion.");
        return new(0, "Des articles aux unités de chargement",
            "Le moteur ne place pas des articles mais des unités physiques : une palette, un carton, une pile de plaques, un tube, un lit de tubes…", l,
            new DetailTable(["Ordre", "Type", "Unités", "Articles", "Unité L × l × H mm", "Poids unité kg", "Appui minimal si gerbée"], rows));
    }

    // ------------------------------------------------------------------ 4. Niveaux

    private static DetailSection LevelsSection(Context ctx)
    {
        var l = new Lines();
        var rows = new List<IReadOnlyList<string>>();
        foreach (var o in ctx.Orders)
        {
            var units = ctx.UnitsOf(o);
            if (units.Count == 0 || !ctx.Plan.LinearMetersByOrder.TryGetValue(o, out var ml))
            {
                continue;
            }

            var u = units.OrderByDescending(x => x.C).First();
            if (!u.CanReceive)
            {
                var why = u.Shape == UnitShape.Staggered ? "lit de tubes en quinconce, déjà empilé sur toute la hauteur utile"
                    : !u.Stackable ? "non gerbable"
                    : u.MustBeOnTop ? "doit rester au sommet"
                    : u.MaxLevels <= 1 ? "NIVEAUX_MAX = 0 (rien dessus)"
                    : "charge supportable nulle";
                l.T($"Ordre {o.Id} : {why} → 1 niveau, rien n'est posé dessus.");
                rows.Add([o.Id, "–", "–", "–", "1", "0"]);
                continue;
            }

            var dz = Orientations.Apply(ml.BestOrientation, u.A, u.B, u.C).DZ;
            var byHeight = (int)Math.Floor(ctx.Usable.Height / dz + 1e-9);
            int? byLoad = u.Weight > 0 ? 1 + (int)Math.Floor(u.MaxLoadOnTop / u.Weight + 1e-9) : null;
            var loadText = byLoad is { } bl
                ? $" ; par la charge : 1 + ⌊{Kg(u.MaxLoadOnTop)} / {Kg(u.Weight)}⌋ = {N(bl)}{(u.MaxLoadAssumed ? " (charge estimée)" : "")}"
                : "";
            var fileText = o.MaxLevels == null ? $"non renseigné, valeur prudente {N(u.MaxLevels)}" : $"NIVEAUX_MAX + 1 = {N(u.MaxLevels)}";
            l.F($"Ordre {o.Id} : niveaux = MIN(fichier : {fileText} ; par la hauteur : ⌊{Mm(ctx.Usable.Height)} / {Mm(dz)}⌋ = {N(byHeight)}{loadText}) = {N(ml.Levels)}");
            rows.Add([o.Id, N(u.MaxLevels), N(byHeight), byLoad is { } b ? N(b) : "–", N(ml.Levels), N(ml.Levels - 1)]);
        }

        l.R("Le plus petit des trois nombres est retenu : c'est le nombre d'unités d'une pile, sol compris.");
        l.N("Le gerbage réel est revérifié au placement, unité par unité : le poids posé descend jusqu'au sol et aucun support ne doit dépasser sa charge supportable.");
        return new(0, "Niveaux de gerbage par ordre",
            "Combien d'unités peut-on empiler ? Trois limites s'appliquent : le fichier, la hauteur du camion et la charge que l'unité du bas peut porter.", l,
            new DetailTable(["Ordre", "Limite fichier", "Limite hauteur", "Limite charge", "Niveaux retenus", "Gerbages"], rows));
    }

    // ------------------------------------------------------------------ 5. Métrage linéaire

    private sealed record MlOption(int Orientation, double Dx, double Dy, int Stacks, int PerRow, int Rows, double MlRows, double MlEq);

    private static MlOption? Option(Context ctx, IReadOnlyList<PhysicalUnit> units, PhysicalUnit u, int levels, int orientation)
    {
        if (!u.Orientations.Contains(orientation))
        {
            return null;
        }

        var v = ctx.Usable;
        var (dx, dy, dz) = Orientations.Apply(orientation, u.A, u.B, u.C);
        if (dx > v.Length + Geometry.Eps || dy > v.Width + Geometry.Eps || dz > v.Height + Geometry.Eps)
        {
            return null;
        }

        var gap = ctx.Gap;
        var stacks = (int)Math.Ceiling(units.Count / (double)levels);
        var perRow = Math.Max(1, (int)Math.Floor((v.Width + gap) / (dy + gap) + 1e-9));
        var rows = (int)Math.Ceiling(stacks / (double)perRow);
        return new MlOption(orientation, dx, dy, stacks, perRow, rows, (rows * dx + Math.Max(0, rows - 1) * gap) / 1000.0, stacks * dx * dy / v.Width / 1000.0);
    }

    private static DetailSection LinearMetersSection(Context ctx)
    {
        var l = new Lines();
        var rows = new List<IReadOnlyList<string>>();
        var gap = ctx.Gap;
        var width = ctx.Usable.Width;
        foreach (var o in ctx.Orders)
        {
            var units = ctx.UnitsOf(o);
            if (units.Count == 0 || !ctx.Plan.LinearMetersByOrder.TryGetValue(o, out var ml))
            {
                continue;
            }

            var u = units.OrderByDescending(x => x.C).First();
            l.H($"Ordre {o.Id} — {N(units.Count)} unité(s), {N(ml.Levels)} niveau(x) par pile");
            l.F($"Piles au sol = ⌈{N(units.Count)} unité(s) / {N(ml.Levels)} niveau(x)⌉ = {N(ml.Stacks)}");
            var options = new[] { Option(ctx, units, u, ml.Levels, 1), Option(ctx, units, u, ml.Levels, 2) }.OfType<MlOption>().ToList();
            var chosen = options.Where(x => x.PerRow == ml.PerRow && x.Rows == ml.Rows && Math.Abs(x.MlRows - ml.MlRows) < 1e-6)
                .OrderBy(x => x.MlEq).FirstOrDefault();
            foreach (var opt in options)
            {
                if (opt.Orientation == 2 && Math.Abs(u.A - u.B) < Geometry.Eps)
                {
                    continue;
                }

                var sens = opt.Orientation == 1 ? "longueur dans le sens du camion" : "longueur en travers du camion";
                var perRowText = gap > 0
                    ? $"⌊({Mm(width)} + {Mm(gap)}) / ({Mm(opt.Dy)} + {Mm(gap)})⌋"
                    : $"⌊{Mm(width)} / {Mm(opt.Dy)}⌋";
                var mlText = gap > 0 && opt.Rows > 1
                    ? $"({N(opt.Rows)} × {Mm(opt.Dx)} + {N(opt.Rows - 1)} × {Mm(gap)}) / 1 000"
                    : $"{N(opt.Rows)} × {Mm(opt.Dx)} / 1 000";
                l.F($"Si {sens} : par rangée {perRowText} = {N(opt.PerRow)} → rangées ⌈{N(opt.Stacks)} / {N(opt.PerRow)}⌉ = {N(opt.Rows)} → " +
                    $"ML rangées = {mlText} = {M(opt.MlRows)} m");
            }

            var footDx = ml.Rows > 0 ? (ml.MlRows * 1000 - Math.Max(0, ml.Rows - 1) * gap) / ml.Rows : 0;
            var sensChosen = chosen == null || Math.Abs(u.A - u.B) < Geometry.Eps ? ""
                : chosen.Orientation == 1 ? " (longueur dans le sens du camion)" : " (longueur en travers)";
            l.F($"Retenu{sensChosen}, le plus court : {N(ml.PerRow)} pile(s) par rangée × {N(ml.Rows)} rangée(s) → ML rangées = {M(ml.MlRows)} m");
            l.F($"ML équivalent = piles × emprise d'une pile / largeur = {N(ml.Stacks)} × {M2(ml.FootprintM2 / Math.Max(1, ml.Stacks))} m² / {M(width / 1000)} m = {M(ml.MlEquivalent)} m");
            rows.Add([o.Id, N(ml.Stacks), N(ml.Levels), N(ml.PerRow), N(ml.Rows), M(ml.MlRows), M(ml.MlEquivalent), footDx > 0 ? Mm(footDx) : "–"]);
        }

        var plan = ctx.Plan;
        var m = plan.Metrics;
        var eqTerms = plan.LinearMetersByOrder.Where(x => ctx.Orders.Contains(x.Key)).Select(x => M(x.Value.MlEquivalent)).ToList();
        var rowTerms = plan.LinearMetersByOrder.Where(x => ctx.Orders.Contains(x.Key)).Select(x => M(x.Value.MlRows)).ToList();
        l.H("Totaux du groupage");
        l.F($"Besoin équivalent = Σ ML équivalent = {Sum(eqTerms, M(m.LinearMetersRequiredEquivalent) + " m")}");
        l.F($"Σ ML rangées = {Sum(rowTerms, M(m.LinearMetersRequiredRows) + " m")} (majorant : chaque ordre sur ses propres rangées)");

        var v = ctx.Vehicle;
        if (v.MaxPayload > 0 && v.VolumeM3 > 0 && v.LinearMeters > 0)
        {
            var units = plan.Placements.Select(p => p.Unit)
                .Concat(plan.Unloaded.Where(x => PlanMetrics.IsSpaceReason(x.Reason)).Select(x => x.Unit)).ToList();
            var w = units.Sum(x => x.Weight);
            var vol = units.Sum(x => x.Volume) / 1e9;
            var eq = m.LinearMetersRequiredEquivalent;
            l.F($"Camions minimum = ⌈MAX(poids {Kg(w)} / {Kg(v.MaxPayload)} = {R(w / v.MaxPayload)} ; volume {M3(vol)} / {M3(v.VolumeM3)} = {R(vol / v.VolumeM3)} ; " +
                $"ML {M(eq)} / {M(v.LinearMeters)} = {R(eq / v.LinearMeters)})⌉ = {N(m.EstimatedVehicles)}");
        }

        l.N("Ce besoin est calculé « sur papier », ordre par ordre, avant tout placement. Le plan 3D peut faire mieux (ordres qui partagent une rangée) " +
            "ou moins bien (accès, rangement, formats qui ne s'emboîtent pas). Le métrage réel est donné camion par camion plus bas.");
        return new(0, "Besoin en métrage linéaire (avant placement)",
            "Estimation théorique de la longueur de plancher nécessaire, à partir des seules données des ordres.", l,
            new DetailTable(["Ordre", "Piles", "Niveaux", "Par rangée", "Rangées", "ML rangées m", "ML équivalent m", "Longueur au sol mm"], rows));
    }

    // ------------------------------------------------------------------ 6. Rangement

    private static DetailSection StowageSection(Context ctx)
    {
        var l = new Lines();
        var o = ctx.Options;
        var mode = o.Stowage;
        var units = ctx.UnitsByOrder.Values.SelectMany(x => x).ToList();
        l.R($"Rangement choisi : {mode.Label()}.");
        switch (mode)
        {
            case StowageMode.Order:
                l.T(ctx.Orders.Any(x => x.Stop > 0)
                    ? "Seule la colonne ARRET compte : l'arrêt le plus lointain va au fond (côté cabine), l'arrêt 1 côté porte. " +
                      "Aucune unité d'un arrêt plus lointain ne peut se trouver entre la porte et une unité d'un arrêt plus proche."
                    : "Aucun arrêt renseigné : pas de contrainte d'accès, les unités sont rangées pour la densité.");
                break;
            case StowageMode.Stops:
                l.T("Les étapes du fichier décident : chargé tôt = au fond, livré tôt = côté porte. Une unité n'est jamais placée là où elle gênerait " +
                    "le chargement ou le déchargement d'une autre (contrainte stricte : sinon reliquat ou camion supplémentaire).");
                foreach (var step in Itinerary(ctx.Orders))
                {
                    l.F(step);
                }

                break;
            case StowageMode.Compact:
                l.W("Aucune contrainte d'accès ni de zone : densité maximale. À réserver à un chargement livré en une seule fois.");
                break;
            default:
                l.T("Chaque zone occupe une tranche continue du camion, de l'avant vers la porte, sans gerbage d'une zone sur une autre. " +
                    "Ordre des zones : chargées d'abord, puis livrées en dernier, puis ordre alphabétique.");
                foreach (var zone in units.Where(u => u.GroupKey != null).GroupBy(u => (u.GroupRank, u.GroupKey)).OrderBy(g => g.Key.GroupRank))
                {
                    l.F($"Zone {N(zone.Key.GroupRank + 1)}{(zone.Key.GroupRank == 0 ? " (au fond)" : "")} : {zone.Key.GroupKey} — ordres " +
                        string.Join(", ", zone.Select(u => u.Order.Id).Distinct()));
                }

                if (units.Any(u => u.Departure > 0 || u.Arrival > 0))
                {
                    l.N("Les étapes du fichier s'appliquent aussi à l'intérieur des zones.");
                }

                break;
        }

        if (!o.RespectDeliveryOrder && mode != StowageMode.Compact)
        {
            l.W("Option « Respecter l'ordre de livraison » désactivée : l'accessibilité n'est pas imposée au placement (seulement signalée).");
        }

        if (mode != StowageMode.Compact && units.Any(u => u.Departure > 0 || u.Arrival > 0))
        {
            l.T("Règle d'accès entre deux unités A (au fond) et B (entre A et la porte, ou posée sur A) : B bloque A si");
            var loading = units.Any(u => u.Departure > 0);
            l.F("au déchargement de A, B est encore à bord (B livrée après A et chargée avant)" + (loading ? ", ou" : "."));
            if (loading)
            {
                l.F("au chargement de A, B est déjà à bord (B chargée avant A et pas encore livrée).");
            }
            l.N("Une unité posée plus haut que A (sans recouvrir sa hauteur) ne la bloque pas : on charge et décharge par-dessus.");
        }

        l.T("Ordre de présentation des unités au moteur : étape de chargement croissante, zone, étape de livraison décroissante, " +
            "unités « au sol » d'abord et « au sommet » en dernier, puis le critère de la stratégie essayée (emprise, volume, poids…).");

        var rows = ctx.Orders.Where(x => ctx.UnitsOf(x).Count > 0).Select(x =>
        {
            var u = ctx.UnitsOf(x)[0];
            return (IReadOnlyList<string>)[
                x.Id, u.Departure > 0 ? N(u.Departure) : "départ", u.Arrival > 0 ? N(u.Arrival) : "–",
                u.GroupKey ?? "–", u.GroupRank >= 0 ? N(u.GroupRank + 1) : "–"
            ];
        }).ToList();
        return new(0, "Ordre de chargement et rangement",
            "Le camion se charge de la cabine vers la porte. Le rangement décide qui va au fond et qui reste accessible à chaque arrêt.", l,
            new DetailTable(["Ordre", "Étape de chargement", "Étape de livraison", "Zone", "Rang de la zone"], rows));
    }

    private static IEnumerable<string> Itinerary(IReadOnlyList<TransportOrder> orders)
    {
        var steps = orders.SelectMany(o => new[] { (Step: o.DepartureStep, Load: true, Order: o), (Step: o.EffectiveArrival, Load: false, Order: o) })
            .Where(x => x.Step > 0)
            .GroupBy(x => x.Step)
            .OrderBy(g => g.Key);
        foreach (var step in steps)
        {
            var parts = new List<string>();
            var loads = step.Where(x => x.Load).Select(x => x.Order).ToList();
            var drops = step.Where(x => !x.Load).Select(x => x.Order).ToList();
            if (loads.Count > 0)
            {
                var where = string.Join(", ", loads.Select(o => o.Warehouse?.Trim()).Where(w => !string.IsNullOrEmpty(w)).Distinct());
                parts.Add($"chargement{(where.Length > 0 ? " " + where : "")} ({string.Join(", ", loads.Select(o => o.Id))})");
            }

            if (drops.Count > 0)
            {
                var who = string.Join(", ", drops.Select(o => o.Customer?.Trim()).Where(c => !string.IsNullOrEmpty(c)).Distinct());
                parts.Add($"livraison{(who.Length > 0 ? " " + who : "")} ({string.Join(", ", drops.Select(o => o.Id))})");
            }

            yield return $"Étape {step.Key} : {string.Join(" ; ", parts)}";
        }
    }

    // ------------------------------------------------------------------ 7. Placement

    private static DetailSection PlacementSection(Context ctx)
    {
        var o = ctx.Options;
        var v = ctx.Vehicle;
        var u = ctx.Usable;
        var l = new Lines();
        l.T("Positions candidates : les « points extrêmes », c'est-à-dire les coins libres laissés par les unités déjà posées (devant, à côté, au-dessus). " +
            "Le premier point est le coin avant gauche du plancher.");
        l.T("Choix de la position : celle qui garde le chargement le plus court (X + longueur posée le plus petit), puis la plus basse, puis la plus à gauche.");
        l.R("Une position n'est acceptée que si toutes les règles suivantes sont respectées :");
        l.F($"1. Dans l'espace chargeable : X + DX ≤ {Mm(v.Length)} ; {Mm(o.SideClearance)} ≤ Y et Y + DY ≤ {Mm(v.Width - o.SideClearance)} ; Z + DZ ≤ {Mm(u.Height)} mm");
        l.F(ctx.Gap > 0
            ? $"2. Aucun chevauchement, et {Mm(ctx.Gap)} mm de jeu avec les unités voisines du même niveau"
            : "2. Aucun chevauchement avec les unités déjà posées");
        l.F($"3. Posée sur d'autres unités : au moins {Pct(o.MinSupportRatio * 100)} de sa surface en appui ({Pct(o.PlaqueSupportRatio * 100)} pour une pile de plaques), " +
            "et son centre au-dessus des appuis");
        l.F("4. Les supports acceptent le gerbage : gerbables, pas « au sommet » ; rien sur une bobine couchée ; sur un tube couché, seulement un tube aligné");
        l.F("5. Niveau de la pile ≤ niveaux autorisés de chaque unité de la pile");
        l.F("6. Charge : le poids de l'unité est réparti sur ses supports au prorata des surfaces de contact, et descend ainsi jusqu'au sol ; " +
            "aucune unité ne doit porter plus que sa charge supportable");
        l.F(v.HasDoor
            ? $"7. Passage par la porte : section posée ≤ {Mm(v.DoorWidth!.Value)} × {Mm(v.DoorHeight!.Value)} mm"
            : "7. Passage par la porte : non contrôlé (ouverture non renseignée)");
        l.F($"8. Charge utile : poids déjà chargé + poids de l'unité ≤ {Kg(v.MaxPayload)} kg");
        l.F("9. Accessibilité et zones du rangement (section précédente)");
        l.T("Si aucune position ne convient, l'unité part en reliquat avec un motif ; les unités identiques suivantes ne sont pas réessayées.");
        if (o.MultiStrategy)
        {
            l.R("OptiTunes essaie 18 façons de remplir chaque camion : 6 ordres de tri × 3 modes de remplissage, puis affine la meilleure.");
            l.T("Ordres de tri : emprise au sol décroissante ; volume décroissant ; poids décroissant ; plus grande dimension d'abord ; non gerbables d'abord ; groupé par ordre.");
            l.T("Modes : parois (remplit la largeur au sol avant de gerber), colonnes (gerbe d'abord), rangées (sens de pose de chaque ordre imposé par le calcul du métrage).");
            l.T("Pour un camion, la meilleure façon est celle qui charge le plus d'ordres complets, puis le plus de volume, puis le chargement le plus court.");
            l.T("Affinage : l'ordre de pose de la meilleure façon est ensuite retravaillé (échange ou déplacement d'unités de même étape, zone et contrainte " +
                "sol / sommet, donc sans toucher au rangement) ; une modification n'est gardée que si le camion n'est pas moins bon. " +
                "Le nombre d'essais est fixe : le même fichier donne toujours le même plan.");
        }
        else
        {
            l.W("Recherche approfondie désactivée : une seule façon est essayée (emprise au sol décroissante · parois).");
        }

        if (o.MaxVehicles > 1)
        {
            l.T($"Camions supplémentaires : tant qu'il reste un reliquat « de place », un nouveau camion identique est rempli de la même façon (au plus {N(o.MaxVehicles)}).");
        }

        return new(0, "Placement des unités (moteur 3D)",
            "Chaque unité est posée à la meilleure position libre qui respecte toutes les règles. Sinon, elle part en reliquat avec son motif.", l);
    }

    // ------------------------------------------------------------------ 8. Choix de la solution

    private static (int, int, int, double, int) Rank(LoadPlan p) =>
        (p.Metrics.ItemsRemaining, p.Loads.Count,
         p.Groupage.Orders.Count(o => p.RemainingItems(o) > 0),
         Math.Round(p.Metrics.LinearMetersReal, 1),
         p.Groupage.Orders.Count(o => p.VehiclesOf(o).Count > 1));

    private static DetailSection? SolutionChoiceSection(Context ctx, IReadOnlyList<LoadPlan>? solutions)
    {
        var l = new Lines();
        l.T("Les solutions sont classées sur 5 critères, dans cet ordre (le suivant ne sert qu'en cas d'égalité) :");
        l.F("1. le moins d'articles en reliquat ; 2. le moins de camions ; 3. le moins d'ordres incomplets ; " +
            "4. le métrage réel total le plus court (arrondi à 0,1 m) ; 5. le moins d'ordres répartis sur plusieurs camions");
        l.N("À reliquat égal, des ordres complets passent avant un plancher plus court : un camion limité par son poids est plein de toute façon.");
        l.T("« Meilleure par camion » choisit la meilleure façon camion par camion ; les autres appliquent une seule façon à tous les camions. " +
            "Les solutions qui donnent exactement le même plan ne sont proposées qu'une fois.");
        var rows = new List<IReadOnlyList<string>>();
        if (solutions is { Count: > 0 })
        {
            var index = 0;
            foreach (var s in solutions)
            {
                index++;
                var r = Rank(s);
                rows.Add([
                    N(index) + (s.IsRecommended ? " ★" : "") + (ReferenceEquals(s, ctx.Plan) ? " ▶" : ""),
                    s.Name, N(r.Item1), N(r.Item2), N(r.Item3), M(r.Item4), N(r.Item5)
                ]);
            }

            var position = solutions.ToList().IndexOf(ctx.Plan) + 1;
            if (position == 1)
            {
                l.R($"La solution affichée « {ctx.Plan.Name} » est classée 1re sur {N(solutions.Count)} : c'est la recommandée.");
            }
            else if (position > 1)
            {
                l.T($"La solution affichée « {ctx.Plan.Name} » est classée {N(position)}e sur {N(solutions.Count)} (★ = recommandée, ▶ = affichée).");
            }
        }

        return new(0, "Choix de la solution",
            "Plusieurs solutions sont calculées puis classées ; la première est recommandée, les autres restent consultables.", l,
            rows.Count > 0 ? new DetailTable(["Rang", "Solution", "Reliquat", "Camions", "Ordres incomplets", "ML réel m", "Ordres scindés"], rows) : null);
    }

    // ------------------------------------------------------------------ 9. Camions

    private static DetailSection LoadSection(Context ctx, VehicleLoad load)
    {
        var l = new Lines();
        var m = load.Metrics;
        var v = load.Vehicle;
        var placed = load.Placements;
        if (placed.Count == 0)
        {
            l.W("Aucune unité chargée dans ce camion.");
            return new(0, $"{load.Label} : résultat chiffré", "Indicateurs du camion recalculés à partir des positions réelles des unités.", l);
        }

        var floor = placed.Where(p => p.OnFloor).ToList();
        if (!string.IsNullOrEmpty(load.Strategy))
        {
            l.T($"Façon retenue : « {load.Strategy} ».");
        }

        l.T($"{N(placed.Count)} unité(s), {N(m.ItemsPlaced)} article(s) : {N(floor.Count)} au sol et {N(placed.Count - floor.Count)} gerbée(s).");

        var byOrder = placed.GroupBy(p => p.Unit.Order).OrderBy(g => g.Min(p => p.Sequence)).ToList();
        var weightTerms = byOrder.Select(g => g.Select(p => p.Unit.Weight).Distinct().Count() == 1
            ? $"{N(g.Count())} × {Kg(g.First().Unit.Weight)}"
            : Kg(g.Sum(p => p.Unit.Weight))).ToList();
        l.F($"Poids chargé = Σ poids des unités = {Sum(weightTerms, Kg(m.LoadedWeightKg) + " kg")}");
        l.F($"Taux de charge = {Kg(m.LoadedWeightKg)} / {Kg(v.MaxPayload)} = {Pct(m.WeightRate)}");

        var minX = floor.Min(p => p.X);
        var maxX = floor.Max(p => p.MaxX);
        l.F($"ML réel = (fin de la dernière unité au sol − début de la première) / 1 000 = ({Mm(maxX)} − {Mm(minX)}) / 1 000 = {M(m.LinearMetersReal)} m");
        l.F($"Occupation de la longueur = {M(m.LinearMetersReal)} / {M(v.LinearMeters)} = {Pct(m.LinearRealRate)}");
        l.F($"Surface au sol = Σ longueur × largeur des unités au sol = {M2(m.FloorUsedM2)} m² sur {M2(v.FloorAreaM2)} m² = {Pct(m.FloorRate)}");
        l.F($"ML équivalent = surface au sol / largeur = {M2(m.FloorUsedM2)} / {M(v.Width / 1000)} = {M(m.LinearMetersEquivalent)} m");
        l.F($"Volume chargé = Σ L × l × H des unités = {M3(m.LoadedVolumeM3)} m³ sur {M3(v.VolumeM3)} m³ = {Pct(m.VolumeRate)}");
        l.F($"Hauteur maximale chargée = {Mm(m.MaxLoadHeight)} mm (limite {Mm(ctx.Usable.Height)} mm)");
        if (m.LoadedWeightKg > 0)
        {
            l.F($"Centre de gravité = Σ (poids × position du milieu) / Σ poids : X = {Mm(m.CgX)} mm depuis la cabine ({Pct(v.Length > 0 ? m.CgX / v.Length * 100 : 0)} de la longueur), " +
                $"Y = {Mm(m.CgY)} mm (axe à {Mm(v.Width / 2)} mm, écart {Mm(Math.Abs(m.CgY - v.Width / 2))} mm), Z = {Mm(m.CgZ)} mm");
        }

        if (placed.Where(p => p.Unit.CanReceive && p.LoadAbove > 0).OrderByDescending(p => p.LoadAbove / p.Unit.MaxLoadOnTop).FirstOrDefault() is { } most)
        {
            l.F($"Unité la plus sollicitée : {most.Unit.Id} porte {Kg(most.LoadAbove)} kg pour {Kg(most.Unit.MaxLoadOnTop)} kg admissibles " +
                $"({Pct(most.LoadAbove / most.Unit.MaxLoadOnTop * 100)}){(most.Unit.MaxLoadAssumed ? ", charge admissible estimée" : "")}");
        }

        l.R($"Ce qui limite ce camion : {m.LimitingFactor}.");
        l.N("Seuils : poids ≥ 95 % → poids ; ML réel ≥ 92 % ou surface ≥ 85 % → plancher ; volume ≥ 80 % → volume ; sinon hauteur, gerbage ou ordre de livraison.");

        var refined = load.Strategy.EndsWith(" · affinée", StringComparison.Ordinal);
        var baseName = refined ? load.Strategy[..^" · affinée".Length] : load.Strategy;
        if (load.StrategyLog.Count > 1 && load.StrategyLog.FirstOrDefault(s => s.Name == baseName) is { } best)
        {
            l.T($"Façons essayées pour ce camion : {N(load.StrategyLog.Count)} ; meilleure « {best.Name} » : " +
                $"{N(best.CompleteOrders)} ordre(s) complet(s), {M3(best.VolumeM3)} m³ chargés, chargement sur {M(best.LengthUsedM)} m.");
        }

        if (refined)
        {
            var volume = placed.Sum(p => p.Unit.Volume) / 1e9;
            l.T($"Affinage : en retravaillant l'ordre de pose, le camion est passé à {N(placed.Select(p => p.Unit.Order).Distinct().Count(o => ctx.Plan.Unloaded.All(u => u.Unit.Order != o)))} ordre(s) complet(s), " +
                $"{M3(volume)} m³ chargés, chargement sur {M(placed.Max(p => p.MaxX) / 1000)} m.");
        }

        var rows = byOrder.Select(g => (IReadOnlyList<string>)[
            g.Key.Id, N(g.Count()), N(g.Sum(p => p.Unit.ItemCount)), Kg(g.Sum(p => p.Unit.Weight)),
            N(g.Count(p => p.OnFloor)), M(g.Where(p => p.OnFloor).Sum(p => p.FootprintArea) / Math.Max(1, v.Width) / 1000),
            N(g.Min(p => p.Sequence)) + " à " + N(g.Max(p => p.Sequence))
        ]).ToList();
        return new(0, $"{load.Label} : résultat chiffré",
            "Les indicateurs du camion, recalculés à partir des positions réelles des unités dans le plan.", l,
            new DetailTable(["Ordre", "Unités", "Articles", "Poids kg", "Au sol", "ML équivalent m", "Séquence de chargement"], rows));
    }

    // ------------------------------------------------------------------ 10. Reliquat

    private static DetailSection UnloadedSection(Context ctx)
    {
        var l = new Lines();
        var plan = ctx.Plan;
        var rows = new List<IReadOnlyList<string>>();
        if (plan.Unloaded.Count == 0)
        {
            l.R("Aucun reliquat : tous les articles sont chargés.");
        }
        else
        {
            foreach (var g in plan.Unloaded.GroupBy(u => (u.Unit.Order, u.Reason)))
            {
                var detail = g.First().Detail;
                l.W($"Ordre {g.Key.Order.Id} : {N(g.Count())} unité(s), {N(g.Sum(u => u.Unit.ItemCount))} article(s) — {g.Key.Reason.Label()}" +
                    (detail != null ? $" ({detail})" : "") + ".");
                l.N(Explain(g.Key.Reason));
                rows.Add([g.Key.Order.Id, N(g.Count()), N(g.Sum(u => u.Unit.ItemCount)), g.Key.Reason.Label()]);
            }

            if (ctx.Options.MaxVehicles <= 1 && plan.Unloaded.Any(u => PlanMetrics.IsSpaceReason(u.Reason)))
            {
                l.T("Pour charger ce reliquat, cochez « Ajouter des camions si reliquat » : il sera réparti sur des camions identiques.");
            }
        }

        return new(0, "Reliquat", "Ce qui n'a pas pu être chargé, et pourquoi.", l,
            rows.Count > 0 ? new DetailTable(["Ordre", "Unités", "Articles", "Motif"], rows) : null);
    }

    private static string Explain(RejectReason reason) => reason switch
    {
        RejectReason.InvalidData => "Une donnée indispensable manque ou est invalide dans le fichier : l'ordre n'est pas calculé.",
        RejectReason.TooLargeForVehicle => "L'unité est plus grande que l'espace chargeable, quelle que soit sa pose.",
        RejectReason.NoAllowedOrientation => "L'unité ne tient que posée d'une façon interdite (sur le flanc, ou rotation au sol désactivée).",
        RejectReason.DoorPassage => "L'unité tiendrait dans le camion mais ne passe pas par l'ouverture arrière.",
        RejectReason.PayloadExceeded => "La charge utile du camion serait dépassée.",
        RejectReason.FloorSaturatedNonStackable => "L'unité ne peut pas être gerbée et il ne reste plus de place au sol.",
        RejectReason.StowageBlocked => "Il resterait de la place, mais toute position bloquerait une autre unité au chargement ou à la livraison (contrainte stricte).",
        _ => "Aucune position libre ne respecte toutes les règles : plancher, hauteur, gerbage ou charge supportable saturés."
    };

    // ------------------------------------------------------------------ 11. Contrôles

    private static DetailSection ChecksSection(ValidationReport report)
    {
        var l = new Lines();
        foreach (var c in report.Checks)
        {
            var text = c.Checked == 0 && c.Violations.Count == 0
                ? $"{c.Name} — {c.Rule} — sans objet pour ce plan"
                : $"{c.Name} — {c.Rule} — {c.Summary}";
            switch (c.Status)
            {
                case CheckStatus.Ok:
                    l.R(text);
                    break;
                case CheckStatus.Warning:
                    l.W(text);
                    break;
                default:
                    l.E(text);
                    break;
            }

            foreach (var v in c.Violations.Take(3))
            {
                l.N(v);
            }

            if (c.Violations.Count > 3)
            {
                l.N($"… et {N(c.Violations.Count - 3)} autre(s), voir l'onglet Contrôles.");
            }
        }

        return new(0, "Contrôles du plan (validateur indépendant)",
            "Après le calcul, un second programme, indépendant du moteur, recompte et revérifie tout le plan à partir des positions.", l);
    }

    // ------------------------------------------------------------------ Récapitulatif

    /// <summary>« 7 ÷ 2 = 3,5 → 4 (arrondi au-dessus) » ou « 8 ÷ 2 = 4 ».</summary>
    private static string DivUp(double a, double b, string unit)
    {
        var exact = a / b;
        var up = (int)Math.Ceiling(exact - 1e-9);
        return Math.Abs(exact - up) < 1e-9
            ? $"{R(a)} ÷ {R(b)} = {N(up)} {unit}"
            : $"{R(a)} ÷ {R(b)} = {exact.ToString("0.##", Fr)} → {N(up)} {unit} (arrondi au-dessus)";
    }

    /// <summary>« 2 700 ÷ 1 300 = 2,08 → 2 » ou « 2 400 ÷ 800 = 3 ».</summary>
    private static string DivDown(double a, double b)
    {
        var exact = a / b;
        var down = (int)Math.Floor(exact + 1e-9);
        return Math.Abs(exact - down) < 1e-9
            ? $"{R(a)} ÷ {R(b)} = {N(down)}"
            : $"{R(a)} ÷ {R(b)} = {exact.ToString("0.##", Fr)} → {N(down)}";
    }

    /// <summary>Toutes les opérations du calcul, numérotées, en phrases courtes et avec les valeurs du groupage.</summary>
    private static DetailSection RecapSection(Context ctx)
    {
        var l = new Lines();
        var n = 0;
        void Op(string text) => l.T($"{++n}. {text}");

        var v = ctx.Vehicle;
        var u = ctx.Usable;
        var o = ctx.Options;
        var plan = ctx.Plan;
        var m = plan.Metrics;

        l.H("Le camion");
        Op($"Largeur chargeable : {Mm(v.Width)} − 2 × {Mm(o.SideClearance)} = {Mm(u.Width)} mm.");
        Op($"Hauteur chargeable : {Mm(v.Height)} − {Mm(o.RoofClearance)} = {Mm(u.Height)} mm.");
        Op($"Longueur du camion : {Mm(v.Length)} mm, soit {M(v.LinearMeters)} m de plancher.");
        Op($"Charge utile : {Kg(v.MaxPayload)} kg.");

        l.H("Les marchandises");
        var weights = ctx.Orders.Where(x => x.UnitWeight > 0).Select(x => $"{N(x.Quantity)} × {Kg(x.UnitWeight)}").ToList();
        var total = ctx.Orders.Where(x => x.UnitWeight > 0).Sum(x => x.Quantity * x.UnitWeight);
        Op($"Poids à charger : {Sum(weights, Kg(total) + " kg")}.");
        Op($"Articles à charger : {N(m.ItemsTotal)}.");

        foreach (var order in ctx.Orders)
        {
            var units = ctx.UnitsOf(order);
            if (units.Count == 0 || !order.IsValid || !plan.LinearMetersByOrder.TryGetValue(order, out var ml))
            {
                continue;
            }

            var unit = units.OrderByDescending(x => x.C).First();
            l.H($"Ordre {order.Id}");
            if (order.Type == PhysicalType.Plaque && unit.ItemCount > 1)
            {
                Op($"Plaques en piles : {DivUp(order.Quantity, units.Max(x => x.ItemCount), "pile(s)")} de {N(units.Max(x => x.ItemCount))} plaques.");
            }
            else if (unit.Shape == UnitShape.Staggered)
            {
                Op($"Tubes en lits en quinconce : {N(order.Quantity)} tubes → {N(units.Count)} lit(s).");
            }
            else
            {
                Op($"Unités à poser : {N(units.Count)} de {Mm(unit.A)} × {Mm(unit.B)} × {Mm(unit.C)} mm, {Kg(unit.Weight)} kg chacune.");
            }

            if (unit.CanReceive)
            {
                var dz = Orientations.Apply(ml.BestOrientation, unit.A, unit.B, unit.C).DZ;
                var parts = new List<string>
                {
                    order.MaxLevels == null ? $"{N(unit.MaxLevels)} (fichier non renseigné, valeur prudente)" : $"{N(unit.MaxLevels)} (fichier)",
                    $"{N((int)Math.Floor(u.Height / dz + 1e-9))} (hauteur : {DivDown(u.Height, dz)})"
                };
                if (unit.Weight > 0)
                {
                    parts.Add($"{N(1 + (int)Math.Floor(unit.MaxLoadOnTop / unit.Weight + 1e-9))} (charge : {DivDown(unit.MaxLoadOnTop, unit.Weight)} dessus, + 1 au sol)");
                }

                Op($"Niveaux par pile : le plus petit de {string.Join(", ", parts)} → {N(ml.Levels)}.");
            }
            else
            {
                Op("Niveaux par pile : 1 (rien ne peut être posé dessus).");
            }

            Op($"Piles au sol : {DivUp(units.Count, ml.Levels, "pile(s)")}.");
            var stackDx = ml.Rows > 0 ? (ml.MlRows * 1000 - Math.Max(0, ml.Rows - 1) * ctx.Gap) / ml.Rows : 0;
            Op($"Rangées : {N(ml.PerRow)} pile(s) de front → {DivUp(ml.Stacks, ml.PerRow, "rangée(s)")}.");
            Op(ctx.Gap > 0 && ml.Rows > 1
                ? $"Métrage par rangées : {N(ml.Rows)} × {M(stackDx / 1000)} m + {N(ml.Rows - 1)} × {M(ctx.Gap / 1000)} m = {M(ml.MlRows)} m."
                : $"Métrage par rangées : {N(ml.Rows)} × {M(stackDx / 1000)} m = {M(ml.MlRows)} m.");
            Op($"Métrage équivalent : {N(ml.Stacks)} × {M2(ml.FootprintM2 / Math.Max(1, ml.Stacks))} m² ÷ {M(u.Width / 1000)} m = {M(ml.MlEquivalent)} m.");
        }

        l.H("Le besoin total");
        var eq = plan.LinearMetersByOrder.Where(x => ctx.Orders.Contains(x.Key)).Select(x => M(x.Value.MlEquivalent)).ToList();
        Op($"Métrage nécessaire : {Sum(eq, M(m.LinearMetersRequiredEquivalent) + " m")}.");
        if (v.LinearMeters > 0 && v.MaxPayload > 0)
        {
            Op($"Par le métrage : {M(m.LinearMetersRequiredEquivalent)} ÷ {M(v.LinearMeters)} = {R(m.LinearMetersRequiredEquivalent / v.LinearMeters)} camion.");
            Op($"Par le poids : {Kg(total)} ÷ {Kg(v.MaxPayload)} = {R(total / v.MaxPayload)} camion.");
            Op($"Camions au minimum : {N(m.EstimatedVehicles)}.");
        }

        foreach (var load in plan.Loads.Where(x => x.Placements.Count > 0))
        {
            var lm = load.Metrics;
            var lv = load.Vehicle;
            l.H($"{load.Label}");
            Op($"Unités chargées : {N(load.Placements.Count)} ({N(lm.ItemsPlaced)} articles), dont {N(load.Placements.Count(p => p.OnFloor))} au sol.");
            Op($"Poids : {Kg(lm.LoadedWeightKg)} ÷ {Kg(lv.MaxPayload)} = {Pct(lm.WeightRate)} de la charge utile.");
            Op($"Plancher utilisé : {M(lm.LinearMetersReal)} ÷ {M(lv.LinearMeters)} = {Pct(lm.LinearRealRate)} de la longueur.");
            Op($"Surface au sol : {M2(lm.FloorUsedM2)} ÷ {M2(lv.FloorAreaM2)} = {Pct(lm.FloorRate)}.");
            Op($"Volume : {M3(lm.LoadedVolumeM3)} ÷ {M3(lv.VolumeM3)} = {Pct(lm.VolumeRate)}.");
            Op($"Hauteur la plus haute : {Mm(lm.MaxLoadHeight)} mm sur {Mm(u.Height)} mm.");
        }

        l.H("Le résultat");
        Op($"Articles chargés : {N(m.ItemsPlaced)} sur {N(m.ItemsTotal)}.");
        Op(m.ItemsRemaining == 0
            ? "Reliquat : aucun."
            : $"Reliquat : {N(m.ItemsTotal)} − {N(m.ItemsPlaced)} = {N(m.ItemsRemaining)} article(s) ({m.LimitingFactor}).");
        Op($"Camions utilisés : {N(plan.Loads.Count(x => x.Placements.Count > 0))} ; plancher total : {M(m.LinearMetersReal)} m.");

        return new(0, "Récapitulatif du calcul",
            "Toutes les opérations, dans l'ordre, écrites simplement avec les valeurs de ce groupage.", l);
    }

    // ------------------------------------------------------------------ 12. Lexique

    private static DetailSection Glossary()
    {
        var l = new Lines
        {
            new("Unité de chargement : ce que le moteur place (palette, carton, pile de plaques, tube, lit de tubes, bobine)."),
            new("Niveau : position dans une pile. Niveau 0 = au sol ; niveau 1 = première unité gerbée."),
            new("Gerbage : poser une unité sur une autre. NIVEAUX_MAX = nombre de gerbages autorisés au-dessus de l'unité du sol."),
            new("Charge supportable : poids maximal que l'unité peut porter au-dessus d'elle, toutes unités gerbées confondues."),
            new("ML (métrage linéaire) : longueur de plancher utilisée, en mètres."),
            new("ML rangées : rangées nécessaires × longueur au sol d'une pile (rangées complètes sur la largeur)."),
            new("ML équivalent : surface au sol occupée / largeur du camion (comme si tout était parfaitement jointif)."),
            new("ML réel : distance entre l'avant de la première unité au sol et l'arrière de la dernière, mesurée dans le plan."),
            new("Débord : espace volontairement laissé libre (entre unités, le long des parois, sous le plafond)."),
            new("Étape : numéro de l'arrêt de l'itinéraire où l'ordre est chargé (DEPART) ou livré (ARRIVEE)."),
            new("Reliquat : articles non chargés, toujours accompagnés de leur motif."),
            new("Séquence de chargement : ordre dans lequel les unités entrent dans le camion (1 = la première, au fond).")
        };
        return new(0, "Lexique", "Les mots utilisés dans ce détail.", l);
    }
}
