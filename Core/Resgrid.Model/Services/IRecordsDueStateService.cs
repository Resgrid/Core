using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Worker command 42 (RmsDueStateEvaluationCommand, RMS plan section 4.7 / RMS-3): once a day per activated
	/// department, work out what every open Record is late for, and emit trigger 112 and notification 32 exactly
	/// on the transition into overdue.
	/// <para>
	/// The guarantee the plan asks for — at most once per record and due-state transition — is carried by the
	/// persisted <see cref="RmsRecordDueState"/> row, never inferred from when the worker last ran. A run that is
	/// missed emits late rather than not at all; a run that repeats emits nothing the first one already did.
	/// </para>
	/// </summary>
	public interface IRecordsDueStateService
	{
		Task<RecordsDueStateSweepResult> SweepAsync(CancellationToken cancellationToken = default);

		/// <summary>Evaluates one department; public so a single department can be driven and tested directly.</summary>
		Task<RecordsDueStateSweepResult> EvaluateDepartmentAsync(int departmentId, CancellationToken cancellationToken = default);

		/// <summary>Clears every open obligation on a Record — called when it is finalized, voided or cancelled.</summary>
		Task ClearForRecordAsync(int departmentId, string recordId, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Worker command 43 (RmsRetentionAndPurgeCommand, RMS plan RMS-3): retention, legal hold and attachment
	/// purge, plus the rescan pass for attachments the scanner could not reach when they were uploaded.
	/// <para>
	/// A purge leaves a content-free tombstone (plan section 4.9): the Record row, its number and its lifecycle
	/// history survive so a reference to it still resolves, and only the content goes. Nothing under an active
	/// legal hold is purged, and the refusal is audited rather than silent.
	/// </para>
	/// </summary>
	public interface IRecordsRetentionService
	{
		Task<RecordsRetentionSweepResult> SweepAsync(CancellationToken cancellationToken = default);

		Task<RecordsRetentionSweepResult> ProcessDepartmentAsync(int departmentId, CancellationToken cancellationToken = default);
	}
}
