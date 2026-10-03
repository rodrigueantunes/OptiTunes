# OptiTunes – Format d'import du groupage (fichier à plat)

Un fichier = un ou plusieurs groupages. Unités : **dimensions en mm, poids en kg**. Décimales `,` ou `.`.
Séparateur détecté automatiquement : `;` (recommandé), tabulation, `|` ou `,`. Encodage UTF-8 ou Windows-1252.

Formats acceptés : **.csv / .txt** uniquement (les classeurs Excel sont refusés avec un message : les enregistrer en CSV).
Deux dispositions sont reconnues automatiquement :

1. **Lignes typées** `GRP` / `OT` (ci-dessous).
2. **Tableau** : une ligne d'en-tête puis une ligne par ordre, sans type d'enregistrement. Les colonnes du groupage et du
   véhicule (ID_GROUPAGE, LONGUEUR_UTILE…) sont répétées sur chaque ligne ; les lignes sont regroupées par ID_GROUPAGE.
   Sans colonnes véhicule, choisir le camion dans la liste VÉHICULE de l'application.

Les cellules peuvent porter une unité : `1200mm`, `120 cm`, `1,2 m` (→ mm) ; `25 kg`, `1,5 t` (→ kg).
Les en-têtes anglais courants sont reconnus (LENGTH, WIDTH, HEIGHT, WEIGHT, QTY, STACKABLE, ORDER_ID, SKU, PAYLOAD…).
L'onglet **Import** de l'application montre le format détecté, la correspondance de chaque colonne et la ligne brute
de chaque alerte. Le fichier est relu automatiquement à chaque enregistrement (option).

## Types d'enregistrement

| 1re colonne | Rôle |
|---|---|
| `GRP` | En-tête de groupage : véhicule et capacités. Les lignes `OT` suivantes lui sont rattachées. |
| `OT` | Ordre de transport : ligne de commande (`CDE`) ou ligne de cadencement (`CAD`) portant un article. |
| `#GRP;...` / `#OT;...` | Ligne d'en-tête optionnelle : redéfinit l'ordre des colonnes (noms ou alias ci-dessous). |
| `#...` | Commentaire. |

Sans ligne d'en-tête, l'ordre par défaut ci-dessous s'applique.

## GRP – groupage / véhicule

| Colonne | Alias acceptés | Priorité | Remarque |
|---|---|---|---|
| ID_GROUPAGE | GROUPAGE, NUM_GROUPAGE | Indispensable | |
| DATE | DATE_DEPART | Facultative | jj/mm/aaaa ou aaaa-mm-jj |
| TRANSPORTEUR | | Facultative | |
| VEHICULE | TYPE_VEHICULE, CAMION | Indispensable | |
| LONGUEUR_UTILE | LC | **Bloquante** | Lc intérieure (mm) |
| LARGEUR_UTILE | | **Bloquante** | lc intérieure (mm) |
| HAUTEUR_UTILE | HC | **Bloquante** | Hc intérieure (mm) |
| CHARGE_UTILE | PMAX, POIDS_MAX | **Bloquante** | Pmax (kg) |
| LARGEUR_PORTE | LARGEUR_OUVERTURE | Recommandée | Absente → passage porte non vérifié (avertissement) |
| HAUTEUR_PORTE | HAUTEUR_OUVERTURE | Recommandée | idem |

## OT – ordre de transport

| Colonne | Alias | Priorité | Remarque |
|---|---|---|---|
| ID_ORDRE | ORDRE, ID_OT | Indispensable | |
| TYPE_ORDRE | | Facultative | `CDE` (ligne de commande) ou `CAD` (cadencement) |
| NUM_CDE / LIGNE_CDE / NUM_CADENCE | COMMANDE / LIGNE / CADENCE | Facultative | Traçabilité |
| CLIENT | DESTINATAIRE | Facultative | |
| ARRET | SEQUENCE, ORDRE_LIVRAISON | Facultative | 1 = premier déchargé (côté porte). Vide/0 = sans contrainte |
| ARTICLE | REFERENCE | **Bloquante** | |
| DESIGNATION | LIBELLE | Facultative | |
| TYPE_PHYSIQUE | FAMILLE | **Bloquante** | BOX/CARTON/CAISSE, PLAQUE, TUBE, FAISCEAU, PALETTE, BOBINE, CUSTOM |
| QUANTITE | QTE | **Bloquante** | Entier > 0 |
| LONGUEUR | L | **Bloquante** | Tube : longueur. Bobine : laize si LARGEUR vide |
| LARGEUR | LAIZE | **Bloquante** (sauf tube) | Bobine : laize |
| HAUTEUR | EPAISSEUR, H | **Bloquante** (sauf tube/bobine) | Plaque : épaisseur unitaire |
| DIAMETRE | D | Bloquante tube/bobine | |
| POIDS_UNITAIRE | POIDS | **Bloquante** | **kg** par article (par plaque pour les plaques) |
| GERBABLE | EMPILABLE | Indispensable | O/N. Vide → **non gerbable** par sécurité |
| NIVEAUX_MAX | NMAX | Si gerbable | **Nombre de gerbages au-dessus de l'unité au sol** : 0 = rien dessus, 1 = une unité dessus, 2 = deux… Vide → 1 gerbage (niveau minimal sûr, avertissement) |
| CHARGE_MAX_DESSUS | PSUPPORT | Recommandée | kg supportables. Vide → (Nmax−1) × poids propre. 0 → ne reçoit rien |
| QTE_MAX_PILE | QMAXPILE | Plaques | Vide → pile limitée par la hauteur utile / porte |
| AU_SOL | | Facultative | O/N : obligatoirement au plancher |
| AU_SOMMET | DESSUS | Facultative | O/N : rien ne peut être posé dessus |
| MAGASIN | IC_CHAR3_1, MAGASIN_DEPART, DEPOT | Facultative | Gestion des arrêts : magasin de départ (chargement) |
| DEPART | IC_NUM1, ETAPE_DEPART | Facultative | Gestion des arrêts : n° d'étape de chargement (commun au groupage) |
| ARRIVEE | IC_NUM2, ETAPE_ARRIVEE | Facultative | Gestion des arrêts : n° d'étape de livraison. Prioritaire sur ARRET |

### Gestion des arrêts (facultative)

Les trois colonnes MAGASIN / DEPART / ARRIVEE décrivent l'itinéraire du camion (étapes numérotées 1, 2, 3… communes à
tout le groupage). Sans elles, le rangement reste celui des versions précédentes (colonne ARRET).

- Un ordre **chargé plus tôt** va plus au fond ; un ordre **livré plus tôt** reste accessible côté porte.
- Contrainte **stricte** : une unité ne peut jamais bloquer (devant elle ou dessus) une unité livrée avant elle ou chargée
  après elle. Si c'est impossible, l'unité part en reliquat (« Accessibilité impossible… ») ou sur un camion supplémentaire.
- DEPART ≥ ARRIVEE : étapes ignorées pour l'ordre (avertissement `ETAPES_INCOHERENTES`). Ordres avec et sans étapes
  dans un même groupage : avertissement `ITINERAIRE_MIXTE`.

Le sélecteur **Rangement** (à gauche de « Solution ») choisit la logique : *Ordre* (colonne ARRET, défaut sans étapes),
*Par arrêts* (défaut avec étapes), *Par client*, *Par magasin*, *Par commande* (zones regroupées dans le camion, sans
mélange de zones dans une pile) ou *Compact* (aucune contrainte d'accès, densité maximale).

### Orientation : automatique

Aucune colonne d'orientation n'est attendue. Le moteur pose chaque unité à plat et peut la **pivoter au sol**
(longueur dans le sens du camion ou en travers) ; il ne la couche jamais sur le flanc.
Tube : couché (dans la longueur ou en travers). Bobine : axe vertical.

## Métrage linéaire calculé (§13)

Pour chaque ordre, à partir des seules données du fichier :

1. **Niveaux par pile** = MIN(NIVEAUX_MAX ; ENT(hauteur utile / hauteur) ; 1 + ENT(CHARGE_MAX_DESSUS / poids)) — 1 si non gerbable.
2. **Piles au sol** = ARRONDI.SUP(unités / niveaux) (plaques : unités = piles de plaques).
3. **Piles par rangée** = ENT(largeur utile / largeur au sol) ; **rangées** = ARRONDI.SUP(piles / piles par rangée).
4. **ML rangées** = rangées × longueur au sol (§13.1) ; **ML équivalent** = piles × L × l / largeur utile (§13.2).
   Le sens de pose (long ou travers) retenu est celui qui donne le plus petit ML rangées.

Totaux : **besoin équivalent** = Σ ML équivalent des ordres (§13.3, indicateur de référence) ;
« Σ rangées » est un majorant (chaque ordre sur ses propres rangées). Après calcul du plan :
**ML équivalent occupé** (emprise au sol / largeur) et **ML réel** = MAX(X + DX) − MIN(X) au plancher (§13.5).

## Exemple (modèle complet)

Le bouton **« Exporter le modèle CSV »** de l'application génère ce fichier (toutes les colonnes, 1 groupage, 2 ordres).
Le bouton **« i »** (ou F1) affiche la description de chaque colonne, onglets *Groupage* et *Ordre de transport*.

```text
# OptiTunes - modele d'import complet - dimensions en mm - poids en kg - lignes # = en-tetes ou commentaires
#GRP;ID_GROUPAGE;DATE;TRANSPORTEUR;VEHICULE;LONGUEUR_UTILE;LARGEUR_UTILE;HAUTEUR_UTILE;CHARGE_UTILE;LARGEUR_PORTE;HAUTEUR_PORTE
GRP;26090007;02/10/2026;GEODIS;SEMI 13.60;13600;2450;2700;24000;2450;2650
#OT;ID_ORDRE;TYPE_ORDRE;NUM_CDE;LIGNE_CDE;NUM_CADENCE;CLIENT;ARRET;ARTICLE;DESIGNATION;TYPE_PHYSIQUE;QUANTITE;LONGUEUR;LARGEUR;HAUTEUR;DIAMETRE;POIDS_UNITAIRE;GERBABLE;NIVEAUX_MAX;CHARGE_MAX_DESSUS;QTE_MAX_PILE;AU_SOL;AU_SOMMET;MAGASIN;DEPART;ARRIVEE
OT;26090007-1;CDE;C26001;10;;C00022-002;;GALIAA09;Palette caisses GALIA;PALETTE;4;1200;800;1300;;117;O;1;400;;N;N;CPF;1;4
OT;26090007-2;CAD;C26002;20;1;C00720-001;;CARTON-RSC-40;Carton RSC 400x300x300;BOX;20;600;400;400;;6,5;O;4;80;;N;N;OPF;2;3
```

Exemples complets : dossier `samples/` (dont `groupage_itineraire.csv` pour la gestion des arrêts).

## Export du plan

« Exporter le plan » produit un CSV `;` avec trois blocs : `PLAN` (indicateurs, rangement, débords), `UNITE` (séquence de chargement, magasin / étapes,
X/Y/Z, dimensions orientées, niveau, charge supportée, supports) et `RELIQUAT` (unité, motif).
