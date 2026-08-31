namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Presentation;

public class EngineHudLayoutTests
{
    [Fact]
    public void SuperHeavyKeepsIftThreeRingBoard()
    {
        var rings = new List<int>();
        EngineHudPresentation.FillBoardRings(33, rings);

        Assert.Equal(new[] { 20, 10, 3 }, rings);
        Assert.Equal(33, rings.Sum());
    }

    [Fact]
    public void CustomStacksAreNotClampedToThirtyThree()
    {
        var rings = new List<int>();
        EngineHudPresentation.FillBoardRings(39, rings);

        Assert.Equal(39, rings.Sum());
        Assert.True(rings.Count >= 2);
        Assert.DoesNotContain(33, rings);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(9)]
    [InlineData(13)]
    public void SmallerClustersFitOnTheBoard(int count)
    {
        var rings = new List<int>();
        EngineHudPresentation.FillBoardRings(count, rings);

        Assert.Equal(count, rings.Sum());
        Assert.NotEmpty(rings);
    }

    [Fact]
    public void EmptyClusterProducesNoRings()
    {
        var rings = new List<int> { 99 };
        EngineHudPresentation.FillBoardRings(0, rings);
        Assert.Empty(rings);
    }
}
