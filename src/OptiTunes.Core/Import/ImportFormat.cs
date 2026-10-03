using System.Text;

namespace OptiTunes.Core.Import;

public enum FieldRequirement
{
    /// <summary>Sans cette colonne, la ligne (ou le groupage) ne peut pas être calculée.</summary>
    Obligatoire,

    /// <summary>Obligatoire selon le cas (type physique, gerbable…).</summary>
    Conditionnelle,

    /// <summary>Calcul possible sans, mais moins fiable : un avertissement est affiché.</summary>
    Recommandee,

    /// <summary>Améliore le plan ou l'affichage.</summary>
    Facultative
}

/// <summary>Description d'une colonne du fichier à plat.</summary>
public sealed record FieldDoc(string Column, string Label, FieldRequirement Requirement, string Description, string Example1, string Example2 = "")
{
    public string RequirementLabel => Requirement switch
    {
        FieldRequirement.Obligatoire => "Obligatoire",
        FieldRequirement.Conditionnelle => "Selon le cas",
        FieldRequirement.Recommandee => "Recommandée",
        _ => "Facultative"
    };
}

/// <summary>
/// Référence du format d'import : documentation de chaque colonne (aide de l'application) et modèle CSV complet.
/// L'ordre des colonnes est celui de l'import par défaut (<see cref="FlatFileImporter.DefaultGroupageColumns"/>,
/// <see cref="FlatFileImporter.DefaultOrderColumns"/>).
/// </summary>
public static class ImportFormat
{
    public static IReadOnlyList<FieldDoc> GroupageFields { get; } =
    [
        new("ID_GROUPAGE", "Identifiant du groupage", FieldRequirement.Obligatoire,
            "Numéro du groupage (ou du voyage). Les lignes OT qui suivent lui sont rattachées.", "26090007"),
        new("DATE", "Date de chargement", FieldRequirement.Facultative,
            "Format jj/mm/aaaa ou aaaa-mm-jj. Affichée dans l'en-tête et sur la feuille de chargement.", "02/10/2026"),
        new("TRANSPORTEUR", "Transporteur", FieldRequirement.Facultative,
            "Nom du transporteur, repris sur la feuille de chargement.", "GEODIS"),
        new("VEHICULE", "Véhicule", FieldRequirement.Recommandee,
            "Type ou nom du véhicule (affichage).", "SEMI 13.60"),
        new("LONGUEUR_UTILE", "Longueur intérieure utile", FieldRequirement.Obligatoire,
            "En mm, de la cabine à la porte. Sans dimensions, choisir un véhicule dans la liste VÉHICULE (ou un véhicule par défaut dans les Paramètres).", "13600"),
        new("LARGEUR_UTILE", "Largeur intérieure utile", FieldRequirement.Obligatoire,
            "En mm, entre les parois latérales.", "2450"),
        new("HAUTEUR_UTILE", "Hauteur intérieure utile", FieldRequirement.Obligatoire,
            "En mm, du plancher au plafond.", "2700"),
        new("CHARGE_UTILE", "Charge utile", FieldRequirement.Obligatoire,
            "Masse maximale transportable, en kg (pas en tonnes).", "24000"),
        new("LARGEUR_PORTE", "Largeur de la porte", FieldRequirement.Recommandee,
            "Ouverture arrière en mm. Sans elle, le passage des unités par la porte n'est pas vérifié.", "2450"),
        new("HAUTEUR_PORTE", "Hauteur de la porte", FieldRequirement.Recommandee,
            "Ouverture arrière en mm. Limite aussi la hauteur des piles de plaques.", "2650")
    ];

    public static IReadOnlyList<FieldDoc> OrderFields { get; } =
    [
        new("ID_ORDRE", "Identifiant de l'ordre", FieldRequirement.Obligatoire,
            "Identifiant unique de l'ordre de transport dans le groupage.", "26090007-1", "26090007-2"),
        new("TYPE_ORDRE", "Type d'ordre", FieldRequirement.Facultative,
            "CDE = ligne de commande, CAD = ligne de cadencement d'une ligne de commande.", "CDE", "CAD"),
        new("NUM_CDE", "Numéro de commande", FieldRequirement.Facultative,
            "Utilisé pour le rangement « par commande » et la traçabilité.", "C26001", "C26002"),
        new("LIGNE_CDE", "Ligne de commande", FieldRequirement.Facultative, "Numéro de ligne de la commande.", "10", "20"),
        new("NUM_CADENCE", "Numéro de cadence", FieldRequirement.Conditionnelle,
            "Recommandé pour les lignes CAD (cadencement).", "", "1"),
        new("CLIENT", "Client", FieldRequirement.Facultative,
            "Client / adresse de livraison. Utilisé pour le rangement « par client » et les couleurs.", "C00022-002", "C00720-001"),
        new("ARRET", "Ordre de livraison simple", FieldRequirement.Facultative,
            "1 = livré en premier (côté porte). Ancienne colonne : ARRIVEE la remplace si elle est renseignée.", "", ""),
        new("ARTICLE", "Référence article", FieldRequirement.Obligatoire, "Code article.", "GALIAA09", "CARTON-RSC-40"),
        new("DESIGNATION", "Désignation", FieldRequirement.Facultative, "Libellé de l'article.", "Palette caisses GALIA", "Carton RSC 400x300x300"),
        new("TYPE_PHYSIQUE", "Type physique", FieldRequirement.Obligatoire,
            "BOX (carton, caisse), PALETTE, PLAQUE, TUBE, FAISCEAU, BOBINE ou CUSTOM. Détermine les dimensions attendues.", "PALETTE", "BOX"),
        new("QUANTITE", "Quantité", FieldRequirement.Obligatoire,
            "Nombre entier d'articles (palettes, cartons, plaques, tubes…).", "4", "20"),
        new("LONGUEUR", "Longueur", FieldRequirement.Conditionnelle,
            "En mm. Obligatoire sauf pour une bobine. Tube : longueur du tube. Placée dans le sens du camion si la rotation au sol est interdite.", "1200", "600"),
        new("LARGEUR", "Largeur", FieldRequirement.Conditionnelle,
            "En mm. Obligatoire sauf pour un tube. Bobine : laize.", "800", "400"),
        new("HAUTEUR", "Hauteur / épaisseur", FieldRequirement.Conditionnelle,
            "En mm. Obligatoire sauf tube et bobine. Plaque : épaisseur d'une plaque.", "1300", "400"),
        new("DIAMETRE", "Diamètre", FieldRequirement.Conditionnelle, "En mm. Obligatoire pour TUBE et BOBINE.", "", ""),
        new("POIDS_UNITAIRE", "Poids unitaire", FieldRequirement.Obligatoire,
            "En kg, par article (par plaque pour les plaques).", "117", "6,5"),
        new("GERBABLE", "Gerbable", FieldRequirement.Obligatoire,
            "O / N. Vide = considéré non gerbable par sécurité (avertissement).", "O", "O"),
        new("NIVEAUX_MAX", "Gerbages maximum", FieldRequirement.Conditionnelle,
            "Si gerbable : nombre d'unités pouvant être posées au-dessus de l'unité au sol (0 = rien dessus, 1 = une dessus…).", "1", "4"),
        new("CHARGE_MAX_DESSUS", "Charge supportable", FieldRequirement.Recommandee,
            "kg supportables au-dessus de l'unité. Vide = limitée au poids de ses propres gerbages. 0 = ne reçoit rien.", "400", "80"),
        new("QTE_MAX_PILE", "Quantité maximale par pile", FieldRequirement.Recommandee,
            "Plaques uniquement : nombre maximal de plaques par pile (sinon limité par la hauteur).", "", ""),
        new("AU_SOL", "Obligatoirement au sol", FieldRequirement.Facultative, "O = l'unité reste au plancher.", "N", "N"),
        new("AU_SOMMET", "Obligatoirement au sommet", FieldRequirement.Facultative, "O = rien ne peut être posé dessus.", "N", "N"),
        new("MAGASIN", "Magasin de départ", FieldRequirement.Facultative,
            "Gestion des arrêts : magasin où l'ordre est chargé (rangement « par magasin »).", "CPF", "OPF"),
        new("DEPART", "Étape de chargement", FieldRequirement.Facultative,
            "Gestion des arrêts : numéro d'étape de l'itinéraire où l'ordre est chargé. Chargé plus tôt = plus au fond.", "1", "2"),
        new("ARRIVEE", "Étape de livraison", FieldRequirement.Facultative,
            "Gestion des arrêts : numéro d'étape où l'ordre est livré. Livré plus tôt = plus près de la porte.", "4", "3")
    ];

    /// <summary>Modèle complet : toutes les colonnes, 1 groupage et 2 ordres d'exemple, séparateur « ; ».</summary>
    public static string TemplateCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# OptiTunes - modele d'import complet - dimensions en mm - poids en kg - lignes # = en-tetes ou commentaires");
        sb.AppendLine("#GRP;" + string.Join(";", GroupageFields.Select(f => f.Column)));
        sb.AppendLine("GRP;" + string.Join(";", GroupageFields.Select(f => f.Example1)));
        sb.AppendLine("#OT;" + string.Join(";", OrderFields.Select(f => f.Column)));
        sb.AppendLine("OT;" + string.Join(";", OrderFields.Select(f => f.Example1)));
        sb.AppendLine("OT;" + string.Join(";", OrderFields.Select(f => f.Example2)));
        return sb.ToString();
    }
}
