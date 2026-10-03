using System.Globalization;
using System.Text;
using OptiTunes.Core.Models;

namespace OptiTunes.Core.Import;

/// <summary>Correspondance d'une colonne du fichier avec un champ OptiTunes (null = ignorée).</summary>
public sealed record ColumnMapping(string Record, int Position, string FileColumn, string? Field);

public sealed class ImportResult
{
    public string Source { get; init; } = "";
    public char Separator { get; init; }
    public int LinesRead { get; set; }
    public List<Groupage> Groupages { get; } = [];
    public List<Issue> Issues { get; } = [];

    /// <summary>« Lignes typées GRP / OT », « Tableau (une ligne par ordre) », « Excel … ».</summary>
    public string Format { get; set; } = "";

    public string Encoding { get; set; } = "";

    /// <summary>Lignes brutes du fichier (index = numéro de ligne − 1).</summary>
    public List<string> RawLines { get; } = [];

    public List<ColumnMapping> Columns { get; } = [];

    public bool HasBlockingErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);
    public int OrderCount => Groupages.Sum(g => g.Orders.Count);
    public int BlockedOrders => Groupages.Sum(g => g.Orders.Count(o => !o.IsValid));

    public string RawLine(int? line) => line is { } n && n > 0 && n <= RawLines.Count ? RawLines[n - 1] : "";
}

/// <summary>
/// Import d'un groupage exporté en fichier à plat (voir docs/FORMAT_IMPORT.md).
/// Lignes typées : GRP (en-tête groupage + véhicule) puis OT (ordres de transport).
/// Une ligne "#GRP;..." ou "#OT;..." redéfinit l'ordre des colonnes.
/// </summary>
public sealed class FlatFileImporter
{
    public static readonly string[] DefaultGroupageColumns =
    [
        "ID_GROUPAGE", "DATE", "TRANSPORTEUR", "VEHICULE", "LONGUEUR_UTILE", "LARGEUR_UTILE",
        "HAUTEUR_UTILE", "CHARGE_UTILE", "LARGEUR_PORTE", "HAUTEUR_PORTE"
    ];

    public static readonly string[] DefaultOrderColumns =
    [
        "ID_ORDRE", "TYPE_ORDRE", "NUM_CDE", "LIGNE_CDE", "NUM_CADENCE", "CLIENT", "ARRET", "ARTICLE",
        "DESIGNATION", "TYPE_PHYSIQUE", "QUANTITE", "LONGUEUR", "LARGEUR", "HAUTEUR", "DIAMETRE",
        "POIDS_UNITAIRE", "GERBABLE", "NIVEAUX_MAX", "CHARGE_MAX_DESSUS", "QTE_MAX_PILE",
        "AU_SOL", "AU_SOMMET", "MAGASIN", "DEPART", "ARRIVEE"
    ];

    private static readonly Dictionary<string, string> Aliases = BuildAliases();

    private string[] _groupageColumns = DefaultGroupageColumns;
    private string[] _orderColumns = DefaultOrderColumns;

    static FlatFileImporter()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public ImportResult ImportFile(string path)
    {
        var name = Path.GetFileName(path);
        if (Path.GetExtension(path).ToLowerInvariant() is ".xls" or ".xlsx" or ".xlsm" or ".ods")
        {
            var refused = new ImportResult { Source = name, Format = "Classeur (non pris en charge)" };
            refused.Issues.Add(new Issue(IssueSeverity.Error, "FORMAT_CLASSEUR",
                "Seuls les fichiers à plat .csv / .txt sont importés : enregistrez le classeur au format CSV (séparateur « ; »)."));
            return refused;
        }

        // Lecture partagée : l'export reste souvent ouvert dans Excel pendant l'import.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var (text, encoding) = DecodeText(buffer.ToArray());
        return ImportText(text, name, null, encoding);
    }

    public ImportResult ImportText(string text, string source = "texte", string? format = null, string encoding = "UTF-8")
    {
        _groupageColumns = DefaultGroupageColumns;
        _orderColumns = DefaultOrderColumns;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var separator = DetectSeparator(lines);
        var result = new ImportResult { Source = source, Separator = separator, Encoding = encoding };
        result.RawLines.AddRange(lines);
        Groupage? current = null;
        var orderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var first = Array.FindIndex(lines, l => l.Trim().Length > 0);
        if (first >= 0 && IsTabular(lines, first, separator))
        {
            result.Format = (format != null ? format + " · " : "") + "tableau (une ligne par ordre)";
            ImportTabular(lines, first, separator, result);
            return Finish(result);
        }

        result.Format = (format != null ? format + " · " : "") + "lignes typées GRP / OT";
        var headerSeen = new HashSet<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var raw = lines[i].Trim();
            if (raw.Length == 0)
            {
                continue;
            }

            result.LinesRead++;
            var isHashLine = raw.StartsWith('#');
            var fields = SplitLine(isHashLine ? raw[1..] : raw, separator);
            var recordType = RecordTypeOf(fields[0]);

            if (recordType != null && LooksLikeHeader(fields))
            {
                var columns = fields.Skip(1).Select(Canonical).ToArray();
                if (recordType == "GRP")
                {
                    _groupageColumns = columns;
                }
                else
                {
                    _orderColumns = columns;
                }

                if (headerSeen.Add(recordType))
                {
                    RecordMappings(result, recordType, fields.Skip(1).ToArray());
                }

                var unknown = fields.Skip(1).Where(f => f.Length > 0 && !Aliases.ContainsKey(Normalize(f))).ToList();
                if (unknown.Count > 0)
                {
                    result.Issues.Add(new Issue(IssueSeverity.Info, "COL_INCONNUE",
                        $"Colonnes ignorées : {string.Join(", ", unknown)}", lineNumber));
                }

                continue;
            }

            if (isHashLine)
            {
                continue;
            }

            switch (recordType)
            {
                case "GRP":
                    current = ParseGroupage(fields, lineNumber, result);
                    result.Groupages.Add(current);
                    break;
                case "OT":
                    if (current == null)
                    {
                        current = new Groupage { Id = "SANS_GROUPAGE", SourceLine = lineNumber };
                        current.BlockingErrors.Add("Aucune ligne GRP avant les ordres : véhicule inconnu.");
                        result.Issues.Add(new Issue(IssueSeverity.Error, "GRP_ABSENT",
                            "Ordres trouvés avant toute ligne GRP : dimensions du véhicule inconnues.", lineNumber));
                        result.Groupages.Add(current);
                    }

                    var order = ParseOrder(fields, lineNumber, result);
                    if (!orderIds.Add($"{current.Id}|{order.Id}"))
                    {
                        result.Issues.Add(new Issue(IssueSeverity.Warning, "OT_DOUBLON",
                            $"Ordre {order.Id} présent plusieurs fois dans le groupage {current.Id}.", lineNumber, order.Id));
                    }

                    current.Orders.Add(order);
                    break;
                default:
                    result.Issues.Add(new Issue(IssueSeverity.Warning, "LIGNE_INCONNUE",
                        $"Type d'enregistrement « {fields[0]} » non reconnu (attendu GRP ou OT) : ligne ignorée.", lineNumber));
                    break;
            }
        }

        foreach (var record in new[] { "GRP", "OT" }.Where(r => !headerSeen.Contains(r)))
        {
            RecordMappings(result, record, (record == "GRP" ? DefaultGroupageColumns : DefaultOrderColumns), byDefault: true);
        }

        return Finish(result);
    }

    private static ImportResult Finish(ImportResult result)
    {
        if (result.Groupages.Count == 0)
        {
            result.Issues.Add(new Issue(IssueSeverity.Error, "FICHIER_VIDE", "Aucun groupage trouvé dans le fichier."));
        }

        // Itinéraire mixte (enlèvement après une livraison) : non prévu pour le moment, signalé.
        foreach (var g in result.Groupages)
        {
            var departures = g.Orders.Where(o => o.DepartureStep > 0).Select(o => o.DepartureStep).ToList();
            var arrivals = g.Orders.Where(o => o.EffectiveArrival > 0).Select(o => o.EffectiveArrival).ToList();
            if (departures.Count > 0 && arrivals.Count > 0 && departures.Max() > arrivals.Min())
            {
                result.Issues.Add(new Issue(IssueSeverity.Warning, "ITINERAIRE_MIXTE",
                    $"Groupage {g.Id} : enlèvement à l'étape {departures.Max()} après une livraison à l'étape {arrivals.Min()} – " +
                    "itinéraire mixte non prévu pour le moment, accessibilité contrôlée mais plan à vérifier.", g.SourceLine));
            }
        }

        foreach (var g in result.Groupages.Where(g => g.Orders.Count == 0))
        {
            result.Issues.Add(new Issue(IssueSeverity.Warning, "GRP_VIDE",
                $"Le groupage {g.Id} ne contient aucun ordre.", g.SourceLine));
        }

        return result;
    }

    private Groupage ParseGroupage(string[] fields, int line, ImportResult result, bool requireVehicle = true)
    {
        var row = new Row(fields, _groupageColumns);
        var g = new Groupage
        {
            Id = row.Text("ID_GROUPAGE") ?? $"GRP-L{line}",
            Date = ParseDate(row.Text("DATE")),
            Carrier = row.Text("TRANSPORTEUR"),
            SourceLine = line
        };

        var v = g.Vehicle;
        v.Id = row.Text("VEHICULE") ?? "Véhicule";
        v.Type = v.Id;
        v.Length = row.Number("LONGUEUR_UTILE") ?? 0;
        v.Width = row.Number("LARGEUR_UTILE") ?? 0;
        v.Height = row.Number("HAUTEUR_UTILE") ?? 0;
        v.MaxPayload = row.Number("CHARGE_UTILE") ?? 0;
        v.DoorWidth = row.Number("LARGEUR_PORTE");
        v.DoorHeight = row.Number("HAUTEUR_PORTE");

        void Require(double value, string label)
        {
            if (value > 0 || !requireVehicle)
            {
                return;
            }

            g.BlockingErrors.Add($"{label} du véhicule absente ou nulle.");
            result.Issues.Add(new Issue(IssueSeverity.Error, "VEH_INDISPENSABLE",
                $"Groupage {g.Id} : {label} du véhicule absente ou nulle – calcul impossible.", line));
        }

        Require(v.Length, "Longueur utile");
        Require(v.Width, "Largeur utile");
        Require(v.Height, "Hauteur utile");
        Require(v.MaxPayload, "Charge utile");

        void Odd(bool condition, string message)
        {
            if (condition)
            {
                result.Issues.Add(new Issue(IssueSeverity.Warning, "VEH_HORS_NORME", $"Groupage {g.Id} : {message}", line));
            }
        }

        Odd(v.Length is > 0 and < 1000 || v.Width is > 0 and < 1000 || v.Height is > 0 and < 1000,
            "dimension intérieure < 1 m : saisie en cm ou en m ? Les dimensions sont attendues en mm.");
        Odd(v.Length > 20000 || v.Width > 3000 || v.Height > 4000, "dimensions intérieures hors gabarit routier : vérifier l'unité (mm).");
        Odd(v.MaxPayload is > 0 and < 100 || v.MaxPayload > 45000, $"charge utile {v.MaxPayload:N0} kg inhabituelle : saisie en tonnes ?");

        if (!requireVehicle)
        {
            g.BlockingErrors.Add("Véhicule non défini dans le fichier : choisir un véhicule dans la liste VÉHICULE.");
            return g;
        }

        if (!v.HasDoor)
        {
            result.Issues.Add(new Issue(IssueSeverity.Warning, "PORTE_INCONNUE",
                $"Groupage {g.Id} : dimensions d'ouverture absentes – le passage par la porte n'est pas vérifié.", line));
        }
        else if (v.DoorWidth > v.Width + Geometry.Eps || v.DoorHeight > v.Height + Geometry.Eps)
        {
            result.Issues.Add(new Issue(IssueSeverity.Warning, "PORTE_INCOHERENTE",
                $"Groupage {g.Id} : ouverture plus grande que l'intérieur utile.", line));
        }

        return g;
    }

    private TransportOrder ParseOrder(string[] fields, int line, ImportResult result)
    {
        var row = new Row(fields, _orderColumns);
        var recordKind = Normalize(fields[0]) == "CAD" ? OrderKind.Cadence : OrderKind.CommandLine;
        var o = new TransportOrder
        {
            Id = row.Text("ID_ORDRE") ?? $"OT-L{line}",
            Kind = ParseKind(row.Text("TYPE_ORDRE")) ?? recordKind,
            OrderNumber = row.Text("NUM_CDE"),
            OrderLine = row.Text("LIGNE_CDE"),
            CadenceNumber = row.Text("NUM_CADENCE"),
            Customer = row.Text("CLIENT"),
            Stop = (int)(row.Number("ARRET") ?? 0),
            Warehouse = row.Text("MAGASIN"),
            DepartureStep = (int)(row.Number("DEPART") ?? 0),
            ArrivalStep = (int)(row.Number("ARRIVEE") ?? 0),
            Article = row.Text("ARTICLE") ?? "",
            Designation = row.Text("DESIGNATION"),
            Length = row.Number("LONGUEUR") ?? 0,
            Width = row.Number("LARGEUR") ?? 0,
            Height = row.Number("HAUTEUR") ?? 0,
            Diameter = row.Number("DIAMETRE") ?? 0,
            UnitWeight = row.Number("POIDS_UNITAIRE") ?? -1,
            Stackable = ParseBool(row.Text("GERBABLE")),
            // Fichier : nombre de gerbages au-dessus de l'unité au sol (0 = rien dessus).
            // Interne (§7.1) : nombre total de niveaux, sol compris.
            MaxLevels = (int?)row.Number("NIVEAUX_MAX") + 1,
            MaxLoadOnTop = row.Number("CHARGE_MAX_DESSUS"),
            Orientations = Orientations.Parse(row.Text("ORIENTATIONS")),
            MaxPerPile = (int?)row.Number("QTE_MAX_PILE"),
            MustBeOnFloor = ParseBool(row.Text("AU_SOL")) ?? false,
            MustBeOnTop = ParseBool(row.Text("AU_SOMMET")) ?? false,
            SourceLine = line
        };

        if (o.Kind == OrderKind.Cadence && string.IsNullOrEmpty(o.CadenceNumber))
        {
            Warn("CADENCE_SANS_NUM", "ligne de cadencement sans numéro de cadence.");
        }

        var typeText = row.Text("TYPE_PHYSIQUE");
        var type = ParsePhysicalType(typeText);
        if (type == null)
        {
            o.TypeKnown = false;
            Block(typeText == null ? "type physique absent" : $"type physique « {typeText} » inconnu");
        }
        else
        {
            o.Type = type.Value;
        }

        // Référence article saisie une colonne trop tôt : ARTICLE vide et un texte non numérique dans ARRET.
        var stopText = row.Text("ARRET");
        if (string.IsNullOrWhiteSpace(o.Article) && !string.IsNullOrWhiteSpace(stopText) && row.Number("ARRET") == null)
        {
            o.Article = stopText;
            Warn("ARTICLE_DANS_ARRET",
                $"ARTICLE vide et « {stopText} » dans la colonne ARRET (numéro attendu) : pris comme référence article. Vérifier l'export.");
        }

        if (string.IsNullOrWhiteSpace(o.Article))
        {
            Block("référence article absente");
        }

        var qty = row.Number("QUANTITE");
        if (qty is null or <= 0 || qty != Math.Floor(qty.Value))
        {
            Block("quantité absente, nulle ou non entière");
        }
        else
        {
            o.Quantity = (int)qty.Value;
        }

        if (o.UnitWeight < 0)
        {
            Block("poids unitaire absent");
        }
        else if (o.UnitWeight == 0)
        {
            Warn("POIDS_NUL", "poids unitaire à 0 kg.");
        }

        if (type != null)
        {
            ValidateDimensions(o, Block, Warn);
        }

        if (o.MaxLoadOnTop is < 0)
        {
            Block("charge maximale supportable négative");
        }

        // Gestion des arrêts (facultative) : étapes de l'itinéraire du groupage.
        if (o.DepartureStep < 0 || o.ArrivalStep < 0)
        {
            Warn("ETAPE_NEGATIVE", "étape de départ ou d'arrivée négative : étapes ignorées.");
            (o.DepartureStep, o.ArrivalStep) = (0, 0);
        }
        else if (o.DepartureStep > 0 && o.ArrivalStep > 0 && o.DepartureStep >= o.ArrivalStep)
        {
            Warn("ETAPES_INCOHERENTES", $"départ à l'étape {o.DepartureStep} après l'arrivée à l'étape {o.ArrivalStep} : étapes ignorées.");
            (o.DepartureStep, o.ArrivalStep) = (0, 0);
        }

        if (o.MaxLevels is < 1)
        {
            Block("NIVEAUX_MAX négatif");
        }

        if (o.Stackable == false && o.MaxLevels > 1)
        {
            Info("NIVEAUX_IGNORES", $"non gerbable : NIVEAUX_MAX = {o.MaxLevels - 1} ignoré.");
        }

        if (type != null && o.BlockingErrors.Count == 0)
        {
            CheckPlausibility(o, Warn);
        }

        if (o.Stackable == null)
        {
            Warn("GERBABLE_INCONNU", "gerbabilité non renseignée – considéré NON gerbable par sécurité.");
        }
        else if (o.Stackable == true)
        {
            if (o.MaxLevels == null)
            {
                Warn("NIVEAUX_INCONNUS", "gerbable sans nombre maximal de niveaux – limité au niveau minimal sûr.");
            }
            else if (o.MaxLevels <= 1)
            {
                Warn("GERBABLE_0_NIVEAU", "gerbable mais NIVEAUX_MAX = 0 : aucun gerbage possible, rien ne sera posé dessus.");
            }

            if (o.MaxLoadOnTop == null)
            {
                Info("CHARGE_INCONNUE", "charge maximale supportable inconnue – limitée au poids de ses propres niveaux.");
            }
            else if (o.MaxLoadOnTop == 0)
            {
                Info("CHARGE_NULLE", "charge supportable à 0 : l'unité ne peut rien recevoir.");
            }
        }

        if (o.Type == PhysicalType.Plaque && o.MaxPerPile == null)
        {
            Info("PILE_MAX_INCONNUE", "quantité maximale par pile non renseignée – pile limitée par la hauteur utile.");
        }

        if (o.MustBeOnFloor && o.MustBeOnTop)
        {
            Warn("SOL_ET_SOMMET", "à la fois « au sol » et « au sommet » : rien ne sera posé dessus.");
        }

        foreach (var error in o.BlockingErrors)
        {
            result.Issues.Add(new Issue(IssueSeverity.Error, "OT_INDISPENSABLE", $"Ordre {o.Id} : {error} – ordre non chargé.", line, o.Id));
        }

        return o;

        void Block(string message) => o.BlockingErrors.Add(message);

        void Warn(string code, string message) =>
            result.Issues.Add(new Issue(IssueSeverity.Warning, code, $"Ordre {o.Id} : {message}", line, o.Id));

        void Info(string code, string message) =>
            result.Issues.Add(new Issue(IssueSeverity.Info, code, $"Ordre {o.Id} : {message}", line, o.Id));
    }

    /// <summary>
    /// Contrôles de vraisemblance : unités (mm / kg) probablement fausses, densité incohérente, valeurs hors norme.
    /// Ils n'empêchent pas le calcul mais signalent une donnée à vérifier.
    /// </summary>
    private static void CheckPlausibility(TransportOrder o, Action<string, string> warn)
    {
        var dims = o.Type switch
        {
            PhysicalType.Tube => new[] { o.Length, o.Diameter },
            PhysicalType.Roll => new[] { o.Diameter, o.Width },
            PhysicalType.Plaque => new[] { o.Length, o.Width },
            _ => new[] { o.Length, o.Width, o.Height }
        };

        if (dims.Any(x => x > 0 && x < 20))
        {
            warn("UNITE_DIMENSION", $"dimension < 20 mm ({string.Join(" × ", dims.Select(x => x.ToString("0.##")))}) : saisie en cm ou en m ? Les dimensions sont attendues en mm.");
        }

        if (dims.Any(x => x > 20000))
        {
            warn("DIMENSION_HORS_NORME", "dimension > 20 m : vérifier l'unité (mm attendus).");
        }

        if (o.Type == PhysicalType.Plaque && (o.Height < 0.2 || o.Height > 100))
        {
            warn("EPAISSEUR_HORS_NORME", $"épaisseur de plaque {o.Height:0.##} mm inhabituelle (colonne HAUTEUR = épaisseur unitaire).");
        }

        if (o.Type == PhysicalType.Tube && o.Diameter > o.Length)
        {
            warn("TUBE_DIAMETRE", $"diamètre ({o.Diameter:0} mm) supérieur à la longueur ({o.Length:0} mm) : colonnes inversées ?");
        }

        // Densité apparente de l'enveloppe (kg/m³). Acier plein ≈ 7 850 ; carton ≈ 100 à 700.
        var volume = o.Type switch
        {
            PhysicalType.Tube => Math.PI * Math.Pow(o.Diameter / 2, 2) * o.Length,
            PhysicalType.Roll => Math.PI * Math.Pow(o.Diameter / 2, 2) * o.Width,
            _ => o.Length * o.Width * o.Height
        } / 1e9;
        if (volume > 0 && o.UnitWeight > 0)
        {
            var density = o.UnitWeight / volume;
            if (density > 8000)
            {
                warn("DENSITE_ELEVEE", $"densité apparente {density:N0} kg/m³ (> acier) : poids en kg et dimensions en mm ?");
            }
            else if (density < 3 && o.Type != PhysicalType.Tube)
            {
                warn("DENSITE_FAIBLE", $"densité apparente {density:N1} kg/m³ très faible : poids unitaire en kg ?");
            }
        }

        if (o.MaxLevels > 11)
        {
            warn("NIVEAUX_ELEVES", $"{o.MaxLevels - 1} gerbages maximum : valeur inhabituelle.");
        }
    }

    private static void ValidateDimensions(TransportOrder o, Action<string> block, Action<string, string> warn)
    {
        switch (o.Type)
        {
            case PhysicalType.Tube:
                if (o.Diameter <= 0 && o.Width > 0)
                {
                    o.Diameter = o.Width;
                    warn("DIAMETRE_DEDUIT", "diamètre absent : largeur utilisée comme diamètre.");
                }

                if (o.Length <= 0 || o.Diameter <= 0)
                {
                    block("longueur et diamètre indispensables pour un tube");
                }

                break;
            case PhysicalType.Roll:
                if (o.Width <= 0 && o.Length > 0)
                {
                    o.Width = o.Length;
                }

                if (o.Diameter <= 0 || o.Width <= 0)
                {
                    block("diamètre et largeur indispensables pour une bobine");
                }

                break;
            case PhysicalType.Plaque:
                if (o.Length <= 0 || o.Width <= 0 || o.Height <= 0)
                {
                    block("longueur, largeur et épaisseur indispensables pour une plaque");
                }

                break;
            default:
                // Export décalé d'une colonne : LONGUEUR vide et un « diamètre » sur un pavé (palette, carton…).
                if (o.Length <= 0 && o.Width > 0 && o.Height > 0 && o.Diameter > 0)
                {
                    warn("COLONNES_DECALEES",
                        $"LONGUEUR vide et DIAMETRE renseigné sur un {o.Type.Label()} : colonnes décalées corrigées → " +
                        $"L {o.Width:0} × l {o.Height:0} × H {o.Diameter:0} mm. Vérifier l'export.");
                    (o.Length, o.Width, o.Height, o.Diameter) = (o.Width, o.Height, o.Diameter, 0);
                }

                if (o.Length <= 0 || o.Width <= 0 || o.Height <= 0)
                {
                    block("longueur, largeur et hauteur indispensables");
                }

                break;
        }
    }

    public static PhysicalType? ParsePhysicalType(string? text) => Normalize(text ?? "") switch
    {
        "BOX" or "CARTON" or "CAISSE" or "BAC" or "COLIS" or "BOITE" => PhysicalType.Box,
        "FLAT" or "PLAQUE" or "PLAQUES" or "PANNEAU" or "INTERCALAIRE" => PhysicalType.Plaque,
        "CYLINDER" or "TUBE" or "TUBES" or "BARRE" or "CYLINDRE" => PhysicalType.Tube,
        "BUNDLE" or "FAISCEAU" or "LOT" => PhysicalType.Bundle,
        "PALLET" or "PALETTE" => PhysicalType.Pallet,
        "ROLL" or "BOBINE" or "ROULEAU" => PhysicalType.Roll,
        "CUSTOM" or "SPECIAL" => PhysicalType.Custom,
        _ => null
    };

    private static OrderKind? ParseKind(string? text) => Normalize(text ?? "") switch
    {
        "CDE" or "COMMANDE" or "LIGNE" or "LIGNECDE" => OrderKind.CommandLine,
        "CAD" or "CADENCE" or "CADENCEMENT" => OrderKind.Cadence,
        _ => null
    };

    public static bool? ParseBool(string? text) => Normalize(text ?? "") switch
    {
        "O" or "OUI" or "Y" or "YES" or "1" or "TRUE" or "VRAI" or "X" => true,
        "N" or "NON" or "NO" or "0" or "FALSE" or "FAUX" => false,
        _ => null
    };

    public static double? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var t = text.Trim().Replace(" ", "").Replace(" ", "").Replace(" ", "").Replace(',', '.').ToLowerInvariant();
        var factor = 1.0;
        foreach (var (suffix, f) in UnitSuffixes)
        {
            if (t.Length > suffix.Length && t.EndsWith(suffix) && char.IsDigit(t[^(suffix.Length + 1)]))
            {
                factor = f;
                t = t[..^suffix.Length];
                break;
            }
        }

        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value * factor : null;
    }

    /// <summary>Unités acceptées dans les cellules : longueurs ramenées en mm, masses en kg.</summary>
    private static readonly (string Suffix, double Factor)[] UnitSuffixes =
    [
        ("mm", 1), ("cm", 10), ("kg", 1), ("m", 1000), ("t", 1000), ("g", 0.001)
    ];

    private static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Date Excel (numéro de série) issue d'un classeur.
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 20000 and < 80000)
        {
            return DateTime.FromOADate(serial);
        }

        string[] formats = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd.MM.yyyy", "yyyyMMdd", "dd/MM/yyyy HH:mm"];
        return DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
    }

    private static string? RecordTypeOf(string field) => Normalize(field) switch
    {
        "GRP" or "GROUPAGE" or "G" or "ENT" or "ENTETE" => "GRP",
        "OT" or "ORDRE" or "O" or "LIG" or "CDE" or "CAD" => "OT",
        _ => null
    };

    private static bool LooksLikeHeader(string[] fields)
    {
        var rest = fields.Skip(1).Where(f => f.Length > 0).ToList();
        return rest.Count > 0 && rest.Count(f => Aliases.ContainsKey(Normalize(f))) * 2 >= rest.Count;
    }

    private static string Canonical(string field) =>
        Aliases.TryGetValue(Normalize(field), out var key) ? key : "?" + field;

    private static char DetectSeparator(string[] lines)
    {
        // Plusieurs lignes : un commentaire en tête (« # ..., ... ») ne doit pas décider seul.
        var sample = lines.Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("//")).Take(10).ToList();
        char[] candidates = [';', '\t', '|', ','];
        return candidates.OrderByDescending(c => sample.Sum(l => l.Count(ch => ch == c))).First();
    }

    private static string[] SplitLine(string line, char separator)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == separator && !inQuotes)
            {
                fields.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        fields.Add(sb.ToString().Trim());
        return fields.ToArray();
    }

    private static (string Text, string Encoding) DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "UTF-8 (BOM)");
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16");
        }

        try
        {
            return (new UTF8Encoding(false, true).GetString(bytes), "UTF-8");
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.GetEncoding(1252).GetString(bytes), "Windows-1252 (ANSI)");
        }
    }

    /// <summary>
    /// Export « tableau » : pas de type d'enregistrement, une ligne par ordre ; les colonnes du groupage et du
    /// véhicule sont répétées sur chaque ligne (ou absentes : le véhicule se choisit alors dans le catalogue).
    /// </summary>
    private static bool IsTabular(string[] lines, int first, char separator)
    {
        var header = lines[first].Trim();
        if (header.StartsWith('#') || !IsTabularHeader(SplitLine(header, separator)))
        {
            return false;
        }

        // « ORDRE » est à la fois un type de ligne et un nom de colonne : la 1re ligne de données tranche.
        var next = lines.Skip(first + 1).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));
        return next == null || RecordTypeOf(SplitLine(next, separator)[0]) == null;
    }

    private static bool IsTabularHeader(string[] fields)
    {
        if (fields.Length < 3)
        {
            return false;
        }

        var known = fields.Where(f => f.Length > 0).Select(f => Aliases.GetValueOrDefault(Normalize(f))).Where(k => k != null).ToList();
        return known.Count >= 3 && known.Count * 2 >= fields.Count(f => f.Length > 0) &&
               known.Contains("QUANTITE") && (known.Contains("ARTICLE") || known.Contains("ID_ORDRE"));
    }

    private void ImportTabular(string[] lines, int headerIndex, char separator, ImportResult result)
    {
        var header = SplitLine(lines[headerIndex].Trim().TrimStart('#'), separator);
        var columns = header.Select(Canonical).ToArray();
        _groupageColumns = columns;
        _orderColumns = columns;
        RecordMappings(result, "Tableau", header);
        result.LinesRead++;

        var vehicleColumns = columns.Intersect(["LONGUEUR_UTILE", "LARGEUR_UTILE", "HAUTEUR_UTILE", "CHARGE_UTILE"]).Any();
        if (!vehicleColumns)
        {
            result.Issues.Add(new Issue(IssueSeverity.Warning, "VEH_ABSENT",
                "Aucune colonne véhicule (LONGUEUR_UTILE, LARGEUR_UTILE, HAUTEUR_UTILE, CHARGE_UTILE) : choisissez le véhicule dans la liste VÉHICULE.",
                headerIndex + 1));
        }

        var groupages = new Dictionary<string, Groupage>(StringComparer.OrdinalIgnoreCase);
        var orderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inconsistent = new HashSet<string>();
        for (var i = headerIndex + 1; i < lines.Length; i++)
        {
            var raw = lines[i].Trim();
            if (raw.Length == 0 || raw.StartsWith('#'))
            {
                continue;
            }

            result.LinesRead++;
            var fields = new[] { "OT" }.Concat(SplitLine(raw, separator)).ToArray();
            var key = new Row(fields, columns).Text("ID_GROUPAGE") ?? "GROUPAGE";
            if (!groupages.TryGetValue(key, out var groupage))
            {
                groupage = ParseGroupage(fields, i + 1, result, vehicleColumns);
                groupage.Id = key;
                groupages[key] = groupage;
                result.Groupages.Add(groupage);
            }
            else if (vehicleColumns && !inconsistent.Contains(key))
            {
                var row = new Row(fields, columns);
                var v = groupage.Vehicle;
                if (row.Number("LONGUEUR_UTILE") is { } l && Math.Abs(l - v.Length) > Geometry.Eps ||
                    row.Number("LARGEUR_UTILE") is { } w && Math.Abs(w - v.Width) > Geometry.Eps ||
                    row.Number("HAUTEUR_UTILE") is { } h && Math.Abs(h - v.Height) > Geometry.Eps)
                {
                    inconsistent.Add(key);
                    result.Issues.Add(new Issue(IssueSeverity.Warning, "VEH_INCOHERENT",
                        $"Groupage {key} : dimensions du véhicule différentes selon les lignes – celles de la ligne {groupage.SourceLine} sont retenues.", i + 1));
                }
            }

            var order = ParseOrder(fields, i + 1, result);
            if (!orderIds.Add($"{key}|{order.Id}"))
            {
                result.Issues.Add(new Issue(IssueSeverity.Warning, "OT_DOUBLON",
                    $"Ordre {order.Id} présent plusieurs fois dans le groupage {key}.", i + 1, order.Id));
            }

            groupage.Orders.Add(order);
        }
    }

    private static void RecordMappings(ImportResult result, string record, string[] fileColumns, bool byDefault = false)
    {
        for (var i = 0; i < fileColumns.Length; i++)
        {
            if (fileColumns[i].Length == 0)
            {
                continue;
            }

            var field = Aliases.GetValueOrDefault(Normalize(fileColumns[i]));
            result.Columns.Add(new ColumnMapping(byDefault ? record + " (ordre par défaut)" : record, i + 1, fileColumns[i], field));
        }
    }

    public static string Normalize(string text)
    {
        var decomposed = text.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (char.IsLetterOrDigit(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static Dictionary<string, string> BuildAliases()
    {
        var map = new Dictionary<string, string>();

        void Add(string key, params string[] aliases)
        {
            map[Normalize(key)] = key;
            foreach (var a in aliases)
            {
                map[Normalize(a)] = key;
            }
        }

        Add("ID_GROUPAGE", "GROUPAGE", "NUM_GROUPAGE", "NO_GROUPAGE", "LOAD_ID", "SHIPMENT", "SHIPMENT_ID", "VOYAGE", "TOURNEE");
        Add("DATE", "DATE_GROUPAGE", "DATE_DEPART", "DATE_CHARGEMENT");
        Add("TRANSPORTEUR", "CARRIER", "HAULIER");
        Add("VEHICULE", "TYPE_VEHICULE", "ID_VEHICULE", "CAMION", "VEHICLE", "TRUCK", "TRAILER");
        Add("LONGUEUR_UTILE", "LONGUEUR_VEHICULE", "LC", "LONGUEUR_CAMION", "INNER_LENGTH", "TRUCK_LENGTH");
        Add("LARGEUR_UTILE", "LARGEUR_VEHICULE", "LARGEUR_CAMION", "INNER_WIDTH", "TRUCK_WIDTH");
        Add("HAUTEUR_UTILE", "HAUTEUR_VEHICULE", "HC", "HAUTEUR_CAMION", "INNER_HEIGHT", "TRUCK_HEIGHT");
        Add("CHARGE_UTILE", "PMAX", "POIDS_MAX", "CHARGE_MAX", "PAYLOAD", "MAX_PAYLOAD", "PTAC_UTILE");
        Add("LARGEUR_PORTE", "LARGEUR_OUVERTURE", "DOOR_WIDTH");
        Add("HAUTEUR_PORTE", "HAUTEUR_OUVERTURE", "DOOR_HEIGHT");

        Add("ID_ORDRE", "ORDRE", "ID_OT", "NUM_OT", "ORDRE_TRANSPORT", "ORDER_ID", "ID_LIGNE", "TRANSPORT_ORDER");
        Add("TYPE_ORDRE", "NATURE_ORDRE");
        Add("NUM_CDE", "COMMANDE", "NUM_COMMANDE", "NO_CDE", "ORDER", "ORDER_NUMBER", "PO");
        Add("LIGNE_CDE", "LIGNE", "NUM_LIGNE", "LIGNE_COMMANDE");
        Add("NUM_CADENCE", "CADENCE", "CADENCEMENT", "LIGNE_CADENCE");
        Add("CLIENT", "CODE_CLIENT", "DESTINATAIRE", "CUSTOMER", "CONSIGNEE", "NOM_CLIENT");
        Add("ARRET", "SEQUENCE", "ORDRE_LIVRAISON", "NUM_ARRET", "STOP", "DROP", "RANG_LIVRAISON");
        Add("ARTICLE", "REFERENCE", "CODE_ARTICLE", "REF_ARTICLE", "ITEM", "SKU", "PRODUCT", "REF");
        Add("DESIGNATION", "LIBELLE", "DESCRIPTION", "LIBELLE_ARTICLE");
        Add("TYPE_PHYSIQUE", "FAMILLE", "TYPE_UNITE", "CONDITIONNEMENT", "EMBALLAGE", "PACKAGING", "UNIT_TYPE");
        Add("QUANTITE", "QTE", "QTE_A_LIVRER", "QUANTITY", "QTY", "NB", "NOMBRE", "NB_COLIS", "NB_PALETTES");
        Add("LONGUEUR", "L", "LONG", "LENGTH", "LONGUEUR_MM");
        Add("LARGEUR", "LARG", "LAIZE", "WIDTH", "LARGEUR_MM");
        Add("HAUTEUR", "EPAISSEUR", "H", "HAUT", "EP", "HEIGHT", "THICKNESS", "HAUTEUR_MM");
        Add("DIAMETRE", "DIAM", "D", "DIAMETER", "DIAMETRE_MM");
        Add("POIDS_UNITAIRE", "POIDS", "POIDS_UNIT", "MASSE", "WEIGHT", "UNIT_WEIGHT", "POIDS_KG", "POIDS_BRUT");
        Add("GERBABLE", "EMPILABLE", "STACKABLE");
        Add("NIVEAUX_MAX", "NMAX", "NIVEAUX", "NB_NIVEAUX", "GERBAGE", "NB_GERBAGES", "MAX_STACK", "STACK_LEVELS");
        Add("CHARGE_MAX_DESSUS", "PSUPPORT", "CHARGE_SUPPORTABLE", "POIDS_MAX_DESSUS", "MAX_LOAD_ON_TOP");
        Add("ORIENTATIONS", "ORIENTATION");
        Add("QTE_MAX_PILE", "QMAXPILE", "QTE_PAR_PILE");
        Add("AU_SOL", "OBLIGATOIREMENT_AU_SOL");
        Add("AU_SOMMET", "OBLIGATOIREMENT_AU_SOMMET", "DESSUS");
        Add("MAGASIN", "MAGASIN_DEPART", "DEPOT", "SITE_DEPART", "IC_CHAR3_1", "WAREHOUSE");
        Add("DEPART", "ETAPE_DEPART", "NUM_DEPART", "IC_NUM1", "PICKUP_STEP");
        Add("ARRIVEE", "ARRIVE", "ETAPE_ARRIVEE", "NUM_ARRIVEE", "IC_NUM2", "DELIVERY_STEP");
        return map;
    }

    private sealed class Row(string[] fields, string[] columns)
    {
        public string? Text(string key)
        {
            var index = Array.IndexOf(columns, key);
            if (index < 0 || index + 1 >= fields.Length)
            {
                return null;
            }

            var value = fields[index + 1].Trim();
            return value.Length == 0 ? null : value;
        }

        public double? Number(string key) => ParseNumber(Text(key));
    }
}
