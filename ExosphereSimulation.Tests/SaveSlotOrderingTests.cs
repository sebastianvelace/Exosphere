namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Persistence;

public sealed class SaveSlotOrderingTests
{
    [Fact]
    public void SelectMostRecent_UsesSerializedTimestampInsteadOfFileName()
    {
        DateTimeOffset older = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset newer = older.AddMinutes(3);

        string? selected = SaveSlotOrdering.SelectMostRecent(
        [
            new SaveSlotMetadata("z-last-alphabetically", older),
            new SaveSlotMetadata("a-newest-save", newer),
        ]);

        Assert.Equal("a-newest-save", selected);
    }

    [Fact]
    public void SelectMostRecent_UsesOrdinalNameTieBreakAndSkipsEmptyNames()
    {
        DateTimeOffset timestamp = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

        string? selected = SaveSlotOrdering.SelectMostRecent(
        [
            new SaveSlotMetadata("", timestamp.AddDays(1)),
            new SaveSlotMetadata("beta", timestamp),
            new SaveSlotMetadata("alpha", timestamp),
        ]);

        Assert.Equal("alpha", selected);
    }

    [Fact]
    public void SelectMostRecent_ReturnsNullForEmptyOrCorruptFilteredDirectory()
    {
        string? selected = SaveSlotOrdering.SelectMostRecent(
        [
            new SaveSlotMetadata("", DateTimeOffset.UtcNow),
            new SaveSlotMetadata("   ", DateTimeOffset.UtcNow),
        ]);

        Assert.Null(selected);
    }
}
