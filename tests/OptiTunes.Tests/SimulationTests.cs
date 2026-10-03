using OptiTunes.Core.Engine;
using OptiTunes.Core.Import;
using OptiTunes.Core.Validation;

namespace OptiTunes.Tests;

public class SimulationTests
{
    [Fact]
    public void Excluding_orders_recomputes_without_them_and_keeps_the_source_intact()
    {
        var g = new FlatFileImporter().ImportFile(Path.Combine(AppContext.BaseDirectory, "Samples", "groupage_3_complet.csv")).Groupages[0];
        var full = new LoadOptimizer().Optimize(g, new() { MaxVehicles = 5 });

        var excluded = new HashSet<string> { "OT-70001", "OT-70004" };
        var partial = g.WithoutOrders(excluded);
        var plan = new LoadOptimizer().Optimize(partial, new() { MaxVehicles = 5 });

        Assert.Equal(9, g.Orders.Count);
        Assert.Equal(7, partial.Orders.Count);
        Assert.DoesNotContain(plan.Placements, p => excluded.Contains(p.Unit.Order.Id));
        Assert.True(plan.Metrics.ItemsTotal < full.Metrics.ItemsTotal);
        Assert.True(plan.Metrics.LinearMetersReal <= full.Metrics.LinearMetersReal);
        Assert.True(PlanValidator.Validate(plan).IsValid);
    }

    [Fact]
    public void Excluding_keeps_vehicle_blocking_errors()
    {
        var g = new FlatFileImporter().ImportText("ORDRE;ARTICLE;FAMILLE;QTE;L;LARGEUR;H;POIDS;GERBABLE\nOT1;PAL;PALETTE;3;1200;800;1000;250;N").Groupages[0];
        Assert.False(g.WithoutOrders(new HashSet<string>()).IsComputable);
    }
}
