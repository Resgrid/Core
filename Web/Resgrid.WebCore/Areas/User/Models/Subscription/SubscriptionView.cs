using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Subscription
{
	public class SubscriptionView : BaseUserModel
	{
		public Department Department { get; set; }
		public string Message { get; set; }
		public Plan Plan { get; set; }
		public Payment Payment { get; set; }
		public string Expires { get; set; }
		public int PersonnelCount { get; set; }
		public string PersonnelLimit { get; set; }
		public string PersonnelBarPrecent { get; set; }
		public int GroupsCount { get; set; }
		public string GroupsLimit { get; set; }
		public string GroupsBarPrecent { get; set; }
		public int UnitsCount { get; set; }
		public string UnitsLimit { get; set; }
		public string UnitsBarPrecent { get; set; }
		public int RolesCount { get; set; }
		public string RolesLimit { get; set; }
		public string RolesBarPrecent { get; set; }
		public bool IsTestingDepartment { get; set; }
		public List<int> PossibleUpgrades { get; set; }
		public List<int> PossibleDowngrades { get; set; }
		public bool HadStripePaymentIn30Days { get; set; }
		public string StripeKey { get; set; }
		public string StripeCustomer { get; set; }

		public bool HasActiveSubscription { get; set; }
		public bool HasActiveAddon { get; set; }
		public string AddonFrequencyString { get; set; }
		public string AddonCost { get; set; }

		public string AddonCost2 { get; set; }

		public string AddonPlanIdToBuy { get; set; }

		public bool IsAddonCanceled { get; set; }
		public DateTime? AddonEndingOn { get; set; }
		public string StripeCustomerPortalUrl { get; set; }


		public string IsEntitiesTabActive()
		{
			if (Plan == null || Plan.PlanId == 1 || Plan.PlanId >= 36)
				return "active";

			return "";
		}

		public string IsLegacyTabActive()
		{
			if (Plan != null && Plan.PlanId < 36 && Plan.PlanId != 1)
				return "active";

			return "";
		}
	}
}
