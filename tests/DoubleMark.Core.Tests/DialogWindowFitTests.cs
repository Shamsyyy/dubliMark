using DoubleMark.Desktop;
using FluentAssertions;

namespace DoubleMark.Core.Tests;

public sealed class DialogWindowFitTests
{
    [Fact]
    public void CalculateFittedHeight_grows_when_content_taller_than_window()
    {
        DialogWindowChrome.CalculateFittedHeight(430, 500, 1040)
            .Should().Be(520);
    }

    [Fact]
    public void CalculateFittedHeight_keeps_current_height_when_content_fits()
    {
        DialogWindowChrome.CalculateFittedHeight(520, 400, 1040)
            .Should().Be(520);
    }

    [Fact]
    public void CalculateFittedHeight_clamps_to_work_area()
    {
        DialogWindowChrome.CalculateFittedHeight(430, 2000, 900)
            .Should().Be(900);
    }

    [Fact]
    public void CalculateFittedHeight_treats_invalid_values_as_empty()
    {
        DialogWindowChrome.CalculateFittedHeight(double.NaN, double.NaN, 0)
            .Should().Be(20);
    }
}
