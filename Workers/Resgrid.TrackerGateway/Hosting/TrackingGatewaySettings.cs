using System;
using System.Collections.Generic;
using Resgrid.Config;
using Resgrid.Providers.Tracking.Protocols;

namespace Resgrid.TrackerGateway.Hosting
{
	public sealed class TrackingProtocolListenerSettings
	{
		public TrackingProtocolListenerSettings(
			string protocolKey,
			bool enabled,
			bool tcpEnabled,
			int tcpPort,
			bool udpEnabled,
			int udpPort)
		{
			ProtocolKey = protocolKey;
			Enabled = enabled;
			TcpEnabled = tcpEnabled;
			TcpPort = tcpPort;
			UdpEnabled = udpEnabled;
			UdpPort = udpPort;
		}

		public string ProtocolKey { get; }
		public bool Enabled { get; }
		public bool TcpEnabled { get; }
		public int TcpPort { get; }
		public bool UdpEnabled { get; }
		public int UdpPort { get; }

		public bool IsEnabled(TrackingSocketTransport transport)
		{
			switch (transport)
			{
				case TrackingSocketTransport.Tcp:
					return TcpEnabled;
				case TrackingSocketTransport.Udp:
					return UdpEnabled;
				default:
					return false;
			}
		}

		public int GetPort(TrackingSocketTransport transport)
		{
			switch (transport)
			{
				case TrackingSocketTransport.Tcp:
					return TcpPort;
				case TrackingSocketTransport.Udp:
					return UdpPort;
				default:
					throw new ArgumentOutOfRangeException(
						nameof(transport),
						transport,
						"Only TCP and UDP listener ports are supported.");
			}
		}
	}

	public sealed class TrackingGatewaySettings
	{
		public TrackingGatewaySettings(
			bool trackingEnabled,
			bool nativeGatewayEnabled,
			string credentialPepper,
			int tcpIdleTimeoutSeconds,
			int maxFrameBytes,
			int maxConnections,
			int maxConnectionsPerIp,
			int gracefulShutdownSeconds,
			int internalHealthPort,
			IEnumerable<TrackingProtocolListenerSettings> protocols)
		{
			TrackingEnabled = trackingEnabled;
			NativeGatewayEnabled = nativeGatewayEnabled;
			CredentialPepper = credentialPepper;
			TcpIdleTimeoutSeconds = tcpIdleTimeoutSeconds;
			MaxFrameBytes = maxFrameBytes;
			MaxConnections = maxConnections;
			MaxConnectionsPerIp = maxConnectionsPerIp;
			GracefulShutdownSeconds = gracefulShutdownSeconds;
			InternalHealthPort = internalHealthPort;
			Protocols = new List<TrackingProtocolListenerSettings>(
				protocols ?? Array.Empty<TrackingProtocolListenerSettings>())
				.AsReadOnly();
		}

		public bool TrackingEnabled { get; }
		public bool NativeGatewayEnabled { get; }
		public string CredentialPepper { get; }
		public int TcpIdleTimeoutSeconds { get; }
		public int MaxFrameBytes { get; }
		public int MaxConnections { get; }
		public int MaxConnectionsPerIp { get; }
		public int GracefulShutdownSeconds { get; }
		public int InternalHealthPort { get; }
		public IReadOnlyCollection<TrackingProtocolListenerSettings> Protocols { get; }

		public static TrackingGatewaySettings FromCurrentConfig()
		{
			return new TrackingGatewaySettings(
				UnitTrackingConfig.Enabled,
				UnitTrackingConfig.NativeGatewayEnabled,
				UnitTrackingConfig.CredentialPepper,
				UnitTrackingConfig.TcpIdleTimeoutSeconds,
				UnitTrackingConfig.MaxFrameBytes,
				UnitTrackingConfig.MaxConnections,
				UnitTrackingConfig.MaxConnectionsPerIp,
				UnitTrackingConfig.GracefulShutdownSeconds,
				UnitTrackingConfig.InternalHealthPort,
				new[]
				{
					new TrackingProtocolListenerSettings(
						TrackingProtocolKeys.Queclink,
						UnitTrackingConfig.EnableQueclink,
						UnitTrackingConfig.EnableQueclinkTcp,
						UnitTrackingConfig.QueclinkTcpPort,
						UnitTrackingConfig.EnableQueclinkUdp,
						UnitTrackingConfig.QueclinkUdpPort),
					new TrackingProtocolListenerSettings(
						TrackingProtocolKeys.Gt06,
						UnitTrackingConfig.EnableGt06,
						UnitTrackingConfig.EnableGt06Tcp,
						UnitTrackingConfig.Gt06TcpPort,
						UnitTrackingConfig.EnableGt06Udp,
						UnitTrackingConfig.Gt06UdpPort),
					new TrackingProtocolListenerSettings(
						TrackingProtocolKeys.Teltonika,
						UnitTrackingConfig.EnableTeltonika,
						UnitTrackingConfig.EnableTeltonikaTcp,
						UnitTrackingConfig.TeltonikaTcpPort,
						UnitTrackingConfig.EnableTeltonikaUdp,
						UnitTrackingConfig.TeltonikaUdpPort)
				});
		}
	}
}
