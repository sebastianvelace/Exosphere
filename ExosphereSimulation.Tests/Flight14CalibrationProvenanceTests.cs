namespace ExosphereSimulation.Tests;

using System.Text.Json;
using Exosphere.Simulation.Data;
using Xunit;

public sealed class Flight14CalibrationProvenanceTests
{
    [Theory]
    [InlineData("return", "starship-flight14-return", "earliestDeorbitMissionSeconds")]
    [InlineData("return", "starship-flight14-return", "targetPeriapsisAltitudeM")]
    [InlineData("descent", "starship-flight14-descent", "downwardSpeedFraction")]
    [InlineData("descent", "starship-flight14-descent", "verticalResponseSeconds")]
    public void GuidanceCalibrationMatchesItsProvenance(string profile, string dataId, string field)
    {
        string data = Flight14EntryCheckpointFixture.DataDirectory();
        var registry = DataProvenanceRegistry.LoadFromDirectory(Path.Combine(data, "provenance"));
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "flight_profiles",
            $"starship_flight14_{profile}_estimate.json")));
        Assert.Equal<double?>(document.RootElement.GetProperty(field).GetDouble(),
            registry.Require(dataId, field).Value);
    }
}
