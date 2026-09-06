using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Work assignments on Records (RMS plan section 5.2 RmsRecordWorkAssignment, RMS-1D): assign, acknowledge,
	/// complete, cancel, and the per-caller queue. An assignment narrows a work queue; it never replaces live
	/// authorization, so every queue read re-checks record visibility.
	/// </summary>
	public interface IRecordWorkAssignmentsService
	{
		Task<RmsRecordWorkAssignment> AssignAsync(int departmentId, string userId, RecordWorkAssignmentInput input, CancellationToken cancellationToken = default);

		Task<RmsRecordWorkAssignment> AcknowledgeAsync(int departmentId, string userId, string assignmentId, long? expectedRowVersion, FieldRecordContext context, RmsOriginClient origin, CancellationToken cancellationToken = default);

		Task<RmsRecordWorkAssignment> CompleteAsync(int departmentId, string userId, string assignmentId, long? expectedRowVersion, FieldRecordContext context, RmsOriginClient origin, CancellationToken cancellationToken = default);

		Task<RmsRecordWorkAssignment> CancelAsync(int departmentId, string userId, string assignmentId, long? expectedRowVersion, string reason, RmsOriginClient origin, CancellationToken cancellationToken = default);

		Task<RmsRecordWorkAssignment> GetAsync(int departmentId, string userId, string assignmentId);

		Task<List<RmsRecordWorkAssignment>> GetForRecordAsync(int departmentId, string userId, string recordId);

		/// <summary>Open and acknowledged assignments addressed to the caller as a person, through a staffed unit, their group, or a held command/dispatch role.</summary>
		Task<List<RmsRecordWorkAssignment>> GetQueueAsync(int departmentId, string userId, FieldRecordContext context, int take);

		/// <summary>Whether the caller is an addressee of the assignment in the given context.</summary>
		Task<bool> IsAssigneeAsync(int departmentId, string userId, RmsRecordWorkAssignment assignment, FieldRecordContext context);
	}
}
