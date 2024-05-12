using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;

namespace Resgrid.Intergration.Tests.Helpers
{
	public static class TcpPortFinder
	{
		public static int FindOpenTcpPortInRange(int start, int end)
		{
			List<int> usedPorts = new List<int>();
			bool foundPort = false;
			int port = 0;

			IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
			TcpConnectionInformation[] tcpConnInfoArray = ipGlobalProperties.GetActiveTcpConnections();

			foreach (TcpConnectionInformation tcpi in tcpConnInfoArray)
			{
				usedPorts.Add(tcpi.LocalEndPoint.Port);
			}

			port = start;
			while (!foundPort)
			{
				if (usedPorts.Where(x => x == port).Count() <= 0)
					foundPort = true;

				port++;

				if (port > end)
					return -1;
			}

			return port;
		}
	}
}
