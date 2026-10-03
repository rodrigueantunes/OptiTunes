namespace OptiTunes.App.Services;

public sealed record ReleaseNote(string Version, string Title, IReadOnlyList<string> Items);

/// <summary>Notes de version affichées une fois à chaque nouvelle version (et sur clic de la pastille de version).</summary>
public static class ReleaseNotes
{
    public static IReadOnlyList<ReleaseNote> All { get; } =
    [
        new("0.0.5", "Gestion des arrêts et du rangement",
        [
            "Colonnes facultatives MAGASIN, DEPART et ARRIVEE sur les ordres : magasin et étapes de chargement / livraison de l'itinéraire.",
            "Rangement « Par arrêts » : chargé tôt au fond, livré tôt côté porte ; aucune palette ne bloque une autre au chargement comme à la livraison (contrainte stricte : sinon reliquat ou camion supplémentaire).",
            "Sélecteur « Rangement » à gauche de la solution : par ordre (calcul actuel), par arrêts, par client, par magasin, par commande, compact. Chaque rangement a ses propres solutions.",
            "Rangement par défaut : par arrêts si le fichier porte des étapes, sinon par ordre ; sans étapes, le calcul est strictement identique à la version précédente.",
            "Résumé de l'itinéraire dans l'en-tête, colonnes Magasin / Départ / Arrivée, couleurs par magasin ou par étape de livraison.",
            "Bouton « Exporter le modèle CSV » (toutes les colonnes, 1 groupage, 2 ordres) et bouton « i » (F1) : explication de chaque colonne, obligatoire ou facultative, avec un onglet Groupage et un onglet Ordre."
        ]),
        new("0.0.4", "Débords de chargement",
        [
            "Débord entre unités (mm) : jeu laissé entre deux palettes voisines, bout à bout comme côte à côte. Les palettes gerbées restent posées l'une sur l'autre.",
            "Débord parois (mm) : une seule valeur pour les parois gauche et droite.",
            "Débord plafond (mm) : espace libre minimal entre le haut des dernières palettes et le plafond.",
            "Valeurs à 0 par défaut, réglables dans la section « DÉBORDS » de la barre latérale et mémorisées.",
            "Zones de débord tracées en 2D (bandes orange) et en 3D, rappelées dans l'en-tête, l'impression et l'export ; métrage linéaire et contrôles en tiennent compte."
        ]),
        new("0.0.3", "Simulation et comparaison",
        [
            "Simulation « et si ? » : décochez « Charger » sur un ordre pour le retirer du calcul, un bandeau rappelle la simulation.",
            "Onglet « Solutions » : toutes les solutions côte à côte (camions, métrage, reliquat, ordres scindés, conformité).",
            "Raccourcis clavier : Ctrl+O importer, F5 recalculer, Ctrl+R recharger, Ctrl+P imprimer, Ctrl+E exporter, Ctrl+F rechercher, Échap fermer.",
            "Vue 3D : nuances alternées pour distinguer deux unités voisines du même ordre.",
            "Cette fenêtre de nouveautés, rouvrable en cliquant sur la pastille de version."
        ]),
        new("0.0.2", "Paramètres et nouvelle présentation",
        [
            "Paramètres : liste des véhicules disponibles modifiable, véhicule par défaut si le fichier n'en définit pas.",
            "Réglages de calcul mémorisés d'une session à l'autre.",
            "Nouveaux boutons et nouvelles grilles (survol, tri, sélection marquée)."
        ]),
        new("0.0.1", "Première version",
        [
            "Import CSV / TXT (lignes GRP / OT ou tableau), rapport d'import et rechargement automatique.",
            "Moteur de placement 3D, multi-camions, solutions alternatives, quinconce, contrôle indépendant du plan.",
            "Vues 3D / 2D, métrage linéaire, impression du plan de chargement."
        ])
    ];
}
