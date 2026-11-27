using System;
using Resgrid.Model.Services;
using RestSharp;

namespace Resgrid.Services
{
	/// <summary>
	/// Proxy to the Billing API's LmsBilling controller (int-CommonApis; protocols-training-knowledge-plan §17.3 R-L10): the pack add-on path,
	/// built once so Workforce Pro reuses it (<see cref="PackAddonBillingProxy"/>, <see cref="WorkforceProBillingService"/>). Cloned from
	/// DedicatedNumberBillingService and given a quantity: Checkout carries Packs, ChangePacks carries Packs and the ExpectedPacks the admin
	/// saw. POST actions carry the department in a JSON body (the Billing API's DepartmentInput binds it). Returns null / false when billing is
	/// not configured or unreachable so callers fail closed; checkout is never an entitlement.
	/// </summary>
	public sealed class LmsBillingService : PackAddonBillingProxy, ILmsBillingService
	{
		public LmsBillingService(Func<RestClient> client) : base(client, "LmsBilling", "LMS")
		{
		}
	}

	/// <summary>
	/// Proxy to the Billing API's WorkforceProBilling controller (int-CommonApis), the LmsBilling contract for PlanAddonTypes.WorkforcePro
	/// (shifts-scheduling-modernization-plan.md §7.4: "graduated Stripe and Paddle prices in the Billing API").
	/// </summary>
	public sealed class WorkforceProBillingService : PackAddonBillingProxy, IWorkforceProBillingService
	{
		public WorkforceProBillingService(Func<RestClient> client) : base(client, "WorkforceProBilling", "Workforce Pro")
		{
		}
	}
}
