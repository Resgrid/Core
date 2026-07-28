using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Resgrid.Model.Services;
using Resgrid.Model.Tracking;
using Resgrid.Providers.Tracking.Protocols;
using Resgrid.TrackerGateway.Health;
using Resgrid.TrackerGateway.Hosting;
using Resgrid.TrackerGateway.Listeners;

namespace Resgrid.TrackerGateway.Sessions
{
	public sealed class TrackingTransportSessionHandler :
		ITrackingTransportSessionHandler
	{
		private readonly TrackingGatewaySettings _settings;
		private readonly ITrackingProtocolModuleRegistry _moduleRegistry;
		private readonly ILifetimeScope _lifetimeScope;
		private readonly TrackingSessionGenerationRegistry _generationRegistry;
		private readonly TrackingGatewayMetrics _metrics;
		private readonly ILogger<TrackingTransportSessionHandler> _logger;

		public TrackingTransportSessionHandler(
			TrackingGatewaySettings settings,
			ITrackingProtocolModuleRegistry moduleRegistry,
			ILifetimeScope lifetimeScope,
			TrackingSessionGenerationRegistry generationRegistry,
			TrackingGatewayMetrics metrics,
			ILogger<TrackingTransportSessionHandler> logger)
		{
			_settings = settings ??
				throw new ArgumentNullException(nameof(settings));
			_moduleRegistry = moduleRegistry ??
				throw new ArgumentNullException(nameof(moduleRegistry));
			_lifetimeScope = lifetimeScope ??
				throw new ArgumentNullException(nameof(lifetimeScope));
			_generationRegistry = generationRegistry ??
				throw new ArgumentNullException(nameof(generationRegistry));
			_metrics = metrics ??
				throw new ArgumentNullException(nameof(metrics));
			_logger = logger ??
				throw new ArgumentNullException(nameof(logger));
		}

		public async Task HandleTcpAsync(
			TrackingListenerDefinition definition,
			Stream stream,
			EndPoint remoteEndPoint,
			CancellationToken cancellationToken)
		{
			if (definition == null)
				throw new ArgumentNullException(nameof(definition));
			if (stream == null)
				throw new ArgumentNullException(nameof(stream));
			if (definition.Transport != TrackingSocketTransport.Tcp)
			{
				throw new ArgumentException(
					"The TCP session handler requires a TCP listener definition.",
					nameof(definition));
			}

			using var sessionCancellation =
				CancellationTokenSource.CreateLinkedTokenSource(
					cancellationToken);
			var sessionToken = sessionCancellation.Token;
			var session = CreateProtocolSession(
				definition,
				remoteEndPoint,
				sessionToken);
			if (session == null)
				return;

			using var scope = _lifetimeScope.BeginLifetimeScope();
			var authenticationService =
				scope.Resolve<IUnitTrackingAuthenticationService>();
			var ingressService =
				scope.Resolve<IUnitTrackingIngressService>();
			var state = new TrackingSessionState();
			using var buffer = new PooledTrackingBuffer(
				_settings.MaxFrameBytes);

			try
			{
				while (!sessionToken.IsCancellationRequested)
				{
					if (!buffer.EnsureWriteCapacity())
					{
						LogFrameLimit(definition, remoteEndPoint);
						return;
					}

					var readLength = await ReadWithIdleTimeoutAsync(
						stream,
						buffer.WriteMemory,
						sessionToken);
					if (readLength <= 0)
						return;

					buffer.Advance(readLength);
					while (buffer.Length > 0)
					{
						var input = new ReadOnlySequence<byte>(
							buffer.WrittenMemory);
						var parseResult = Parse(
							session,
							ref input,
							definition,
							remoteEndPoint);
						if (parseResult == null)
							return;

						if (!TryGetSequenceOffsets(
							    input,
							    parseResult,
							    out var consumedLength,
							    out _))
						{
							LogInvalidModuleResult(
								definition,
								remoteEndPoint);
							return;
						}

						if (parseResult.Status !=
							    ProtocolParseStatus.NeedMoreData &&
						    consumedLength > 0)
						{
							_metrics.ObserveFrameBytes(
								definition.ProtocolKey,
								consumedLength);
						}

						if (parseResult.Status ==
						    ProtocolParseStatus.NeedMoreData)
						{
							buffer.Consume(consumedLength);
							if (!buffer.EnsureWriteCapacity())
							{
								LogFrameLimit(
									definition,
									remoteEndPoint);
								return;
							}

							break;
						}

						if (parseResult.Status ==
							    ProtocolParseStatus.Malformed ||
						    parseResult.Status ==
							    ProtocolParseStatus.Unsupported ||
						    parseResult.Status ==
						    ProtocolParseStatus.CloseSession)
						{
							_metrics.RecordParseFailure(
								definition.ProtocolKey,
								ParseFailureReason(
									parseResult.Status));
							_logger.LogDebug(
								"TCP tracking session closed after protocol parse status {ParseStatus} for {ProtocolKey} from {RemoteEndpoint}.",
								parseResult.Status,
								definition.ProtocolKey,
								TrackingEndpointMasker.Mask(
									remoteEndPoint));
							return;
						}

						if (consumedLength <= 0 ||
						    !IsMessageValid(parseResult))
						{
							LogInvalidModuleResult(
								definition,
								remoteEndPoint);
							return;
						}

						var outcome = await HandleMessageAsync(
							definition,
							session,
							parseResult.Message,
							remoteEndPoint,
							state,
							sessionCancellation,
							authenticationService,
							ingressService,
							useGeneration: true,
							sessionToken);
						_metrics.RecordIngressMessage(
							definition,
							parseResult.Message,
							outcome.Acceptance);
						if (state.Generation != null &&
						    !state.Generation.IsCurrent)
							return;

						if (!TryBuildResponse(
							    session,
							    parseResult.Message,
							    outcome.Acceptance,
							    definition,
							    remoteEndPoint,
							    out var response))
							return;

						if (!response.IsEmpty)
						{
							await stream.WriteAsync(
								response,
								sessionToken);
						}

						buffer.Consume(consumedLength);
						if (outcome.CloseAfterResponse)
							return;
					}
				}
			}
			catch (OperationCanceledException)
				when (sessionCancellation.IsCancellationRequested)
			{
			}
			finally
			{
				state.Generation?.Dispose();
			}
		}

		public async Task<ReadOnlyMemory<byte>> HandleUdpAsync(
			TrackingListenerDefinition definition,
			ReadOnlyMemory<byte> datagram,
			EndPoint remoteEndPoint,
			CancellationToken cancellationToken)
		{
			if (definition == null)
				throw new ArgumentNullException(nameof(definition));
			if (definition.Transport != TrackingSocketTransport.Udp)
			{
				throw new ArgumentException(
					"The UDP session handler requires a UDP listener definition.",
					nameof(definition));
			}
			if (datagram.IsEmpty ||
			    datagram.Length > _settings.MaxFrameBytes)
			{
				if (datagram.Length > _settings.MaxFrameBytes)
				{
					_metrics.RecordParseFailure(
						definition.ProtocolKey,
						"frame-too-large");
				}

				return ReadOnlyMemory<byte>.Empty;
			}

			_metrics.ObserveFrameBytes(
				definition.ProtocolKey,
				datagram.Length);

			using var sessionCancellation =
				CancellationTokenSource.CreateLinkedTokenSource(
					cancellationToken);
			var sessionToken = sessionCancellation.Token;
			var session = CreateProtocolSession(
				definition,
				remoteEndPoint,
				sessionToken);
			if (session == null)
				return ReadOnlyMemory<byte>.Empty;

			using var scope = _lifetimeScope.BeginLifetimeScope();
			var authenticationService =
				scope.Resolve<IUnitTrackingAuthenticationService>();
			var ingressService =
				scope.Resolve<IUnitTrackingIngressService>();
			var input = new ReadOnlySequence<byte>(datagram);
			var parseResult = Parse(
				session,
				ref input,
				definition,
				remoteEndPoint);
			if (parseResult == null ||
			    parseResult.Status ==
			    ProtocolParseStatus.NeedMoreData ||
			    parseResult.Status ==
			    ProtocolParseStatus.Malformed ||
			    parseResult.Status ==
			    ProtocolParseStatus.Unsupported ||
			    parseResult.Status ==
			    ProtocolParseStatus.CloseSession)
			{
				if (parseResult != null)
				{
					_metrics.RecordParseFailure(
						definition.ProtocolKey,
						parseResult.Status ==
						ProtocolParseStatus.NeedMoreData
							? "incomplete-datagram"
							: ParseFailureReason(
								parseResult.Status));
				}

				return ReadOnlyMemory<byte>.Empty;
			}

			if (!TryGetSequenceOffsets(
				    input,
				    parseResult,
				    out var consumedLength,
				    out _) ||
			    consumedLength != datagram.Length ||
			    !IsMessageValid(parseResult))
			{
				LogInvalidModuleResult(
					definition,
					remoteEndPoint);
				return ReadOnlyMemory<byte>.Empty;
			}

			var outcome = await HandleMessageAsync(
				definition,
				session,
				parseResult.Message,
				remoteEndPoint,
				new TrackingSessionState(),
				sessionCancellation,
				authenticationService,
				ingressService,
				useGeneration: false,
				sessionToken);
			_metrics.RecordIngressMessage(
				definition,
				parseResult.Message,
				outcome.Acceptance);
			return TryBuildResponse(
				session,
				parseResult.Message,
				outcome.Acceptance,
				definition,
				remoteEndPoint,
				out var response)
				? response
				: ReadOnlyMemory<byte>.Empty;
		}

		private ITrackingProtocolSession CreateProtocolSession(
			TrackingListenerDefinition definition,
			EndPoint remoteEndPoint,
			CancellationToken cancellationToken)
		{
			try
			{
				var module = _moduleRegistry.Resolve(
					definition.ProtocolKey,
					definition.Transport);
				return module.CreateSession(
					new TrackingSessionContext
					{
						SessionId = Guid.NewGuid().ToString("N"),
						Transport = definition.Transport,
						RemoteEndPoint = remoteEndPoint,
						ConnectedOnUtc = DateTime.UtcNow,
						MaxFrameBytes = _settings.MaxFrameBytes,
						CancellationToken = cancellationToken
					});
			}
			catch (Exception ex)
			{
				_logger.LogWarning(
					ex,
					"Unable to create tracking protocol session for {ProtocolKey} over {Transport} from {RemoteEndpoint}.",
					definition.ProtocolKey,
					definition.Transport,
					TrackingEndpointMasker.Mask(remoteEndPoint));
				return null;
			}
		}

		private ProtocolParseResult Parse(
			ITrackingProtocolSession session,
			ref ReadOnlySequence<byte> input,
			TrackingListenerDefinition definition,
			EndPoint remoteEndPoint)
		{
			try
			{
				var parseResult = session.Parse(ref input);
				if (parseResult == null)
				{
					_metrics.RecordParseFailure(
						definition.ProtocolKey,
						"invalid-result");
				}

				return parseResult;
			}
			catch (Exception ex)
			{
				_metrics.RecordParseFailure(
					definition.ProtocolKey,
					"parser-exception");
				_logger.LogWarning(
					ex,
					"Tracking protocol parser failed for {ProtocolKey} over {Transport} from {RemoteEndpoint}.",
					definition.ProtocolKey,
					definition.Transport,
					TrackingEndpointMasker.Mask(remoteEndPoint));
				return null;
			}
		}

		private async Task<int> ReadWithIdleTimeoutAsync(
			Stream stream,
			Memory<byte> buffer,
			CancellationToken sessionToken)
		{
			using var idleCancellation =
				CancellationTokenSource.CreateLinkedTokenSource(
					sessionToken);
			idleCancellation.CancelAfter(
				TimeSpan.FromSeconds(
					_settings.TcpIdleTimeoutSeconds));

			try
			{
				return await stream.ReadAsync(
					buffer,
					idleCancellation.Token);
			}
			catch (OperationCanceledException)
				when (!sessionToken.IsCancellationRequested &&
				      idleCancellation.IsCancellationRequested)
			{
				return -1;
			}
		}

		private async Task<MessageHandlingOutcome> HandleMessageAsync(
			TrackingListenerDefinition definition,
			ITrackingProtocolSession session,
			ProtocolMessage message,
			EndPoint remoteEndPoint,
			TrackingSessionState state,
			CancellationTokenSource sessionCancellation,
			IUnitTrackingAuthenticationService authenticationService,
			IUnitTrackingIngressService ingressService,
			bool useGeneration,
			CancellationToken cancellationToken)
		{
			var sourceResult = await ResolveSourceAsync(
				definition,
				message,
				remoteEndPoint,
				state,
				sessionCancellation,
				authenticationService,
				useGeneration,
				cancellationToken);
			if (sourceResult.Status !=
			    TrackingAcceptanceStatus.Accepted)
			{
				_metrics.RecordAuthFailure(
					definition.Transport,
					sourceResult.ReasonCode);
				return new MessageHandlingOutcome(
					sourceResult,
					closeAfterResponse: true);
			}

			if (message.MessageType ==
			    ProtocolMessageType.Login)
			{
				return new MessageHandlingOutcome(
					Accepted(0),
					closeAfterResponse: false);
			}

			if (message.MessageType ==
			    ProtocolMessageType.Heartbeat)
			{
				try
				{
					var heartbeatResult =
						await ingressService.AcceptHeartbeatAsync(
							state.Source,
							DateTime.UtcNow,
							cancellationToken);
					return FromIngressResult(
						heartbeatResult);
				}
				catch (OperationCanceledException)
					when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					_logger.LogWarning(
						ex,
						"Canonical tracking heartbeat acceptance failed for {ProtocolKey} from {RemoteEndpoint}.",
						definition.ProtocolKey,
						TrackingEndpointMasker.Mask(
							remoteEndPoint));
					return new MessageHandlingOutcome(
						Unavailable("ingress-unavailable"),
						closeAfterResponse: true);
				}
			}

			if (message.MessageType !=
			    ProtocolMessageType.Positions)
			{
				return new MessageHandlingOutcome(
					Rejected("message-type-invalid"),
					closeAfterResponse: true);
			}

			try
			{
				if (session is
				    ITrackingProtocolPositionEnricher enricher)
				{
					enricher.EnrichPositions(
						message,
						state.Source.Device);
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning(
					ex,
					"Tracking profile enrichment failed for {ProtocolKey} from {RemoteEndpoint}.",
					definition.ProtocolKey,
					TrackingEndpointMasker.Mask(
						remoteEndPoint));
				return new MessageHandlingOutcome(
					Unavailable("profile-enrichment-failed"),
					closeAfterResponse: true);
			}
			finally
			{
				message.ProtocolData = null;
			}

			TrackingIngressResult ingressResult;
			var publishStartedTimestamp =
				Stopwatch.GetTimestamp();
			try
			{
				ingressResult = await ingressService.AcceptAsync(
					state.Source,
					message.Positions,
					cancellationToken);
			}
			catch (OperationCanceledException)
				when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				_logger.LogWarning(
					ex,
					"Canonical tracking ingress failed for {ProtocolKey} from {RemoteEndpoint}.",
					definition.ProtocolKey,
					TrackingEndpointMasker.Mask(remoteEndPoint));
				return new MessageHandlingOutcome(
					Unavailable("ingress-unavailable"),
					closeAfterResponse: true);
			}
			finally
			{
				_metrics.ObserveQueuePublishDuration(
					definition.Transport,
					Stopwatch.GetElapsedTime(
						publishStartedTimestamp));
			}

			return FromIngressResult(ingressResult);
		}

		private async Task<TrackingAcceptance> ResolveSourceAsync(
			TrackingListenerDefinition definition,
			ProtocolMessage message,
			EndPoint remoteEndPoint,
			TrackingSessionState state,
			CancellationTokenSource sessionCancellation,
			IUnitTrackingAuthenticationService authenticationService,
			bool useGeneration,
			CancellationToken cancellationToken)
		{
			var identifier =
				string.IsNullOrWhiteSpace(message.ExternalIdentifier)
					? state.Source?.ReportedDeviceIdentifier
					: message.ExternalIdentifier.Trim();
			if (string.IsNullOrWhiteSpace(identifier))
				return Rejected("identifier-required");

			Resgrid.Model.UnitTrackingDevice device;
			try
			{
				device = await authenticationService
					.GetEnabledDeviceByProtocolIdentifierAsync(
						definition.ProtocolKey,
						identifier,
						cancellationToken);
			}
			catch (OperationCanceledException)
				when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				_logger.LogWarning(
					ex,
					"Native tracking device mapping failed for {ProtocolKey} from {RemoteEndpoint}.",
					definition.ProtocolKey,
					TrackingEndpointMasker.Mask(remoteEndPoint));
				return Unavailable("mapping-unavailable");
			}

			if (device == null ||
			    string.IsNullOrWhiteSpace(
				    device.UnitTrackingDeviceId))
				return Rejected("device-not-found");

			if (state.Source?.Device != null &&
			    !string.Equals(
				    state.Source.Device.UnitTrackingDeviceId,
				    device.UnitTrackingDeviceId,
				    StringComparison.Ordinal))
				return Rejected("identifier-changed");

			var remoteAddress =
				(remoteEndPoint as IPEndPoint)?.Address;
			if (!UnitTrackingSourceNetworkPolicy.IsAllowed(
				    remoteAddress,
				    device.AllowedSourceCidrs))
				return Rejected("source-not-allowed");

			if (useGeneration && state.Generation == null)
			{
				state.Generation = _generationRegistry.Activate(
					device.UnitTrackingDeviceId,
					sessionCancellation);
			}

			state.Source = new AuthenticatedTrackingSource
			{
				Device = device,
				ReportedDeviceIdentifier = identifier
			};
			return Accepted(0);
		}

		private bool TryBuildResponse(
			ITrackingProtocolSession session,
			ProtocolMessage message,
			TrackingAcceptance acceptance,
			TrackingListenerDefinition definition,
			EndPoint remoteEndPoint,
			out ReadOnlyMemory<byte> response)
		{
			response = ReadOnlyMemory<byte>.Empty;
			if (!message.RequiresResponse)
				return true;

			try
			{
				response = session.BuildResponse(
					message,
					acceptance);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(
					ex,
					"Tracking response generation failed for {ProtocolKey} over {Transport} from {RemoteEndpoint}.",
					definition.ProtocolKey,
					definition.Transport,
					TrackingEndpointMasker.Mask(remoteEndPoint));
				return false;
			}

			if (response.Length > _settings.MaxFrameBytes)
			{
				_logger.LogWarning(
					"Tracking response exceeded the configured frame limit for {ProtocolKey} over {Transport}; response was dropped.",
					definition.ProtocolKey,
					definition.Transport);
				response = ReadOnlyMemory<byte>.Empty;
				return false;
			}

			if (response.IsEmpty &&
			    acceptance.Status ==
			    TrackingAcceptanceStatus.Accepted)
			{
				_logger.LogWarning(
					"Tracking protocol module returned an empty required acceptance response for {ProtocolKey} over {Transport}.",
					definition.ProtocolKey,
					definition.Transport);
				return false;
			}

			return true;
		}

		private bool IsMessageValid(
			ProtocolParseResult parseResult)
		{
			if (parseResult.Message == null ||
			    parseResult.Message.AcknowledgementToken.Length >
			    _settings.MaxFrameBytes)
				return false;

			return parseResult.Status switch
			{
				ProtocolParseStatus.Login =>
					parseResult.Message.MessageType ==
					ProtocolMessageType.Login,
				ProtocolParseStatus.Heartbeat =>
					parseResult.Message.MessageType ==
					ProtocolMessageType.Heartbeat,
				ProtocolParseStatus.Positions =>
					parseResult.Message.MessageType ==
					ProtocolMessageType.Positions &&
					parseResult.Message.Positions != null,
				_ => false
			};
		}

		private static bool TryGetSequenceOffsets(
			ReadOnlySequence<byte> input,
			ProtocolParseResult parseResult,
			out int consumedLength,
			out int examinedLength)
		{
			consumedLength = 0;
			examinedLength = 0;
			if (parseResult == null)
				return false;

			try
			{
				var consumed = input
					.Slice(0, parseResult.Consumed)
					.Length;
				var examined = input
					.Slice(0, parseResult.Examined)
					.Length;
				if (consumed > int.MaxValue ||
				    examined > int.MaxValue ||
				    consumed > examined)
					return false;

				consumedLength = (int)consumed;
				examinedLength = (int)examined;
				return true;
			}
			catch (ArgumentOutOfRangeException)
			{
				return false;
			}
		}

		private static TrackingAcceptance Accepted(
			int acceptedPositions)
		{
			return new TrackingAcceptance
			{
				Status = TrackingAcceptanceStatus.Accepted,
				AcceptedPositions = acceptedPositions
			};
		}

		private static TrackingAcceptance Rejected(
			string reasonCode)
		{
			return new TrackingAcceptance
			{
				Status = TrackingAcceptanceStatus.Rejected,
				ReasonCode = reasonCode
			};
		}

		private static TrackingAcceptance Unavailable(
			string reasonCode)
		{
			return new TrackingAcceptance
			{
				Status = TrackingAcceptanceStatus.Unavailable,
				ReasonCode = reasonCode
			};
		}

		private static MessageHandlingOutcome FromIngressResult(
			TrackingIngressResult ingressResult)
		{
			if (ingressResult == null ||
			    ingressResult.Status ==
			    TrackingIngressStatus.Unavailable)
			{
				return new MessageHandlingOutcome(
					Unavailable("ingress-unavailable"),
					closeAfterResponse: true);
			}

			if (ingressResult.Status !=
			    TrackingIngressStatus.Accepted)
			{
				return new MessageHandlingOutcome(
					Rejected("ingress-invalid"),
					closeAfterResponse: true);
			}

			return new MessageHandlingOutcome(
				Accepted(ingressResult.Accepted),
				closeAfterResponse: false);
		}

		private void LogFrameLimit(
			TrackingListenerDefinition definition,
			EndPoint remoteEndPoint)
		{
			_metrics.RecordParseFailure(
				definition.ProtocolKey,
				"frame-too-large");
			_logger.LogDebug(
				"TCP tracking frame exceeded the configured limit for {ProtocolKey} from {RemoteEndpoint}.",
				definition.ProtocolKey,
				TrackingEndpointMasker.Mask(remoteEndPoint));
		}

		private void LogInvalidModuleResult(
			TrackingListenerDefinition definition,
			EndPoint remoteEndPoint)
		{
			_metrics.RecordParseFailure(
				definition.ProtocolKey,
				"invalid-result");
			_logger.LogWarning(
				"Tracking protocol module returned an invalid parse result for {ProtocolKey} over {Transport} from {RemoteEndpoint}.",
				definition.ProtocolKey,
				definition.Transport,
				TrackingEndpointMasker.Mask(remoteEndPoint));
		}

		private static string ParseFailureReason(
			ProtocolParseStatus status)
		{
			return status switch
			{
				ProtocolParseStatus.Malformed => "malformed",
				ProtocolParseStatus.Unsupported => "unsupported",
				ProtocolParseStatus.CloseSession => "close-session",
				_ => "invalid-result"
			};
		}

		private sealed class TrackingSessionState
		{
			public AuthenticatedTrackingSource Source { get; set; }
			public TrackingSessionGenerationLease Generation { get; set; }
		}

		private sealed class MessageHandlingOutcome
		{
			public MessageHandlingOutcome(
				TrackingAcceptance acceptance,
				bool closeAfterResponse)
			{
				Acceptance = acceptance;
				CloseAfterResponse = closeAfterResponse;
			}

			public TrackingAcceptance Acceptance { get; }
			public bool CloseAfterResponse { get; }
		}

		private sealed class PooledTrackingBuffer : IDisposable
		{
			private const int InitialBufferBytes = 4096;

			private readonly int _maximumLength;
			private byte[] _buffer;
			private int _capacity;
			private int _length;

			public PooledTrackingBuffer(int maximumLength)
			{
				if (maximumLength <= 0)
					throw new ArgumentOutOfRangeException(
						nameof(maximumLength));

				_maximumLength = maximumLength;
				_capacity = Math.Min(
					InitialBufferBytes,
					maximumLength);
				_buffer = ArrayPool<byte>.Shared.Rent(
					_capacity);
			}

			public int Length => _length;
			public ReadOnlyMemory<byte> WrittenMemory =>
				_buffer.AsMemory(0, _length);
			public Memory<byte> WriteMemory =>
				_buffer.AsMemory(
					_length,
					_capacity - _length);

			public bool EnsureWriteCapacity()
			{
				if (_length < _capacity)
					return true;
				if (_capacity >= _maximumLength)
					return false;

				var nextCapacity =
					_capacity <= _maximumLength / 2
						? _capacity * 2
						: _maximumLength;
				var replacement =
					ArrayPool<byte>.Shared.Rent(
						nextCapacity);
				Buffer.BlockCopy(
					_buffer,
					0,
					replacement,
					0,
					_length);
				ArrayPool<byte>.Shared.Return(
					_buffer,
					clearArray: true);
				_buffer = replacement;
				_capacity = nextCapacity;
				return true;
			}

			public void Advance(int count)
			{
				if (count < 0 ||
				    count > _capacity - _length)
				throw new ArgumentOutOfRangeException(
					nameof(count));

				_length += count;
			}

			public void Consume(int count)
			{
				if (count < 0 || count > _length)
				throw new ArgumentOutOfRangeException(
					nameof(count));
				if (count == 0)
					return;

				var remainingLength = _length - count;
				if (remainingLength > 0)
				Buffer.BlockCopy(
					_buffer,
					count,
					_buffer,
					0,
					remainingLength);
				_length = remainingLength;
			}

			public void Dispose()
			{
				var buffer = Interlocked.Exchange(
					ref _buffer,
					null);
				if (buffer != null)
				ArrayPool<byte>.Shared.Return(
					buffer,
					clearArray: true);
			}
		}
	}
}
