using ClothingStore.Core;

namespace ClothingStore.Tests;

public class ZoomStepsTests
{
    [Theory]
    [InlineData(1920, 1.5)]
    [InlineData(1600, 1.5)]
    [InlineData(1440, 1.4)]
    [InlineData(1366, 1.25)]
    [InlineData(1280, 1.25)]
    [InlineData(1024, 1.0)]
    [InlineData(911, 1.0)] // 1366 x 768 with Windows at 150%: never more than 100%, but 90% stays possible
    public void A_screen_offers_the_zoom_that_still_leaves_the_design_width(double screen, double max) =>
        Assert.Equal(max, ZoomSteps.MaxFor(screen));

    [Fact]
    public void Steps_go_up_and_down_within_the_limits()
    {
        Assert.Equal(1.1, ZoomSteps.Larger(1.0, 1.5));
        Assert.Equal(1.25, ZoomSteps.Larger(1.25, 1.25)); // the screen's maximum: stays
        Assert.Equal(0.9, ZoomSteps.Smaller(1.0));
        Assert.Equal(0.9, ZoomSteps.Smaller(0.9));        // smallest: stays
        Assert.Equal(1.25, ZoomSteps.Smaller(1.4));
    }

    [Fact]
    public void A_saved_zoom_is_snapped_to_a_step_the_screen_allows()
    {
        Assert.Equal(1.25, ZoomSteps.Clamp(1.5, 1.25)); // remembered on a bigger screen
        Assert.Equal(1.1, ZoomSteps.Clamp(1.12, 1.5));
        Assert.Equal(1.0, ZoomSteps.Clamp(0, 1.5));
        Assert.Equal(1.0, ZoomSteps.Clamp(double.NaN, 1.5));
    }
}
