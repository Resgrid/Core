using System;
using Microsoft.Extensions.Logging;
using Resgrid.Providers.Tracking.Protocols;
using Resgrid.TrackerGateway.Health;
using Resgrid.TrackerGateway.Hosting;
using Resgrid.TrackerGateway.Sessions;

namespace Resgrid.TrackerGateway.Listeners
{
	public sealed class TrackingSocketListenerFactory : ITrackingListenerFactory
	{
		private readonly TrackingGatewaySettings _settings;
		private readonly TrackingConnectionAdmission _connectionAdmission;
		private readonly ITrackingTransportSessionHandler _sessionHandler;
		private readonly TrackingGatewayMetrics _metrics;
		private readonly ILoggerFactory _loggerFactory;

		public TrackingSocketListenerFactory(
			TrackingGatewaySettings settings,
			TrackingConnectionAdmission connectionAdmission,
			ITrackingTransportSessionHandler sessionHandler,
			TrackingGatewayMetrics metrics,
			ILoggerFactory loggerFactory)
		{
			_settings = settings;
			_connectionAdmission = connectionAdmission;
			_sessionHandler = sessionHandler;
			_metrics = metrics;
			_loggerFactory = loggerFactory;
		}

		public bool Supports(TrackingListenerDefinition definition)
		{
			return definition != null &&
			       (definition.Transport == TrackingSocketTransport.Tcp ||
			        definition.Transport == TrackingSocketTransport.Udp);
		}

		public ITrackingListener Create(TrackingListenerDefinition definition)
		{
			if (!Supports(definition))
			{
				throw new NotSupportedException(
					$"No socket listener supports {definition}.");
			}

			return definition.Transport switch
			{
				TrackingSocketTransport.Tcp => new TcpTrackingListener(
					definition,
					_settings,
					_connectionAdmission,
					_sessionHandler,
					_metrics,
					_loggerFactory.CreateLogger<TcpTrackingListener>()),
				TrackingSocketTransport.Udp => new UdpTrackingListener(
					definition,
					_settings,
					_connectionAdmission,
					_sessionHandler,
					_metrics,
					_loggerFactory.CreateLogger<UdpTrackingListener>()),
				_ => throw new NotSupportedException(
					$"No socket listener supports {definition}.")
			};
		}
	}
}
