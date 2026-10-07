# OptiTunes

```text
┌─────────────────────────────────────────────────────────────┐
│  OPTITUNES                                                  │
│  LOAD PLANNING / TRUCK OPTIMIZATION                         │
│                                                             │
│  données transport  →  contraintes  →  placement  →  plan   │
└─────────────────────────────────────────────────────────────┘
```

OptiTunes est un outil Windows de préparation et d'optimisation du chargement de véhicules.

Le principe est simple : partir d'une liste de marchandises à transporter, reconstruire leur réalité physique, appliquer les contraintes du camion et du transport, puis rechercher une organisation réellement chargeable.

Le résultat n'est donc pas seulement un taux de remplissage.

OptiTunes produit un plan de chargement spatial, contrôlé, visualisable en 2D et en 3D, avec métrage linéaire, gestion du gerbage, des arrêts, des contraintes physiques et, si nécessaire, de plusieurs véhicules. Chaque résultat est expliqué pas à pas, chiffres à l'appui.

> Version actuelle : `0.0.6`
> Plateforme : `Windows`
> Runtime : `.NET 10`
> Interface : `WPF`

---

## Ce que cherche à résoudre OptiTunes

Un chargement n'est pas simplement :

```text
volume marchandises < volume camion
```

Deux chargements présentant le même volume peuvent être totalement différents.

Il faut tenir compte de la forme des produits, de leur poids, de leur orientation, de leur capacité à supporter une charge, de leur ordre de livraison ou encore de leur passage réel par la porte du véhicule.

OptiTunes travaille donc sur plusieurs niveaux de contraintes en même temps.

```text
                         ┌──────────────┐
                         │   GROUPAGE   │
                         └──────┬───────┘
                                │
                                ▼
                  ┌─────────────────────────┐
                  │ Construction des unités │
                  │ physiques à transporter │
                  └────────────┬────────────┘
                               │
             ┌─────────────────┼─────────────────┐
             ▼                 ▼                 ▼
       dimensions          poids / charge     logistique
       orientations        gerbage            arrêts
       porte               supports           regroupements
             └─────────────────┼─────────────────┘
                               ▼
                     ┌──────────────────┐
                     │ moteur OptiTunes │
                     └────────┬─────────┘
                              ▼
                ┌──────────────────────────┐
                │ solutions de chargement  │
                └─────────────┬────────────┘
                              ▼
                  contrôle indépendant
                              │
                              ▼
          plan 2D / plan 3D / détail du calcul
```

---

# Le moteur

Le repère utilisé dans le camion est tridimensionnel.

```text
X  → longueur du véhicule
Y  → largeur du véhicule
Z  → hauteur
```

La cabine correspond au début de l'axe X.

La porte arrière se trouve à l'extrémité opposée.

Chaque unité placée possède notamment :

```text
position       X / Y / Z
dimensions     DX / DY / DZ
poids
niveau
charge portée
supports
camion
séquence de chargement
```

### Placement par points extrêmes

Les unités sont posées une à une, de la cabine vers la porte. Les positions candidates sont les « points extrêmes » : les coins libres laissés par les unités déjà posées (devant, à côté, au-dessus).

Parmi les positions valides, le moteur retient celle qui garde le chargement le plus court, puis la plus basse, puis la plus à gauche.

Une position n'est acceptée que si toutes les règles sont respectées : espace chargeable, absence de collision, débords, surface d'appui, gerbabilité des supports, niveaux, charge supportable, passage de porte, charge utile, accessibilité et zones de rangement.

Une unité sans position valide part en reliquat, avec son motif.

### 18 façons de charger

Pour chaque camion, le moteur essaie en parallèle 6 ordres de tri × 3 modes de remplissage :

| Ordres de tri | Modes de remplissage |
| --- | --- |
| emprise au sol décroissante | **parois** : la largeur au sol d'abord |
| volume décroissant | **colonnes** : le gerbage d'abord |
| poids décroissant | **rangées** : sens de pose de chaque ordre imposé par le calcul du métrage |
| plus grande dimension d'abord | |
| non gerbables d'abord | |
| groupé par ordre | |

Pour un camion, la meilleure façon est celle qui charge le plus d'ordres complets, puis le plus de volume, puis sur la longueur la plus courte.

---

# Marchandises prises en charge

OptiTunes ne considère pas toutes les marchandises comme de simples rectangles interchangeables.

Le comportement dépend de leur nature physique (`TYPE_PHYSIQUE`).

### Cartons et caisses (`BOX`)

Placement à plat avec possibilité de rotation de 90° au sol.

Le calcul tient compte du gerbage, de la hauteur disponible, du poids, des charges supportables et des surfaces d'appui.

### Palettes (`PALETTE`)

Traitement proche des caisses avec prise en compte de leur emprise au sol, de leur hauteur, des niveaux autorisés et de la charge portée.

### Plaques (`PLAQUE`)

Les plaques sont regroupées en piles. `HAUTEUR` est alors l'épaisseur d'une plaque.

La constitution d'une pile dépend de :

```text
quantité
épaisseur
hauteur disponible
hauteur de porte
QTE_MAX_PILE
```

```text
plaques par pile = MIN( ENT(hauteur disponible / épaisseur) ; QTE_MAX_PILE )
piles            = ARRONDI.SUP(quantité / plaques par pile)
```

Le besoin de support est plus strict que pour une marchandise standard (95 %).

### Tubes (`TUBE`)

Les tubes sont chargés couchés. Un tube ne se pose que sur un tube de même diamètre, aligné, et aucune caisse n'est posée sur des tubes couchés.

OptiTunes compare deux organisations :

```text
classique          ○ ○ ○ ○
                   ○ ○ ○ ○

quinconce           ○ ○ ○
                   ○ ○ ○ ○
                    ○ ○ ○
```

En quinconce, le pas vertical utilisé est :

```text
0,866 × diamètre      (√3 / 2)
```

La quinconce n'est retenue que si elle loge plus de tubes que la grille simple. Le lit obtenu est alors une seule unité, sur laquelle rien n'est posé.

### Bobines (`BOBINE`)

Les bobines sont construites à partir de leur diamètre et de leur laize, posées axe vertical.

Le gerbage n'est autorisé que lorsque les données le permettent. Rien n'est posé sur une bobine couchée.

### Faisceaux (`FAISCEAU`)

Les faisceaux sont traités comme des volumes parallélépipédiques soumis aux mêmes contrôles physiques que les autres unités rectangulaires.

### Produits spécifiques (`CUSTOM`)

Le type `CUSTOM` permet de représenter une marchandise dont les dimensions sont directement définies par les données d'entrée.

---

# Gerbage

Le moteur ne se contente pas de savoir si un article est déclaré gerbable.

Il contrôle également ce qu'il est réellement possible de poser dessus.

Les principales données concernées sont :

```text
GERBABLE
NIVEAUX_MAX
CHARGE_MAX_DESSUS
POIDS_UNITAIRE
HAUTEUR
AU_SOL
AU_SOMMET
```

`NIVEAUX_MAX` est le nombre de gerbages au-dessus de l'unité posée au sol :

```text
NIVEAUX_MAX = 0   → rien au-dessus
NIVEAUX_MAX = 1   → une unité posée dessus
NIVEAUX_MAX = 2   → deux unités empilées dessus
```

Le nombre de niveaux d'une pile (sol compris) est le plus petit de trois limites :

```text
niveaux = MIN( NIVEAUX_MAX + 1 ;
               ENT(hauteur disponible / hauteur) ;
               1 + ENT(CHARGE_MAX_DESSUS / poids) )
```

Lorsqu'une unité est posée sur une autre, son poids est réparti sur ses supports au prorata des surfaces de contact, puis propagé jusqu'au sol.

Une unité située au sol doit donc être capable de supporter non seulement l'unité directement posée dessus, mais également les charges transmises par les niveaux supérieurs.

Valeurs prudentes lorsque la donnée manque :

```text
GERBABLE vide            → non gerbable
NIVEAUX_MAX vide         → 1 gerbage
CHARGE_MAX_DESSUS vide   → poids de ses propres gerbages
```

---

# Surface d'appui

Une unité en hauteur doit disposer d'un support suffisant.

Valeurs utilisées par défaut :

| Marchandise | Support minimal |
| ----------- | --------------: |
| Standard    |            80 % |
| Plaque      |            95 % |

Le contrôle ne repose pas uniquement sur le pourcentage de surface.

La position du centre de l'unité est également prise en compte afin d'éviter des placements théoriquement superposés mais physiquement incohérents.

---

# Orientation

L'orientation n'a pas besoin d'être fournie dans le fichier.

Pour une unité rectangulaire, OptiTunes peut notamment comparer :

```text
LONGUEUR × LARGEUR

        et

LARGEUR × LONGUEUR
```

La marchandise reste posée sur sa face prévue au sol.

Le moteur ne retourne pas arbitrairement un produit sur une autre face simplement pour gagner de la place.

La rotation au sol peut être désactivée : la `LONGUEUR` reste alors toujours dans le sens de la longueur du camion.

---

# Porte du véhicule

Un produit peut entrer dans le volume intérieur d'un camion tout en étant impossible à charger réellement.

Lorsque les dimensions de porte sont connues, OptiTunes contrôle donc également le passage de chaque unité.

```text
LARGEUR_PORTE
HAUTEUR_PORTE
```

Si ces informations sont absentes, le calcul continue, mais le passage par la porte n'est pas contrôlé et un avertissement le signale.

---

# Débords et marges de sécurité

Le moteur peut réserver différents espaces non utilisables.

```text
┌──────────────────────────────────── camion ──────────────────────────────────┐
│ paroi                                                                        │
│   ↕ marge                                                                    │
│                                                                              │
│      ┌──────────────┐    espace    ┌──────────────┐                          │
│      │              │<────────────>│              │                          │
│      │    unité A   │              │    unité B   │                          │
│      │              │              │              │                          │
│      └──────────────┘              └──────────────┘                          │
│                                                                              │
└──────────────────────────────────────────────────────────────────────────────┘
```

Trois réglages sont disponibles dans la section « DÉBORDS (mm) » de la barre latérale (0 par défaut, mémorisés) :

```text
Entre unités            jeu entre deux unités voisines au même niveau
Parois gauche / droite  une seule valeur, appliquée des deux côtés
Plafond (hauteur)       espace libre sous le plafond
```

Le débord entre marchandises est appliqué horizontalement.

Il ne crée pas artificiellement un espace vertical entre deux produits correctement gerbés.

```text
largeur chargeable = largeur − 2 × débord parois
hauteur chargeable = hauteur − débord plafond
```

Ces marges sont utilisées par le moteur, le validateur, le métrage linéaire, les vues 2D / 3D, la feuille de chargement et l'export (`DEBORD_UNITES_MM`, `DEBORD_PAROIS_MM`, `DEBORD_PLAFOND_MM`).

---

# Métrage linéaire

Le métrage linéaire ne correspond pas uniquement à la longueur du camion utilisée après calcul.

OptiTunes distingue plusieurs notions.

### Capacité de gerbage

```text
Niveaux possibles
    =
minimum(
    capacité de gerbage,
    hauteur disponible,
    limite de charge
)
```

### Piles nécessaires au sol

```text
Piles = ARRONDI.SUP(Quantité / Niveaux)
```

### Piles dans une rangée

```text
Piles par rangée =
ENT(Largeur utile / Largeur au sol)
```

### Rangées nécessaires

```text
Rangées =
ARRONDI.SUP(Piles / Piles par rangée)
```

### Métrage par rangées

```text
ML rangées =
Rangées × Longueur au sol
```

### Métrage équivalent

```text
                 Piles × Longueur × Largeur
ML équivalent = ─────────────────────────────
                        Largeur utile
```

### Métrage du plan obtenu

Après optimisation :

```text
ML réel =
MAX(X + DX) - MIN(X)
```

pour les unités occupant le plancher.

Les deux sens de pose (longueur dans le sens du camion ou en travers) sont comparés pour chaque ordre ; le plus court est retenu. Le débord entre unités est ajouté entre les piles et entre les rangées.

### Nombre minimum de camions

```text
Camions minimum =
ARRONDI.SUP( MAX( poids / charge utile ;
                  volume / volume du camion ;
                  ML équivalent / longueur du camion ) )
```

---

# Tournées et arrêts

OptiTunes peut travailler sur un simple groupage ou prendre en compte une véritable séquence logistique.

Trois données facultatives permettent de décrire cette logique :

```text
MAGASIN   magasin de départ            (ERP : IC_CHAR3_1)
DEPART    étape de chargement          (ERP : IC_NUM1)
ARRIVEE   étape de livraison           (ERP : IC_NUM2)
```

Les étapes sont numérotées 1, 2, 3… et communes à tout le groupage. Sans ces colonnes, le calcul reste celui de la colonne `ARRET`.

Le principe recherché est celui d'un chargement exploitable sur le terrain.

```text
CABINE                                          PORTE
  │                                               │
  ▼                                               ▼

┌─────────────────────────────────────────────────┐
│ arrivée tardive │ intermédiaire │ arrivée tôt   │
└─────────────────────────────────────────────────┘
```

Un produit destiné à être livré rapidement doit rester accessible.

Une unité B, placée entre une unité A et la porte (ou posée sur A), bloque A si :

```text
au déchargement de A, B est encore à bord   (B livrée après A et chargée avant)
au chargement de A, B est déjà à bord       (B chargée avant A et pas encore livrée)
```

La contrainte est stricte. Si aucune position compatible n'existe, le moteur peut :

```text
essayer un autre camion
        ou
laisser l'unité en reliquat
```

---

# Modes de rangement

La même liste de marchandises peut être interprétée différemment selon l'objectif opérationnel.

Le sélecteur « Rangement » se trouve à gauche du choix de la solution.

| Mode     | Principe                                                         |
| -------- | ---------------------------------------------------------------- |
| Ordre    | Respect de la colonne `ARRET` (défaut sans étapes)               |
| Arrêts   | Utilisation des étapes de départ et d'arrivée (défaut avec étapes) |
| Client   | Une zone continue par client                                     |
| Magasin  | Une zone continue par point de départ                            |
| Commande | Conservation des lignes d'une même commande ensemble             |
| Compact  | Recherche de densité sans contrainte logistique d'accessibilité  |

En mode Client, Magasin ou Commande, chaque zone occupe une tranche continue du camion, de l'avant vers la porte, sans gerbage d'une zone sur une autre.

Le mode compact sert notamment de référence pour mesurer le coût spatial imposé par les contraintes logistiques.

---

# Plusieurs camions

Lorsque le chargement ne tient pas dans un seul véhicule, OptiTunes peut continuer la recherche dans plusieurs camions identiques (option « Ajouter des camions si reliquat »).

Le nombre maximal est configurable.

La valeur initiale est :

```text
5 véhicules
```

Un camion supplémentaire est utilisé lorsque la marchandise est physiquement compatible avec le véhicule mais que la capacité restante n'est plus suffisante.

À l'inverse, un produit impossible à charger à cause de ses dimensions ou du passage de porte n'est pas simplement déplacé dans un camion supplémentaire.

Le problème physique resterait identique.

---

# Plusieurs solutions, pas un seul résultat

Un calcul peut produire plusieurs plans valides.

OptiTunes conserve différentes solutions afin de permettre leur comparaison :

```text
« Meilleure par camion »   chaque camion prend la meilleure des 18 façons
une solution par façon     la même façon appliquée à tous les camions
```

Les plans identiques ne sont proposés qu'une fois. Les solutions sont classées sur cinq critères, dans cet ordre :

```text
1. le moins d'articles en reliquat
2. le moins de camions
3. le métrage réel total le plus court (arrondi à 0,1 m)
4. le moins d'ordres répartis sur plusieurs camions
5. le moins d'ordres incomplets
```

La première est recommandée. Les autres restent consultables dans le sélecteur « Solution » et comparables dans l'onglet **Solutions** (camions, articles, reliquat, ML, poids, ordres scindés ou incomplets, conformité).

L'objectif est de conserver plusieurs possibilités réellement exploitables plutôt que de masquer toutes les alternatives derrière un unique résultat.

---

# Les indicateurs

Le plan final regroupe notamment :

```text
poids chargé
charge utile utilisée
volume chargé
occupation volumique
surface au sol
occupation du plancher
métrage théorique
métrage équivalent
métrage réel
centre de gravité
nombre d'unités
quantité chargée
quantité totale
reliquat
facteur limitant
nombre de véhicules
nombre minimum théorique de véhicules
```

---

# Contrôle du plan

Le résultat produit par le moteur est contrôlé par un composant distinct : `PlanValidator`.

Il vérifie à nouveau le plan obtenu à partir des positions, sans réutiliser l'état du moteur ni considérer que son résultat est correct.

Les 16 contrôles portent sur :

```text
conservation des quantités
inclusion dans le véhicule
pose de l'unité
absence de collision
support et stabilité
gerbabilité
niveaux de gerbage
charge supportable
charge utile de chaque camion
passage par la porte
reliquat justifié
accessibilité par arrêt
rangement par zone
centre de gravité
débords
tubes en quinconce
```

L'application expose le résultat de ces vérifications dans l'onglet **Contrôles** et dans la pastille de l'en-tête (« Plan conforme » ou non).

---

# Reliquats

Une marchandise non chargée n'est pas simplement marquée comme « impossible ».

OptiTunes conserve la raison ayant empêché son placement.

```text
données indispensables absentes
dimensions incompatibles avec le véhicule
aucune orientation compatible
passage de porte impossible
charge utile dépassée
produit non gerbable et plancher saturé
aucune position valide
accessibilité incompatible avec la tournée
```

Cela permet de distinguer un problème de capacité d'un problème de données ou d'une impossibilité physique.

---

# Détail du calcul

L'onglet **Détail du calcul** explique la solution affichée, étape par étape, en français et avec les chiffres du groupage.

```text
★  En résumé
1. Le véhicule et l'espace chargeable
2. Les ordres à charger
3. Des articles aux unités de chargement
4. Niveaux de gerbage par ordre
5. Besoin en métrage linéaire (avant placement)
6. Ordre de chargement et rangement
7. Placement des unités (moteur 3D)
8. Choix de la solution
9. Camion N : résultat chiffré          (un par camion)
…  Reliquat
…  Contrôles du plan
…  Lexique
```

Chaque calcul est posé avec ses valeurs, par exemple :

```text
Niveaux = MIN(NIVEAUX_MAX + 1 = 2 ; ⌊2 700 / 1 300⌋ = 2 ; 1 + ⌊400 / 117⌋ = 4) = 2
Piles au sol = ⌈8 unité(s) / 2 niveau(x)⌉ = 4
ML réel = (10 000 − 0) / 1 000 = 10,00 m
```

- sommaire cliquable ;
- calculs surlignés, résultats, précisions, points d'attention et anomalies distingués ;
- tableaux de chiffres (ordres, niveaux, métrage, camions, solutions, reliquat) ;
- le calcul de chaque ordre est un bloc ouvert par défaut, repliable d'un clic sur son en-tête ;
- bouton « Copier le détail » pour le coller dans un courriel ou un document.

Le détail suit la solution et le rangement affichés.

---

# Lecture du résultat

OptiTunes dispose de plusieurs représentations du même chargement.

### Vue 3D

Basée sur `HelixToolkit.Wpf`.

Elle permet notamment de :

```text
faire tourner la scène
changer de point de vue (3/4, dessus, côté, arrière)
survoler les unités
sélectionner une marchandise
identifier un ordre
visualiser les niveaux
observer les débords
examiner plusieurs camions
rejouer le chargement pas à pas
```

Les informations d'une unité, affichées au survol, peuvent comprendre :

```text
ordre
commande
client
article
camion
séquence
étapes
position
dimensions
poids
niveau
charge portée
supports
```

### Vues 2D

Trois projections complètent la vue 3D :

```text
dessus
côté
arrière
```

Elles sont particulièrement utiles pour contrôler rapidement l'encombrement réel.

### Grilles

```text
Ordres de transport   besoin en métrage, chargé, reliquat, statut, simulation
Plan de chargement    unité par unité : séquence, position, niveau, charge, supports, étapes, zone
Import                format détecté, colonnes reconnues, alertes avec la ligne brute
```

---

# Identification visuelle

Les couleurs peuvent être utilisées pour distinguer les unités selon quatre axes :

```text
ordre
client
étape de livraison
magasin de départ
```

La légende cliquable et la recherche permettent ensuite d'isoler visuellement une partie du chargement : le reste est estompé.

---

# Simulation

Chaque ordre peut être temporairement inclus ou exclu du calcul (case « Charger » de l'onglet Ordres de transport).

Cela permet de tester rapidement :

```text
un chargement sans une commande
un chargement sans un client
l'impact d'un produit volumineux
le passage de 2 camions à 1 camion
la variation du métrage linéaire
```

Le même groupage peut aussi être recalculé sur un autre véhicule du catalogue.

La donnée source n'est pas modifiée.

---

# Véhicules

OptiTunes embarque un catalogue initial servant de base.

| Véhicule      |   L utile |  l utile |  H utile | Charge utile |
| ------------- | --------: | -------: | -------: | -----------: |
| Semi 13,60 m  | 13 600 mm | 2 450 mm | 2 700 mm |    24 000 kg |
| Semi méga     | 13 600 mm | 2 450 mm | 3 000 mm |    24 000 kg |
| Porteur 19 t  |  9 600 mm | 2 450 mm | 2 600 mm |     9 500 kg |
| Porteur 12 t  |  7 200 mm | 2 450 mm | 2 400 mm |     5 500 kg |
| Fourgon 20 m³ |  4 300 mm | 2 100 mm | 2 200 mm |     1 100 kg |

Le catalogue est modifiable depuis l'application (Paramètres).

Un véhicule peut être :

```text
créé
modifié
dupliqué
supprimé
réordonné
défini par défaut
```

Le véhicule par défaut s'applique quand le fichier ne définit pas de véhicule. Le catalogue initial peut également être restauré.

---

# Données d'entrée

OptiTunes lit :

```text
.csv
.txt
```

Un classeur Excel doit être enregistré dans l'un de ces formats avant import (un classeur est refusé avec un message explicite).

Les séparateurs détectés sont :

```text
;
TAB
|
,
```

Le point-virgule reste le format recommandé.

Encodages pris en charge :

```text
UTF-8
Windows-1252
```

Les dimensions sont converties en millimètres et les poids en kilogrammes.

Les valeurs suivantes sont par exemple interprétables :

```text
1200mm
120 cm
1,20 m
25 kg
1,5 t
```

Les deux notations décimales sont acceptées :

```text
1,50
1.50
```

L'import signale et corrige ce qui peut l'être :

```text
colonnes décalées
référence article saisie dans la colonne ARRET
valeurs invraisemblables (cm au lieu de mm, charge utile en tonnes…)
étapes incohérentes (DEPART ≥ ARRIVEE)
fichier ouvert dans Excel (lecture partagée)
```

Le fichier peut être importé par le bouton, par glisser-déposer ou par « Ouvrir avec ». Il est relu automatiquement à chaque enregistrement (option) ; les fichiers récents restent accessibles.

---

# Structure d'un fichier

Deux présentations sont possibles.

### GRP / OT

```text
GRP → informations du groupage et du véhicule
OT  → marchandises / ordres de transport
```

Les en-têtes `#GRP;` et `#OT;` sont facultatifs et permettent un ordre de colonnes libre. Les noms courants (anglais, ERP) sont reconnus comme alias.

### Tableau classique

Une ligne représente un ordre.

Les informations de groupage peuvent alors être répétées et sont regroupées lors de l'import.

### Modèle et aide intégrés

- **Exporter le modèle CSV** : fichier complet avec toutes les colonnes, un groupage et deux ordres d'exemple.
- **Bouton « i »** (ou F1) : explication de chaque colonne, son statut (obligatoire, selon le cas, recommandée, facultative) et un exemple, avec un onglet *Groupage* et un onglet *Ordre de transport*.

---

# Exemple anonymisé

Toutes les valeurs ci-dessous sont fictives.

Elles ne correspondent à aucun client, article, magasin, commande ou donnée d'exploitation réelle.

```csv
#GRP;ID_GROUPAGE;DATE;TRANSPORTEUR;VEHICULE;LONGUEUR_UTILE;LARGEUR_UTILE;HAUTEUR_UTILE;CHARGE_UTILE;LARGEUR_PORTE;HAUTEUR_PORTE
GRP;GRP-0001;01/01/2026;TRANSPORTEUR-A;SEMI-01;13600;2450;2700;24000;2450;2650

#OT;ID_ORDRE;TYPE_ORDRE;NUM_CDE;LIGNE_CDE;NUM_CADENCE;CLIENT;ARRET;ARTICLE;DESIGNATION;TYPE_PHYSIQUE;QUANTITE;LONGUEUR;LARGEUR;HAUTEUR;DIAMETRE;POIDS_UNITAIRE;GERBABLE;NIVEAUX_MAX;CHARGE_MAX_DESSUS;QTE_MAX_PILE;AU_SOL;AU_SOMMET;MAGASIN;DEPART;ARRIVEE
OT;ORD-001;CDE;CMD-0001;10;;CLI-001;;ART-PAL-01;Palette exemple;PALETTE;4;1200;800;1300;;117;O;1;400;;N;N;SITE-A;1;4
OT;ORD-002;CDE;CMD-0002;20;;CLI-002;;ART-BOX-01;Carton exemple;BOX;20;600;400;400;;6,5;O;4;80;;N;N;SITE-B;2;3
OT;ORD-003;CDE;CMD-0003;30;;CLI-003;;ART-TUBE-01;Tube exemple;TUBE;12;2500;;;220;18;O;3;120;;N;N;SITE-A;1;2
```

Ce fichier s'importe sans alerte : 36 articles chargés sur un camion, rangement « Par arrêts », plan conforme.

Aucune donnée utilisateur ou donnée métier réelle n'est nécessaire pour comprendre ou tester le format présenté dans cette documentation. D'autres exemples sont fournis dans `samples/`.

---

# Colonnes principales

## Véhicule / groupage

| Donnée           | Utilité                    | Statut       |
| ---------------- | -------------------------- | ------------ |
| `ID_GROUPAGE`    | Identification du groupage | Obligatoire  |
| `DATE`           | Date du transport          | Facultative  |
| `TRANSPORTEUR`   | Information descriptive    | Facultative  |
| `VEHICULE`       | Identification du véhicule | Recommandée  |
| `LONGUEUR_UTILE` | Volume intérieur (mm)      | Obligatoire* |
| `LARGEUR_UTILE`  | Volume intérieur (mm)      | Obligatoire* |
| `HAUTEUR_UTILE`  | Volume intérieur (mm)      | Obligatoire* |
| `CHARGE_UTILE`   | Limite de poids (kg)       | Obligatoire* |
| `LARGEUR_PORTE`  | Contrôle d'entrée          | Recommandée  |
| `HAUTEUR_PORTE`  | Contrôle d'entrée          | Recommandée  |

\* sauf si un véhicule est choisi dans l'application ou défini par défaut dans les Paramètres.

## Marchandise

| Donnée              | Utilité                                   | Statut       |
| ------------------- | ----------------------------------------- | ------------ |
| `ID_ORDRE`          | Identification de l'ordre                 | Obligatoire  |
| `TYPE_ORDRE`        | `CDE` (commande) ou `CAD` (cadencement)   | Facultative  |
| `NUM_CDE`           | Regroupement commande                     | Facultative  |
| `CLIENT`            | Regroupement client                       | Facultative  |
| `ARRET`             | Ordre de livraison simple                 | Facultative  |
| `ARTICLE`           | Identification de l'article               | Obligatoire  |
| `TYPE_PHYSIQUE`     | Comportement physique                     | Obligatoire  |
| `QUANTITE`          | Quantité à charger                        | Obligatoire  |
| `LONGUEUR`          | Dimension                                 | Selon le cas |
| `LARGEUR`           | Dimension / laize                         | Selon le cas |
| `HAUTEUR`           | Dimension / épaisseur d'une plaque        | Selon le cas |
| `DIAMETRE`          | Tube / bobine                             | Selon le cas |
| `POIDS_UNITAIRE`    | Calcul de charge (kg)                     | Obligatoire  |
| `GERBABLE`          | Autorisation de gerbage (O / N)           | Obligatoire  |
| `NIVEAUX_MAX`       | Limite verticale                          | Selon le cas |
| `CHARGE_MAX_DESSUS` | Résistance (kg)                           | Recommandée  |
| `QTE_MAX_PILE`      | Constitution des piles de plaques         | Recommandée  |
| `AU_SOL`            | Obligatoirement au plancher               | Facultative  |
| `AU_SOMMET`         | Rien ne peut être posé dessus             | Facultative  |
| `MAGASIN`           | Regroupement logistique                   | Facultative  |
| `DEPART`            | Étape de chargement                       | Facultative  |
| `ARRIVEE`           | Étape de livraison                        | Facultative  |

La documentation détaillée du format est disponible dans :

```text
docs/FORMAT_IMPORT.md
```

---

# Export

Un plan peut être exporté (Ctrl+E) dans un fichier CSV structuré en plusieurs blocs.

```text
PLAN
CAMION
ORDRE
UNITE
RELIQUAT
```

`PLAN` contient les indicateurs globaux, le rangement et les débords.

`CAMION` détaille le résultat véhicule par véhicule.

`ORDRE` restitue la synthèse par ordre, besoin en métrage compris.

`UNITE` contient les positions physiques réelles.

`RELIQUAT` explique ce qui n'a pas pu être chargé.

Exemple de données présentes au niveau d'une unité :

```text
camion
séquence
ordre
article
quantité
X / Y / Z
DX / DY / DZ
niveau
poids
charge
supports
étapes logistiques
```

---

# Impression

OptiTunes génère une feuille de chargement par véhicule (Ctrl+P) avec notamment :

```text
vue de dessus
vue latérale
séquence de chargement
contenu du camion
reliquats
informations véhicule
```

L'impression Windows permet ensuite, si nécessaire, de produire un PDF (« Microsoft Print to PDF »).

---

# Raccourcis

| Touche   | Action                        |
| -------- | ----------------------------- |
| Ctrl+O   | Importer un groupage          |
| Ctrl+R   | Recharger le fichier          |
| F5       | Recalculer                    |
| Ctrl+P   | Imprimer le plan              |
| Ctrl+E   | Exporter le plan (CSV)        |
| Ctrl+,   | Paramètres                    |
| F1       | Aide sur le format d'import   |
| Échap    | Fermer / effacer la sélection |

---

# Paramètres

Les préférences sont enregistrées dans :

```text
%APPDATA%\OptiTunes\settings.json
```

Elles comprennent le catalogue de véhicules, le véhicule par défaut, les options de calcul (support minimal, rotation au sol, tubes en quinconce, ordre de livraison, recherche approfondie, camions supplémentaires), les débords, le rechargement automatique et les fichiers récents.

La variable d'environnement `OPTITUNES_SETTINGS` permet d'utiliser un autre fichier (tests, poste partagé).

---

# Architecture du dépôt

```text
OptiTunes
│
├── OptiTunes.slnx
│
├── src
│   │
│   ├── OptiTunes.Core
│   │   │
│   │   ├── Engine
│   │   │   ├── Clearance.cs
│   │   │   ├── LinearMeterCalculator.cs
│   │   │   ├── LoadOptimizer.cs
│   │   │   ├── StackingRules.cs
│   │   │   ├── Stowage.cs
│   │   │   └── UnitBuilder.cs
│   │   │
│   │   ├── Export
│   │   │   ├── CalculationDetails.cs
│   │   │   └── PlanExporter.cs
│   │   │
│   │   ├── Import
│   │   │   ├── FlatFileImporter.cs
│   │   │   └── ImportFormat.cs
│   │   │
│   │   ├── Models
│   │   └── Validation
│   │       ├── PlanValidator.cs
│   │       └── SelfTestSuite.cs
│   │
│   └── OptiTunes.App
│       ├── Assets
│       ├── Converters
│       ├── Services
│       ├── Themes
│       ├── ViewModels
│       └── Views
│
├── tests
│   └── OptiTunes.Tests
│
├── samples
│
└── docs
    └── FORMAT_IMPORT.md
```

---

# Deux couches

```text
┌────────────────────────────────────┐
│          OptiTunes.App             │
│                                    │
│   WPF / MVVM / 2D / 3D / UX        │
└─────────────────┬──────────────────┘
                  │
                  ▼
┌────────────────────────────────────┐
│          OptiTunes.Core            │
│                                    │
│ import                             │
│ construction des unités            │
│ optimisation                       │
│ métrage                            │
│ validation                         │
│ export et détail du calcul         │
└────────────────────────────────────┘
```

`OptiTunes.Core` contient la logique métier et ne dépend pas de l'interface graphique.

`OptiTunes.App` fournit l'application Windows.

---

# Principaux composants

### `FlatFileImporter`

Lecture, interprétation et validation des fichiers d'entrée.

### `ImportFormat`

Référence des colonnes : alimente l'aide « i » et le modèle CSV exporté.

### `UnitBuilder`

Transformation d'une quantité métier en unités physiques utilisables par le moteur (piles de plaques, lits de tubes…).

### `LoadOptimizer`

Recherche des positions, propagation des charges dans les piles et construction des différentes solutions.

### `StackingRules`

Ce qui peut être posé sur quoi : tubes et bobines couchés, centre de l'unité au-dessus de ses appuis.

### `Stowage`

Contraintes d'ordre, d'accessibilité et de rangement.

### `Clearance`

Règle commune des débords entre unités.

### `LinearMeterCalculator`

Calcul des différents métrages linéaires.

### `PlanValidator`

Contrôle indépendant du plan final.

### `SelfTestSuite`

Banc d'essai rejoué dans l'application (onglet **Auto-test**) : cas vérifiables à la main et groupages aléatoires.

### `PlanExporter`

Production des données d'export.

### `CalculationDetails`

Explication pas à pas du calcul, avec les chiffres (onglet **Détail du calcul**).

---

# Stack

```text
C#
.NET 10
WPF
MVVM
CommunityToolkit.Mvvm
HelixToolkit.Wpf
xUnit
coverlet
```

Versions actuellement utilisées dans le projet :

| Composant             | Version |
| --------------------- | ------: |
| CommunityToolkit.Mvvm |   8.4.2 |
| HelixToolkit.Wpf      |   3.1.2 |
| xUnit                 |   2.9.3 |
| coverlet.collector    |   6.0.4 |

---

# Tests

Le dépôt contient une suite automatisée couvrant notamment :

```text
import CSV / TXT
formats numériques
unités
alias
modèle d'import
placements
gerbage
supports
métrage linéaire
débords
plaques
tubes
quinconce
multi-camions
solutions alternatives
arrêts
modes de rangement
simulations
détail du calcul
cas générés
```

```bash
dotnet test
```

Un système d'auto-test existe également directement dans l'application (onglet **Auto-test**).

---

# Compiler depuis les sources

1. Ouvrir `OptiTunes.slnx` dans Visual Studio 2026.
2. Définir **OptiTunes.App** comme projet de démarrage, puis F5.
3. Les fichiers de `samples/` sont copiés dans `Samples/` à côté de l'exécutable et proposés dans la section EXEMPLES de l'application.

---

# Télécharger / installer / utiliser

Les versions distribuées d'OptiTunes sont disponibles directement depuis la page Releases du projet.

### → [OptiTunes — Releases](https://github.com/rodrigueantunes/OptiTunes/releases)

La page Releases constitue le point d'entrée pour récupérer une version prête à l'emploi ainsi que les informations associées à chaque publication.

Le dépôt source n'a donc pas vocation à servir de procédure d'installation utilisateur.

---

# Évolution du projet

```text
0.0.1
│
├── import
├── moteur 3D
├── métrage linéaire
├── multi-camions
├── contrôle
└── vues 2D / 3D
      │
      ▼
0.0.2
│
├── paramètres
└── véhicules personnalisables
      │
      ▼
0.0.3
│
├── simulations
├── solutions
└── améliorations de lecture
      │
      ▼
0.0.4
│
├── débords entre unités
├── parois
└── plafond
      │
      ▼
0.0.5
│
├── magasin
├── départ / arrivée
├── gestion des arrêts
├── rangement client
├── rangement magasin
├── rangement commande
├── mode compact
├── modèle CSV
└── aide « i » sur les colonnes
      │
      ▼
0.0.6
│
├── détail du calcul
├── formules posées avec les chiffres
├── blocs par ordre repliables
└── copie du détail
```

---

# 0.0.6

La version actuelle rend le calcul lisible de bout en bout.

Avec la 0.0.5, l'optimisation tenait compte de la manière dont les marchandises entrent dans le véhicule, mais aussi de la manière dont elles devront en sortir.

```text
faire entrer les produits
          +
respecter leur réalité physique
          +
préparer leur déchargement
```

La 0.0.6 ajoute la dernière brique : comprendre pourquoi le plan est celui-là.

```text
chaque étape         expliquée en français
chaque calcul        posé avec ses valeurs
chaque reliquat      justifié par son motif
chaque contrôle      visible et vérifiable
```

C'est cette combinaison qui constitue le cœur d'OptiTunes.

---

```text
                 OPTITUNES

            volume     ≠     chargement

        un bon résultat doit également être
        physiquement possible,
        logistiquement exploitable,
        vérifiable
        et compréhensible.
```
