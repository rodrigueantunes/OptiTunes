using OptiTunes.Core.Engine;
using OptiTunes.Core.Import;
using OptiTunes.Core.Models;
using OptiTunes.Core.Validation;

namespace OptiTunes.Tests;

public class LinearMeterTests
{
    private static OrderLinearMeters Compute(double vehicleWidth, TransportOrder order, double length = 13600, double height = 2700)
    {
        var g = SelfTestSuite.NewGroupage("ML", length, vehicleWidth, height, 30000);
        g.Orders.Add(order);
        var plan = new LoadOptimizer().Optimize(g);
        return plan.LinearMetersByOrder[order];
    }

    private static TransportOrder Pallets(int qty, bool stackable = false, int? levels = null, double? maxLoad = null, double height = 1500) =>
        new()
        {
            Id = "PAL", Article = "EUR", Type = PhysicalType.Pallet, Quantity = qty, Length = 1200, Width = 800,
            Height = height, UnitWeight = 300, Stackable = stackable, MaxLevels = levels, MaxLoadOnTop = maxLoad
        };

    [Fact]
    public void Analysis_example_pallet_in_2400_wide_truck_is_040_ml_equivalent()
    {
        var ml = Compute(2400, Pallets(1));
        Assert.Equal(0.40, ml.MlEquivalent, 3);
    }

    [Fact]
    public void Thirty_three_non_stackable_pallets_need_11_rows_of_3()
    {
        var ml = Compute(2450, Pallets(33));

        Assert.Equal(3, ml.PerRow);
        Assert.Equal(11, ml.Rows);
        Assert.Equal(13.2, ml.MlRows, 3);
        Assert.Equal(33 * 1.2 * 0.8 / 2.45, ml.MlEquivalent, 3);
    }

    [Fact]
    public void Stacking_halves_the_floor_need()
    {
        var ml = Compute(2450, Pallets(10, true, 2, 400, 1200));

        Assert.Equal(2, ml.Levels);
        Assert.Equal(5, ml.Stacks);
        Assert.Equal(2.4, ml.MlRows, 3);
    }

    [Fact]
    public void Supportable_load_limits_the_levels()
    {
        // 300 kg par palette, 200 kg supportables : aucune palette ne peut être gerbée.
        var ml = Compute(2450, Pallets(10, true, 2, 200, 1200));

        Assert.Equal(1, ml.Levels);
        Assert.Equal(10, ml.Stacks);

        // En travers (0,80 m au sol, 2 par rangée) : 5 rangées = 4,0 m, mieux que 4 rangées de 1,20 m.
        Assert.Equal(2, ml.PerRow);
        Assert.Equal(4.0, ml.MlRows, 3);
    }

    [Fact]
    public void Height_limits_the_levels()
    {
        var ml = Compute(2450, Pallets(6, true, 4, 5000, 1000), height: 2400);

        Assert.Equal(2, ml.Levels);
        Assert.Equal(3, ml.Stacks);
    }

    [Fact]
    public void File_without_orientation_column_is_accepted_without_warning()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Samples", "groupage_1_indispensables.csv");
        var result = new FlatFileImporter().ImportFile(path);

        Assert.DoesNotContain(result.Issues, i => i.Code.StartsWith("ORIENTATION"));
        Assert.All(result.Groupages[0].Orders, o => Assert.Null(o.Orientations));

        var plan = new LoadOptimizer().Optimize(result.Groupages[0]);
        Assert.True(PlanValidator.Validate(plan).IsValid);
        Assert.Equal(result.Groupages[0].Orders.Count, plan.LinearMetersByOrder.Count);
        Assert.Equal(plan.LinearMetersByOrder.Values.Sum(m => m.MlRows), plan.Metrics.LinearMetersRequiredRows, 6);
    }
}
