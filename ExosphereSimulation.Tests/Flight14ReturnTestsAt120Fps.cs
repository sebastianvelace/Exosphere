namespace ExosphereSimulation.Tests;

using Xunit;
using Xunit.Abstractions;

public sealed class Flight14ReturnTestsAt120Fps(ITestOutputHelper output)
{
    [Fact]
    public void LoadedPadToEntryConsumesTheSameReserveWithoutReseeding() =>
        Flight14ReturnTests.VerifyLoadedPadToEntry(120, output);
}
