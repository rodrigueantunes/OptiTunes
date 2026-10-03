using OptiTunes.Core.Engine;
using OptiTunes.Core.Import;
using OptiTunes.Core.Models;
using OptiTunes.Core.Validation;

namespace OptiTunes.Tests;

/// <summary>Cas remontés sur un vrai export (groupage 26100001).</summary>
public class UserFileTests
{
    private const string Export = """
        #GRP;ID_GROUPAGE;VEHICULE;LONGUEUR_UTILE;LARGEUR_UTILE;HAUTEUR_UTILE;CHARGE_UTILE;;;;;
        GRP;26100001;SEMI 13.40;13600;2450;2400;25000;;;;;
        #OT;ID_ORDRE;ARTICLE;TYPE_PHYSIQUE;QUANTITE;LONGUEUR;LARGEUR;HAUTEUR;DIAMETRE;POIDS_UNITAIRE;GERBABLE;NIVEAUX_MAX
        OT;26CC2760-1/0/1;GALIAA09;PALETTE;1;;1400;1000;1300;117;O;1
        OT;26CC0188-1/0/1;PS00124-0005;PALETTE;2;1730;1370;1220;;299,795;O;1
        """;

    [Fact]
    public void Shifted_dimension_columns_are_repaired_with_a_warning()
    {
        var result = new FlatFileImporter().ImportText(Export);
        var order = result.Groupages[0].Orders[0];

        Assert.True(order.IsValid);
        Assert.Equal((1400, 1000, 1300, 0), (order.Length, order.Width, order.Height, order.Diameter));
        Assert.Contains(result.Issues, i => i.Code == "COLONNES_DECALEES" && i.OrderId == order.Id);
    }

    [Fact]
    public void Niveaux_max_counts_stackings_above_the_floor_unit()
    {
        var result = new FlatFileImporter().ImportText(Export);
        Assert.Equal(2, result.Groupages[0].Orders[0].MaxLevels); // 1 gerbage = 2 niveaux
        Assert.DoesNotContain(result.Issues, i => i.Code.StartsWith("GERBABLE_0"));

        var zero = new FlatFileImporter().ImportText(Export.Replace("117;O;1", "117;O;0"));
        Assert.Equal(1, zero.Groupages[0].Orders[0].MaxLevels);
        Assert.Contains(zero.Issues, i => i.Code == "GERBABLE_0_NIVEAU");
    }

    [Fact]
    public void Two_pallets_1730_wide_1370_take_346_m_lengthwise_and_274_m_crosswise()
    {
        var g = new FlatFileImporter().ImportText(Export).Groupages[0];
        var order = g.Orders[1];

        var ml = new LoadOptimizer().Optimize(g).LinearMetersByOrder[order];
        Assert.Equal(3.46, ml.MlLengthwise!.Value, 3);
        Assert.Equal(2.74, ml.MlCrosswise!.Value, 3);
        Assert.Equal(2.74, ml.MlRows, 3);

        var fixedPlan = new LoadOptimizer().Optimize(g, new PackingOptions { AllowFloorRotation = false });
        Assert.Equal(3.46, fixedPlan.LinearMetersByOrder[fixedPlan.Groupage.Orders[1]].MlRows, 3);
        Assert.True(PlanValidator.Validate(fixedPlan).IsValid);
        Assert.All(fixedPlan.Placements, p => Assert.Equal(1, p.Orientation));
    }

    [Fact]
    public void Real_export_loads_everything_once_columns_are_repaired()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Samples", "groupage_2_CPL.csv");
        if (!File.Exists(path))
        {
            return;
        }

        var g = new FlatFileImporter().ImportFile(path).Groupages[0];
        var plan = new LoadOptimizer().Optimize(g, new PackingOptions { MaxVehicles = 5 });

        Assert.All(g.Orders, o => Assert.True(o.IsValid));
        Assert.Equal(0, plan.Metrics.ItemsRemaining);
        Assert.True(PlanValidator.Validate(plan).IsValid);
    }
}
