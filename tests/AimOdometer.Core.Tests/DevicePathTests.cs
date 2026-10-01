using AimOdometer.Core.Input;

namespace AimOdometer.Core.Tests;

public class DevicePathTests
{
    private const string UsbMouse =
        @"\?\HID#VID_046D&PID_C547&MI_02&Col01#8&2a4b1c3&0&0000#{378de44c-56ef-11d1-bc8c-00a0c91405dd}";

    [Fact]
    public void Usb_ParsesVendorAndProduct()
    {
        var path = DevicePath.Parse(UsbMouse);
        Assert.Equal(0x046D, path.VendorId);
        Assert.Equal(0xC547, path.ProductId);
    }

    [Fact]
    public void StableKey_DropsCollectionAndInstance()
    {
        var path = DevicePath.Parse(UsbMouse);
        Assert.Equal("HID#VID_046D&PID_C547&MI_02", path.StableKey);
    }

    [Fact]
    public void StableKey_IsSameAfterReplugIntoAnotherPort()
    {
        var other = DevicePath.Parse(
            @"\?\HID#VID_046D&PID_C547&MI_02&Col01#8&99ffee1&0&0000#{378de44c-56ef-11d1-bc8c-00a0c91405dd}");
        Assert.Equal(DevicePath.Parse(UsbMouse).StableKey, other.StableKey);
    }

    [Fact]
    public void UniqueKey_DiffersForTwoIdenticalMice()
    {
        var a = DevicePath.Parse(UsbMouse);
        var b = DevicePath.Parse(
            @"\?\HID#VID_046D&PID_C547&MI_02&Col01#8&99ffee1&0&0000#{378de44c-56ef-11d1-bc8c-00a0c91405dd}");
        Assert.NotEqual(a.UniqueKey, b.UniqueKey);
    }

    [Fact]
    public void BluetoothClassic_ParsesIdsAfterVendorSourcePrefix()
    {
        var path = DevicePath.Parse(
            @"\?\HID#{00001124-0000-1000-8000-00805f9b34fb}_VID&0002046d_PID&b01a&Col01#8&1d2e3f&0&0000#{378de44c-56ef-11d1-bc8c-00a0c91405dd}");
        Assert.Equal(0x046D, path.VendorId);
        Assert.Equal(0xB01A, path.ProductId);
    }

    [Fact]
    public void TouchpadCollections_ShareSiblingKey()
    {
        var mouse = DevicePath.Parse(@"\?\HID#VEN_ELAN&DEV_0732&Col01#4&2d8a3c1b&0&0000#{378de44c-56ef-11d1-bc8c-00a0c91405dd}");
        var touchpad = DevicePath.Parse(@"\?\HID#VEN_ELAN&DEV_0732&Col02#4&2d8a3c1b&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}");
        Assert.Equal(mouse.SiblingKey, touchpad.SiblingKey);
        Assert.Null(mouse.VendorId);
    }

    [Fact]
    public void Rdp_DoesNotThrow()
    {
        var path = DevicePath.Parse(@"\?\Root#RDP_MOU#0000#{378de44c-56ef-11d1-bc8c-00a0c91405dd}");
        Assert.Equal("ROOT#RDP_MOU", path.StableKey);
    }
}
