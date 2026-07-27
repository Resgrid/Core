using System;
using Resgrid.Providers.Tracking.Protocols;

namespace Resgrid.TrackerGateway.Hosting
{
	public sealed class TrackingListenerDefinition : IEquatable<TrackingListenerDefinition>
	{
		public TrackingListenerDefinition(
			string protocolKey,
			TrackingSocketTransport transport,
			int port)
		{
			ProtocolKey = protocolKey;
			Transport = transport;
			Port = port;
		}

		public string ProtocolKey { get; }
		public TrackingSocketTransport Transport { get; }
		public int Port { get; }
		public string Key => $"{ProtocolKey}:{Transport}:{Port}";

		public bool Equals(TrackingListenerDefinition other)
		{
			return other != null &&
			       string.Equals(ProtocolKey, other.ProtocolKey, StringComparison.OrdinalIgnoreCase) &&
			       Transport == other.Transport &&
			       Port == other.Port;
		}

		public override bool Equals(object obj)
		{
			return Equals(obj as TrackingListenerDefinition);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(
				StringComparer.OrdinalIgnoreCase.GetHashCode(ProtocolKey ?? string.Empty),
				Transport,
				Port);
		}

		public override string ToString()
		{
			return $"{ProtocolKey} {Transport} port {Port}";
		}
	}
}
