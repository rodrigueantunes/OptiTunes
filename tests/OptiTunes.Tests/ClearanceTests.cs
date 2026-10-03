using OptiTunes.Core.Engine;
using OptiTunes.Core.Models;
using OptiTunes.Core.Validation;

namespace OptiTunes.Tests;

public class ClearanceTests
{
    private static TransportOrder Pallets(int qty) => new()
    {
        Id = "PAL", Article = "EUR", Type = PhysicalType.Pallet, Quantity = qty, Length = 1200, Width = 800, Height = 1500,
        UnitWeight = 300, Stackable = false
    };

    [Fact]
    public void Linear_meters_include_the_gap_between_rows_and_units()
    {
        var g = SelfTestSuite.NewGroupage("ML", 13600, 2450, 2700, 30000);
        g.Orders.Add(Pallets(33));

        var plain = new LoadOptimizer().Optimize(g).LinearMetersByOrder[g.Orders[0]];
        var withGap = new LoadOptimizer().Optimize(g, new PackingOptions { GapBetweenUnits = 50 }).LinearMetersByOrder[g.Orders[0]];

        Assert.Equal(13.2, plain.MlRows, 3);
        // En travers : 2 palettes de 1200 + 50 de jeu (2450 ≥ 2450), 17 rangées de 800 + 16 jeux de 50 = 14,40 m.
        Assert.Equal(14.4, withGap.MlRows, 3);
    }

    [Fact]
    public void Validator_rejects_a_plan_that_ignores_the_clearances()
    {
        var g = SelfTestSuite.NewGroupage("V", 2000, 1000, 1000, 24000);
        g.Orders.Add(SelfTestSuite.Box("C", 500, 500, 500, 20, 8));

        // Plan calculé sans débord, contrôlé avec débords : il doit être refusé.
        var plan = new LoadOptimizer().Optimize(g);
        plan.Options.GapBetweenUnits = 100;
        plan.Options.SideClearance = 20;
        plan.Options.RoofClearance = 600;

        var report = PlanValidator.Validate(plan);
        var check = report.Checks.Single(c => c.Name == "Débords");
        Assert.Equal(CheckStatus.Error, check.Status);
        Assert.Contains(check.Violations, v => v.Contains("paroi"));
        Assert.Contains(check.Violations, v => v.Contains("plafond"));
        Assert.Contains(check.Violations, v => v.Contains("l'un de l'autre"));
    }

    [Fact]
    public void Random_clearances_never_produce_an_invalid_plan()
    {
        var failures = new List<string>();
        for (var seed = 1; seed <= 120; seed++)
        {
            var rnd = new Random(seed);
            var options = new PackingOptions
            {
                GapBetweenUnits = rnd.Next(0, 101), SideClearance = rnd.Next(0, 61), RoofClearance = rnd.Next(0, 301), MaxVehicles = 3
            };
            var plan = new LoadOptimizer().Optimize(SelfTestSuite.RandomGroupage(seed), options);
            var report = PlanValidator.Validate(plan);
            if (!report.IsValid)
            {
                failures.Add($"#{seed} : {report.AllErrors.First()}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
