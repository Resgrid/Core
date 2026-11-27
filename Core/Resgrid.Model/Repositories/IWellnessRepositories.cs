using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Member exposure and critical-incident entries (§13b WL0, M0360).</summary>
	public interface IRmsResponderExposuresRepository : IRepository<RmsResponderExposure>
	{
		Task<RmsResponderExposure> GetByIdForDepartmentAsync(int departmentId, string exposureId);

		/// <summary>Every entry about one member, any status, newest first.</summary>
		Task<IEnumerable<RmsResponderExposure>> GetForUserAsync(int departmentId, string userId);

		/// <summary>Every entry in the department that happened in the window (either bound optional), newest first.</summary>
		Task<IEnumerable<RmsResponderExposure>> GetForDepartmentAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc);

		Task<IEnumerable<RmsResponderExposure>> GetForCallAsync(int departmentId, int callId);

		Task<IEnumerable<RmsResponderExposure>> GetFromFlagAsync(int departmentId, string callExposureFlagId);

		/// <summary>Removes the still-unconfirmed suggestions a flag created (the flag was cleared). Confirmed entries stay.</summary>
		Task<int> DeleteSuggestionsFromFlagAsync(int departmentId, string callExposureFlagId, CancellationToken cancellationToken = default);

		/// <summary>Departments with a confirmed, opted-in entry whose next anniversary is on or before <paramref name="dueBy"/>.</summary>
		Task<IEnumerable<int>> GetDepartmentsWithDueAnniversariesAsync(DateTime dueBy, int take);

		Task<IEnumerable<RmsResponderExposure>> GetDueAnniversariesAsync(int departmentId, DateTime dueBy, int take);

		/// <summary>
		/// Moves an entry's next anniversary on, only while it still holds <paramref name="expected"/>: the claim that keeps two
		/// worker runs from sending the same reminder twice.
		/// </summary>
		Task<bool> ClaimAnniversaryAsync(int departmentId, string exposureId, DateTime expected, DateTime next, DateTime sentOn, CancellationToken cancellationToken = default);

		/// <summary>Departments holding an entry that happened before <paramref name="cutoff"/> (retention candidates).</summary>
		Task<IEnumerable<int>> GetDepartmentsWithEntriesBeforeAsync(DateTime cutoff, int take);

		Task<IEnumerable<RmsResponderExposure>> GetBeforeAsync(int departmentId, DateTime cutoff, int take);

		Task<int> DeleteForDepartmentAsync(int departmentId, string exposureId, CancellationToken cancellationToken = default);
	}

	/// <summary>Post-call critical-incident and exposure flags on calls (§13b WL0, M0360).</summary>
	public interface IRmsCallExposureFlagsRepository : IRepository<RmsCallExposureFlag>
	{
		Task<RmsCallExposureFlag> GetByIdForDepartmentAsync(int departmentId, string flagId);

		Task<IEnumerable<RmsCallExposureFlag>> GetForCallAsync(int departmentId, int callId);

		/// <summary>Recent flags of the department, newest first.</summary>
		Task<IEnumerable<RmsCallExposureFlag>> GetRecentForDepartmentAsync(int departmentId, int take);

		/// <summary>Active flags whose suggestions were not built yet, oldest first, across departments (worker 91).</summary>
		Task<IEnumerable<RmsCallExposureFlag>> GetPendingAsync(int take);

		/// <summary>Marks the flag's suggestions as being built, only while nobody else has; false when another run claimed it.</summary>
		Task<bool> ClaimGenerationAsync(int departmentId, string flagId, DateTime generatedOn, CancellationToken cancellationToken = default);

		Task<int> SetSuggestionCountAsync(int departmentId, string flagId, int count, CancellationToken cancellationToken = default);

		/// <summary>Gives a claimed flag back after a failure, so the next run tries again.</summary>
		Task<int> ReleaseGenerationAsync(int departmentId, string flagId, CancellationToken cancellationToken = default);
	}

	/// <summary>At-risk screening flags on members (§13b WL0, M0360).</summary>
	public interface IRmsResponderScreeningFlagsRepository : IRepository<RmsResponderScreeningFlag>
	{
		Task<RmsResponderScreeningFlag> GetByIdForDepartmentAsync(int departmentId, string flagId);

		Task<RmsResponderScreeningFlag> GetActiveForUserAsync(int departmentId, string userId);

		Task<IEnumerable<RmsResponderScreeningFlag>> GetActiveForDepartmentAsync(int departmentId);

		Task<IEnumerable<RmsResponderScreeningFlag>> GetForUserAsync(int departmentId, string userId);
	}
}
