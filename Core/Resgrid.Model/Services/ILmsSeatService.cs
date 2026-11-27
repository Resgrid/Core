using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// LMS seats (§17.4 seat rules, decision 7, §25.4a item 5): purchased seats are the add-on's pack quantity × the pack size;
	/// only active members can hold one; enabling beyond the purchased seats is refused with an offer to add a pack; seats are per
	/// department. Instructors, evaluators and officers need none: only being trained through LMS features uses a seat. Each call
	/// asks the LMS gate once; nothing here runs from a sweep (R-L9).
	/// </summary>
	public interface ILmsSeatService
	{
		Task<LmsSeatSummary> GetSummaryAsync(int departmentId);

		/// <summary>The members who can be trained through LMS features now (see <see cref="LmsSeatSummary.LicensedUserIds"/>).</summary>
		Task<HashSet<string>> GetLicensedUserIdsAsync(int departmentId);

		/// <summary>Whether <paramref name="additional"/> more members could be enabled now, and if not, why.</summary>
		Task<LmsSeatChangeResult> CanEnableAsync(int departmentId, int additional = 1);

		Task<LmsSeatChangeResult> SetSeatAsync(int departmentId, string userId, bool enabled, string actingUserId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Enables or disables every member of a group. Enabling stops at the purchased seats (members are taken in name order) and
		/// reports how many were refused; inactive members are skipped.
		/// </summary>
		Task<LmsSeatBulkResult> SetSeatsForGroupAsync(int departmentId, int departmentGroupId, bool enabled, string actingUserId, CancellationToken cancellationToken = default);

		/// <summary>The fewest packs that still seat every enabled member (a pack decrease never goes below it), never below the floor.</summary>
		Task<int> GetMinimumPacksAsync(int departmentId);
	}

	/// <summary>
	/// The LMS add-on's billing (pack add-on path, R-L10): proxy for the Billing API's LmsBilling controller (int-CommonApis, owed).
	/// Returns null / false when billing is not configured or unreachable, so callers fail closed; checkout is never an entitlement.
	/// </summary>
	public interface ILmsBillingService
	{
		Task<LmsBillingStatus> GetAsync(int departmentId);

		/// <summary>Starts (or resumes) the department's checkout for <paramref name="packs"/> packs.</summary>
		Task<LmsBillingCheckout> BeginCheckoutAsync(int departmentId, int packs);

		/// <summary>
		/// Sets the pack quantity: an increase is invoiced at once, prorated, by the provider; a decrease is scheduled for the next
		/// billing date. <paramref name="expectedPacks"/> is the quantity the admin saw, so a stale page cannot apply twice.
		/// </summary>
		Task<LmsPackChangeResult> ChangePacksAsync(int departmentId, int packs, int expectedPacks);

		Task<bool> CancelRenewalAsync(int departmentId);
	}

	/// <summary>
	/// Lapse rule 12 (§17.3, R-L7): when the add-on comes back, paused due dates are re-based: time spent lapsed does not count
	/// toward lateness (due dates shift by the lapse), and nothing becomes overdue or enforceable until the reactivation grace has
	/// passed. The requirement engine (T3) calls this; it never runs from a sweep of every department (R-L9).
	/// </summary>
	public interface ILmsReactivationService
	{
		/// <summary>The department's closed lapses, oldest first.</summary>
		Task<List<LmsAddonLapse>> GetLapsesAsync(int departmentId);

		/// <summary>The most recent reactivation (the end of the last closed lapse), UTC; null when the add-on never came back from a lapse.</summary>
		Task<DateTime?> GetLastReactivationAsync(int departmentId);

		/// <summary>
		/// Re-bases the items against every closed lapse of the department (an open lapse is still paused and moves nothing yet).
		/// Returns one result per item, in order.
		/// </summary>
		Task<List<LmsRebasedDue>> RebaseAsync(int departmentId, IEnumerable<LmsDueItem> items);
	}
}
