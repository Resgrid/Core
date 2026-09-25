using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Resgrid.Model.AdminAssist
{
	public enum DispatchTraceStage { Selected, Excluded, Attempted, ServiceCompleted, ServiceDeclined, Failed, Skipped, BroadcastStarted, BroadcastCompleted, ProviderAccepted, ProviderDeclined, ProviderResultUnknown }
	public enum DispatchTraceChannel { Routing, Push, Sms, Email, Voice, UnitPush }
	public enum DispatchTraceProvider { None, Twilio, SignalWire, Postmark }
	public enum DispatchTraceReason { None, DuplicateRoute, EmptyShiftFallback, ProfileMissing, BroadcastDisabled, MemberIneligible, ChannelDisabled, ContactUnverified, SendException }
	public sealed record DispatchTraceObservation(string Id, int DepartmentId, int CallId, int? QueueItemId,
		string AttemptId, DateTime OccurredOnUtc, DispatchTraceStage Stage, DispatchTraceChannel Channel,
		DispatchTraceReason Reason, string RecipientId, DispatchRouteKind? RouteKind, string SourceId, DateTime? InputAsOfUtc,
		int Sequence = 0, int PriorDropped = 0, string ResolverVersion = null,
		string LogicalMessageId = null, DispatchTraceProvider Provider = DispatchTraceProvider.None, string ProviderMessageId = null);

	/// <summary>Bounded, non-blocking capture. Nothing here contacts a database, broker, logger or model.</summary>
	public static class DispatchTraceTelemetry
	{
		private sealed class Context { public int DepartmentId; public int CallId; public int? QueueItemId; public string AttemptId; public int Count; public int Dropped; }
		private static readonly AsyncLocal<Context> Current = new();
		private sealed record ChannelContext(string LogicalMessageId, string RecipientId);
		private static readonly AsyncLocal<ChannelContext> Sending = new();
		private static readonly Channel<DispatchTraceObservation> Queue = Channel.CreateBounded<DispatchTraceObservation>(new BoundedChannelOptions(2048) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, AllowSynchronousContinuations = false });
		private static long _dropped;
		public static long Dropped => Interlocked.Read(ref _dropped);
		public static ChannelReader<DispatchTraceObservation> Reader => Queue.Reader;
		public static IDisposable Begin(int departmentId, int callId, int? queueItemId, bool enabled)
		{
			var previous = Current.Value;
			Current.Value = enabled ? new Context { DepartmentId = departmentId, CallId = callId, QueueItemId = queueItemId, AttemptId = Guid.NewGuid().ToString("D") } : null;
			return new Scope(() => Current.Value = previous);
		}
		public static void Observe(DispatchTraceStage stage, DispatchTraceChannel channel = DispatchTraceChannel.Routing,
			DispatchTraceReason reason = DispatchTraceReason.None, string recipientId = null, DispatchRouteKind? routeKind = null, string sourceId = null, DateTime? inputAsOfUtc = null,
			DispatchTraceProvider provider = DispatchTraceProvider.None, string providerMessageId = null)
		{
			var context = Current.Value;
			if (context == null) return;
			// IDs only, never provider bodies, phone numbers, addresses, exception messages or call text.
			var sequence = Interlocked.Increment(ref context.Count);
			if (sequence > 10000 && stage != DispatchTraceStage.BroadcastCompleted || recipientId?.Length > 128 || sourceId?.Length > 128)
			{
				Interlocked.Increment(ref context.Dropped); Interlocked.Increment(ref _dropped); return;
			}
			var observation = new DispatchTraceObservation(Guid.NewGuid().ToString("D"), context.DepartmentId, context.CallId, context.QueueItemId,
				context.AttemptId, DateTime.UtcNow, stage, channel, reason, recipientId, routeKind, sourceId, inputAsOfUtc,
				sequence, Volatile.Read(ref context.Dropped), DispatchRecipientResolver.Version, Sending.Value?.LogicalMessageId, provider, providerMessageId);
			if (!Queue.Writer.TryWrite(observation)) { Interlocked.Increment(ref context.Dropped); Interlocked.Increment(ref _dropped); }
		}
		public static async Task<bool> AttemptAsync(DispatchTraceChannel channel, string recipientId, Func<Task<bool>> send)
		{
			var previous = Sending.Value;
			Sending.Value = Current.Value == null ? null : new ChannelContext(Guid.NewGuid().ToString("D"), recipientId);
			Observe(DispatchTraceStage.Attempted, channel, recipientId: recipientId);
			try
			{
				var result = await send();
				// The existing service's boolean is not a provider receipt or proof of delivery.
				Observe(result ? DispatchTraceStage.ServiceCompleted : DispatchTraceStage.ServiceDeclined, channel, recipientId: recipientId);
				return result;
			}
			catch { Observe(DispatchTraceStage.Failed, channel, DispatchTraceReason.SendException, recipientId); throw; }
			finally { Sending.Value = previous; }
		}
		/// <summary>Creation response only, never a delivery receipt. Called at the actual provider boundary.</summary>
		public static void ProviderResult(DispatchTraceProvider provider, DispatchTraceChannel transport, string messageId, bool? accepted)
		{
			var sending = Sending.Value;
			if (Current.Value == null || sending == null || provider == DispatchTraceProvider.None || !Enum.IsDefined(provider)) return;
			if (!IsProviderMessageId(provider, messageId)) { messageId = null; if (accepted == true) accepted = null; }
			Observe(accepted == true ? DispatchTraceStage.ProviderAccepted : accepted == false ? DispatchTraceStage.ProviderDeclined : DispatchTraceStage.ProviderResultUnknown,
				transport, recipientId: sending.RecipientId, provider: provider, providerMessageId: messageId);
		}
		public static bool IsProviderMessageId(DispatchTraceProvider provider, string id)
		{
			if (string.IsNullOrWhiteSpace(id) || id.Length > 64) return false;
			if (provider is DispatchTraceProvider.Postmark or DispatchTraceProvider.SignalWire && Guid.TryParseExact(id, "D", out var guid) && guid != Guid.Empty) return true;
			if (provider is DispatchTraceProvider.Twilio or DispatchTraceProvider.SignalWire && id.Length == 34 && (id.StartsWith("SM", StringComparison.Ordinal) || id.StartsWith("MM", StringComparison.Ordinal) || id.StartsWith("CA", StringComparison.Ordinal)))
			{
				for (var i = 2; i < id.Length; i++) if (!Uri.IsHexDigit(id[i])) return false;
				return true;
			}
			return false;
		}
		private sealed class Scope(Action dispose) : IDisposable { public void Dispose() => dispose(); }
	}
	[Table("AdminAssistDispatchTraces")]
	public sealed class AdminAssistDispatchTraceRow : IEntity
	{
		[Key, DatabaseGenerated(DatabaseGeneratedOption.None)] public string AdminAssistDispatchTraceId { get; set; }
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string AttemptId { get; set; }
		public string Stage { get; set; }
		public string ResolverVersion { get; set; }
		public DateTime OccurredOn { get; set; }
		public string Content { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }
		[NotMapped] public object IdValue { get => AdminAssistDispatchTraceId; set => AdminAssistDispatchTraceId = value?.ToString(); }
		[NotMapped] public string TableName => "AdminAssistDispatchTraces";
		[NotMapped] public string IdName => nameof(AdminAssistDispatchTraceId);
		[NotMapped] public int IdType => 1;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { nameof(IdValue), nameof(TableName), nameof(IdName), nameof(IdType) };
	}
	public interface IAdminAssistTraceWriter
	{
		/// <summary>Returns null when new trace capture is disabled for this host or department.</summary>
		Task<AdminAssistDispatchTraceRow> PrepareAsync(DispatchTraceObservation observation, CancellationToken ct);
		Task PersistPreparedAsync(AdminAssistDispatchTraceRow row, CancellationToken ct);
		Task PersistAsync(DispatchTraceObservation observation, CancellationToken ct);
	}
	public interface IAdminAssistTraceStore
	{
		Task<bool> TraceDepartmentExistsAsync(int departmentId, CancellationToken ct);
		Task SaveTraceAsync(AdminAssistDispatchTraceRow row, CancellationToken ct);
	}
	public enum DispatchTraceReceiveResult { Empty, Persisted }
	/// <summary>Only prepared envelopes cross the durable queue. The sender never awaits this interface.</summary>
	public interface IAdminAssistTraceQueue
	{
		Task EnqueueAsync(AdminAssistDispatchTraceRow row, CancellationToken ct);
		Task<DispatchTraceReceiveResult> ProcessNextAsync(Func<AdminAssistDispatchTraceRow, CancellationToken, Task> persist, CancellationToken ct);
	}
}
