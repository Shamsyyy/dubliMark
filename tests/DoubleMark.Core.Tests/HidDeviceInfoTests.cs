using DoubleMark.Desktop.Services;
using FluentAssertions;

namespace DoubleMark.Core.Tests;

public class HidDeviceInfoTests
{
    private const string PathA =
        @"\\?\HID#VID_1A86&PID_5456&MI_00#7&1563a5fe&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}";

    private const string PathB =
        @"\\?\HID#VID_1A86&PID_5456&MI_01#8&2b9f1c11&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}";

    [Fact]
    public void MatchesConfiguredDevice_accepts_same_vid_pid_different_instance()
    {
        HidDeviceInfo.MatchesConfiguredDevice(PathB, PathA).Should().BeTrue();
    }

    [Fact]
    public void MatchesConfiguredDevice_rejects_different_vid_pid()
    {
        var other = @"\\?\HID#VID_046D&PID_C52B&MI_00#1&2d3e4f5&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}";
        HidDeviceInfo.MatchesConfiguredDevice(other, PathA).Should().BeFalse();
    }
}
