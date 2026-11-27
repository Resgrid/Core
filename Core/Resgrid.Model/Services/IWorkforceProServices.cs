using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The Workforce Pro entitlement gate (shifts-scheduling-modernization-plan.md §7; owner 2026-11-26: member wellness and workplace exposure
	/// are Workforce Pro). One evaluation is the fresh Workforce.Pro operator flag → billing configured → a live PlanAddonTypes.WorkforcePro
	/// payment window with no entitlement cache → packs × the pack size → the members holding a licensed seat. Fails closed on any error (a
	/// billing failure is read-only, never full access). The wellness services enforce <see cref="WorkforceProAccess"/> themselves, so no
	/// controller, API or worker can bypass it.
	/// </summary>
	public interface IWorkforceProAccessService
	{
		/// <summary>The Workforce.Pro flag is on for the department (navigation; no billing round trip). Fails closed.</summary>
		Task<bool> IsOfferedAsync(int departmentId);

		/// <summary>
		/// The department holds a live Workforce Pro add-on window right now, or a failed renewal inside its seven-day dunning grace (no flag
		/// check): the dashboards' held add-ons. Self-hosted (billing not configured) never holds it.
		/// </summary>
		Task<bool> HasActiveAddonAsync(int departmentId);

		/// <summary>
		/// One evaluation for a request (remembered for the rest of the request's lifetime scope, briefly; never across requests): what the
		/// department may see and do, and who holds a seat.
		/// </summary>
		Task<WorkforceProAccess> GetAccessAsync(int departmentId);

		/// <summary>Forgets the remembered evaluation (after a seat change in the same request).</summary>
		void Invalidate(int departmentId);
	}

	/// <summary>
	/// Workforce Pro seats (§7.2 seat rules, the LMS seat model): purchased seats are the running interval's packs × the pack size; only active
	/// members can hold one; switching one on beyond the purchased seats is refused with the packs to add; seats are per department; switching
	/// one off never deletes a record. Only department administrators switch seats; officers and managers acting on others need their
	/// permission, not a seat of their own.
	/// </summary>
	public interface IWorkforceProSeatService
	{
		Task<WorkforceProSeatSummary> GetSummaryAsync(int departmentId);

		Task<WorkforceProSeatChangeResult> SetSeatAsync(int departmentId, string userId, bool enabled, string actingUserId, CancellationToken cancellationToken = default);

		/// <summary>The fewest packs that still seat every enabled member (a pack decrease never goes below it), never below the floor.</summary>
		Task<int> GetMinimumPacksAsync(int departmentId);
	}

	/// <summary>
	/// The Workforce Pro add-on's billing: proxy for the Billing API's WorkforceProBilling controller (int-CommonApis), the same contract as the
	/// LMS pack add-on (<see cref="LmsBillingStatus"/>, <see cref="LmsBillingCheckout"/>, <see cref="LmsPackChangeResult"/>). Returns null /
	/// false when billing is not configured or unreachable, so callers fail closed; checkout is never an entitlement.
	/// </summary>
	public interface IWorkforceProBillingService
	{
		Task<LmsBillingStatus> GetAsync(int departmentId);

		/// <summary>Starts (or resumes) the department's checkout for <paramref name="packs"/> packs.</summary>
		Task<LmsBillingCheckout> BeginCheckoutAsync(int departmentId, int packs);

		/// <summary>
		/// Sets the pack quantity: an increase is invoiced at once, prorated, by the provider; a decrease is scheduled for the next billing date.
		/// <paramref name="expectedPacks"/> is the quantity the admin saw, so a stale page cannot apply twice.
		/// </summary>
		Task<LmsPackChangeResult> ChangePacksAsync(int departmentId, int packs, int expectedPacks);

		Task<bool> CancelRenewalAsync(int departmentId);
	}
}
