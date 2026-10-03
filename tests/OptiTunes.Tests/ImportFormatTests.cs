using OptiTunes.Core.Engine;
using OptiTunes.Core.Import;
using OptiTunes.Core.Models;
using OptiTunes.Core.Validation;

namespace OptiTunes.Tests;

public class ImportFormatTests
{
    [Fact]
    public void Tabular_export_groups_rows_by_groupage_and_reads_vehicle_columns()
    {
        var text = """
            ID_GROUPAGE;LONGUEUR_UTILE;LARGEUR_UTILE;HAUTEUR_UTILE;CHARGE_UTILE;ID_ORDRE;ARTICLE;TYPE_PHYSIQUE;QUANTITE;LONGUEUR;LARGEUR;HAUTEUR;POIDS_UNITAIRE;GERBABLE;NIVEAUX_MAX
            G1;13600;2450;2700;24000;OT1;PAL-A;PALETTE;4;1200;800;1200;300;O;1
            G1;13600;2450;2700;24000;OT2;PAL-B;PALETTE;2;1200;1000;1000;250;N;
            G2;7200;2450;2400;9000;OT3;CARTON;BOX;10;600;400;400;8;O;3
            """;
        var result = new FlatFileImporter().ImportText(text);

        Assert.Contains("tableau", result.Format);
        Assert.Equal(2, result.Groupages.Count);
        Assert.Equal(2, result.Groupages[0].Orders.Count);
        Assert.Equal(7200, result.Groupages[1].Vehicle.Length);
        Assert.Equal(2, result.Groupages[0].Orders[0].MaxLevels);
        Assert.All(result.Groupages, g => Assert.True(PlanValidator.Validate(new LoadOptimizer().Optimize(g)).IsValid));
        Assert.Contains(result.Columns, c => c.FileColumn == "QUANTITE" && c.Field == "QUANTITE");
    }

    [Fact]
    public void Tabular_export_without_vehicle_asks_for_a_vehicle_choice()
    {
        var text = "ORDRE;ARTICLE;FAMILLE;QTE;L;LARGEUR;H;POIDS;GERBABLE\nOT1;PAL;PALETTE;3;1200;800;1000;250;N";
        var result = new FlatFileImporter().ImportText(text);
        var g = Assert.Single(result.Groupages);

        Assert.False(g.IsComputable);
        Assert.Contains(result.Issues, i => i.Code == "VEH_ABSENT");

        var plan = new LoadOptimizer().Optimize(g.WithVehicle(VehicleCatalog.Presets[0]));
        Assert.Equal(3, plan.Metrics.ItemsPlaced);
    }

    [Theory]
    [InlineData("1200mm", 1200)]
    [InlineData("120 cm", 1200)]
    [InlineData("1,2 m", 1200)]
    [InlineData("1,5t", 1500)]
    [InlineData("25 kg", 25)]
    [InlineData("1 200", 1200)]
    [InlineData("12,5", 12.5)]
    public void Numbers_accept_units_and_french_formats(string text, double expected) =>
        Assert.Equal(expected, FlatFileImporter.ParseNumber(text)!.Value, 6);

    [Fact]
    public void English_headers_are_recognised()
    {
        var text = """
            #GRP;LOAD_ID;TRUCK;INNER_LENGTH;INNER_WIDTH;INNER_HEIGHT;PAYLOAD
            GRP;L1;SEMI;13600;2450;2700;24000
            #OT;ORDER_ID;SKU;PACKAGING;QTY;LENGTH;WIDTH;HEIGHT;WEIGHT;STACKABLE;STACK_LEVELS
            OT;A;BOX-1;BOX;5;600;400;400;10;Y;2
            """;
        var g = new FlatFileImporter().ImportText(text).Groupages[0];
        Assert.Equal(24000, g.Vehicle.MaxPayload);
        Assert.Equal(5, g.Orders[0].Quantity);
        Assert.Equal(3, g.Orders[0].MaxLevels);
    }

    [Fact]
    public void Spreadsheet_files_are_refused_with_a_clear_message()
    {
        var path = Path.Combine(Path.GetTempPath(), $"optitunes_{Guid.NewGuid():N}.xlsx");
        File.WriteAllBytes(path, [0x50, 0x4B, 0x03, 0x04]);
        try
        {
            var result = new FlatFileImporter().ImportFile(path);
            Assert.Contains(result.Issues, i => i.Code == "FORMAT_CLASSEUR");
            Assert.Empty(result.Groupages);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Report_keeps_raw_lines_and_column_mapping()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Samples", "groupage_3_complet.csv");
        var result = new FlatFileImporter().ImportFile(path);

        Assert.Contains("GRP / OT", result.Format);
        Assert.StartsWith("GRP;", result.RawLine(2));
        Assert.Contains(result.Columns, c => c.Record == "OT" && c.FileColumn == "NIVEAUX_MAX");
        Assert.DoesNotContain(result.Columns, c => c.Field == null);
    }
}
