using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// The call location index (registry M0259) and the reads the location history runs against it. Candidate reads
	/// join Calls so deleted calls never surface, and always cap at <c>take</c> newest-first.
	/// </summary>
	public interface ICallLocationKeysRepository
	{
		Task UpsertAsync(IEnumerable<CallLocationKey> keys, CancellationToken cancellationToken = default);
		Task DeleteForCallAsync(int callId, CancellationToken cancellationToken = default);
		Task<int> DeleteForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default);

		Task<List<CallLocationCandidate>> GetByAddressKeyAsync(int departmentId, string addressKey, int take);
		Task<List<CallLocationCandidate>> GetWithinBoundsAsync(int departmentId, decimal minLatitude, decimal maxLatitude, decimal minLongitude, decimal maxLongitude, int take);
		/// <summary>Calls linked to any of the contacts, with their index row when they have one.</summary>
		Task<List<CallLocationCandidate>> GetContactCallCandidatesAsync(int departmentId, IEnumerable<string> contactIds, int take);
		/// <summary>Live linked calls per contact, for every contact in the department that has any.</summary>
		Task<Dictionary<string, int>> GetCallCountsByContactAsync(int departmentId);

		Task<List<Call>> GetCallsAsync(int departmentId, IEnumerable<int> callIds);
		Task<List<CallNote>> GetNotesForCallsAsync(IEnumerable<int> callIds);

		Task<CallLocationIndexState> GetStateAsync(int departmentId);
		Task SaveStateAsync(CallLocationIndexState state, CancellationToken cancellationToken = default);
		/// <summary>Departments with no state row, or one that is neither complete at <paramref name="keyVersion"/> nor suppressed.</summary>
		Task<List<int>> GetDepartmentsNeedingIndexAsync(int keyVersion, int take);
		/// <summary>Unsuppressed departments whose data protection policy is no longer Disabled.</summary>
		Task<List<int>> GetDepartmentsToSuppressAsync(int take);
		/// <summary>Suppressed departments whose data protection policy is Disabled again (or gone).</summary>
		Task<List<int>> GetDepartmentsToResumeAsync(int take);
		/// <summary>Live calls below <paramref name="beforeCallId"/> (all when null), highest id first.</summary>
		Task<List<CallLocationSource>> GetSourcesAsync(int departmentId, int? beforeCallId, int take);
	}
}
