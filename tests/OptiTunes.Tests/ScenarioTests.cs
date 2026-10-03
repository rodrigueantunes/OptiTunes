using OptiTunes.Core.Validation;

namespace OptiTunes.Tests;

public class ScenarioTests
{
    public static TheoryData<string> ScenarioNames()
    {
        var data = new TheoryData<string>();
        foreach (var s in SelfTestSuite.Scenarios)
        {
            data.Add(s.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void Scenario_respects_expected_result(string name)
    {
        var scenario = SelfTestSuite.Scenarios.Single(s => s.Name == name);
        var result = SelfTestSuite.Run(scenario);
        Assert.True(result.Passed, $"{scenario.Expected} → {result.Obtained}");
    }

    [Fact]
    public void Random_groupages_never_violate_a_mandatory_constraint()
    {
        var failures = Enumerable.Range(1, 300)
            .Select(SelfTestSuite.RunFuzz)
            .Where(r => !r.Passed)
            .Select(r => $"{r.Name} : {r.Obtained}")
            .ToList();

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
