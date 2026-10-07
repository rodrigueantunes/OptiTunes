using System.Globalization;
using OptiTunes.Core.Engine;
using OptiTunes.Core.Export;
using OptiTunes.Core.Import;
using OptiTunes.Core.Models;
using OptiTunes.Core.Validation;

namespace OptiTunes.Tests;

public class CalculationDetailsTests
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private static Groupage Sample(string file) =>
        new FlatFileImporter().ImportFile(Path.Combine(AppContext.BaseDirectory, "Samples", file)).Groupages[0];

    public static TheoryData<string> Samples()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Samples"), "*.csv"))
        {
            data.Add(Path.GetFileName(f));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Every_sample_and_stowage_mode_gives_a_complete_readable_detail(string file)
    {
        var g = Sample(file);
        foreach (var mode in Enum.GetValues<StowageMode>())
        {
            foreach (var trucks in new[] { 1, 3 })
            {
                var options = new PackingOptions { Stowage = mode, MaxVehicles = trucks, GapBetweenUnits = trucks == 3 ? 30 : 0 };
                var solutions = new LoadOptimizer().OptimizeSolutions(g, options);
                var plan = solutions[0];
                var sections = CalculationDetails.Build(plan, PlanValidator.Validate(plan), solutions);

                Assert.Equal("En résumé", sections[0].Title);
                Assert.Equal("Lexique", sections[^1].Title);
                Assert.Equal(Enumerable.Range(1, sections.Count - 1), sections.Skip(1).Select(s => s.Number));
                Assert.Equal(plan.Loads.Count, sections.Count(s => s.Title.StartsWith("Camion ")));
                Assert.All(sections, s => Assert.NotEmpty(s.Lines));

                var text = CalculationDetails.ToText(sections);
                Assert.DoesNotContain("NaN", text);
                Assert.DoesNotContain("∞", text);
                Assert.DoesNotContain("-1 kg", text);
            }
        }
    }

    [Fact]
    public void Figures_match_the_plan()
    {
        var g = Sample("groupage_itineraire.csv");
        var plan = new LoadOptimizer().Optimize(g, new PackingOptions { Stowage = Stowage.DefaultFor(g) });
        var sections = CalculationDetails.Build(plan);
        string Section(string title) => string.Join("\n", sections.Single(s => s.Title.StartsWith(title)).Lines.Select(l => l.Text)).Replace('\u00A0', ' ').Replace('\u202F', ' ');
        string F2(double v) => v.ToString("#,0.00", Fr).Replace('\u00A0', ' ').Replace('\u202F', ' ');

        // 8 × 117 + 6 × 180 + 10 × 95 + 4 × 420 = 4 646 kg
        Assert.Contains("8 × 117 + 6 × 180 + 10 × 95 + 4 × 420 = 4 646 kg", Section("Les ordres"));
        Assert.Contains($"= {F2(plan.Metrics.LinearMetersRequiredEquivalent)} m", Section("Besoin en métrage"));
        Assert.Contains("⌈8 unité(s) / 2 niveau(x)⌉ = 4", Section("Besoin en métrage"));
        Assert.Contains("1 + ⌊400 / 117⌋ = 4) = 2", Section("Niveaux"));

        var truck = Section("Camion 1");
        var m = plan.Loads[0].Metrics;
        Assert.Contains($"= {F2(m.LinearMetersReal)} m", truck);
        Assert.Contains($"Ce qui limite ce camion : {m.LimitingFactor}", truck);
        Assert.Contains("Étape 1 : chargement CPF", Section("Ordre de chargement"));
    }

    [Fact]
    public void Linear_meter_section_has_one_foldable_block_per_order_plus_totals()
    {
        var g = Sample("groupage_itineraire.csv");
        var section = CalculationDetails.Build(new LoadOptimizer().Optimize(g)).Single(s => s.Title.StartsWith("Besoin en métrage"));

        Assert.Equal(g.Orders.Select(o => o.Id).Append("Totaux"), section.Blocks.Select(b => b.Title.Split(' ')[0] == "Ordre" ? b.Title.Split(' ')[1] : b.Title.Split(' ')[0]));
        Assert.All(section.Blocks, b => Assert.NotEmpty(b.Lines));
        Assert.Equal(section.Lines.Count, section.Intro.Count + section.Blocks.Count + section.Blocks.Sum(b => b.Lines.Count));
    }

    [Fact]
    public void Recap_lists_every_operation_numbered_with_the_groupage_values()
    {
        var g = Sample("groupage_itineraire.csv");
        var plan = new LoadOptimizer().Optimize(g, new PackingOptions { Stowage = Stowage.DefaultFor(g) });
        var sections = CalculationDetails.Build(plan);
        var recap = sections.Single(s => s.Title == "Récapitulatif du calcul");
        Assert.Equal("Lexique", sections[sections.IndexOf(recap) + 1].Title);

        var ops = recap.Lines.Where(l => l.Kind != DetailKind.Heading).Select(l => l.Text).ToList();
        Assert.Equal(Enumerable.Range(1, ops.Count).Select(i => $"{i}."), ops.Select(t => t.Split(' ')[0]));
        var text = string.Join("\n", ops).Replace('\u00A0', ' ').Replace('\u202F', ' ');
        Assert.Contains("Largeur chargeable : 2 450 − 2 × 0 = 2 450 mm.", text);
        Assert.Contains("8 × 117 + 6 × 180 + 10 × 95 + 4 × 420 = 4 646 kg", text);
        Assert.Contains("Niveaux par pile : le plus petit de 2 (fichier), 2 (hauteur : 2 700 ÷ 1 300 = 2,08 → 2), 4 (charge : 400 ÷ 117 = 3,42 → 3 dessus, + 1 au sol) → 2.", text);
        Assert.Contains("Rangées : 2 pile(s) de front → 3 ÷ 2 = 1,5 → 2 rangée(s) (arrondi au-dessus).", text);
        Assert.Contains($"Articles chargés : {plan.Metrics.ItemsPlaced} sur {plan.Metrics.ItemsTotal}.", text);
        Assert.Equal(g.Orders.Select(o => $"Ordre {o.Id}"), recap.Blocks.Select(b => b.Title).Where(t => t.StartsWith("Ordre ")));
    }

    [Fact]
    public void Plates_and_staggered_tubes_are_explained()
    {
        var plates = CalculationDetails.ToText(CalculationDetails.Build(new LoadOptimizer().Optimize(Sample("groupage_plaques_tubes.csv"))));
        Assert.Contains("plaques par pile = ⌊hauteur disponible / épaisseur⌋", plates);
        Assert.Contains("écarté avant le placement", plates);

        var tubes = CalculationDetails.ToText(CalculationDetails.Build(new LoadOptimizer().Optimize(Sample("groupage_tubes_quinconce.csv"))));
        Assert.Contains("en quinconce ; pas vertical = 0,866 × 200", tubes);
    }

    [Fact]
    public void Reliquat_is_explained_with_its_reason()
    {
        var g = Sample("groupage_itineraire.csv");
        var small = VehicleCatalog.Copy(g.Vehicle);
        small.Length = 4000;
        var plan = new LoadOptimizer().Optimize(g.WithVehicle(small), new PackingOptions { Stowage = StowageMode.Stops });
        Assert.True(plan.Metrics.ItemsRemaining > 0);

        var reliquat = CalculationDetails.Build(plan).Single(s => s.Title == "Reliquat");
        Assert.Contains(reliquat.Lines, l => l.Kind == DetailKind.Warning);
        Assert.Contains(reliquat.Lines, l => l.Text.Contains("Ajouter des camions si reliquat"));
        Assert.Equal(plan.Unloaded.Sum(u => u.Unit.ItemCount), reliquat.Table!.Rows.Sum(r => int.Parse(r[2], Fr)));
    }
}
