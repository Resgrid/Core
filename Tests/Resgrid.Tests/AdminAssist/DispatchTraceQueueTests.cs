using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using RabbitMQ.Client;
using Resgrid.Model.AdminAssist;
using Resgrid.Providers.Bus.Rabbit;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class DispatchTraceQueueTests
	{
		private Mock<IConnectionFactory> _factory;
		private Mock<IConnection> _connection;
		private Mock<IChannel> _channel;
		private RabbitAdminAssistTraceQueue _queue;
		private byte[] _body;
		private BasicProperties _properties;
		[SetUp]
		public void SetUp()
		{
			_body = null; _properties = null;
			_factory = new(); _connection = new(); _channel = new();
			_connection.SetupGet(c => c.IsOpen).Returns(true); _channel.SetupGet(c => c.IsOpen).Returns(true);
			_factory.Setup(f => f.CreateConnectionAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(_connection.Object);
			_connection.Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(_channel.Object);
			_channel.Setup(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
				.Callback<string, string, bool, BasicProperties, ReadOnlyMemory<byte>, CancellationToken>((exchange, route, mandatory, properties, body, token) => { _body = body.ToArray(); _properties = properties; })
				.Returns(ValueTask.CompletedTask);
			_channel.Setup(c => c.BasicGetAsync(It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => _body == null ? null : new BasicGetResult(17, true, "", "trace", 0, _properties, _body));
			_queue = new(_factory.Object);
		}
		[TearDown] public async Task TearDown() => await _queue.DisposeAsync();
		internal static AdminAssistDispatchTraceRow Row()
		{
			var observation = new DispatchTraceObservation(Guid.NewGuid().ToString("D"), 7, 19, null, Guid.NewGuid().ToString("D"), DateTime.UtcNow,
				DispatchTraceStage.Attempted, DispatchTraceChannel.Email, DispatchTraceReason.None, "member-reference", null, null, null,
				1, 0, DispatchRecipientResolver.Version);
			return new() { AdminAssistDispatchTraceId = observation.Id, DepartmentId = 7, CallId = 19, AttemptId = observation.AttemptId,
				Stage = observation.Stage.ToString(), ResolverVersion = observation.ResolverVersion, OccurredOn = observation.OccurredOnUtc,
				Content = JsonSerializer.Serialize(observation) };
		}
		[Test]
		public async Task Persistent_confirmed_envelope_is_acknowledged_only_after_the_store_commits()
		{
			var row = Row(); await _queue.EnqueueAsync(row, CancellationToken.None);
			Assert.That(_properties.DeliveryMode, Is.EqualTo(DeliveryModes.Persistent));
			Assert.That(_properties.Expiration, Is.Null, "Queue expiry must not discard evidence under a hold.");
			Assert.That(_properties.MessageId, Is.EqualTo(row.AdminAssistDispatchTraceId));
			Assert.That(System.Text.Encoding.UTF8.GetString(_body), Does.Not.Contain("IdValue").And.Not.Contain("TableName"));
			var stored = false;
			var result = await _queue.ProcessNextAsync((received, token) =>
			{
				_channel.Verify(c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
				Assert.That(received.Content, Is.EqualTo(row.Content)); stored = true; return Task.CompletedTask;
			}, CancellationToken.None);
			Assert.That(result, Is.EqualTo(DispatchTraceReceiveResult.Persisted)); Assert.That(stored, Is.True);
			_channel.Verify(c => c.BasicAckAsync(17, false, It.IsAny<CancellationToken>()), Times.Once);
			_connection.Verify(c => c.CreateChannelAsync(It.Is<CreateChannelOptions>(o => o.PublisherConfirmationsEnabled && o.PublisherConfirmationTrackingEnabled), It.IsAny<CancellationToken>()), Times.Exactly(2));
			_channel.Verify(c => c.QueueDeclareAsync(It.IsAny<string>(), true, false, false,
				It.Is<IDictionary<string, object>>(a => (string)a["x-overflow"] == "reject-publish" && !a.ContainsKey("x-message-ttl")), false, false, It.IsAny<CancellationToken>()), Times.Exactly(2));
		}
		[Test]
		public async Task Store_outage_returns_unacknowledged_delivery_and_replay_keeps_the_same_observation_id()
		{
			var row = Row(); await _queue.EnqueueAsync(row, CancellationToken.None);
			Assert.ThrowsAsync<IOException>(async () => await _queue.ProcessNextAsync((_, _) => throw new IOException("simulated outage"), CancellationToken.None));
			_channel.Verify(c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
			_channel.Verify(c => c.DisposeAsync(), Times.Once);
			await _queue.ProcessNextAsync((received, _) => { Assert.That(received.AdminAssistDispatchTraceId, Is.EqualTo(row.AdminAssistDispatchTraceId)); return Task.CompletedTask; }, CancellationToken.None);
			_channel.Verify(c => c.BasicAckAsync(17, false, It.IsAny<CancellationToken>()), Times.Once);
		}
		[Test]
		public async Task Crash_after_commit_before_ack_replays_without_changing_the_idempotency_key()
		{
			await _queue.EnqueueAsync(Row(), CancellationToken.None); var committed = new HashSet<string>(); var attempted = 0;
			Task Store(AdminAssistDispatchTraceRow row, CancellationToken _) { attempted++; committed.Add(row.AdminAssistDispatchTraceId); return Task.CompletedTask; }
			_channel.SetupSequence(c => c.BasicAckAsync(17, false, It.IsAny<CancellationToken>()))
				.Throws(new IOException("lost ack")).Returns(ValueTask.CompletedTask);
			Assert.ThrowsAsync<IOException>(async () => await _queue.ProcessNextAsync(Store, CancellationToken.None));
			await _queue.ProcessNextAsync(Store, CancellationToken.None);
			Assert.That(attempted, Is.EqualTo(2)); Assert.That(committed.Count, Is.EqualTo(1));
		}
		[Test]
		public async Task Malformed_or_mismatched_envelopes_are_never_acknowledged_or_persisted()
		{
			await _queue.EnqueueAsync(Row(), CancellationToken.None); _properties.MessageId = Guid.NewGuid().ToString("D");
			var stored = false;
			Assert.ThrowsAsync<ArgumentException>(async () => await _queue.ProcessNextAsync((_, _) => { stored = true; return Task.CompletedTask; }, CancellationToken.None));
			Assert.That(stored, Is.False); _channel.Verify(c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
			_channel.Verify(c => c.BasicRejectAsync(17, false, It.IsAny<CancellationToken>()), Times.Once, "A poison envelope must not be requeued ahead of valid evidence.");
		}
		[Test]
		public async Task Store_failure_is_not_rejected_so_the_envelope_is_redelivered()
		{
			await _queue.EnqueueAsync(Row(), CancellationToken.None);
			Assert.ThrowsAsync<IOException>(async () => await _queue.ProcessNextAsync((_, _) => throw new IOException("simulated outage"), CancellationToken.None));
			_channel.Verify(c => c.BasicRejectAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
		}
		[Test]
		public async Task Empty_queue_does_not_invoke_the_writer()
		{
			Assert.That(await _queue.ProcessNextAsync((_, _) => throw new InvalidOperationException(), CancellationToken.None), Is.EqualTo(DispatchTraceReceiveResult.Empty));
		}
		[Test]
		public void Protection_flag_cannot_disguise_plaintext_as_an_envelope()
		{
			var row = Row(); row.IsProtected = true; row.ProtectedCatalogVersion = 30;
			Assert.ThrowsAsync<ArgumentException>(async () => await _queue.EnqueueAsync(row, CancellationToken.None));
			Assert.That(_body, Is.Null);
		}
		[Test]
		public void Invalid_tenant_metadata_cannot_be_queued()
		{
			var row = Row(); row.DepartmentId = 8;
			Assert.ThrowsAsync<ArgumentException>(async () => await _queue.EnqueueAsync(row, CancellationToken.None));
			Assert.That(_body, Is.Null);
		}
	}
}
