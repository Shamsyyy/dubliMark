using DoubleMark.Desktop.Services;
using DoubleMark.Desktop.Settings;
using FluentAssertions;

namespace DoubleMark.Core.Tests;

public class ScannerConnectionAdvisorTests
{
    [Fact]
    public void Apply_does_not_change_manual_Hid_mode_to_Com()
    {
        var settings = new AppSettings
        {
            ScannerMode = ScannerMode.Hid,
            SelectedHidDeviceId = @"\\?\HID#VID_FFFF&PID_0000&MI_00#1&0&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}"
        };

        ScannerConnectionAdvisor.Apply(settings, persist: false);

        settings.ScannerMode.Should().Be(ScannerMode.Hid);
    }

    [Fact]
    public void Apply_keeps_Auto_mode()
    {
        var settings = new AppSettings { ScannerMode = ScannerMode.Auto };
        ScannerConnectionAdvisor.Apply(settings, persist: false);
        settings.ScannerMode.Should().Be(ScannerMode.Auto);
    }

    [Fact]
    public void ResolveHidListen_uses_all_keyboards_when_saved_path_missing()
    {
        ScannerSourceFactory.ResetHidBindingSession();
        var settings = new AppSettings
        {
            ScannerMode = ScannerMode.Hid,
            SelectedHidDeviceId = @"\\?\HID#VID_DEAD&PID_BEEF&MI_00#1&0&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}"
        };

        var (path, listenAll) = ScannerSourceFactory.ResolveHidListen(settings, filterByDevice: true);

        path.Should().BeNull();
        listenAll.Should().BeTrue();
    }

    [Fact]
    public void ResolveHidListen_uses_all_keyboards_in_auto_until_bound()
    {
        ScannerSourceFactory.ResetHidBindingSession();
        var settings = new AppSettings
        {
            ScannerMode = ScannerMode.Auto,
            ScannerAutoBindHid = true,
            SelectedHidDeviceId =
                @"\\?\HID#VID_1A86&PID_5723&MI_00#7&abc&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}"
        };

        var (path, listenAll) = ScannerSourceFactory.ResolveHidListen(settings, filterByDevice: true);

        path.Should().BeNull();
        listenAll.Should().BeTrue();
    }

    [Fact]
    public void ResolveHidListen_after_bind_flag_does_not_force_listen_all()
    {
        ScannerSourceFactory.HidListenAllUntilBound = false;
        var path =
            @"\\?\HID#VID_1A86&PID_5723&MI_00#7&abc&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}";
        var settings = new AppSettings
        {
            ScannerMode = ScannerMode.Auto,
            ScannerAutoBindHid = true,
            SelectedHidDeviceId = path
        };

        var (attachPath, listenAll) = ScannerSourceFactory.ResolveHidListen(settings, filterByDevice: true);

        if (ScannerConnectionAdvisor.IsConfiguredHidPresent(settings))
        {
            listenAll.Should().BeFalse();
            attachPath.Should().Be(path);
        }
        else
        {
            listenAll.Should().BeTrue();
            attachPath.Should().BeNull();
        }
    }
}
