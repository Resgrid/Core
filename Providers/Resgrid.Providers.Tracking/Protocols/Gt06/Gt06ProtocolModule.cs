using System;
using System.Collections.Generic;

namespace Resgrid.Providers.Tracking.Protocols.Gt06
{
	public sealed class Gt06ProtocolModule :
		ITrackingProtocolModule
	{
		private static readonly IReadOnlySet<TrackingSocketTransport>
			Transports = new HashSet<TrackingSocketTransport>
			{
				TrackingSocketTransport.Tcp
			};

		public string ProtocolKey =>
			TrackingProtocolKeys.Gt06;

		public IReadOnlySet<TrackingSocketTransport>
			SupportedTransports => Transports;

		public ITrackingProtocolSession CreateSession(
			TrackingSessionContext context)
		{
			if (context == null)
				throw new ArgumentNullException(nameof(context));
			if (context.Transport !=
			    TrackingSocketTransport.Tcp)
			{
				throw new NotSupportedException(
					"The bounded GT06/Jimi module supports TCP only.");
			}

			return new Gt06ProtocolSession(context);
		}
	}
}
