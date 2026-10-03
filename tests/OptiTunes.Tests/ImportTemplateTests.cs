using OptiTunes.Core.Engine;
using OptiTunes.Core.Import;
using OptiTunes.Core.Models;
using OptiTunes.Core.Validation;

namespace OptiTunes.Tests;

public class ImportTemplateTests
{
    [Fact]
    public void Documentation_covers_every_import_column_in_order()
    {
        Assert.Equal(FlatFileImporter.DefaultGroupageColumns, ImportFormat.GroupageFields.Select(f => f.Column));
        Assert.Equal(FlatFileImporter.DefaultOrderColumns, ImportFormat.OrderFields.Select(f => f.Column));
        Assert.All(ImportFormat.GroupageFields.Concat(ImportFormat.OrderFields), f => Assert.False(string.IsNullOrWhiteSpace(f.Description)));
    }

    [Fact]
    public void Template_imports_cleanly_with_one_groupage_and_two_orders()
    {
        var result = new FlatFileImporter().ImportText(ImportFormat.TemplateCsv(), "modele.csv");

        var g = Assert.Single(result.Groupages);
        Assert.Equal(2, g.Orders.Count);
        Assert.DoesNotContain(result.Issues, i => i.Severity == IssueSeverity.Error);
        Assert.DoesNotContain(result.Columns, c => c.Field == null);
        Assert.Equal(("CPF", 1, 4), (g.Orders[0].Warehouse, g.Orders[0].DepartureStep, g.Orders[0].ArrivalStep));
        Assert.Equal(OrderKind.Cadence, g.Orders[1].Kind);

        var plan = new LoadOptimizer().Optimize(g, new PackingOptions { Stowage = Stowage.DefaultFor(g) });
        Assert.Equal(24, plan.Metrics.ItemsPlaced);
        Assert.True(PlanValidator.Validate(plan).IsValid);
    }

    /// <summary>Modèle rempli par l'utilisateur : référence article saisie dans ARRET, ARTICLE vide.</summary>
    [Fact]
    public void Article_typed_in_the_stop_column_is_recovered_with_a_warning()
    {
        const string text = """
            # OptiTunes - modele d'import complet - dimensions en mm - poids en kg - lignes # = en-tetes ou commentaires;;;;;;;;;;;;;;;;;;;;;;;;;
            #GRP;ID_GROUPAGE;DATE;TRANSPORTEUR;VEHICULE;LONGUEUR_UTILE;LARGEUR_UTILE;HAUTEUR_UTILE;CHARGE_UTILE;LARGEUR_PORTE;HAUTEUR_PORTE;;;;;;;;;;;;;;;
            GRP;26090007;30/09/2026;GORRON FRET                   ;LOC SEMI 13.60 M         ;13600;2450;2400;25000;;;;;;;;;;;;;;;;;
            #OT;ID_ORDRE;TYPE_ORDRE;NUM_CDE;LIGNE_CDE;NUM_CADENCE;CLIENT;ARRET;ARTICLE;DESIGNATION;TYPE_PHYSIQUE;QUANTITE;LONGUEUR;LARGEUR;HAUTEUR;DIAMETRE;POIDS_UNITAIRE;GERBABLE;NIVEAUX_MAX;CHARGE_MAX_DESSUS;QTE_MAX_PILE;AU_SOL;AU_SOMMET;MAGASIN;DEPART;ARRIVEE
            OT;26CC2729-6/0/1;CAD;26CC2729;6;1;DEVILLE                       ;GALIAC13;;200-- 400X 300X 200                     ;PALETTE;1;1400;1000;1125;;144;O;1;;;;;CPF;1;4
            OT;26CO1581-1/0/3;CAD;26CO1581;1;3;JEHIER SAS                    ;PS00720-0004;;452-BC70K-2080X1380X 200                ;PALETTE;1;2010;2510;550;;350,55;O;1;;;;;OPF;2;3
            OT;26CC2729-2/0/1;CAD;26CC2729;2;1;DEVILLE                       ;GALIACV12A14;;452-- 400X 300X  50                     ;PALETTE;1;1000;1200;1300;;150,66;O;1;;;;;CPF;1;4
            """;

        var result = new FlatFileImporter().ImportText(text, "modele.csv");
        var g = Assert.Single(result.Groupages);

        Assert.Equal(["GALIAC13", "PS00720-0004", "GALIACV12A14"], g.Orders.Select(o => o.Article));
        Assert.All(g.Orders, o => Assert.Equal(0, o.Stop));
        Assert.Equal(3, result.Issues.Count(i => i.Code == "ARTICLE_DANS_ARRET"));
        Assert.DoesNotContain(g.Orders, o => o.BlockingErrors.Count > 0 && o.BlockingErrors.Any(e => e.Contains("article")));
    }
}
