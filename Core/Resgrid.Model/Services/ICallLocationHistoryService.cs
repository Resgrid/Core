using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// "Previous calls at this location": the calls related to a call, a contact or an occupancy, found by contact link
	/// and by address, where addresses are compared parsed rather than as text ("110 S Main St" = "110 South Main"), plus
	/// a proximity fallback for calls without a street address. Every read is limited to the caller's department and to
	/// the calls their dispatch scope lets them see. Address matching needs the call location index (registry M0259),
	/// which is kept only while the department's Advanced Data Protection policy is Disabled; with protection on, only
	/// contact links are searched and <see cref="CallLocationHistoryResult.AddressMatchingAvailable"/> is false.
	/// </summary>
	public interface ICallLocationHistoryService
	{
		/// <summary>Write path: (re)indexes one call after it is saved. Never throws; a failure is logged and the backfill will not revisit it.</summary>
		Task IndexCallAsync(Call call, CancellationToken cancellationToken = default);

		/// <summary>Worker 73: suppress/resume for data protection changes, then backfill departments newest call first, within <paramref name="budget"/>.</summary>
		Task<CallLocationIndexSweepResult> RunIndexSweepAsync(TimeSpan budget, CancellationToken cancellationToken = default);

		/// <summary>Other calls at the call's location or linked to the call's contacts.</summary>
		Task<CallLocationHistoryResult> GetHistoryForCallAsync(int departmentId, string userId, int callId, int limit = CallLocationQuery.DefaultLimit);

		/// <summary>Calls linked to the contact, plus calls at any occupancy the contact is linked to.</summary>
		Task<CallLocationHistoryResult> GetHistoryForContactAsync(int departmentId, string userId, string contactId, int limit = CallLocationQuery.DefaultLimit);

		/// <summary>Calls at the occupancy's location, plus calls linked to its contacts that carry no location of their own.</summary>
		Task<CallLocationHistoryResult> GetHistoryForOccupancyAsync(int departmentId, string userId, string occupancyId, int limit = CallLocationQuery.DefaultLimit);

		/// <summary>The general form (an address being entered on a new call, say).</summary>
		Task<CallLocationHistoryResult> GetHistoryAsync(int departmentId, string userId, CallLocationQuery query);

		/// <summary>Linked call counts per contact for the contacts list; null when the user's dispatch scope is not department-wide (counts are not filtered call by call).</summary>
		Task<Dictionary<string, int>> GetCallCountsForContactsAsync(int departmentId, string userId);

		/// <summary>Call counts per occupancy for the occupancies list; null when the user's dispatch scope is not department-wide.</summary>
		Task<Dictionary<string, int>> GetCallCountsForOccupanciesAsync(int departmentId, string userId, IEnumerable<string> occupancyIds);
	}
}
