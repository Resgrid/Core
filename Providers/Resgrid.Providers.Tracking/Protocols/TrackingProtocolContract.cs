using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using Resgrid.Model;
using Resgrid.Model.Tracking;

namespace Resgrid.Providers.Tracking.Protocols
{
	public static class TrackingProtocolContract
	{
		public const int Version = 1;
	}

	public enum TrackingSocketTransport
	{
		Unknown = 0,
		Tcp = 1,
		Udp = 2
	}

	public enum ProtocolParseStatus
	{
		NeedMoreData = 0,
		Login = 1,
		Heartbeat = 2,
		Positions = 3,
		Malformed = 4,
		Unsupported = 5,
		CloseSession = 6
	}

	public enum ProtocolMessageType
	{
		Unknown = 0,
		Login = 1,
		Heartbeat = 2,
		Positions = 3
	}

	public enum TrackingAcceptanceStatus
	{
		Accepted = 0,
		Rejected = 1,
		Unavailable = 2
	}

	public sealed class TrackingSessionContext
	{
		public string SessionId { get; set; }
		public TrackingSocketTransport Transport { get; set; }
		public EndPoint RemoteEndPoint { get; set; }
		public DateTime ConnectedOnUtc { get; set; }
		public int MaxFrameBytes { get; set; }
		public CancellationToken CancellationToken { get; set; }
	}

	public sealed class ProtocolMessage
	{
		public ProtocolMessageType MessageType { get; set; }
		public string ExternalIdentifier { get; set; }
		public IReadOnlyCollection<CanonicalTrackingPosition> Positions { get; set; } =
			Array.Empty<CanonicalTrackingPosition>();
		public ReadOnlyMemory<byte> AcknowledgementToken { get; set; }
		public bool RequiresResponse { get; set; }
		public object ProtocolData { get; set; }
	}

	public sealed class ProtocolParseResult
	{
		public ProtocolParseStatus Status { get; set; }
		public SequencePosition Consumed { get; set; }
		public SequencePosition Examined { get; set; }
		public ProtocolMessage Message { get; set; }
		public string ReasonCode { get; set; }
	}

	public sealed class TrackingAcceptance
	{
		public TrackingAcceptanceStatus Status { get; set; }
		public int AcceptedPositions { get; set; }
		public string ReasonCode { get; set; }
	}

	public interface ITrackingProtocolModule
	{
		string ProtocolKey { get; }
		IReadOnlySet<TrackingSocketTransport> SupportedTransports { get; }
		ITrackingProtocolSession CreateSession(TrackingSessionContext context);
	}

	public interface ITrackingProtocolSession
	{
		ProtocolParseResult Parse(ref ReadOnlySequence<byte> input);
		ReadOnlyMemory<byte> BuildResponse(
			ProtocolMessage message,
			TrackingAcceptance acceptance);
	}

	public interface ITrackingProtocolPositionEnricher
	{
		void EnrichPositions(
			ProtocolMessage message,
			UnitTrackingDevice device);
	}
}
