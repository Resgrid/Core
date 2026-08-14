using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// CRUD and trigger matching for run cards (CAD-style response plans) and station
	/// coverage requirements. Recommendation/selection logic lives in
	/// IDispatchRecommendationService; this service owns persistence and "which card
	/// applies to this call" resolution.
	/// </summary>
	public interface IRunCardsService
	{
		/// <summary>All run cards for the department, fully hydrated (triggers, alarm levels with requirements, selections). Cached.</summary>
		Task<List<RunCard>> GetAllRunCardsForDepartmentAsync(int departmentId, bool bypassCache = false);

		/// <summary>One run card, fully hydrated. Null when not found.</summary>
		Task<RunCard> GetRunCardByIdAsync(int runCardId);

		/// <summary>
		/// Saves the card header and replaces its child rows (triggers, alarm levels,
		/// requirements, selections) to match the supplied graph. Invalidates the cache.
		/// </summary>
		Task<RunCard> SaveRunCardAsync(RunCard runCard, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> DeleteRunCardAsync(int runCardId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Best-matching enabled run card for a call's priority/type, or null. Specificity
		/// wins (PriorityAndType over Type over Priority); ties break to the newest card.
		/// The call type is matched by name (trimmed, case-insensitive) per the Call.Type
		/// convention.
		/// </summary>
		Task<RunCard> GetMatchingRunCardAsync(int departmentId, int priority, string callTypeName);

		Task<List<StationCoverageRequirement>> GetStationCoverageRequirementsForDepartmentAsync(int departmentId);

		Task<StationCoverageRequirement> SaveStationCoverageRequirementAsync(StationCoverageRequirement requirement, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> DeleteStationCoverageRequirementAsync(int stationCoverageRequirementId, int departmentId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Most recent dispatch time per unit (rest-period input).</summary>
		Task<Dictionary<int, DateTime>> GetLastUnitDispatchTimesAsync(int departmentId);

		/// <summary>Most recent dispatch time per user (rest-period input).</summary>
		Task<Dictionary<string, DateTime>> GetLastUserDispatchTimesAsync(int departmentId);
	}
}
