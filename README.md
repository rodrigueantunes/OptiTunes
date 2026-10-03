# OptiTunes

```text
┌─────────────────────────────────────────────────────────────┐
│  OPTITUNES                                                  │
│  LOAD PLANNING / TRUCK OPTIMIZATION                         │
│                                                             │
│  données transport  →  contraintes  →  placement  →  plan  │
└─────────────────────────────────────────────────────────────┘
```

OptiTunes est un outil Windows de préparation et d’optimisation du chargement de véhicules.

Le principe est simple : partir d’une liste de marchandises à transporter, reconstruire leur réalité physique, appliquer les contraintes du camion et du transport, puis rechercher une organisation réellement chargeable.

Le résultat n’est donc pas seulement un taux de remplissage.

OptiTunes produit un plan de chargement spatial, contrôlé, visualisable en 2D et en 3D, avec métrage linéaire, gestion du gerbage, des arrêts, des contraintes physiques et, si nécessaire, de plusieurs véhicules.

> Version actuelle : `0.0.5`
> Plateforme : `Windows`
> Runtime : `.NET 10`
> Interface : `WPF`

---

## Ce que cherche à résoudre OptiTunes

Un chargement n’est pas simplement :

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
                │ solutions de chargement │
                └─────────────┬────────────┘
                              ▼
                  contrôle indépendant
                              │
                              ▼
                     plan 2D / plan 3D
```

---

# Le moteur

Le repère utilisé dans le camion est tridimensionnel.

```text
X  → longueur du véhicule
Y  → largeur du véhicule
Z  → hauteur
```

La cabine correspond au début de l’axe X.

La porte arrière se trouve à l’extrémité opposée.

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

Plusieurs stratégies de placement peuvent être exécutées sur un même groupage. Les résultats sont ensuite comparés avant présentation.

---

# Marchandises prises en charge

OptiTunes ne considère pas toutes les marchandises comme de simples rectangles interchangeables.

Le comportement dépend de leur nature physique.

### Cartons et caisses

Placement à plat avec possibilité de rotation de 90° au sol.

Le calcul tient compte du gerbage, de la hauteur disponible, du poids, des charges supportables et des surfaces d’appui.

### Palettes

Traitement proche des caisses avec prise en compte de leur emprise au sol, de leur hauteur, des niveaux autorisés et de la charge portée.

### Plaques

Les plaques peuvent être regroupées en piles.

La constitution d’une pile dépend notamment de :

```text
quantité
épaisseur
hauteur disponible
hauteur de porte
QTE_MAX_PILE
```

Le besoin de support est plus strict que pour une marchandise standard.

### Tubes

Les tubes sont chargés couchés.

OptiTunes peut comparer plusieurs organisations :

```text
classique          ○ ○ ○ ○
                   ○ ○ ○ ○

quinconce           ○ ○ ○
                   ○ ○ ○ ○
                    ○ ○ ○
```

En quinconce, le pas vertical utilisé est proche de :

```text
0,866 × diamètre
```

Les orientations possibles dans le véhicule sont également comparées.

### Bobines

Les bobines peuvent être construites à partir de leur diamètre et de leur laize.

Le gerbage n’est autorisé que lorsque les données le permettent.

### Faisceaux

Les faisceaux sont traités comme des volumes parallélépipédiques soumis aux mêmes contrôles physiques que les autres unités rectangulaires.

### Produits spécifiques

Le type `CUSTOM` permet de représenter une marchandise dont les dimensions sont directement définies par les données d’entrée.

---

# Gerbage

Le moteur ne se contente pas de savoir si un article est déclaré gerbable.

Il contrôle également ce qu’il est réellement possible de poser dessus.

Les principales données concernées sont :

```text
GERBABLE
NIVEAUX_MAX
CHARGE_MAX_DESSUS
POIDS_UNITAIRE
HAUTEUR
```

Exemple :

```text
NIVEAUX_MAX = 0   → rien au-dessus
NIVEAUX_MAX = 1   → un niveau supplémentaire
NIVEAUX_MAX = 2   → deux niveaux supplémentaires
```

Lorsqu’une unité est posée sur une autre, la charge transmise est propagée dans la pile.

Une unité située au sol doit donc être capable de supporter non seulement l’unité directement posée dessus, mais également les charges transmises par les niveaux supérieurs.

---

# Surface d’appui

Une unité en hauteur doit disposer d’un support suffisant.

Valeurs utilisées par défaut :

| Marchandise | Support minimal |
| ----------- | --------------: |
| Standard    |            80 % |
| Plaque      |            95 % |

Le contrôle ne repose pas uniquement sur le pourcentage de surface.

La position du centre de l’unité est également prise en compte afin d’éviter des placements théoriquement superposés mais physiquement incohérents.

---

# Orientation

L’orientation n’a pas besoin d’être fournie dans le fichier.

Pour une unité rectangulaire, OptiTunes peut notamment comparer :

```text
LONGUEUR × LARGEUR

        et

LARGEUR × LONGUEUR
```

La marchandise reste posée sur sa face prévue au sol.

Le moteur ne retourne pas arbitrairement un produit sur une autre face simplement pour gagner de la place.

---

# Porte du véhicule

Un produit peut entrer dans le volume intérieur d’un camion tout en étant impossible à charger réellement.

Lorsque les dimensions de porte sont connues, OptiTunes contrôle donc également le passage de chaque unité.

```text
LARGEUR_PORTE
HAUTEUR_PORTE
```

Si ces informations sont absentes, le calcul peut continuer mais le contrôle de passage ne peut pas être aussi précis.

---

# Débords et marges de sécurité

Le moteur peut réserver différents espaces non utilisables.

```text
┌──────────────────────────────────── camion ───────────────────────────────────┐
│ paroi                                                                        │
│   ↕ marge                                                                    │
│                                                                              │
│      ┌──────────────┐    espace    ┌──────────────┐                           │
│      │              │<────────────>│              │                           │
│      │    unité A   │              │    unité B   │                           │
│      │              │              │              │                           │
│      └──────────────┘              └──────────────┘                           │
│                                                                              │
└──────────────────────────────────────────────────────────────────────────────┘
```

Trois réglages sont disponibles :

```text
DEBORD_UNITES
DEBORD_PAROIS
DEBORD_PLAFOND
```

Le débord entre marchandises est appliqué horizontalement.

Il ne crée pas artificiellement un espace vertical entre deux produits correctement gerbés.

Ces marges sont utilisées par le moteur, le validateur, le métrage linéaire et les représentations du plan.

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

Les différentes orientations sont comparées lorsque cela est pertinent.

---

# Tournées et arrêts

OptiTunes peut travailler sur un simple groupage ou prendre en compte une véritable séquence logistique.

Trois données facultatives permettent de décrire cette logique :

```text
MAGASIN
DEPART
ARRIVEE
```

Le principe recherché est celui d’un chargement exploitable sur le terrain.

```text
CABINE                                          PORTE
  │                                               │
  ▼                                               ▼

┌─────────────────────────────────────────────────┐
│ arrivée tardive │ intermédiaire │ arrivée tôt  │
└─────────────────────────────────────────────────┘
```

Un produit destiné à être livré rapidement doit rester accessible.

Une unité ne doit pas bloquer physiquement une marchandise dont le déchargement est prévu avant elle.

Si aucune position compatible n’existe, le moteur peut :

```text
essayer un autre camion
        ou
laisser l’unité en reliquat
```

---

# Modes de rangement

La même liste de marchandises peut être interprétée différemment selon l’objectif opérationnel.

OptiTunes propose plusieurs logiques.

| Mode     | Principe                                                        |
| -------- | --------------------------------------------------------------- |
| Ordre    | Respect de l’ordre logistique fourni                            |
| Arrêts   | Utilisation des étapes de départ et d’arrivée                   |
| Client   | Regroupement des marchandises d’un même client                  |
| Magasin  | Regroupement par point de départ                                |
| Commande | Conservation des lignes d’une même commande ensemble            |
| Compact  | Recherche de densité sans contrainte logistique d’accessibilité |

Le mode compact sert notamment de référence pour mesurer le coût spatial imposé par les contraintes logistiques.

---

# Plusieurs camions

Lorsque le chargement ne tient pas dans un seul véhicule, OptiTunes peut continuer la recherche dans plusieurs camions identiques.

Le nombre maximal est configurable.

La valeur initiale est :

```text
5 véhicules
```

Un camion supplémentaire est utilisé lorsque la marchandise est physiquement compatible avec le véhicule mais que la capacité restante n’est plus suffisante.

À l’inverse, un produit impossible à charger à cause de ses dimensions ou du passage de porte n’est pas simplement déplacé dans un camion supplémentaire.

Le problème physique resterait identique.

---

# Plusieurs solutions, pas un seul résultat

Un calcul peut produire plusieurs plans valides.

OptiTunes conserve différentes solutions afin de permettre leur comparaison.

Quelques critères observés :

```text
quantité chargée
reliquat
nombre de véhicules
volume utilisé
métrage linéaire
ordres incomplets
validité physique
```

L’objectif est de conserver plusieurs possibilités réellement exploitables plutôt que de masquer toutes les alternatives derrière un unique résultat.

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
nombre d’unités
quantité chargée
quantité totale
reliquat
facteur limitant
nombre de véhicules
```

---

# Contrôle du plan

Le résultat produit par le moteur est contrôlé par un composant distinct : `PlanValidator`.

Il vérifie à nouveau le plan obtenu sans simplement considérer que le résultat du moteur est correct.

Les contrôles portent notamment sur :

```text
dimensions
collisions
charge utile
passage de porte
gerbage
nombre de niveaux
surface d’appui
charge supportée
débords
accessibilité
rangement
positions
```

L’application expose le résultat de ces vérifications dans la zone `Contrôles`.

---

# Reliquats

Une marchandise non chargée n’est pas simplement marquée comme « impossible ».

OptiTunes conserve la raison ayant empêché son placement.

Exemples :

```text
données indispensables absentes
dimensions incompatibles avec le véhicule
aucune orientation compatible
passage de porte impossible
charge utile dépassée
plancher saturé
aucune position valide
accessibilité incompatible avec la tournée
```

Cela permet de distinguer un problème de capacité d’un problème de données ou d’une impossibilité physique.

---

# Lecture du résultat

OptiTunes dispose de plusieurs représentations du même chargement.

### Vue 3D

Basée sur `HelixToolkit.Wpf`.

Elle permet notamment de :

```text
faire tourner la scène
changer de point de vue
survoler les unités
sélectionner une marchandise
identifier un ordre
visualiser les niveaux
observer les débords
examiner plusieurs camions
```

Les informations d’une unité peuvent comprendre :

```text
ordre
commande
article
camion
séquence
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

Elles sont particulièrement utiles pour contrôler rapidement l’encombrement réel.

---

# Identification visuelle

Les couleurs peuvent être utilisées pour distinguer les unités selon différents axes :

```text
ordre
client
arrêt
magasin
étape logistique
```

La légende permet ensuite d’isoler visuellement une partie du chargement.

---

# Simulation

Chaque ordre peut être temporairement inclus ou exclu du calcul.

Cela permet de tester rapidement :

```text
un chargement sans une commande
un chargement sans un client
l’impact d’un produit volumineux
le passage de 2 camions à 1 camion
la variation du métrage linéaire
```

La donnée source n’est pas modifiée.

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

Le catalogue est modifiable depuis l’application.

Un véhicule peut être :

```text
créé
modifié
dupliqué
supprimé
réordonné
défini par défaut
```

Le catalogue initial peut également être restauré.

---

# Données d’entrée

OptiTunes lit actuellement :

```text
.csv
.txt
```

Un classeur Excel doit être exporté dans l’un de ces formats avant import.

Les séparateurs détectés sont notamment :

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

---

# Structure d’un fichier

Deux présentations sont possibles.

### GRP / OT

```text
GRP → informations du groupage et du véhicule
OT  → marchandises / ordres de transport
```

### Tableau classique

Une ligne représente un ordre.

Les informations de groupage peuvent alors être répétées et sont regroupées lors de l’import.

---

# Exemple anonymisé

Toutes les valeurs ci-dessous sont fictives.

Elles ne correspondent à aucun client, article, magasin, commande ou donnée d’exploitation réelle.

```csv
#GRP;ID_GROUPAGE;DATE;TRANSPORTEUR;VEHICULE;LONGUEUR_UTILE;LARGEUR_UTILE;HAUTEUR_UTILE;CHARGE_UTILE;LARGEUR_PORTE;HAUTEUR_PORTE
GRP;GRP-0001;01/01/2026;TRANSPORTEUR-A;SEMI-01;13600;2450;2700;24000;2450;2650

#OT;ID_ORDRE;TYPE_ORDRE;NUM_CDE;LIGNE_CDE;NUM_CADENCE;CLIENT;ARRET;ARTICLE;DESIGNATION;TYPE_PHYSIQUE;QUANTITE;LONGUEUR;LARGEUR;HAUTEUR;DIAMETRE;POIDS_UNITAIRE;GERBABLE;NIVEAUX_MAX;CHARGE_MAX_DESSUS;QTE_MAX_PILE;AU_SOL;AU_SOMMET;MAGASIN;DEPART;ARRIVEE
OT;ORD-001;CDE;CMD-0001;10;;CLI-001;;ART-PAL-01;Palette exemple;PALETTE;4;1200;800;1300;;117;O;1;400;;N;N;SITE-A;1;4
OT;ORD-002;CDE;CMD-0002;20;;CLI-002;;ART-BOX-01;Carton exemple;BOX;20;600;400;400;;6,5;O;4;80;;N;N;SITE-B;2;3
OT;ORD-003;CDE;CMD-0003;30;;CLI-003;;ART-TUBE-01;Tube exemple;TUBE;12;2500;;;220;18;O;3;120;;N;N;SITE-A;1;2
```

Aucune donnée utilisateur ou donnée métier réelle n’est nécessaire pour comprendre ou tester le format présenté dans cette documentation.

---

# Colonnes principales

## Véhicule / groupage

| Donnée           | Utilité                    |
| ---------------- | -------------------------- |
| `ID_GROUPAGE`    | Identification du groupage |
| `DATE`           | Date du transport          |
| `TRANSPORTEUR`   | Information descriptive    |
| `VEHICULE`       | Identification du véhicule |
| `LONGUEUR_UTILE` | Volume intérieur           |
| `LARGEUR_UTILE`  | Volume intérieur           |
| `HAUTEUR_UTILE`  | Volume intérieur           |
| `CHARGE_UTILE`   | Limite de poids            |
| `LARGEUR_PORTE`  | Contrôle d’entrée          |
| `HAUTEUR_PORTE`  | Contrôle d’entrée          |

## Marchandise

| Donnée              | Utilité                     |
| ------------------- | --------------------------- |
| `ID_ORDRE`          | Identification de l’ordre   |
| `NUM_CDE`           | Regroupement commande       |
| `CLIENT`            | Regroupement client         |
| `ARTICLE`           | Identification de l’article |
| `TYPE_PHYSIQUE`     | Comportement physique       |
| `QUANTITE`          | Quantité à charger          |
| `LONGUEUR`          | Dimension                   |
| `LARGEUR`           | Dimension                   |
| `HAUTEUR`           | Dimension                   |
| `DIAMETRE`          | Tube / bobine               |
| `POIDS_UNITAIRE`    | Calcul de charge            |
| `GERBABLE`          | Autorisation de gerbage     |
| `NIVEAUX_MAX`       | Limite verticale            |
| `CHARGE_MAX_DESSUS` | Résistance                  |
| `QTE_MAX_PILE`      | Constitution des piles      |
| `MAGASIN`           | Regroupement logistique     |
| `DEPART`            | Étape de chargement         |
| `ARRIVEE`           | Étape de livraison          |

La documentation détaillée du format est disponible dans :

```text
docs/FORMAT_IMPORT.md
```

---

# Export

Un plan peut être exporté dans un fichier structuré en plusieurs blocs.

```text
PLAN
CAMION
ORDRE
UNITE
RELIQUAT
```

`PLAN` contient les indicateurs globaux.

`CAMION` détaille le résultat véhicule par véhicule.

`ORDRE` restitue la synthèse par ordre.

`UNITE` contient les positions physiques réelles.

`RELIQUAT` explique ce qui n’a pas pu être chargé.

Exemple de données présentes au niveau d’une unité :

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

OptiTunes permet de générer une feuille de chargement par véhicule avec notamment :

```text
vue de dessus
vue latérale
séquence de chargement
contenu du camion
reliquats
informations véhicule
```

L’impression Windows permet ensuite, si nécessaire, de produire un PDF.

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
│   │   ├── Import
│   │   ├── Models
│   │   └── Validation
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
│   WPF / MVVM / 2D / 3D / UX       │
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
│ export                             │
└────────────────────────────────────┘
```

`OptiTunes.Core` contient la logique métier et ne dépend pas de l’interface graphique.

`OptiTunes.App` fournit l’application Windows.

---

# Principaux composants

### `FlatFileImporter`

Lecture, interprétation et validation des fichiers d’entrée.

### `UnitBuilder`

Transformation d’une quantité métier en unités physiques utilisables par le moteur.

### `LoadOptimizer`

Recherche des positions et construction des différentes solutions.

### `StackingRules`

Règles de gerbage et de transmission des charges.

### `Stowage`

Contraintes d’ordre, d’accessibilité et de rangement.

### `LinearMeterCalculator`

Calcul des différents métrages linéaires.

### `PlanValidator`

Contrôle indépendant du plan final.

### `PlanExporter`

Production des données d’export.

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
cas générés
```

Un système d’auto-test existe également directement dans l’application.

---

# Télécharger / installer / utiliser

Les versions distribuées d’OptiTunes sont disponibles directement depuis la page Releases du projet.

### → [OptiTunes — Releases](https://github.com/rodrigueantunes/OptiTunes/releases)

La page Releases constitue le point d’entrée pour récupérer une version prête à l’emploi ainsi que les informations associées à chaque publication.

Le dépôt source n’a donc pas vocation à servir de procédure d’installation utilisateur.

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
└── mode compact
```

---

# 0.0.5

La version actuelle introduit principalement la notion d’itinéraire dans le calcul.

Avant cette évolution, l’optimisation déterminait essentiellement comment faire entrer les marchandises dans le véhicule.

Le moteur peut désormais également tenir compte de la manière dont elles devront en sortir.

```text
faire entrer les produits
          +
respecter leur réalité physique
          +
préparer leur déchargement
```

C’est cette combinaison qui constitue le cœur d’OptiTunes.

---

```text
                 OPTITUNES

            volume     ≠     chargement

        un bon résultat doit également être
        physiquement possible,
        logistiquement exploitable
        et vérifiable.
```
