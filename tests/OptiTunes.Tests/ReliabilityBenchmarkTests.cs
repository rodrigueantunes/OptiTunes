using System.Text.Json;
using OptiTunes.Core.Engine;
using OptiTunes.Core.Models;
using OptiTunes.Core.Validation;

namespace OptiTunes.Tests;

/// <summary>
/// Banc de fiabilité : groupages variés (palettes, non gerbables, formats longs, aléatoires, lourds, plusieurs camions)
/// dont l'optimum est connu (prouvé par un solveur exact, ou égal à la borne poids / surface).
/// Le moteur doit rester conforme, ne jamais faire moins bien que le résultat enregistré, et garder au moins autant
/// de cas à l'optimum.
/// </summary>
public class ReliabilityBenchmarkTests
{
    private sealed record Case(string Id, int MaxVehicles, VehicleData Vehicle, List<OrderData> Orders, Optimum Optimum, Result Optitunes);
    private sealed record VehicleData(int L, int W, int H, double Payload);
    private sealed record OrderData(string Id, double L, double W, double H, int Qty, double Weight, bool? Stackable, int? Levels, double? MaxLoad);
    private sealed record Optimum(double? Ml, int? Trucks);
    private sealed record Result(int Placed, int Trucks, double Ml);

    private static readonly List<Case> Cases = JsonSerializer.Deserialize<List<Case>>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "fiabilite.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private static (LoadPlan Plan, Result Result) Run(Case c)
    {
        var g = SelfTestSuite.NewGroupage(c.Id, c.Vehicle.L, c.Vehicle.W, c.Vehicle.H, c.Vehicle.Payload);
        foreach (var o in c.Orders)
        {
            g.Orders.Add(SelfTestSuite.Box(o.Id, o.L, o.W, o.H, o.Weight, o.Qty, o.Stackable == true, o.Levels, o.MaxLoad));
        }

        var plan = new LoadOptimizer().Optimize(g, new PackingOptions { MaxVehicles = c.MaxVehicles });
        return (plan, new Result(plan.Metrics.ItemsPlaced, plan.Loads.Count(l => l.Placements.Count > 0),
            Math.Round(plan.Metrics.LinearMetersReal * 1000)));
    }

    [Fact]
    public void Benchmark_contains_single_and_multi_truck_cases()
    {
        Assert.True(Cases.Count(c => c.MaxVehicles == 1) >= 60);
        Assert.True(Cases.Count(c => c.MaxVehicles > 1) >= 40);
    }

    [Fact]
    public void Every_plan_is_valid_and_never_worse_than_recorded()
    {
        var failures = new List<string>();
        var atOptimum = 0;
        var recordedAtOptimum = 0;
        foreach (var c in Cases)
        {
            var (plan, r) = Run(c);
            var report = PlanValidator.Validate(plan);
            if (!report.IsValid)
            {
                failures.Add($"{c.Id} : plan non conforme – {report.AllErrors.First()}");
                continue;
            }

            if (r.Placed < c.Optitunes.Placed || r.Trucks > c.Optitunes.Trucks ||
                (r.Placed == c.Optitunes.Placed && r.Trucks == c.Optitunes.Trucks && r.Ml > c.Optitunes.Ml + 1))
            {
                failures.Add($"{c.Id} : {r} moins bon que l'enregistré {c.Optitunes}");
            }

            bool AtOptimum(Result x) => c.Optimum.Trucks is { } t ? x.Trucks <= t : x.Ml <= c.Optimum.Ml!.Value + 5;
            atOptimum += AtOptimum(r) ? 1 : 0;
            recordedAtOptimum += AtOptimum(c.Optitunes) ? 1 : 0;
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.True(atOptimum >= recordedAtOptimum, $"{atOptimum} cas à l'optimum, {recordedAtOptimum} attendus");
    }
}
