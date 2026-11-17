using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4CompositionTimeEntryTests
{
    [Fact]
    public void Constructor_ShouldStoreSampleCountAndOffset()
    {
        // Arrange
        const uint sampleCount = 5;
        const int sampleOffset = 100;

        // Act
        Mp4CompositionTimeEntry entry =
            new(sampleCount, sampleOffset);

        // Assert
        Assert.Equal(sampleCount, entry.SampleCount);
        Assert.Equal(sampleOffset, entry.SampleOffset);
    }

    [Fact]
    public void Constructor_ShouldSupportNegativeCompositionOffset()
    {
        // Arrange
        const int sampleOffset = -50;

        // Act
        Mp4CompositionTimeEntry entry =
            new(3, sampleOffset);

        // Assert
        Assert.Equal(-50, entry.SampleOffset);
    }

    [Fact]
    public void Constructor_ShouldSupportZeroCompositionOffset()
    {
        // Act
        Mp4CompositionTimeEntry entry = new(1, 0);

        // Assert
        Assert.Equal(1U, entry.SampleCount);
        Assert.Equal(0, entry.SampleOffset);
    }

    [Fact]
    public void Constructor_ShouldSupportMaximumPositiveOffset()
    {
        // Act
        Mp4CompositionTimeEntry entry =
            new(1, int.MaxValue);

        // Assert
        Assert.Equal(int.MaxValue, entry.SampleOffset);
    }

    [Fact]
    public void Constructor_ShouldSupportMinimumNegativeOffset()
    {
        // Act
        Mp4CompositionTimeEntry entry =
            new(1, int.MinValue);

        // Assert
        Assert.Equal(int.MinValue, entry.SampleOffset);
    }
}
