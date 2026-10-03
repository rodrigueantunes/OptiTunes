using OptiTunes.Core.Engine;
using OptiTunes.Core.Import;
using OptiTunes.Core.Models;
using OptiTunes.Core.Validation;

namespace OptiTunes.Tests;

public class ImportTests
{
    private const string Sample = """
        #GRP;ID_GROUPAGE;DATE;TRANSPORTEUR;VEHICULE;LONGUEUR_UTILE;LARGEUR_UTILE;HAUTEUR_UTILE;CHARGE_UTILE;LARGEUR_PORTE;HAUTEUR_PORTE
        GRP;G001;01/10/2026;TRANS;SEMI;13600;2450;2700;24000;2450;2650
        #OT;ID_ORDRE;TYPE_ORDRE;NUM_CDE;LIGNE_CDE;NUM_CADENCE;CLIENT;ARRET;ARTICLE;DESIGNATION;TYPE_PHYSIQUE;QUANTITE;LONGUEUR;LARGEUR;HAUTEUR;DIAMETRE;POIDS_UNITAIRE;GERBABLE;NIVEAUX_MAX;CHARGE_MAX_DESSUS;ORIENTATIONS;QTE_MAX_PILE;AU_SOL;AU_SOMMET
        OT;OT1;CDE;C100;1;;CLIENT A;1;CARTON-A;Carton;BOX;24;600;400;400;;18,5;O;4;120;1,2;;N;N
        OT;OT2;CAD;C100;2;3;CLIENT A;1;PLAQUE-1200;Plaque;PLAQUE;500;1200;1000;4;;1,2;N;;;12;200;;
        OT;OT3;CDE;C101;1;;CLIENT B;2;TUBE-90;Tube;TUBE;12;6000;;;90;25;O;3;;1,2;;;
        OT;OT4;CDE;C102;1;;CLIENT C;2;INCONNU;?;TRUC;5;100;100;100;;1;O;2;;;;;
        """;

    [Fact]
    public void Imports_groupage_vehicle_and_orders()
    {
        var result = new FlatFileImporter().ImportText(Sample);

        var g = Assert.Single(result.Groupages);
        Assert.Equal("G001", g.Id);
        Assert.Equal(13600, g.Vehicle.Length);
        Assert.Equal(2650, g.Vehicle.DoorHeight);
        Assert.Equal(4, g.Orders.Count);

        var carton = g.Orders[0];
        Assert.Equal(18.5, carton.UnitWeight);
        Assert.True(carton.Stackable);
        Assert.Equal(OrientationSet.Upright, carton.Orientations);

        var cadence = g.Orders[1];
        Assert.Equal(OrderKind.Cadence, cadence.Kind);
        Assert.Equal("3", cadence.CadenceNumber);
        Assert.Equal(200, cadence.MaxPerPile);

        Assert.Equal(PhysicalType.Tube, g.Orders[2].Type);
        Assert.Equal(90, g.Orders[2].Diameter);
    }

    [Fact]
    public void Unknown_physical_type_blocks_only_its_order()
    {
        var result = new FlatFileImporter().ImportText(Sample);
        var g = result.Groupages[0];

        Assert.False(g.Orders[3].IsValid);
        Assert.Contains(result.Issues, i => i.Severity == IssueSeverity.Error && i.OrderId == "OT4");

        var plan = new LoadOptimizer().Optimize(g);
        Assert.True(PlanValidator.Validate(plan).IsValid);
        Assert.Equal(5, plan.RemainingItems(g.Orders[3]));
        Assert.Equal(24 + 500 + 12, plan.Metrics.ItemsPlaced);
    }

    [Fact]
    public void Missing_stackable_is_treated_as_non_stackable_with_warning()
    {
        var text = """
            GRP;G2;;;PORTEUR;7000;2400;2400;8000;;
            OT;OT1;CDE;C1;1;;;0;ART;;BOX;4;1000;1000;1000;;50;;;;;;;
            """;
        var result = new FlatFileImporter().ImportText(text);
        Assert.Contains(result.Issues, i => i.Code == "GERBABLE_INCONNU");
        Assert.Contains(result.Issues, i => i.Code == "PORTE_INCONNUE");

        var plan = new LoadOptimizer().Optimize(result.Groupages[0]);
        Assert.All(plan.Placements, p => Assert.True(p.OnFloor));
    }

    [Fact]
    public void Missing_vehicle_dimension_blocks_the_calculation()
    {
        var result = new FlatFileImporter().ImportText("GRP;G3;;;SEMI;13600;;2700;24000;;\nOT;OT1;CDE;C1;1;;;0;ART;;BOX;1;100;100;100;;5;N;;;;;;");
        Assert.False(result.Groupages[0].IsComputable);

        var plan = new LoadOptimizer().Optimize(result.Groupages[0]);
        Assert.Empty(plan.Placements);
        Assert.Equal(1, plan.Unloaded.Sum(u => u.Unit.ItemCount));
    }

    [Fact]
    public void Header_line_can_reorder_columns_and_use_aliases()
    {
        var text = """
            #GRP;ID_GROUPAGE;VEHICULE;LC;LARGEUR_UTILE;HC;PMAX
            GRP;G4;SEMI;13600;2450;2700;24000
            #OT;ID_ORDRE;ARTICLE;TYPE_PHYSIQUE;QTE;L;LARGEUR;EPAISSEUR;POIDS;GERBABLE;NMAX
            OT;A;CARTON;CARTON;3;800;600;500;10;oui;3
            """;
        var g = new FlatFileImporter().ImportText(text).Groupages[0];

        Assert.Equal(13600, g.Vehicle.Length);
        Assert.Equal(24000, g.Vehicle.MaxPayload);
        Assert.Equal(500, g.Orders[0].Height);
        Assert.Equal(4, g.Orders[0].MaxLevels); // NMAX 3 = 3 gerbages au-dessus du sol = 4 niveaux
    }

    [Theory]
    [InlineData("groupage_exemple.csv")]
    [InlineData("groupage_multi_arrets.csv")]
    [InlineData("groupage_plaques_tubes.csv")]
    [InlineData("groupage_1_indispensables.csv")]
    [InlineData("groupage_2_facultatives.csv")]
    [InlineData("groupage_3_complet.csv")]
    public void Sample_files_import_and_produce_a_valid_plan(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Samples", file);
        var result = new FlatFileImporter().ImportFile(path);

        Assert.NotEmpty(result.Groupages);
        foreach (var g in result.Groupages)
        {
            var plan = new LoadOptimizer().Optimize(g);
            var report = PlanValidator.Validate(plan);
            Assert.True(report.IsValid, string.Join(Environment.NewLine, report.AllErrors));
        }
    }
}
