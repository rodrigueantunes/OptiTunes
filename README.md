<div align="center">

<img src="src/PalTunes.App/Assets/logo.png" width="150" alt="PalTunes">

# PalTunes

### PALLET / PACK / OPTIMIZE

Palettisation, conditionnement et optimisation de chargement.

Caisses · Cartons pliés · Bobines · Tubes · Bagues · Plaques · Sacs · Fûts · Bacs · Bidons · Seaux · Bouteilles · Cuves IBC

`v0.2.3` · Windows · .NET 10 · WPF

</div>

---

```text
Un produit.
Une contrainte.
Une palette.

PalTunes cherche le plan.
```

PalTunes est un outil de palettisation et de conditionnement conçu pour traiter aussi bien les cas simples que les chargements plus contraints : produits rectangulaires, cylindriques, creux, pliés, lourds, fragiles, gerbables ou non, palettes mono-article ou compositions multi-articles, mise en caisse avant palettisation.

L'objectif n'est pas simplement de remplir une palette.

Il faut construire une solution exploitable.

---

# 01 / LE PRINCIPE

Une palettisation correcte doit répondre simultanément à plusieurs questions :

```text
                     ┌──────────────────┐
                     │     ARTICLE      │
                     └────────┬─────────┘
                              │
              dimensions / poids / contraintes
                              │
                              ▼
┌─────────────────┐    ┌───────────────┐    ┌─────────────────┐
│     PALETTE     │───▶│    PalTunes   │◀───│   CONTRAINTES   │
└─────────────────┘    └───────┬───────┘    └─────────────────┘
                               │
                               ▼
                     ┌──────────────────┐
                     │     SOLUTIONS    │
                     ├──────────────────┤
                     │ plan de couche   │
                     │ nombre/couche    │
                     │ nombre couches   │
                     │ poids            │
                     │ dimensions       │
                     │ remplissage      │
                     │ gerbage          │
                     │ ordre de pose    │
                     └────────┬─────────┘
                              │
                  ┌───────────┴───────────┐
                  ▼                       ▼
             VUE 2D / 3D            SPÉCIFICATION
```

PalTunes calcule plusieurs configurations possibles, contrôle leur validité puis classe les solutions afin de faire ressortir la plus pertinente.

---

# 02 / CE QUE PALTUNES SAIT TRAITER

| Produit         | Géométrie prise en compte                | Exemples d'utilisation            |
| --------------- | ---------------------------------------- | --------------------------------- |
| Caisse / carton | parallélépipède, monté ou **plié**       | colis, cartons, caisses           |
| Bobine          | cylindre, plein ou creux (mandrin)       | papier, film, rouleau             |
| Tube            | cylindre long, plein ou **creux**        | tubes, profilés, bagues, mandrins |
| Plaque          | parallélépipède mince                    | panneaux, feuilles, plaques       |
| Sac             | volume rectangulaire                     | sacs industriels                  |
| Fût             | cylindre debout                          | fûts métalliques, barils          |
| Bac             | parallélépipède                          | bacs logistiques                  |
| Autre           | dimensions libres                        | cas spécifiques                   |
| Bidon           | pavé hors tout, poignée et bouchon       | jerricans plastiques 5 à 60 L     |
| Seau            | cylindre debout (diamètre du haut)       | seaux, pots à anse                |
| Bouteille       | cylindre debout, col et bouchon          | bouteilles, flacons               |
| Cuve IBC        | pavé sur palette intégrée, cage          | GRV 1000 L                        |

Bidons, seaux, bouteilles et cuves (catégorie « Autre ») se calculent comme leur forme de base — bac rigide ou fût
debout —, en homogène comme en hétérogène, et sont dessinés avec leur forme réelle. Gerbés sur plusieurs couches
sans intercalaire, bidons, seaux et bouteilles déclenchent un conseil (dessus non plat).

**Forme du dessus** (bidon, seau, bouteille, facultative) : dessus droit ou arrondi, angle, poignée encastrée,
saillante, anse rabattable ou aucune. Le calcul en déduit le gerbage :

```text
pose directe     pente ≤ 14° (glissement plastique sur plastique : tan θ ≤ 0,25), appui ≥ 40 %, poignée non saillante
intercalaire     sinon : intercalaire rigide sous chaque couche, charge admissible réduite (× 0,8 à × 0,5)
non gerbable     dessus bombé à plus de 45°
```

La fiche article affiche le verdict et son raisonnement ; l'aperçu 3D suit la forme.

**Poignée** (bidon, seau, bouteille, fût) : ajoutée ou retirée, encastrée, saillante ou anse rabattable, droite ou
arrondie (angle au bord de chaque côté, au curseur), pleine ou ouverte, et ses dimensions en mm (jamais plus grandes que
celles du produit). Encastrée : le puits est retiré de la surface
d'appui ; saillante : avec un intercalaire, l'appui se fait sur le dessus des poignées (sous 5 % de contact, charge
× 0,3). Sans dimensions : proportions usuelles.

**Sac tassable en caisse** (case décochée par défaut) : à la mise en caisse, l'épaisseur diminue du taux saisi (10 %,
25 % au plus), l'empreinte ne change pas (air chassé) ; sur palette, l'épaisseur saisie est conservée.

Chaque famille conserve ses propres règles.

Une bobine n'est pas traitée comme un carton auquel on aurait simplement ajouté un diamètre.

Pour un tube ou une bobine, le diamètre pris en compte est le diamètre extérieur, à défaut le diamètre intérieur : un article peut avoir l'un, l'autre ou les deux, sans conversion de ses données. Avec les deux, il est traité comme creux et dessiné comme tel.

Seules les données utiles au calcul sont obligatoires selon le type ; désignation, client, famille, références et notes restent facultatives.

---

# 03 / HOMOGÈNE

Mode mono-article.

Une référence est répétée sur une ou plusieurs couches d'une même palette.

PalTunes recherche notamment :

```text
surface disponible
      ↓
plans de couche possibles
      ↓
orientation des produits
      ↓
quantité par couche
      ↓
nombre de couches admissible
      ↓
hauteur / poids / contraintes
      ↓
comparaison des solutions
```

Le moteur peut travailler sur plusieurs familles de plans, notamment les organisations régulières et les dispositions combinées adaptées aux dimensions du produit et de la palette, avec une borne théorique qui permet d'indiquer quand le plan trouvé est prouvé optimal.

Pour les produits cylindriques, un moteur spécifique gère les implantations circulaires (maille carrée ou quinconce).

L'assistant « Proposer » compare toutes les palettes actives du catalogue pour un article.

---

# 04 / HÉTÉROGÈNE

Mode multi-articles.

Plusieurs références peuvent partager une même palette.

Le problème devient alors différent : il ne suffit plus de trouver le meilleur plan pour un article, il faut trouver une combinaison compatible entre plusieurs articles.

PalTunes prend notamment en compte :

* les dimensions de chaque référence ;
* les quantités demandées ;
* le poids ;
* la position disponible ;
* la charge supportable ;
* les articles fragiles ;
* les limites de gerbage ;
* l'ordre lourd / léger ;
* les surfaces encore exploitables ;
* les incompatibilités verticales ;
* les zones disponibles au fur et à mesure du chargement.

Un **profil de gerbage** est déduit de chaque article, sans donnée obligatoire supplémentaire : classe (carton, rigide, souple, roulant), zone conseillée (bas, milieu, haut) et poids qu'il peut porter.

Le moteur hétérogène utilise une recherche par points extrêmes pour construire progressivement le chargement.

### Grandes quantités

Une commande peut compter **100 000 produits et plus** (bagues, bouchons…) :

```text
article qui ne tient pas sur la palette  → exclu, signalé, solution calculée avec les autres
quantité ≥ une palette pleine            → palettes complètes mono-article
reste                                    → couches complètes par quantités, reliquat sur le dessus
```

Exemple mesuré : 100 000 bagues + 2 300 autres produits → 20 palettes en 3 secondes.

Jusqu'à 20 000 produits à mélanger, les trois stratégies sont calculées et la meilleure est retenue ; au-delà, seule « Couches homogènes » l'est (un message l'indique).

### Exemple fictif

```text
Palette P-01
1200 × 800 mm

┌──────────────────────────────────────────┐
│ REF-C03 │ REF-C03 │      REF-B11        │
│         │         │                     │
├─────────┴─────────┼─────────────────────┤
│      REF-A01      │      REF-A01        │
│                   │                     │
└──────────────────────────────────────────┘

REF-A01     400 × 300 × 220 mm     8,4 kg
REF-B11     600 × 400 × 180 mm    12,1 kg
REF-C03     300 × 200 × 150 mm     3,2 kg
```

Les références de cet exemple sont volontairement fictives.

---

# 05 / GERBAGE

Le gerbage n'est pas traité comme un simple multiplicateur.

PalTunes distingue notamment :

```text
GERBABLE
│
├─ charge maximale supportée
├─ nombre maximum de couches
├─ limites de hauteur
├─ limites de poids
└─ compatibilité avec ce qui est placé au-dessus


NON GERBABLE
│
└─ aucune charge autorisée au-dessus
```

Le nombre de gerbages suit une convention simple : `0` = non gerbable, `1` = un conditionnement gerbé sur le premier, etc.

Un article peut également être marqué fragile.

Dans ce cas, le calcul interdit les configurations incompatibles avec cette contrainte.

---

# 06 / ORIENTATION

Selon le produit, plusieurs orientations peuvent être autorisées ou imposées.

### Produits rectangulaires

```text
HAUT_IMPOSE
└─ le produit conserve son orientation verticale

LIBRE
└─ les orientations compatibles peuvent être étudiées
```

### Tubes, bagues et bobines

```text
VERTICAL
HORIZONTAL
INDIFFERENT
```

En mode `INDIFFERENT`, PalTunes compare les deux axes et retient la configuration la plus intéressante ; l'axe peut être forcé par conditionnement.

La même règle vaut en caisse : chaque caisse est proposée debout et couchée, la meilleure position est recommandée.

---

# 07 / CARTONS PLIÉS

Un carton livré à plat ne se palettise pas avec ses dimensions montées.

La fiche d'un carton accepte trois dimensions pliées facultatives :

```text
LONGUEUR PLIÉE   → remplace la longueur
LARGEUR PLIÉE    → remplace la largeur
HAUTEUR PLIÉE    → remplace la hauteur

dimension pliée vide ou 0 → dimension montée conservée
```

Chaque dimension pliée renseignée remplace uniquement la sienne, pour la palettisation (homogène, hétérogène, assistant) comme pour le colisage. La fiche conserve les dimensions montées ; la spécification indique celles qui ont été prises en compte.

---

# 08 / PALETTES

PalTunes embarque un catalogue de palettes prêt à être utilisé et entièrement modifiable.

Il comprend notamment différentes familles :

```text
EUR
ISO
CP
palettes plastiques
palettes pleines
formats industriels
```

Pour chaque palette peuvent être définis :

* code ;
* désignation ;
* longueur ;
* largeur ;
* hauteur ;
* poids à vide ;
* charge dynamique ;
* charge statique ;
* représentation ;
* disponibilité pour les calculs.

La construction visuelle des palettes peut également représenter différentes structures : blocs, semelles, patins, pieds ou plateau plein.

Plusieurs palettes physiques peuvent être associées pour construire une base de chargement plus importante.

---

# 09 / DÉBORDS ET ACCESSOIRES

Toutes les marchandises ne rentrent pas forcément exactement dans les dimensions du support.

PalTunes peut travailler avec des débords lorsqu'ils sont autorisés (longueur et largeur, 0 par défaut).

```text
            CHARGE
       ┌───────────────┐
       │               │
   ┌───┴───────────────┴───┐
   │        PALETTE        │
   └───────────────────────┘
     ← débord →   ← débord →
```

La surface réellement disponible est recalculée avant la recherche des plans.

Les accessoires sont pris en compte dans l'encombrement, le poids et les vues : intercalaires, coiffe, cornières, film étirable, cerclages.

Cas particulier des **tubes en débord** : les cornières restent au niveau de la palette, les tubes qui les gêneraient sont retirés et la perte est comptée. Une option permet au contraire de faire suivre les tubes aux cornières.

---

# 10 / HAUTEUR ET POIDS

Chaque solution est confrontée aux limites du conditionnement.

Les calculs distinguent notamment :

```text
hauteur produit
+
hauteur des couches
+
éléments éventuels
+
hauteur palette
=
hauteur totale
```

Le poids est également contrôlé par rapport aux capacités du support et aux contraintes définies.

Un poids unitaire impossible pour les dimensions de l'article (matière plus dense que 20 kg/dm³) est signalé sur la fiche, au colisage, au conditionnement et à l'import.

---

# 11 / COLISAGE

PalTunes ne s'arrête pas à la palette.

Le module de colisage permet de rechercher un contenant adapté à un produit avant sa palettisation.

Le catalogue des caisses dispose de son propre espace, à l'image des palettes, avec un jeu par défaut modifiable :

* cartons modulaires ;
* cartons standard ;
* bacs plastiques ;
* caisses bois ;
* dimensions personnalisées.

PalTunes peut :

1. rechercher les caisses compatibles, pour un article ou pour **plusieurs articles** (colisage hétérogène) ;
2. comparer les possibilités en fonction de la palette de destination ;
3. proposer le meilleur contenant (ou en forcer un) ;
4. afficher son contenu, caisse ouverte ou fermée ;
5. visualiser le plan intérieur ;
6. expliquer pourquoi aucune caisse ne convient (trop lourd, trop grand) ;
7. créer l'article correspondant au colis ;
8. lancer directement sa palettisation.

### Colisage hétérogène

Plusieurs articles × quantités dans une même caisse, comme une palette multi-articles : mêmes règles d'appui, de
charge et d'ordre lourd / léger, autant de caisses que nécessaire. Les trois choix sont conservés : meilleure caisse
du catalogue (en fonction de la palette de destination), caisse imposée du catalogue, caisse spécifique.

```text
meilleure caisse   manutentionnable à la main (25 kg brut) → moins de palettes → moins de volume de caisses → moins de caisses
chaque caisse      poids des produits ≤ charge maxi de la caisse et ≤ 25 kg brut (si chaque produit le permet)
résultat           n caisses, contenu et poids brut de chacune, caisses par palette, palettes nécessaires
```

Chaque caisse peut devenir un article « caisse mixte » (contenu dans la désignation, quantité totale par caisse).

### Chaîne possible

```text
ARTICLE
   │
   ▼
COLISAGE
   │
   ▼
CAISSE
   │
   ▼
PALETTISATION
   │
   ▼
PALETTE
```

---

# 12 / LES VUES

Une solution n'a d'intérêt que si elle peut être comprise rapidement.

PalTunes dispose de plusieurs représentations complémentaires.

### 3D

Visualisation spatiale de la palette, du support, des accessoires et des produits.

La scène permet de contrôler visuellement :

* la disposition ;
* les couches ;
* les orientations ;
* les volumes occupés ;
* les produits creux (tubes, bagues, bobines : paroi intérieure et extrémités en couronne) ;
* la cohérence globale de la solution.

Pour les très grandes quantités (petits articles par dizaines de milliers), seuls les produits visibles de l'extérieur sont dessinés.

```text
clic droit    rotation
molette       zoom vers le point sous la souris
```

Le zoom avant s'arrête avant d'entrer dans le volume de la palette : la perspective reste fidèle, rien n'est coupé. Une ombre douce au sol aide à lire les volumes.

### 2D

Trois lectures sont disponibles :

```text
DESSUS        CÔTÉ        FACE
```

Elles permettent de lire précisément le plan sans dépendre de la perspective 3D. Les cotes de palette, de charge et d'encombrement y figurent ; le détail des calculs reste dans la spécification.

Les produits creux y sont lisibles : cercle intérieur lorsqu'ils sont vus en bout, alésage en traits interrompus lorsqu'ils sont vus de côté.

---

# 13 / INDICATEURS

Chaque solution peut être décrite à partir de plusieurs métriques.

```text
┌──────────────────────────────────────┐
│ QUANTITÉ / PALETTE                  │
│ QUANTITÉ / COUCHE                   │
│ NOMBRE DE COUCHES                   │
│ POIDS TOTAL                         │
│ HAUTEUR TOTALE                      │
│ DIMENSIONS DE CHARGE                │
│ DIMENSIONS D'ENCOMBREMENT           │
│ REMPLISSAGE                         │
│ SCHÉMA DE POSE                      │
│ NOMBRE DE PALETTES PHYSIQUES        │
└──────────────────────────────────────┘
```

Ces informations permettent de comparer les configurations sans se limiter à la quantité maximale.

---

# 14 / ORDRE DE POSE ET PLAN DE PALETTISATION

Pour les solutions qui le nécessitent, PalTunes ne conserve pas uniquement la géométrie finale.

Un ordre de placement peut être généré pour rendre la solution exploitable sur le terrain.

```text
01 → positionner
02 → positionner
03 → positionner
...
```

La fiche imprimée se termine par un **plan de palettisation** : tableau des couches (plan, nombre de produits, intercalaires), puis pour chaque plan de couche une vue de dessus numérotée et la position de chaque produit.

L'objectif est de passer d'un résultat mathématique à une instruction compréhensible.

---

# 15 / IMPORT ARTICLES

Les articles peuvent être importés depuis un fichier CSV.

Formats de séparateur reconnus :

```text
;
,
TAB
|
```

Les décimales peuvent utiliser `,` ou `.`.

Les noms de colonnes sont tolérants à la casse, aux accents et plusieurs synonymes sont reconnus.

### Format minimal

```csv
CODE;TYPE;LONGUEUR;LARGEUR;HAUTEUR;DIAMETRE;POIDS
ART-001;CAISSE;400;300;250;;12
ART-002;BOBINE;;700;;1000;380
ART-003;TUBE;1200;;;110;4,5
ART-004;PLAQUE;1600;1200;10;;15
ART-005;SAC;600;400;120;;25
ART-006;FUT;;;880;585;220
ART-007;BAC;600;400;300;;8
```

Ces données sont fictives et servent uniquement à illustrer le format attendu.

---

# 16 / FORMAT ARTICLE ÉTENDU

Des informations complémentaires peuvent enrichir les calculs et l'organisation du catalogue.

```text
CODE
TYPE
LONGUEUR
LARGEUR
HAUTEUR
LONGUEUR_PLIEE
LARGEUR_PLIEE
HAUTEUR_PLIEE
QTE_PAR_CAISSE
DIAMETRE
DIAMETRE_INT
POIDS
ORIENTATION
AXE
CHARGE_MAX
COUCHES_MAX
FRAGILE
DESIGNATION
CLIENT
FAMILLE
SOUS_FAMILLE
REF_CLIENT
EAN
COULEUR
NOTES
```

Les champs nécessaires dépendent du type de produit. La colonne `CLIENT` contient le **code** du client.

### Minimum par géométrie

| Type   | Dimensions nécessaires         | Facultatif                        |
| ------ | ------------------------------ | --------------------------------- |
| Caisse | longueur × largeur × hauteur   | dimensions pliées (carton à plat) |
| Sac    | longueur × largeur × hauteur   |                                   |
| Bac    | longueur × largeur × hauteur   |                                   |
| Plaque | longueur × largeur × épaisseur |                                   |
| Bobine | diamètre × laize               | diamètre du mandrin (creux)       |
| Tube   | diamètre × longueur            | diamètre intérieur (tube creux)   |
| Fût    | diamètre × hauteur             |                                   |
| Bidon, cuve | longueur × largeur × hauteur hors tout |                         |
| Seau, bouteille | diamètre × hauteur         |                                   |

Le code, le type et le poids complètent ces informations minimales.

Pour un tube, la colonne `HAUTEUR` est lue comme l'épaisseur de paroi (diamètre intérieur = diamètre − 2 × épaisseur) lorsque `DIAMETRE_INT` est vide.

### Diamètres des tubes et des bobines

| Colonnes renseignées        | Diamètre pris en compte                  | Schémas      |
| --------------------------- | ---------------------------------------- | ------------ |
| `DIAMETRE`                  | diamètre extérieur                       | plein        |
| `DIAMETRE` + `DIAMETRE_INT` | diamètre extérieur                       | creux        |
| `DIAMETRE_INT` seul         | diamètre intérieur, avec avertissement   | plein        |
| aucune                      | ligne refusée                            |              |

Les données sont gardées telles qu'elles sont importées ou saisies : c'est le logiciel qui choisit le diamètre à utiliser.

---

# 17 / MISE À JOUR PAR IMPORT

Un article est identifié par son **code pour son client** : le même code peut exister chez deux clients.

Lorsqu'un article importé existe déjà :

```text
MÊME CODE + MÊME CLIENT
      │
      ├─ colonne présente dans le CSV → mise à jour
      │
      └─ colonne absente              → valeur conservée

MÊME CODE + AUTRE CLIENT  → nouvel article
SANS COLONNE CLIENT       → le code seul suffit s'il est unique
```

Les clients sont importés de la même façon, identifiés par leur code ; deux clients peuvent porter le même nom.

L'import produit un rapport permettant de distinguer :

* les créations ;
* les mises à jour ;
* les erreurs ;
* les avertissements (poids suspect, épaisseur de paroi lue, diamètre intérieur pris comme diamètre) ;
* les colonnes inconnues.

---

# 18 / ORGANISATION DES ARTICLES

Le catalogue peut être parcouru sous plusieurs angles.

Exemples :

```text
Client
└── Famille
    └── Sous-famille
        └── Article
```

ou

```text
Famille
└── Type
    └── Article
```

ou encore :

```text
Type
└── Client
    └── Article
```

Lorsque seul le client est affiché, il apparaît sous la forme `CODE - Nom`.

Une recherche permet également d'accéder directement aux références, et chaque liste déroulante (clients, articles, palettes, caisses…) se filtre en tapant. Pour choisir un article, la liste des clients vient d'abord et limite les articles proposés.

---

# 19 / CONTRAINTES PAR CLIENT

Une configuration peut être associée à un client sans modifier les caractéristiques physiques de l'article.

Il est notamment possible de définir :

* une palette imposée ;
* une hauteur maximale ;
* une limite de gerbage ;
* des règles de conditionnement particulières.

### Exemple fictif

```text
CLIENT-001

Palette préférée : PAL-01
Hauteur max      : 1 650 mm
Gerbages max     : 2
```

Aucune donnée réelle de client n'est nécessaire au fonctionnement des exemples du projet.

---

# 20 / EXPORT

PalTunes peut produire une spécification exploitable de la solution calculée.

Pour une palettisation mono-article, l'export CSV peut notamment contenir :

* quantité de produits ;
* quantité par palette ;
* nombre de palettes physiques ;
* gerbage ;
* dimensions de la palette ;
* dimensions de charge ;
* dimensions d'encombrement ;
* nombre de couches ;
* schéma ;
* intercalaires ;
* cornières ;
* poids ;
* taux de remplissage ;
* plan par couche ;
* couches avec intercalaire.

Trois fiches peuvent être imprimées ou enregistrées au format PDF via le système d'impression Windows :

```text
FICHE PALETTE            spécification, plans de couche, plan de palettisation      Ctrl + P
FICHE DE COLISAGE        produit, caisse, quantité par caisse, poids brut, plans, vue 3D
FICHE DE CONDITIONNEMENT fiche palette, puis fiche(s) de colisage, chacune sur une nouvelle page
```

Les fiches portent sur **l'article affiché**, quel que soit le menu : article sélectionné, article du conditionnement, produit du colisage (dans les autres espaces, le dernier article affiché, rappelé au-dessus des boutons). Chaque bouton est actif si la fiche est possible pour cet article, grisé sinon — le survol en donne la raison :

```text
fiche palette              un conditionnement existe (affiché, sélectionné ou enregistré pour l'article)
fiche de colisage          un colisage existe (affiché, caisse créée au colisage, ou au moins une quantité par caisse)
fiche de conditionnement   les deux : le conditionnement de la caisse et son colisage
```

Palette hétérogène avec des caisses (affichée aux Conditionnements ou sélectionnée dans la Gestion) : les fiches de colisage sont celles de ses caisses. Dès que plusieurs colisages sont disponibles, le bouton devient **« Imprimer les fiches de colisage »** (une fiche par caisse, chacune sur une nouvelle page) et la fiche de conditionnement imprime la fiche palette puis toutes les fiches de colisage.

Une caisse mixte (plusieurs articles) a une fiche de colisage complète : composition (quantité et poids de chaque article), stratégie et règles de remplissage, caisse, poids brut, schémas (niveaux, côté, face) et vue 3D de la caisse ouverte avec la légende des couleurs.

Rien n'est calculé à la place de l'utilisateur. Une caisse dont seule la quantité par caisse est connue donne une fiche de colisage résumée (quantité, caisse, palettisation enregistrée, sans plans).

La première page tient toujours sur une seule page : si elle déborde, elle se resserre d'elle-même (images du cartouche réduites, texte et marges plus serrés, spécification sur deux colonnes), sans retirer d'information.

Mise en page : première page avec un cartouche (informations, image de la palette choisie et de la caisse du catalogue) et la spécification ; schémas (couches, côté, face, vue 3D) sur une page à part ; plan de palettisation ensuite ; pied de page numéroté sur chaque page.

### Détails du calcul

Les écrans Conditionnements et Colisage ont un onglet **Détails du calcul** : chaque étape avec ses chiffres et son explication — base et limites (surface et hauteur utiles, charge), produit retenu, plan de couche et sa borne théorique, nombre de couches par la hauteur, le poids, la résistance et le facteur limitant, poids et encombrement, gerbage ; en hétérogène, bornes de volume et de poids, règles de pose, stratégie retenue et détail par unité ; au colisage, caisse et limites de poids (charge maxi, manutention à la main), poids brut et palettisation des caisses.

La section **Plan de couche pas à pas** montre comment le nombre de produits par couche est trouvé : surface utile et empreinte du produit, grilles simples comparées (tout en long, tout en travers ; pour les ronds, rangées alignées et en quinconce), plan retenu décomposé en blocs ou en rangées, gain sur la meilleure grille simple, borne par la surface et surface couverte. Tout en bas, **Les opérations, en bref** reprend le calcul en opérations simples numérotées (couches = 1 656 ÷ 250 = 6,62 → 6 ; produits = 8 × 6 = 48 ; poids ; hauteur).

Pour chaque nombre, on sait pourquoi : chaque contrainte (hauteur, poids, résistance, couches maxi, forme du dessus)
donne son nombre maximal de couches avec son opération, la plus petite décide ; la section **Pourquoi pas plus ?** dit
ce qu'une couche de plus dépasserait, pourquoi pas un produit de plus par couche et pourquoi la dernière couche est
incomplète. « Copier le détail » met tout le texte dans le presse-papiers.

Deux sections expliquent les quantités :

```text
Quantité par palette    produits par couche × couches = quantité (dernière couche incomplète détaillée)
                        palette de caisses : colis × quantité par caisse = produits contenus
                        hétérogène : quantité de chaque article sur chaque palette
Quantité par colisage   Colisage : produits par couche × couches, facteur limitant (ou composition de chaque caisse)
                        Conditionnements : colisage recalculé de chaque caisse de la palette, ou quantité saisie
```

Un article caisse peut porter une **quantité par caisse** (facultative) : renseignée automatiquement à la création d'une caisse au colisage, ou importée (`QTE_PAR_CAISSE`). La fiche palette indique alors aussi les produits contenus par palette ; vide, rien ne change.

---

# 21 / VALIDATION

Trouver une solution et démontrer qu'elle respecte les règles sont deux opérations différentes.

PalTunes possède donc un validateur indépendant du moteur de recherche.

```text
MOTEUR
  │
  └── produit une solution
              │
              ▼
        ┌─────────────┐
        │ VALIDATEUR  │
        └──────┬──────┘
               │
       ┌───────┴────────┐
       ▼                ▼
    VALIDE          REJETÉE
```

Cette séparation permet de contrôler les configurations générées avant de les présenter comme utilisables. Les contrôles de chevauchement et d'appui reposent sur un index spatial : ils restent rapides même avec des dizaines de milliers de produits.

Le moteur est lui-même validé contre des références indépendantes — optimums publiés du *pallet loading problem*, bornes de surface et de Barnes, mailles de cercles, usages industriels, bornes de volume et de poids en hétérogène — par des tests rejoués à chaque compilation : [`docs/VALIDATION_MOTEUR.md`](docs/VALIDATION_MOTEUR.md).

```text
19 palettes homogènes (tous les types)   optimum ou meilleure maille atteint, sauf 1 instance publiée à 1 boîte
8 palettes hétérogènes multi-types       toutes conformes, 6 à la borne de palettes, 2 à borne + 1
12 colisages homogènes (tous les types)  référence atteinte partout (Barnes, mailles, carton de 12 bouteilles)
7 colisages hétérogènes multi-types      tous conformes, 6 à la borne de caisses, 1 à borne + 1
```

---

# 22 / LES ESPACES

```text
Ctrl+1  CLIENTS            coordonnées, exigences, articles du client, import / export
Ctrl+2  ARTICLES           arborescence, fiche selon le type, aperçu 3D, import / export
Ctrl+3  PALETTES           catalogue, construction, charges, aperçu 3D
Ctrl+4  CAISSES            catalogue des contenants, aperçu 3D
Ctrl+5  CONDITIONNEMENTS   homogène / hétérogène, contraintes, accessoires, solutions, vues, spécification
Ctrl+6  COLISAGE           meilleure caisse, caisse ouverte / fermée, création de l'article caisse
Ctrl+7  GESTION DES        tous les conditionnements rangés par client, famille, type ;
        CONDITIONNEMENTS   recherche, fiche résumée, aperçu 3D, ouvrir / dupliquer / supprimer
```

**Mode sombre** : « Mode sombre » en bas de la navigation ou Ctrl+Maj+D, mémorisé ; la bascule garde l'écran et la
saisie en cours. **Colonnes redimensionnables** : glisser le séparateur entre deux colonnes (largeur mémorisée par
écran, double-clic : largeur d'origine).

**Recherche globale** : Ctrl+K (ou « Rechercher… » en haut de la navigation) trouve articles, colisages,
conditionnements, clients, palettes, caisses et espaces en une saisie, accents ignorés ; Entrée ouvre le résultat.
**Précédent / suivant** : Alt+← / Alt+→, flèches sous le logo ou boutons latéraux de la souris. Tous les raccourcis :
Ctrl+F1.

Les écrans sont reliés entre eux :

```text
Gestion des conditionnements  Voir le colisage (choix si plusieurs caisses) · Voir l'article
Conditionnements              Voir le colisage · Fiche article · icône colisage sur chaque ligne caisse
Articles                      Palettiser · Mettre en caisse · Voir le colisage · Ses conditionnements
Colisage                      Fiche article · Ses conditionnements
```

« Voir le colisage » ouvre l'espace Colisage réglé sur le colisage de la caisse (produit, caisse, quantité par caisse, position) ; « Ses conditionnements » ouvre la gestion filtrée sur l'article (palettes de ses caisses comprises).

L'écran **Conditionnements** ne garde que les **10 derniers** conditionnements modifiés (liste « Récents », repliable) ; une recherche porte sur tous. Les autres se retrouvent dans **Gestion des conditionnements**, rangés comme les articles.

Au-dessus, la liste repliable **« En cours »** garde les conditionnements créés ou modifiés et pas encore enregistrés : on passe de l'un à l'autre sans perdre la saisie (gardée pendant la session) ; la croix abandonne une saisie.

Sur les vues, chaque article reçoit une **couleur bien distincte** des autres ; la case **Couleur d'origine** (décochée par défaut, mémorisée) reprend les couleurs des fiches articles.

---

# 23 / ARCHITECTURE

Le projet sépare volontairement le moteur métier de l'interface.

```text
PalTunes
│
├── src
│   │
│   ├── PalTunes.Core
│   │   │
│   │   ├── Catalog
│   │   ├── Engine
│   │   ├── Export
│   │   ├── Import
│   │   ├── Models
│   │   ├── Storage
│   │   └── Validation
│   │
│   └── PalTunes.App
│       │
│       ├── Assets
│       ├── Converters
│       ├── Services
│       ├── Themes
│       ├── ViewModels
│       └── Views
│
├── tests
│   └── PalTunes.Tests
│
├── samples
│   ├── articles_minimal.csv
│   ├── articles_exemple.csv
│   └── clients_exemple.csv
│
└── docs
    ├── ETUDE_PALETTISATION.md
    ├── ETUDE_HETEROGENE.md
    └── FORMAT_IMPORT_ARTICLES.md
```

`PalTunes.Core` ne dépend pas de l'interface graphique.

Les moteurs de calcul peuvent donc évoluer indépendamment de WPF.

---

# 24 / MOTEURS

Le cœur de PalTunes regroupe plusieurs composants spécialisés.

```text
RectLayerSolver
│
├─ produits rectangulaires
├─ recherche de plans
└─ optimisation des couches


CircleLayerSolver
│
├─ produits cylindriques
└─ placement circulaire


HomogeneousEngine
│
└─ palettisation mono-article


HeterogeneousEngine
│
└─ palettisation multi-articles


StackingProfile
│
└─ profil de gerbage déduit de chaque article


CaseEngine
│
└─ colisage


MetricsCalculator
│
└─ métriques des solutions


PackagingCalculator
│
└─ calcul global du conditionnement


PlacementGrid / TopIndex
│
└─ index spatial des produits placés


SolutionValidator
│
└─ contrôle indépendant
```

---

# 25 / TECHNOLOGIES

```text
Language      C#
Runtime       .NET 10
Desktop       WPF
Architecture  MVVM
MVVM Toolkit  CommunityToolkit.Mvvm
3D            HelixToolkit.Wpf
Tests         xUnit
Storage       JSON
```

La base PalTunes est stockée localement dans un fichier JSON.

L'écriture est réalisée de manière atomique afin de limiter les risques de corruption lors d'une sauvegarde. Une copie de sauvegarde est créée avant toute conversion d'une base par une nouvelle version et avant une réinitialisation ; les noms de fichiers réservés au programme sont refusés comme base.

---

# 26 / INSTALLATION

Les versions prêtes à utiliser, les fichiers nécessaires et les indications associées à chaque version sont disponibles directement dans les Releases GitHub :

https://github.com/rodrigueantunes/PalTunes/releases

---

# 27 / UTILISATION

Le point d'entrée pour utiliser PalTunes est également la page Releases :

https://github.com/rodrigueantunes/PalTunes/releases

Choisir la version souhaitée puis suivre les indications fournies avec la release correspondante.

Le panneau « Nouveautés » présente le contenu de chaque version au premier lancement ; la pastille de version permet de le rouvrir.

### Depuis les sources

```bash
dotnet run --project src/PalTunes.App
dotnet test
```

---

# 28 / DOCUMENTATION TECHNIQUE

Le dépôt contient également les études utilisées comme référence pour le moteur de calcul.

### Étude générale

[`docs/ETUDE_PALETTISATION.md`](docs/ETUDE_PALETTISATION.md)

Elle couvre notamment :

* vocabulaire métier ;
* données minimales ;
* géométrie ;
* palettisation homogène ;
* plans de couche ;
* produits cylindriques ;
* plaques ;
* débords ;
* palettes multiples ;
* poids ;
* hauteur ;
* stabilité ;
* gerbage ;
* accessoires ;
* métriques.

### Chargements hétérogènes

[`docs/ETUDE_HETEROGENE.md`](docs/ETUDE_HETEROGENE.md)

Référence spécifique aux palettes multi-articles.

### Validation du moteur

[`docs/VALIDATION_MOTEUR.md`](docs/VALIDATION_MOTEUR.md)

Méthode, références indépendantes et résultats chiffrés (homogène par type, hétérogène multi-types).

### Import

[`docs/FORMAT_IMPORT_ARTICLES.md`](docs/FORMAT_IMPORT_ARTICLES.md)

Description complète du format d'import CSV des articles et des clients.

---

# 29 / RACCOURCIS

```text
F5          Calculer
Ctrl + S    Enregistrer
Ctrl + P    Imprimer la fiche palette
Ctrl + E    Exporter
Ctrl + I    Importer
F1          Aide import
Ctrl + 1    Clients
Ctrl + 2    Articles
Ctrl + 3    Palettes
Ctrl + 4    Caisses
Ctrl + 5    Conditionnements
Ctrl + 6    Colisage
Ctrl + 7    Gestion des conditionnements
Échap       Fermer
```

---

# 30 / PALTUNES EN UNE LIGNE

```text
dimensions + contraintes + support
                ↓
             PalTunes
                ↓
 plan calculé + contrôlé + visualisable
```

---

<div align="center">

### PalTunes

`PALLET / PACK / OPTIMIZE`

Concevoir le chargement avant de charger.

[Releases](https://github.com/rodrigueantunes/PalTunes/releases) ·
[Étude de palettisation](docs/ETUDE_PALETTISATION.md) ·
[Chargements hétérogènes](docs/ETUDE_HETEROGENE.md) ·
[Format d'import](docs/FORMAT_IMPORT_ARTICLES.md)

</div>
