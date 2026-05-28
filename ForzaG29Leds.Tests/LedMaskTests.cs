using Xunit;

namespace ForzaG29Leds.Tests;

public class LedMaskTests
{
    // Each LED lights when the scaled ratio crosses the ceiling boundary.
    // With 5 LEDs: boundaries are at 0, 0.2, 0.4, 0.6, 0.8.
    [Theory]
    [InlineData(0.00f, 0x00)]   // all off
    [InlineData(0.01f, 0x01)]   // LED 1
    [InlineData(0.20f, 0x01)]   // still LED 1 (exactly on boundary — ceiling(1.0)=1)
    [InlineData(0.21f, 0x03)]   // LED 2
    [InlineData(0.40f, 0x03)]   // still LED 2
    [InlineData(0.41f, 0x07)]   // LED 3
    [InlineData(0.60f, 0x07)]   // still LED 3
    [InlineData(0.61f, 0x0F)]   // LED 4
    [InlineData(0.80f, 0x0F)]   // still LED 4
    [InlineData(0.81f, 0x1F)]   // LED 5 (all on)
    [InlineData(1.00f, 0x1F)]   // all on
    public void RatioToMask_ReturnsCorrectMask(float ratio, byte expectedMask)
    {
        Assert.Equal(expectedMask, LogitechWheelLeds.RatioToMask(ratio));
    }

    [Fact]
    public void RatioToMask_ClampsAboveOne()
    {
        Assert.Equal(0x1F, LogitechWheelLeds.RatioToMask(1.5f));
        Assert.Equal(0x1F, LogitechWheelLeds.RatioToMask(99f));
    }

    [Fact]
    public void RatioToMask_ClampsNegative()
    {
        Assert.Equal(0x00, LogitechWheelLeds.RatioToMask(-0.5f));
    }

    [Fact]
    public void AllOnMask_Is0x1F()
    {
        // Verify the "all LEDs on" bit pattern
        Assert.Equal(0x1F, LogitechWheelLeds.RatioToMask(1f));
        Assert.Equal(0b00011111, LogitechWheelLeds.RatioToMask(1f));
    }
}
