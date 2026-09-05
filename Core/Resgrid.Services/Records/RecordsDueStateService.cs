using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Worker command 42, the bounded RecordOverdue evaluation (RMS plan RMS-3). For every activated department it
	/// works out where each open Record stands against its obligations, writes the answer to
	/// <see cref="RmsRecordDueState"/>, and emits trigger 112 plus notification 32 only where the answer changed
	/// into overdue.
	/// <para>
	/// The plan is explicit that the "at most once per record/due-state transition" guarantee must be carried by
	/// the persisted row rather than inferred from the last run time. That is why nothing here reads a
	/// last-run timestamp: a run that never happened emits late, and a run that happens twice emits once.
	/// </para>
	/// <para>
	/// Bounded by construction: a department is capped at <see cref="MaxRecordsPerDepartment"/> open Records per
	/// sweep and <see cref="MaxEmissionsPerDepartment"/> emissions, so a department that has let a thousand
	/// reports go stale produces a steady trickle of chasing rather than a thousand notifications at 04:00.
	/// </para>
	/// </summary>
	public class RecordsDueStateService : IRecordsDueStateService
	{
		public const int MaxRecordsPerDepartment = 2000;
		public const int MaxEmissionsPerDepartment = 100;

		/// <summary>How long a returned or rejected Record may sit with its author before it counts as overdue.</summary>
		public const int CorrectionGraceHours = 72;

		private readonly IRmsDepartmentCutoversRepository _cutovers;
		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRmsIncidentReportsRepository _incidentReports;
		private readonly IRmsRecordDueStatesRepository _dueStates;
		private readonly IDomainEventOutboxService _outbox;
		private readonly IOutboundQueueProvider _outboundQueue;

		public RecordsDueStateService(IRmsDepartmentCutoversRepository cutovers, IRmsOperationalRecordsRepository records,
			IRmsIncidentReportsRepository incidentReports, IRmsRecordDueStatesRepository dueStates, IDomainEventOutboxService outbox,
			IOutboundQueueProvider outboundQueue)
		{
			_cutovers = cutovers;
			_records = records;
			_incidentReports = incidentReports;
			_dueStates = dueStates;
			_outbox = outbox;
			_outboundQueue = outboundQueue;
		}

		public async Task<RecordsDueStateSweepResult> SweepAsync(CancellationToken cancellationToken = default)
		{
			var result = new RecordsDueStateSweepResult();
			var active = (await _cutovers.GetActiveAsync())?.ToList() ?? new List<RmsDepartmentCutover>();

			foreach (var cutover in active)
			{
				cancellationToken.ThrowIfCancellationRequested();
				result.DepartmentsEvaluated++;

				try
				{
					var department = await EvaluateDepartmentAsync(cutover.DepartmentId, cancellationToken);
					result.RecordsEvaluated += department.RecordsEvaluated;
					result.BecameOverdue += department.BecameOverdue;
					result.Cleared += department.Cleared;
					result.NotificationsSent += department.NotificationsSent;
					result.Errors += department.Errors;
				}
				catch (Exception ex)
				{
					// One department's bad data never stops the sweep for the rest.
					Logging.LogException(ex, $"Due-state evaluation failed for department {cutover.DepartmentId}.");
					result.Errors++;
				}
			}

			result.Message = $"{result.DepartmentsEvaluated} departments, {result.RecordsEvaluated} records, {result.BecameOverdue} newly overdue.";
			return result;
		}

		public async Task<RecordsDueStateSweepResult> EvaluateDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var result = new RecordsDueStateSweepResult { DepartmentsEvaluated = 1 };
			var now = DateTime.UtcNow;
			var emissions = new List<Emission>();
			var observed = new HashSet<string>(StringComparer.Ordinal);

			var open = (await _records.GetOpenAsync(departmentId))?.Take(MaxRecordsPerDepartment).ToList() ?? new List<RmsOperationalRecord>();
			foreach (var record in open)
			{
				cancellationToken.ThrowIfCancellationRequested();
				result.RecordsEvaluated++;
				foreach (var observation in ObserveOperational(record, now))
					await ApplyAsync(departmentId, observation, now, emissions, observed, result, cancellationToken);
			}

			var reports = (await _incidentReports.QueryAsync(departmentId, new RmsIncidentReportQuery
			{
				States = OpenReportStates,
				Skip = 0,
				Take = MaxRecordsPerDepartment
			}))?.ToList() ?? new List<RmsIncidentReport>();

			foreach (var report in reports)
			{
				cancellationToken.ThrowIfCancellationRequested();
				result.RecordsEvaluated++;
				foreach (var observation in ObserveIncidentReport(report, now))
					await ApplyAsync(departmentId, observation, now, emissions, observed, result, cancellationToken);
			}

			// Anything still carrying an open obligation that this pass did not observe has met it (or the Record
			// left the state that created it). Clearing here rather than from the lifecycle transitions keeps the
			// bookkeeping self-healing: a transition that failed to call in still resolves on the next sweep, and
			// a stale Overdue row can never block a later, genuine emission.
			foreach (var stale in (await _dueStates.GetOpenForDepartmentAsync(departmentId, MaxRecordsPerDepartment))?.ToList() ?? new List<RmsRecordDueState>())
			{
				if (observed.Contains(Key(stale.RecordId, (RmsRecordObligation)stale.Obligation)))
					continue;

				stale.LastEmittedState = (int)RmsDueState.Cleared;
				stale.ModifiedOn = now;
				stale.RowVersion += 1;
				await _dueStates.UpdateAsync(stale, cancellationToken, true);
				result.Cleared++;
			}

			// Events and notifications leave only after the state rows are committed, so a crash between the two
			// re-emits at worst nothing: the row already says overdue, so the next run stays silent.
			await _outbox.DispatchAfterCommitAsync(emissions.Where(e => e.OutboxId.HasValue).Select(e => e.OutboxId.Value), cancellationToken);
			foreach (var emission in emissions.Where(e => e.Notification != null))
			{
				try
				{
					await _outboundQueue.EnqueueNotification(emission.Notification);
					result.NotificationsSent++;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Overdue notification for record {emission.RecordId} could not be queued.");
					result.Errors++;
				}
			}

			return result;
		}

		public async Task ClearForRecordAsync(int departmentId, string recordId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(recordId))
				return;

			try
			{
				await _dueStates.ClearForRecordAsync(departmentId, recordId, DateTime.UtcNow, cancellationToken);
			}
			catch (Exception ex)
			{
				// Clearing is bookkeeping; a failure must never fail the lifecycle transition that asked for it.
				Logging.LogException(ex, $"Due states for record {recordId} could not be cleared.");
			}
		}

		private static readonly List<int> OpenReportStates = new List<int>
		{
			(int)RmsRecordState.Draft, (int)RmsRecordState.ReadyForReview, (int)RmsRecordState.Returned,
			(int)RmsRecordState.Approved, (int)RmsRecordState.Rejected
		};

		/// <summary>One obligation's computed standing for one Record.</summary>
		private readonly struct Observation
		{
			public Observation(string recordId, RmsRecordKind kind, RmsRecordObligation obligation, DateTime? dueOn, RmsDueState state, string responsibleUserId, object recordBlock)
			{
				RecordId = recordId;
				Kind = kind;
				Obligation = obligation;
				DueOn = dueOn;
				State = state;
				ResponsibleUserId = responsibleUserId;
				RecordBlock = recordBlock;
			}

			public string RecordId { get; }
			public RmsRecordKind Kind { get; }
			public RmsRecordObligation Obligation { get; }
			public DateTime? DueOn { get; }
			public RmsDueState State { get; }
			public string ResponsibleUserId { get; }
			public object RecordBlock { get; }
		}

		/// <summary>Identity of one obligation on one Record, as the reconciliation pass compares them.</summary>
		private static string Key(string recordId, RmsRecordObligation obligation) => recordId + "|" + (int)obligation;

		private sealed class Emission
		{
			public string RecordId { get; set; }
			public long? OutboxId { get; set; }
			public NotificationItem Notification { get; set; }
		}

		private static IEnumerable<Observation> ObserveOperational(RmsOperationalRecord record, DateTime now)
		{
			var state = (RmsRecordState)record.State;
			var block = RecordsService.RecordBlock(record, null, state);

			if (state == RmsRecordState.ReadyForReview)
			{
				yield return new Observation(record.RmsOperationalRecordId, RmsRecordKind.Operational, RmsRecordObligation.Review,
					record.ReviewDueOn, Standing(record.ReviewDueOn, now), record.ReviewerUserId ?? record.OwnerUserId, block);
			}
			else if (state == RmsRecordState.Returned)
			{
				var due = record.ReturnedOn?.AddHours(CorrectionGraceHours);
				yield return new Observation(record.RmsOperationalRecordId, RmsRecordKind.Operational, RmsRecordObligation.Correction,
					due, Standing(due, now), record.OwnerUserId ?? record.AuthorUserId, block);
			}
		}

		private static IEnumerable<Observation> ObserveIncidentReport(RmsIncidentReport report, DateTime now)
		{
			var state = (RmsRecordState)report.State;
			var block = IncidentReportsService.RecordBlock(report, null, state);

			if (state == RmsRecordState.ReadyForReview)
			{
				yield return new Observation(report.RmsIncidentReportId, RmsRecordKind.IncidentReport, RmsRecordObligation.Review,
					report.ReviewDueOn, Standing(report.ReviewDueOn, now), report.ReviewerUserId ?? report.OwnerUserId, block);
			}
			else if (state == RmsRecordState.Returned)
			{
				var due = report.ReturnedOn?.AddHours(CorrectionGraceHours);
				yield return new Observation(report.RmsIncidentReportId, RmsRecordKind.IncidentReport, RmsRecordObligation.Correction,
					due, Standing(due, now), report.OwnerUserId ?? report.AuthorUserId, block);
			}
			else if (state == RmsRecordState.Rejected)
			{
				// A rejected filing is the department's problem to fix; the clock runs from the rejection.
				var due = report.RejectedOn?.AddHours(CorrectionGraceHours);
				yield return new Observation(report.RmsIncidentReportId, RmsRecordKind.IncidentReport, RmsRecordObligation.Submission,
					due, Standing(due, now), report.OwnerUserId ?? report.AuthorUserId, block);
			}
		}

		/// <summary>An obligation with no deadline is never overdue; the department simply has not set one.</summary>
		private static RmsDueState Standing(DateTime? dueOn, DateTime now)
		{
			if (!dueOn.HasValue)
				return RmsDueState.NotDue;
			if (now >= dueOn.Value)
				return RmsDueState.Overdue;
			return now >= dueOn.Value.AddHours(-24) ? RmsDueState.DueSoon : RmsDueState.NotDue;
		}

		private async Task ApplyAsync(int departmentId, Observation observation, DateTime now, List<Emission> emissions, HashSet<string> observed, RecordsDueStateSweepResult result, CancellationToken cancellationToken)
		{
			observed.Add(Key(observation.RecordId, observation.Obligation));
			var row = await _dueStates.GetAsync(departmentId, observation.RecordId, observation.Obligation);
			var deadlineMoved = row != null && row.DueOn != observation.DueOn;

			if (row == null)
			{
				row = new RmsRecordDueState
				{
					RmsRecordDueStateId = Guid.NewGuid().ToString(),
					DepartmentId = departmentId,
					RecordId = observation.RecordId,
					RecordKind = (int)observation.Kind,
					Obligation = (int)observation.Obligation,
					DueOn = observation.DueOn,
					LastEmittedState = (int)RmsDueState.NotDue,
					ResponsibleUserId = observation.ResponsibleUserId,
					CreatedOn = now,
					ModifiedOn = now,
					RowVersion = 1
				};
				await _dueStates.InsertAsync(row, cancellationToken, true);
			}

			// A moved deadline re-arms emission: the obligation the department is now tracking is a different one.
			var alreadyEmitted = (RmsDueState)row.LastEmittedState == RmsDueState.Overdue && !deadlineMoved;
			var becameOverdue = observation.State == RmsDueState.Overdue && !alreadyEmitted;

			row.DueOn = observation.DueOn;
			row.ResponsibleUserId = observation.ResponsibleUserId;
			row.ModifiedOn = now;

			if (becameOverdue)
			{
				row.LastEmittedState = (int)RmsDueState.Overdue;
				row.LastEmittedOn = now;
				row.OverdueCount += 1;
				result.BecameOverdue++;
			}
			else if (observation.State != RmsDueState.Overdue && (RmsDueState)row.LastEmittedState == RmsDueState.Overdue)
			{
				row.LastEmittedState = (int)observation.State;
				result.Cleared++;
			}
			else if (!becameOverdue && (RmsDueState)row.LastEmittedState != RmsDueState.Overdue)
			{
				row.LastEmittedState = (int)observation.State;
			}

			row.RowVersion += 1;
			await _dueStates.UpdateAsync(row, cancellationToken, true);

			if (!becameOverdue || emissions.Count >= MaxEmissionsPerDepartment)
				return;

			var emission = new Emission { RecordId = observation.RecordId };
			var overdueHours = observation.DueOn.HasValue ? (int)Math.Max(0, (now - observation.DueOn.Value).TotalHours) : 0;

			try
			{
				var entry = await _outbox.EnqueueAsync(departmentId, DomainEventProducers.Records, new DomainEventEnvelope
				{
					EventName = WorkflowTriggerEventType.RecordOverdue.ToString(),
					SchemaVersion = 1,
					AggregateType = observation.Kind == RmsRecordKind.IncidentReport ? IncidentReportsService.IncidentAggregate : DomainEventProducers.RecordsAggregate,
					AggregateId = observation.RecordId,
					AggregateVersion = 0,
					Trigger = WorkflowTriggerEventType.RecordOverdue,
					Payload = new Dictionary<string, object>
					{
						["record"] = observation.RecordBlock,
						["obligation"] = new
						{
							type = observation.Obligation.ToString(),
							due_on = observation.DueOn,
							overdue_hours = overdueHours,
							responsible_user_id = observation.ResponsibleUserId ?? string.Empty,
							overdue_count = row.OverdueCount
						}
					},
					CorrelationId = observation.RecordId,
					OriginClient = RmsOriginClient.System
				}, cancellationToken);
				emission.OutboxId = entry.DomainEventOutboxId;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Overdue event for record {observation.RecordId} could not be enqueued.");
				result.Errors++;
			}

			if (!string.IsNullOrWhiteSpace(observation.ResponsibleUserId))
			{
				emission.Notification = new NotificationItem
				{
					DepartmentId = departmentId,
					Type = (int)EventTypes.RecordReviewOverdue,
					Value = observation.RecordId + "|" + (int)observation.Obligation,
					UserId = observation.ResponsibleUserId
				};
			}

			emissions.Add(emission);
		}
	}
}
