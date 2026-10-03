using OptiTunes.Core.Engine;
using OptiTunes.Core.Import;
using OptiTunes.Core.Models;
using OptiTunes.Core.Validation;

namespace OptiTunes.Tests;

/// <summary>
/// Propriétés vérifiées sur des centaines de cas tirés au hasard : le moteur doit toujours faire au moins
/// aussi bien qu'un rangement en grille simple calculé à la main, sans jamais violer une contrainte.
/// </summary>
public class CapacityPropertyTests
{
    /// <summary>Borne basse : meilleure grille simple (pose à plat, rotation au sol) × niveaux possibles.</summary>
    private static int GridBound(Vehicle v, double l, double w, double h, int levels)
    {
        var layers = Math.Max(1, Math.Min(levels, (int)Math.Floor(v.Height / h + 1e-9)));
        var a = (int)Math.Floor(v.Length / l + 1e-9) * (int)Math.Floor(v.Width / w + 1e-9);
        var b = (int)Math.Floor(v.Length / w + 1e-9) * (int)Math.Floor(v.Width / l + 1e-9);
        return Math.Max(a, b) * layers;
    }

    [Fact]
    public void Homogeneous_boxes_reach_at_least_the_simple_grid()
    {
        var failures = new List<string>();
        for (var seed = 1; seed <= 200; seed++)
        {
            var rnd = new Random(seed);
            var g = SelfTestSuite.NewGroupage($"H{seed}", rnd.Next(2000, 13601), rnd.Next(1500, 2451), rnd.Next(1500, 2801), 1_000_000);
            var l = rnd.Next(200, 1601);
            var w = rnd.Next(200, 1301);
            var h = rnd.Next(200, 1401);
            var stackable = rnd.NextDouble() < 0.6;
            var levels = stackable ? rnd.Next(2, 6) : 1;
            if (h > g.Vehicle.Height || Math.Min(l, w) > g.Vehicle.Width)
            {
                continue;
            }

            var bound = GridBound(g.Vehicle, l, w, h, levels);
            g.Orders.Add(SelfTestSuite.Box("B", l, w, h, 10, bound + 5, stackable, levels, 10_000));

            var plan = new LoadOptimizer().Optimize(g);
            var report = PlanValidator.Validate(plan);
            if (!report.IsValid)
            {
                failures.Add($"#{seed} invalide : {report.AllErrors.First()}");
            }
            else if (plan.Metrics.ItemsPlaced < bound)
            {
                failures.Add($"#{seed} {l}×{w}×{h} niv {levels} dans {g.Vehicle} : {plan.Metrics.ItemsPlaced} < grille {bound}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Lying_tubes_reach_at_least_the_best_of_grid_and_stagger()
    {
        var failures = new List<string>();
        for (var seed = 1; seed <= 150; seed++)
        {
            var rnd = new Random(seed);
            var v = SelfTestSuite.NewGroupage($"T{seed}", 13600, rnd.Next(1500, 2451), rnd.Next(1000, 2701), 1_000_000);
            var d = rnd.Next(40, 401);
            var length = rnd.Next(2000, 13601);
            var levels = rnd.Next(2, 12);
            var vehicle = v.Vehicle;
            var perRow = (int)Math.Floor(vehicle.Width / d);
            var grid = perRow * Math.Min(levels, (int)Math.Floor(vehicle.Height / d)) * (int)Math.Floor(vehicle.Length / length);
            var order = new TransportOrder
            {
                Id = "T", Article = "TUBE", Type = PhysicalType.Tube, Quantity = grid + 10, Length = length, Diameter = d,
                UnitWeight = 5, Stackable = true, MaxLevels = levels, MaxLoadOnTop = 10_000, Orientations = OrientationSet.O1
            };
            v.Orders.Add(order);
            if (grid == 0)
            {
                continue;
            }

            var plan = new LoadOptimizer().Optimize(v);
            var report = PlanValidator.Validate(plan);
            if (!report.IsValid)
            {
                failures.Add($"#{seed} invalide : {report.AllErrors.First()}");
            }
            else if (plan.Metrics.ItemsPlaced < grid)
            {
                failures.Add($"#{seed} Ø{d} L{length} niv {levels} dans {vehicle} : {plan.Metrics.ItemsPlaced} < grille {grid}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Plaques_are_split_into_piles_that_respect_height_and_pile_limit()
    {
        for (var seed = 1; seed <= 100; seed++)
        {
            var rnd = new Random(seed);
            var g = SelfTestSuite.NewGroupage($"P{seed}", 13600, 2450, rnd.Next(1500, 2701), 1_000_000);
            var thickness = rnd.Next(2, 15);
            var maxPile = rnd.NextDouble() < 0.5 ? rnd.Next(20, 400) : (int?)null;
            var order = new TransportOrder
            {
                Id = "P", Article = "PLAQUE", Type = PhysicalType.Plaque, Quantity = rnd.Next(1, 4000), Length = rnd.Next(500, 2500),
                Width = rnd.Next(400, 1600), Height = thickness, UnitWeight = 1.5, Stackable = rnd.NextDouble() < 0.5, MaxLevels = 3,
                MaxLoadOnTop = 5000, MaxPerPile = maxPile
            };
            g.Orders.Add(order);

            var units = UnitBuilder.Build(g, new PackingOptions()).Units;
            var perHeight = (int)Math.Floor(g.Vehicle.Height / thickness);
            var limit = maxPile is { } m ? Math.Min(m, perHeight) : perHeight;

            Assert.Equal(order.Quantity, units.Sum(u => u.ItemCount));
            Assert.All(units, u => Assert.True(u.ItemCount <= limit && u.C <= g.Vehicle.Height + 1e-6));
            Assert.Equal((int)Math.Ceiling(order.Quantity / (double)limit), units.Count);
        }
    }

    [Theory]
    [InlineData("12,5", "8", "1,2", "UNITE_DIMENSION")]
    [InlineData("1200", "800", "1000", null)]
    public void Dimensions_in_cm_are_flagged(string l, string w, string h, string? expectedCode)
    {
        var text = $"GRP;G;;;SEMI;13600;2450;2700;24000;2450;2650\nOT;O1;CDE;C;1;;;0;ART;;BOX;1;{l};{w};{h};;50;N;;;;;";
        var result = new FlatFileImporter().ImportText(text);
        if (expectedCode == null)
        {
            Assert.DoesNotContain(result.Issues, i => i.Code == "UNITE_DIMENSION");
        }
        else
        {
            Assert.Contains(result.Issues, i => i.Code == expectedCode);
        }
    }

    [Fact]
    public void Absurd_weight_is_flagged()
    {
        var text = "GRP;G;;;SEMI;13600;2450;2700;24000;2450;2650\nOT;O1;CDE;C;1;;;0;ART;;BOX;1;100;100;100;;50;N;;;;;";
        Assert.Contains(new FlatFileImporter().ImportText(text).Issues, i => i.Code == "DENSITE_ELEVEE");
    }

    [Fact]
    public void Payload_entered_in_tonnes_is_flagged()
    {
        var text = "GRP;G;;;SEMI;13600;2450;2700;24;2450;2650";
        Assert.Contains(new FlatFileImporter().ImportText(text).Issues, i => i.Code == "VEH_HORS_NORME");
    }
}
