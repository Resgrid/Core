using System;
using Resgrid.Model.Events;

namespace Resgrid.Model
{
	/// <summary>Why a run was recorded as Skipped without executing (RMS plan section 5.6: Records events are never silently dropped).</summary>
	public static class WorkflowRunSkipReasons
	{
		public const string RateLimit = "RateLimit";
		public const string DailyLimit = "DailyLimit";
	}

	/// <summary>
	/// Carries the domain-event envelope onto a WorkflowRun (plan section 5.6): the immutable EventId that
	/// deduplicates runs per (WorkflowId, EventId), plus the correlation, causation, sequence and origin data
	/// that run history shows. Pure functions so the provider's behaviour is testable without a container.
	/// </summary>
	public static class WorkflowRunEnvelope
	{
		public static WorkflowRun Apply(WorkflowRun run, DomainEventDispatchedEvent envelope)
		{
			if (run == null)
				throw new ArgumentNullException(nameof(run));

			if (envelope == null)
				return run;

			run.EventId = string.IsNullOrWhiteSpace(envelope.EventId) ? null : envelope.EventId;
			run.EventSchemaVersion = envelope.SchemaVersion;
			run.CorrelationId = envelope.CorrelationId;
			run.CausationId = envelope.CausationId;
			run.RecordSequence = envelope.Sequence;
			run.OriginClient = envelope.OriginClient;
			run.AggregateId = envelope.AggregateId;
			return run;
		}

		/// <summary>A run that was suppressed before execution: durable, visible in history, never a transition.</summary>
		public static WorkflowRun MarkSkipped(WorkflowRun run, string reason, DateTime utcNow)
		{
			if (run == null)
				throw new ArgumentNullException(nameof(run));

			run.Status = (int)WorkflowRunStatus.Skipped;
			run.SkipReason = string.IsNullOrWhiteSpace(reason) ? null : reason;
			run.CompletedOn = utcNow;
			run.ErrorMessage = null;
			return run;
		}

		/// <summary>
		/// The unique index on (WorkflowId, EventId) is the dedup backstop for two dispatchers racing on the same
		/// event (post-commit dispatcher and worker sweep). Without a provider-specific exception type at this
		/// layer, the violation is recognised by the messages SQL Server and PostgreSQL emit.
		/// </summary>
		public static bool IsDuplicateKeyViolation(Exception exception)
		{
			for (var ex = exception; ex != null; ex = ex.InnerException)
			{
				var message = ex.Message ?? string.Empty;
				if (message.Contains("UX_WorkflowRuns_Workflow_Event", StringComparison.OrdinalIgnoreCase) ||
					message.Contains("ux_workflowruns_workflow_event", StringComparison.OrdinalIgnoreCase) ||
					message.Contains("23505", StringComparison.Ordinal) ||
					message.Contains("Cannot insert duplicate key", StringComparison.OrdinalIgnoreCase) ||
					message.Contains("duplicate key value violates unique constraint", StringComparison.OrdinalIgnoreCase))
					return true;
			}

			return false;
		}
	}
}
