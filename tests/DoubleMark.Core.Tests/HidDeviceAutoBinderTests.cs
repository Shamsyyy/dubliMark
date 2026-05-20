using DoubleMark.Desktop.Services;
using DoubleMark.Desktop.Settings;
using FluentAssertions;

namespace DoubleMark.Core.Tests;

public class HidDeviceAutoBinderTests
{
    private const string ScannerPath =
        @"\\?\HID#VID_1A86&PID_5723&MI_00#7&abc&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}";

    private const string OtherPath =
        @"\\?\HID#VID_046D&PID_C52B&MI_00#1&0&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}";

    [Fact]
    public void TryBindFromLastScan_saves_device_on_fast_scan()
    {
        var scan = new RawInputScanEventArgs
        {
            Barcode = "test",
            DevicePath = ScannerPath,
            IsFastScan = true,
            AverageIntervalMs = 20
        };

        var settings = new AppSettings
        {
            ScannerMode = ScannerMode.Hid,
            SelectedHidDeviceId = OtherPath,
            ScannerAutoBindHid = true
        };

        HidDeviceAutoBinder.TryBind(settings, scan, out _).Should().BeTrue();
        settings.EffectiveHidDevicePath.Should().Be(ScannerPath);
    }

    [Fact]
    public void TryBindFromLastScan_skips_slow_keyboard_input()
    {
        var scan = new RawInputScanEventArgs
        {
            DevicePath = ScannerPath,
            IsFastScan = false
        };

        var settings = new AppSettings { ScannerMode = ScannerMode.Hid, ScannerAutoBindHid = true };

        HidDeviceAutoBinder.TryBind(settings, scan, out _).Should().BeFalse();
    }
}
