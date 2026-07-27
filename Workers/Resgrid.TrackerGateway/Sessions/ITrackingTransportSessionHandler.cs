using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.TrackerGateway.Hosting;

namespace Resgrid.TrackerGateway.Sessions
{
	public interface ITrackingTransportSessionHandler
	{
		Task HandleTcpAsync(
			TrackingListenerDefinition definition,
			Stream stream,
			EndPoint remoteEndPoint,
			CancellationToken cancellationToken);

		Task<ReadOnlyMemory<byte>> HandleUdpAsync(
			TrackingListenerDefinition definition,
			ReadOnlyMemory<byte> datagram,
			EndPoint remoteEndPoint,
			CancellationToken cancellationToken);
	}
}
