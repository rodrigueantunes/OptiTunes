using OptiTunes.Core.Engine;
using OptiTunes.Core.Import;
using OptiTunes.Core.Models;
using OptiTunes.Core.Validation;

namespace OptiTunes.Tests;

public class StopsTests
{
    private const string Itinerary = """
        #GRP;ID_GROUPAGE;VEHICULE;LONGUEUR_UTILE;LARGEUR_UTILE;HAUTEUR_UTILE;CHARGE_UTILE
        GRP;26090007;SEMI;13600;2450;2400;25000
        #OT;ID_ORDRE;CLIENT;ARTICLE;TYPE_PHYSIQUE;QUANTITE;LONGUEUR;LARGEUR;HAUTEUR;POIDS_UNITAIRE;GERBABLE;NIVEAUX_MAX;MAGASIN;DEPART;ARRIVEE
        OT;26090007-1;C00022-002;PAL-A;PALETTE;6;1200;800;1100;250;N;;CPF;1;4
        OT;26090007-2;C00720-001;PAL-B;PALETTE;4;1200;1000;1200;300;N;;OPF;2;3
        OT;26090007-3;C00022-002;PAL-C;PALETTE;4;1200;800;1100;250;N;;CPF;1;4
        """;

    [Fact]
    public void Itinerary_columns_are_imported_and_select_the_stops_mode()
    {
        var g = new FlatFileImporter().ImportText(Itinerary).Groupages[0];

        Assert.Equal(("OPF", 2, 3), (g.Orders[1].Warehouse, g.Orders[1].DepartureStep, g.Orders[1].ArrivalStep));
        Assert.Equal(StowageMode.Stops, Stowage.DefaultFor(g));

        var plan = new LoadOptimizer().Optimize(g, new PackingOptions { Stowage = StowageMode.Stops });
        Assert.Equal(14, plan.Metrics.ItemsPlaced);
        Assert.True(PlanValidator.Validate(plan).IsValid);
    }

    [Fact]
    public void Erp_column_names_are_accepted()
    {
        var text = Itinerary.Replace("MAGASIN;DEPART;ARRIVEE", "IC_CHAR3_1;IC_NUM1;IC_NUM2");
        var g = new FlatFileImporter().ImportText(text).Groupages[0];
        Assert.Equal(("CPF", 1, 4), (g.Orders[0].Warehouse, g.Orders[0].DepartureStep, g.Orders[0].ArrivalStep));
    }

    [Fact]
    public void Departure_after_arrival_is_ignored_with_a_warning()
    {
        var result = new FlatFileImporter().ImportText(Itinerary.Replace("OPF;2;3", "OPF;3;2"));
        Assert.Contains(result.Issues, i => i.Code == "ETAPES_INCOHERENTES");
        Assert.Equal((0, 0), (result.Groupages[0].Orders[1].DepartureStep, result.Groupages[0].Orders[1].ArrivalStep));
    }

    [Fact]
    public void Mixed_itinerary_is_flagged()
    {
        var result = new FlatFileImporter().ImportText(Itinerary.Replace("OPF;2;3", "OPF;5;6"));
        Assert.Contains(result.Issues, i => i.Code == "ITINERAIRE_MIXTE");
    }

    [Fact]
    public void Without_steps_the_stops_mode_gives_exactly_the_historical_plan()
    {
        for (var seed = 1; seed <= 40; seed++)
        {
            var g = SelfTestSuite.RandomGroupage(seed);
            var historical = new LoadOptimizer().Optimize(g, new PackingOptions { Stowage = StowageMode.Order });
            var stops = new LoadOptimizer().Optimize(g, new PackingOptions { Stowage = StowageMode.Stops });

            Assert.Equal(Signature(historical), Signature(stops));
        }
    }

    [Theory]
    [InlineData(StowageMode.Order)]
    [InlineData(StowageMode.Stops)]
    [InlineData(StowageMode.Customer)]
    [InlineData(StowageMode.Warehouse)]
    [InlineData(StowageMode.Command)]
    [InlineData(StowageMode.Compact)]
    public void Every_stowage_mode_produces_valid_plans_with_itineraries(StowageMode mode)
    {
        var failures = new List<string>();
        for (var seed = 1; seed <= 40; seed++)
        {
            var rnd = new Random(seed);
            var g = SelfTestSuite.RandomGroupage(seed);
            foreach (var o in g.Orders)
            {
                o.Steps(rnd.NextDouble() < 0.5 ? "CPF" : "OPF", rnd.Next(1, 3), rnd.Next(3, 6), $"CLIENT {rnd.Next(1, 4)}");
            }

            var plan = new LoadOptimizer().Optimize(g, new PackingOptions { Stowage = mode, MaxVehicles = 4 });
            var report = PlanValidator.Validate(plan);
            if (!report.IsValid)
            {
                failures.Add($"#{seed} : {report.AllErrors.First()}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Itinerary_sample_is_fully_loaded_and_accessible()
    {
        var result = new FlatFileImporter().ImportFile(Path.Combine(AppContext.BaseDirectory, "Samples", "groupage_itineraire.csv"));
        var g = Assert.Single(result.Groupages);
        Assert.DoesNotContain(result.Issues, i => i.Severity != IssueSeverity.Info);
        Assert.Equal(StowageMode.Stops, Stowage.DefaultFor(g));

        var plan = new LoadOptimizer().Optimize(g, new PackingOptions { Stowage = StowageMode.Stops });
        Assert.Equal(0, plan.Metrics.ItemsRemaining);
        Assert.Single(plan.Loads);
        Assert.True(PlanValidator.Validate(plan).IsValid);
    }

    private static string Signature(LoadPlan p) =>
        string.Join("|", p.Placements.OrderBy(x => x.Unit.Id).Select(x => $"{x.Unit.Id}:{x.VehicleNumber}:{x.X:0}:{x.Y:0}:{x.Z:0}"));
}
