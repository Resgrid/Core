using System;
using System.Collections.Generic;

namespace Resgrid.Providers.Tracking.Protocols.Queclink
{
	public sealed class QueclinkProtocolModule :
		ITrackingProtocolModule
	{
		private static readonly IReadOnlySet<TrackingSocketTransport>
			Transports = new HashSet<TrackingSocketTransport>
			{
				TrackingSocketTransport.Tcp
			};

		public string ProtocolKey =>
			TrackingProtocolKeys.Queclink;

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
					"The bounded Queclink @Track module supports TCP only.");
			}

			return new QueclinkProtocolSession(context);
		}
	}
}
