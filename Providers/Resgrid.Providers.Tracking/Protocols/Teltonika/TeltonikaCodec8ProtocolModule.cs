using System;
using System.Collections.Generic;

namespace Resgrid.Providers.Tracking.Protocols.Teltonika
{
	public sealed class TeltonikaCodec8ProtocolModule :
		ITrackingProtocolModule
	{
		public const string Key = "teltonika-codec8";

		private static readonly IReadOnlySet<TrackingSocketTransport>
			Transports = new HashSet<TrackingSocketTransport>
			{
				TrackingSocketTransport.Tcp,
				TrackingSocketTransport.Udp
			};

		public string ProtocolKey => Key;
		public IReadOnlySet<TrackingSocketTransport>
			SupportedTransports => Transports;

		public ITrackingProtocolSession CreateSession(
			TrackingSessionContext context)
		{
			if (context == null)
				throw new ArgumentNullException(nameof(context));
			if (context.Transport ==
			    TrackingSocketTransport.Tcp)
			{
				return new TeltonikaCodec8ProtocolSession(
					context);
			}
			if (context.Transport ==
			    TrackingSocketTransport.Udp)
			{
				return new TeltonikaCodec8UdpProtocolSession(
					context);
			}

			throw new NotSupportedException(
				"Teltonika Codec8/8E supports TCP and UDP.");
		}
	}
}
