using OptiTunes.Core.Engine;
using OptiTunes.Core.Import;
using OptiTunes.Core.Models;
using OptiTunes.Core.Validation;

namespace OptiTunes.Tests;

public class SolutionTests
{
    [Theory]
    [InlineData("groupage_3_complet.csv")]
    [InlineData("groupage_plaques_tubes.csv")]
    public void Every_alternative_solution_is_valid_and_distinct(string file)
    {
        var g = new FlatFileImporter().ImportFile(Path.Combine(AppContext.BaseDirectory, "Samples", file)).Groupages[0];
        var solutions = new LoadOptimizer().OptimizeSolutions(g, new PackingOptions { MaxVehicles = 3 });

        Assert.True(solutions[0].IsRecommended);
        Assert.True(solutions.Count > 1);
        Assert.Equal(solutions.Count, solutions.Select(s => s.Name).Distinct().Count());
        foreach (var plan in solutions)
        {
            var report = PlanValidator.Validate(plan);
            Assert.True(report.IsValid, $"{plan.Name} : {string.Join(" | ", report.AllErrors)}");
        }
    }

    [Fact]
    public void Simulating_a_smaller_vehicle_requires_more_trucks()
    {
        var g = new FlatFileImporter().ImportFile(Path.Combine(AppContext.BaseDirectory, "Samples", "groupage_3_complet.csv")).Groupages[0];
        var porteur = VehicleCatalog.Presets.First(v => v.Id.StartsWith("Porteur 12"));

        var plan = new LoadOptimizer().Optimize(g.WithVehicle(porteur), new PackingOptions { MaxVehicles = 10 });

        Assert.True(plan.Loads.Count >= plan.Metrics.EstimatedVehicles);
        Assert.True(plan.Loads.Count >= 3);
        Assert.True(PlanValidator.Validate(plan).IsValid);
        Assert.Equal(13600, g.Vehicle.Length);
    }
}
