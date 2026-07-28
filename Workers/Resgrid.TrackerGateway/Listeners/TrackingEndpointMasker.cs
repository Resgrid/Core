using System;
using System.Net;
using System.Net.Sockets;

namespace Resgrid.TrackerGateway.Listeners
{
	public static class TrackingEndpointMasker
	{
		public static string Mask(EndPoint endPoint)
		{
			if (endPoint is not IPEndPoint ipEndPoint)
				return "unknown";

			var address = ipEndPoint.Address.IsIPv4MappedToIPv6
				? ipEndPoint.Address.MapToIPv4()
				: ipEndPoint.Address;
			var addressBytes = address.GetAddressBytes();

			if (address.AddressFamily == AddressFamily.InterNetwork)
			{
				addressBytes[3] = 0;
				return $"{new IPAddress(addressBytes)}:{ipEndPoint.Port}";
			}

			if (address.AddressFamily == AddressFamily.InterNetworkV6)
			{
				Array.Clear(addressBytes, 8, 8);
				return $"[{new IPAddress(addressBytes)}]:{ipEndPoint.Port}";
			}

			return "unknown";
		}
	}
}
