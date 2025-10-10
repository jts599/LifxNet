using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;

namespace LifxNet
{
	public partial class LifxClient : IDisposable
	{
		private static uint identifier = 1;
		private static object identifierLock = new object();
		private UInt32 discoverSourceID;
		private CancellationTokenSource? _DiscoverCancellationSource;
		private Dictionary<string, Device> DiscoveredBulbs = new Dictionary<string, Device>();

		private static uint GetNextIdentifier()
		{
			lock (identifierLock)
				return identifier++;
		}

		/// <summary>
		/// Event fired when a LIFX bulb is discovered on the network
		/// </summary>
		public event EventHandler<DeviceDiscoveryEventArgs>? DeviceDiscovered;
		/// <summary>
		/// Event fired when a LIFX bulb hasn't been seen on the network for a while (for more than 5 minutes)
		/// </summary>
		public event EventHandler<DeviceDiscoveryEventArgs>? DeviceLost;

		private IList<Device> devices = new List<Device>();

		/// <summary>
		/// Gets a list of currently known devices
		/// </summary>
		public IEnumerable<Device> Devices { get { return devices; } }

		/// <summary>
		/// Event args for <see cref="DeviceDiscovered"/> and <see cref="DeviceLost"/> events.
		/// </summary>
		public sealed class DeviceDiscoveryEventArgs : EventArgs
		{
			internal DeviceDiscoveryEventArgs(Device device) => Device = device;
			/// <summary>
			/// The device the event relates to
			/// </summary>
			public Device Device { get; }
		}

		private void ProcessDeviceDiscoveryMessage(System.Net.IPAddress remoteAddress, int remotePort, LifxResponse msg)
		{
			string id = msg.Header.TargetMacAddressName; //remoteAddress.ToString()
			if (DiscoveredBulbs.ContainsKey(id))  //already discovered
			{
				DiscoveredBulbs[id].LastSeen = DateTime.UtcNow; //Update datestamp
				DiscoveredBulbs[id].HostName = remoteAddress.ToString(); //Update hostname in case IP changed

				return;
			}
			if (msg.Source != discoverSourceID || //did we request the discovery?
				_DiscoverCancellationSource == null ||
				_DiscoverCancellationSource.IsCancellationRequested) //did we cancel discovery?
				return;

			var device = new LightBulb(remoteAddress.ToString(), msg.Header.TargetMacAddress, msg.Payload[0]
				, BitConverter.ToUInt32(msg.Payload, 1))
			{
				LastSeen = DateTime.UtcNow
			};
			DiscoveredBulbs[id] = device;
			devices.Add(device);
			if (DeviceDiscovered != null)
			{
				DeviceDiscovered(this, new DeviceDiscoveryEventArgs(device));
			}
		}

		/// <summary>
		/// Begins searching for bulbs.
		/// </summary>
		/// <seealso cref="DeviceDiscovered"/>
		/// <seealso cref="DeviceLost"/>
		/// <seealso cref="StopDeviceDiscovery"/>
		public void StartDeviceDiscovery()
		{
			if (_DiscoverCancellationSource != null && !_DiscoverCancellationSource.IsCancellationRequested)
				return;
			_DiscoverCancellationSource = new CancellationTokenSource();
			var token = _DiscoverCancellationSource.Token;
			var source = discoverSourceID = GetNextIdentifier();
			//Start discovery thread
			Task.Run(async () =>
			{
				System.Diagnostics.Debug.WriteLine("Sending GetServices");
				FrameHeader header = new FrameHeader()
				{
					Identifier = source
				};
				
				// Initial delay to ensure socket is fully ready
				await Task.Delay(200);
				
				while (!token.IsCancellationRequested)
				{
					try
					{
						await BroadcastMessageToAllSubnetsAsync(header);
					}
					catch { }
					await Task.Delay(5000);
					var lostDevices = devices.Where(d => (DateTime.UtcNow - d.LastSeen).TotalMinutes > 5).ToArray();
					if (lostDevices.Any())
					{
						foreach (var device in lostDevices)
						{
							devices.Remove(device);
							DiscoveredBulbs.Remove(device.MacAddressName);
							if (DeviceLost != null)
								DeviceLost(this, new DeviceDiscoveryEventArgs(device));
						}
					}
				}
			});
		}

		private async Task BroadcastMessageToAllSubnetsAsync(FrameHeader header)
		{
			var broadcastIPs = GetSubnetBroadcastIPs();
			Console.WriteLine($"Broadcasting to {broadcastIPs.Count} subnets...");

			foreach (var ip in broadcastIPs)
			{
				System.Diagnostics.Debug.WriteLine($"Broadcasting GetService to {ip}");
				Console.WriteLine($"Broadcasting GetService to {ip}");

				try
				{
					var result = await BroadcastMessageAsync<UnknownResponse>(ip, header, MessageType.DeviceGetService);
					Console.WriteLine($"✓ Broadcast to {ip} completed");
				}
				catch (Exception ex)
				{
					Console.WriteLine($"✗ Broadcast to {ip} failed: {ex.Message}");
				}
			}
		}

		private List<string> GetSubnetBroadcastIPs()
		{
			List<string> localIPs = new List<string>();
			System.Diagnostics.Debug.WriteLine("GetSubnetBroadcastIPs: Starting discovery");
			Console.WriteLine("GetSubnetBroadcastIPs: Starting discovery");

			try
			{
				var host = Dns.GetHostEntry(Dns.GetHostName());
				System.Diagnostics.Debug.WriteLine($"Host name: {Dns.GetHostName()}");
				System.Diagnostics.Debug.WriteLine($"Found {host.AddressList.Length} addresses");
				Console.WriteLine($"Host name: {Dns.GetHostName()}");
				Console.WriteLine($"Found {host.AddressList.Length} addresses");

				foreach (var ip in host.AddressList)
				{
					System.Diagnostics.Debug.WriteLine($"Checking IP: {ip} (Family: {ip.AddressFamily})");
					Console.WriteLine($"Checking IP: {ip} (Family: {ip.AddressFamily})");

					if (ip.AddressFamily == AddressFamily.InterNetwork)
					{
						var bytes = ip.GetAddressBytes();
						System.Diagnostics.Debug.WriteLine($"IPv4 Address bytes: {string.Join(".", bytes)}");
						Console.WriteLine($"IPv4 Address bytes: {string.Join(".", bytes)}");

						if (bytes[0] == 10)
						{
							var broadcastIP = BroadcastFromIP(ip);
							localIPs.Add(broadcastIP);
							System.Diagnostics.Debug.WriteLine($"Added 10.x.x.x broadcast: {broadcastIP}");
							Console.WriteLine($"Added 10.x.x.x broadcast: {broadcastIP}");
						}
						if (bytes[0] == 192 && bytes[1] == 168) // MY subnet is 192.168.x.x
						{
							var broadcastIP = BroadcastFromIP(ip);
							localIPs.Add(broadcastIP);
							System.Diagnostics.Debug.WriteLine($"Added 192.168.x.x broadcast: {broadcastIP}");
							Console.WriteLine($"Added 192.168.x.x broadcast: {broadcastIP}");
						}
					}
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error in GetSubnetBroadcastIPs: {ex.Message}");
				Console.WriteLine($"Error in GetSubnetBroadcastIPs: {ex.Message}");
			}

			if (localIPs.Count == 0)
			{
				localIPs.Add("255.255.255.255");
				System.Diagnostics.Debug.WriteLine("No local IPs found, added global broadcast");
				Console.WriteLine("No local IPs found, added global broadcast");
			}

			System.Diagnostics.Debug.WriteLine($"Final broadcast IPs: {string.Join(", ", localIPs)}");
			Console.WriteLine($"Final broadcast IPs: {string.Join(", ", localIPs)}");
			return localIPs;
		}

		private string BroadcastFromIP(IPAddress ip)
		{
			var bytes = ip.GetAddressBytes();
			bytes[3] = 255;
			return new IPAddress(bytes).ToString();
		}

		/// <summary>
		/// Stops device discovery
		/// </summary>
		/// <seealso cref="StartDeviceDiscovery"/>
		public void StopDeviceDiscovery()
		{
			if (_DiscoverCancellationSource == null || _DiscoverCancellationSource.IsCancellationRequested)
				return;
			_DiscoverCancellationSource.Cancel();
			_DiscoverCancellationSource = null;
		}
	}

	/// <summary>
	/// LIFX Generic Device
	/// </summary>
	public abstract class Device
	{
		internal Device(string hostname, byte[] macAddress, byte service, UInt32 port)
		{
			if (hostname == null)
				throw new ArgumentNullException(nameof(hostname));
			if (string.IsNullOrWhiteSpace(hostname))
				throw new ArgumentException(nameof(hostname));
			HostName = hostname;
			MacAddress = macAddress;
			Service = service;
			Port = port;
			LastSeen = DateTime.MinValue;
		}

		/// <summary>
		/// Hostname for the device
		/// </summary>
		public string HostName { get; internal set; }

		/// <summary>
		/// Service ID
		/// </summary>
		public byte Service { get; }

		/// <summary>
		/// Service port
		/// </summary>
		public UInt32 Port { get; }

		internal DateTime LastSeen { get; set; }

		/// <summary>
		/// Gets the MAC address
		/// </summary>
		public byte[] MacAddress { get; }

		/// <summary>
		/// Gets the MAC address
		/// </summary>
		public string MacAddressName
		{
			get
			{
				if (MacAddress == null) return string.Empty;
				return string.Join(":", MacAddress.Take(6).Select(tb => tb.ToString("X2")).ToArray());
			}
		}
	}
	/// <summary>
	/// LIFX light bulb
	/// </summary>
	public sealed class LightBulb : Device
	{
		/// <summary>
		/// Initializes a new instance of a bulb instead of relying on discovery. At least the host name must be provide for the device to be usable.
		/// </summary>
		/// <param name="hostname">Required</param>
		/// <param name="macAddress"></param>
		/// <param name="service"></param>
		/// <param name="port"></param>
		public LightBulb(string hostname, byte[] macAddress, byte service = 0, UInt32 port = 0) : base(hostname, macAddress, service, port)
		{
		}
	}
}
