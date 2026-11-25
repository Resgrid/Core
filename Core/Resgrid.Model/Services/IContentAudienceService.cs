using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Resolves content audience rules (<see cref="ContentAudienceKinds"/>) to active members. Shared by Training (T0) and
	/// Protocols (P1): neither owner's rules live here, only the resolution. Only active members are returned (not deleted,
	/// disabled, hidden or dormant); user ids compare without regard to case.
	/// </summary>
	public interface IContentAudienceService
	{
		/// <summary>The active members the rules resolve to, and the picked users (User rules) that were skipped and why.</summary>
		Task<ContentAudienceResolution> ResolveAsync(int departmentId, IEnumerable<IContentAudienceRule> rules);

		/// <summary>What one member belongs to, read once, for testing them against many rule sets.</summary>
		Task<ContentAudienceMemberFacts> GetMemberFactsAsync(int departmentId, string userId);

		Task<bool> IsUserInAudienceAsync(int departmentId, string userId, IEnumerable<IContentAudienceRule> rules);

		/// <summary>Why each of the picked users cannot be addressed (not in the department, deleted, disabled, hidden, dormant).</summary>
		Task<List<ContentAudienceSkip>> ExplainSkippedAsync(int departmentId, IEnumerable<string> pickedUserIds);
	}

	/// <summary>
	/// Queues a membership change for worker 83 (group, role, shift, certification, unit crew, joining the department).
	/// Never throws: a failed enqueue is logged and must not fail the membership write.
	/// </summary>
	public interface IContentAudienceChangeQueue
	{
		/// <summary>Queues one member (or the whole department when <paramref name="userId"/> is null).</summary>
		Task EnqueueAsync(int departmentId, string userId, ContentAudienceChangeSources source, CancellationToken cancellationToken = default);

		Task EnqueueManyAsync(int departmentId, IEnumerable<string> userIds, ContentAudienceChangeSources source, CancellationToken cancellationToken = default);
	}

	/// <summary>Member leave of absence (R-C2): pauses training due dates and reminders, shifts them on return.</summary>
	public interface IMemberLeaveService
	{
		Task<List<MemberLeavePeriod>> GetForDepartmentAsync(int departmentId);

		Task<List<MemberLeavePeriod>> GetForMemberAsync(int departmentId, string userId);

		/// <summary>The department's leave periods by member (case-insensitive ids).</summary>
		Task<Dictionary<string, List<MemberLeavePeriod>>> GetByMemberAsync(int departmentId);

		/// <summary>Saves a leave period from department-local dates (start of the first day to end of the last). Audited.</summary>
		Task<MemberLeavePeriod> SaveAsync(int departmentId, string userId, DateTime startLocalDate, DateTime endLocalDate, MemberLeaveKinds kind, string note,
			string actingUserId, int memberLeavePeriodId = 0, CancellationToken cancellationToken = default);

		Task<bool> DeleteAsync(int departmentId, int memberLeavePeriodId, string actingUserId, CancellationToken cancellationToken = default);
	}

	/// <summary>The member's My Training page (web and v4): assignments by state, the catalog and certifications due.</summary>
	public interface IMyTrainingService
	{
		Task<MyTrainingOverview> GetOverviewAsync(int departmentId, string userId, DateTime utcNow);

		Task<bool> PinCertificationAsync(int departmentId, string userId, int personnelCertificationId, bool pinned, CancellationToken cancellationToken = default);
	}
}
