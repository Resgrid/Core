using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Administration
{
	/// <summary>
	/// admin.plan (plan §7.11): the subscription plan, limits used and add-ons held / offered. Billing figures (cost, renewal) only for
	/// a member who may manage the subscription (the Subscription page's own rule); everyone else sees the plan and limits.
	/// </summary>
	public sealed class AdminPlanWidgetData
	{
		public int PlanId { get; set; }

		public string PlanName { get; set; }

		/// <summary>The Billing API returned no plan; the widget shows the free-plan fallback the Subscription page shows.</summary>
		public bool PlanUnavailable { get; set; }

		public bool IsFree { get; set; }

		/// <summary>Entity plans count personnel and units against one limit.</summary>
		public bool IsEntityPlan { get; set; }

		public int PersonnelCount { get; set; }

		public int PersonnelLimit { get; set; }

		public int UnitsCount { get; set; }

		public int UnitsLimit { get; set; }

		public int EntityCount { get; set; }

		public int EntityLimit { get; set; }

		/// <summary>The limit that binds (entity or personnel), used 0–100+; null when unlimited.</summary>
		public int? UsedPercent { get; set; }

		public bool CanManageBilling { get; set; }

		/// <summary>Manager only. Null otherwise.</summary>
		public decimal? Cost { get; set; }

		/// <summary>Manager only: PlanFrequency name (Monthly, Yearly…).</summary>
		public string Frequency { get; set; }

		/// <summary>Manager only: the current payment's end; null when it never expires.</summary>
		public DateTime? RenewsOn { get; set; }

		public bool Cancelled { get; set; }

		/// <summary>Every add-on in the catalog order of the Subscription page.</summary>
		public List<AdminPlanAddon> Addons { get; set; } = new List<AdminPlanAddon>();
	}

	public sealed class AdminPlanAddon
	{
		/// <summary>PlanAddonTypes name (PTT, ADP, ReadinessPro, BusinessOperations, EnhancedAi, IncidentBackOffice) — W.admin.plan.Addon.{key}.</summary>
		public string Key { get; set; }

		/// <summary>Offered to the department (its operator toggle is on).</summary>
		public bool Offered { get; set; }

		public bool Held { get; set; }

		/// <summary>Held but set to end.</summary>
		public bool Cancelled { get; set; }

		public DateTime? EndsOn { get; set; }
	}
}
