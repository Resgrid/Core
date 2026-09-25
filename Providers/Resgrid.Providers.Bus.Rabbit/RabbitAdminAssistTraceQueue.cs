using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using Resgrid.Config;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Providers.Bus.Rabbit
{
	/// <summary>
	/// Dedicated durable telemetry queue and connections. Publisher confirmation precedes success; a
	/// consumer acknowledges only after idempotent persistence. No dispatch connection, message or retry
	/// is touched. Full queues reject new evidence rather than evicting older evidence (including holds).
	/// </summary>
	public sealed class RabbitAdminAssistTraceQueue(IConnectionFactory factory) : IAdminAssistTraceQueue, IAsyncDisposable
	{
		private readonly Lane _publisher = new();
		private readonly Lane _consumer = new();
		private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
		private static string QueueName => RabbitConnection.SetQueueNameForEnv(AdminAssistConfig.TraceQueueName);
		private sealed record Envelope(int Version, string Id, int DepartmentId, int CallId, string AttemptId,
			string Stage, string ResolverVersion, DateTime OccurredOn, string Content, bool IsProtected, int ProtectedCatalogVersion)
		{
			public static Envelope From(AdminAssistDispatchTraceRow row) => new(1, row.AdminAssistDispatchTraceId, row.DepartmentId,
				row.CallId, row.AttemptId, row.Stage, row.ResolverVersion, row.OccurredOn, row.Content, row.IsProtected, row.ProtectedCatalogVersion);
			public AdminAssistDispatchTraceRow ToRow()
			{
				if (Version != 1) throw new ArgumentException("Unsupported trace envelope version.");
				return new() { AdminAssistDispatchTraceId = Id, DepartmentId = DepartmentId, CallId = CallId, AttemptId = AttemptId,
					Stage = Stage, ResolverVersion = ResolverVersion, OccurredOn = OccurredOn, Content = Content,
					IsProtected = IsProtected, ProtectedCatalogVersion = ProtectedCatalogVersion };
			}
		}
		public async Task EnqueueAsync(AdminAssistDispatchTraceRow row, CancellationToken ct)
		{
			DispatchTraceEnvelope.Validate(row);
			var body = JsonSerializer.SerializeToUtf8Bytes(Envelope.From(row), Json);
			if (body.Length > DispatchTraceEnvelope.MaximumBytes) throw new ArgumentException("Trace envelope exceeds the queue bound.");
			await RunAsync(_publisher, async channel =>
			{
				await channel.BasicPublishAsync(string.Empty, QueueName, true,
					new BasicProperties { DeliveryMode = DeliveryModes.Persistent, MessageId = row.AdminAssistDispatchTraceId,
						ContentType = DispatchTraceEnvelope.ContentType }, body, ct);
				return true;
			}, ct);
		}
		public Task<DispatchTraceReceiveResult> ProcessNextAsync(Func<AdminAssistDispatchTraceRow, CancellationToken, Task> persist, CancellationToken ct) =>
			RunAsync(_consumer, async channel =>
			{
				var delivery = await channel.BasicGetAsync(QueueName, false, ct);
				if (delivery == null) return DispatchTraceReceiveResult.Empty;
				if (delivery.Body.Length > DispatchTraceEnvelope.MaximumBytes || delivery.BasicProperties.ContentType != DispatchTraceEnvelope.ContentType)
					throw new ArgumentException("Invalid trace transport envelope.");
				var row = (JsonSerializer.Deserialize<Envelope>(delivery.Body.Span, Json) ?? throw new ArgumentException("Empty trace envelope.")).ToRow();
				DispatchTraceEnvelope.Validate(row);
				if (delivery.BasicProperties.MessageId != row.AdminAssistDispatchTraceId) throw new ArgumentException("Trace message identity mismatch.");
				await persist(row, ct);
				await channel.BasicAckAsync(delivery.DeliveryTag, false, ct);
				return DispatchTraceReceiveResult.Persisted;
			}, ct);

		private async Task<T> RunAsync<T>(Lane lane, Func<IChannel, Task<T>> operation, CancellationToken ct)
		{
			await lane.Gate.WaitAsync(ct);
			try
			{
				if (lane.Channel?.IsOpen != true || lane.Connection?.IsOpen != true)
				{
					await lane.ResetAsync();
					var hosts = new[] { ServiceBusConfig.RabbitHostname, ServiceBusConfig.RabbitHostname2, ServiceBusConfig.RabbitHostname3 }
						.Where(h => !string.IsNullOrWhiteSpace(h)).Distinct(StringComparer.Ordinal).ToArray();
					lane.Connection = await factory.CreateConnectionAsync(hosts, "Resgrid-AdminAssist-Trace", ct);
					lane.Channel = await lane.Connection.CreateChannelAsync(new CreateChannelOptions(true, true), ct);
					await lane.Channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false,
						arguments: new Dictionary<string, object> { ["x-max-length"] = 100000, ["x-max-length-bytes"] = 268435456L, ["x-overflow"] = "reject-publish" }, cancellationToken: ct);
				}
				return await operation(lane.Channel);
			}
			catch
			{
				// Closing returns unacknowledged deliveries to the broker, including a process/DB failure
				// after persistence but before ack. Same observation ID makes replay idempotent.
				await lane.ResetAsync();
				throw;
			}
			finally { lane.Gate.Release(); }
		}
		private sealed class Lane
		{
			public readonly SemaphoreSlim Gate = new(1, 1);
			public IConnection Connection;
			public IChannel Channel;
			public async Task ResetAsync()
			{
				var channel = Channel; var connection = Connection; Channel = null; Connection = null;
				try { if (channel != null) await channel.DisposeAsync(); } catch { /* Diagnostic transport cleanup only. */ }
				try { if (connection != null) await connection.DisposeAsync(); } catch { /* Never change send behavior. */ }
			}
		}
		public async ValueTask DisposeAsync()
		{
			foreach (var lane in new[] { _publisher, _consumer })
			{
				await lane.Gate.WaitAsync();
				try { await lane.ResetAsync(); }
				finally { lane.Gate.Release(); }
			}
		}
	}
}
