using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LifxNet;

namespace SampleApp.NET462
{
    class Program
    {
        static LifxNet.LifxClient client;
        static async Task Main(string[] args)
        {
            var task = LifxNet.LifxClient.CreateAsync();
            task.Wait();
            client = task.Result;
            client.DeviceDiscovered += Client_DeviceDiscovered;
            client.DeviceLost += Client_DeviceLost;
            await client.DoInitialDeviceDiscovery();
            await Task.Delay(1000);
            foreach (var bulb in client.Devices.OfType<LifxNet.LightBulb>()) {
                await client.SetLightPowerAsync(bulb, TimeSpan.FromMilliseconds(0), true);
                await SetColorRed(bulb);
            }
            
            await Task.Delay(1000);
            await client.RefreshDevicesAsync();

            Console.Read();
        }

        private static void Client_DeviceLost(object sender, LifxClient.DeviceDiscoveryEventArgs e)
        {
            Console.WriteLine("Device lost");
        }

        private static async void Client_DeviceDiscovered(object sender, LifxClient.DeviceDiscoveryEventArgs e)
        {
            Console.WriteLine($"Device {e.Device.MacAddressName} found @ {e.Device.HostName}");
            var version = await client.GetDeviceVersionAsync(e.Device);
            //var label = await client.GetDeviceLabelAsync(e.Device);
            var state = await client.GetLightStateAsync(e.Device as LightBulb);
            Console.WriteLine($"{state.Label}\n\tIs on: {state.IsOn}\n\tHue: {state.Hue}\n\tSaturation: {state.Saturation}\n\tBrightness: {state.Brightness}\n\tTemperature: {state.Kelvin}");
        }

        private async static Task SetColorRed(LifxNet.LightBulb bulb)
        {
            LifxNet.Color color = new LifxNet.Color() { R = 255, G = 0, B = 0 };
            await client.SetColorAsync(bulb, color, 3500, TimeSpan.FromMilliseconds(500));
        }
    }
}
