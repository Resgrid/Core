using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Models.IncidentBackOffice
{
	/// <summary>
	/// The module's navigation follows the ICS section structure (plan §14.5): Command, Planning, Logistics, Finance/Administration,
	/// Records, Coordination, and the department-scoped preparedness screens — not a flat list of thirty screens. Links mirror the
	/// target controller's claim and capability flag, so a link never leads to a refusal.
	/// </summary>
	public static class IncidentBackOfficeNavigation
	{
		public sealed class Item
		{
			public string Controller { get; set; }
			public string Action { get; set; } = "Index";
			public string LabelKey { get; set; }
			public string Icon { get; set; }
			public string Area { get; set; } = "Foundation";
			public bool NeedsActivation { get; set; } = true;
			public Func<bool> Visible { get; set; } = () => true;
		}

		public sealed class Section
		{
			public string Key { get; set; }
			public string LabelKey { get; set; }
			public string Icon { get; set; }
			public List<Item> Items { get; set; } = new List<Item>();
		}

		private static Item I(string controller, string label, string icon, string area = "Foundation", bool activation = true, Func<bool> visible = null, string action = "Index")
			=> new Item { Controller = controller, Action = action, LabelKey = label, Icon = icon, Area = area, NeedsActivation = activation, Visible = visible ?? (() => true) };

		public static readonly IReadOnlyList<Section> Sections = new[]
		{
			new Section { Key = "Command", LabelKey = "Section.Command", Icon = "fa-flag", Items =
			{
				I("IncidentBackOffice", "Nav.Incidents", "fa-th-list", activation: false),
				I("IncidentBackOffice", "Nav.CommandDesk", "fa-tachometer", action: "Command"),
				I("IncidentOperations", "Nav.OperationsDesk", "fa-crosshairs")
			} },
			new Section { Key = "Planning", LabelKey = "Section.Planning", Icon = "fa-calendar", Items =
			{
				I("IncidentPeriods", "Nav.Periods", "fa-clock-o"),
				I("IncidentOrganization", "Nav.Organization", "fa-sitemap"),
				I("IncidentParticipants", "Nav.Participants", "fa-users"),
				I("IncidentCheckIn", "Nav.CheckIn", "fa-sign-in"),
				I("IncidentDemob", "Nav.Demob", "fa-sign-out")
			} },
			new Section { Key = "Logistics", LabelKey = "Section.Logistics", Icon = "fa-truck", Items =
			{
				I("IncidentLogistics", "Nav.LogisticsDesk", "fa-tachometer"),
				I("IncidentRequests", "Nav.Requests", "fa-shopping-cart"),
				I("IncidentFacilities", "Nav.Facilities", "fa-building"),
				I("IncidentLodging", "Nav.Lodging", "fa-bed"),
				I("IncidentFeeding", "Nav.Feeding", "fa-cutlery"),
				I("IncidentWater", "Nav.Water", "fa-tint"),
				I("IncidentServices", "Nav.Services", "fa-wrench"),
				I("IncidentGroundSupport", "Nav.GroundSupport", "fa-bus"),
				I("IncidentCommunications", "Nav.Communications", "fa-headphones"),
				I("IncidentVendors", "Nav.IncidentVendors", "fa-handshake-o")
			} },
			new Section { Key = "Finance", LabelKey = "Section.Finance", Icon = "fa-usd", Items =
			{
				I("IncidentFinance", "Nav.FinanceDesk", "fa-tachometer", "Business"),
				I("IncidentProcurement", "Nav.Procurement", "fa-shopping-bag", "Business", visible: () => ClaimsAuthorizationHelper.CanViewIncidentProcurement()),
				I("IncidentBudget", "Nav.Budget", "fa-balance-scale", "Business", visible: () => ClaimsAuthorizationHelper.CanViewIncidentBudget()),
				I("IncidentTime", "Nav.Time", "fa-hourglass-half", "Business", visible: () => ClaimsAuthorizationHelper.CanViewIncidentTime()),
				I("IncidentCost", "Nav.Cost", "fa-line-chart", "Business", visible: () => ClaimsAuthorizationHelper.CanViewIncidentCost()),
				I("IncidentRecovery", "Nav.Recovery", "fa-life-ring", "Business", visible: () => ClaimsAuthorizationHelper.CanViewIncidentCost()),
				I("IncidentIntake", "Nav.Intake", "fa-inbox", "Business", visible: () => ClaimsAuthorizationHelper.CanViewIncidentProcurement() || ClaimsAuthorizationHelper.CanViewIncidentTime() || ClaimsAuthorizationHelper.CanViewIncidentCost()),
				I("IncidentClaims", "Nav.Claims", "fa-medkit", "Business", visible: () => ClaimsAuthorizationHelper.CanViewIncidentClaim())
			} },
			new Section { Key = "Records", LabelKey = "Section.Records", Icon = "fa-folder-open", Items =
			{
				I("IncidentIap", "Nav.Iap", "fa-book"),
				I("IncidentCloseout", "Nav.Closeout", "fa-lock", "Business")
			} },
			new Section { Key = "Coordination", LabelKey = "Section.Coordination", Icon = "fa-globe", Items =
			{
				I("CoordinationBoards", "Nav.Boards", "fa-columns", "Coordination", false),
				I("IncidentSituation", "Nav.Situation", "fa-map", "Coordination")
			} },
			new Section { Key = "Department", LabelKey = "Section.Department", Icon = "fa-cog", Items =
			{
				I("IncidentTeams", "Nav.Teams", "fa-id-badge", activation: false),
				I("IncidentPositions", "Nav.Positions", "fa-list", activation: false),
				I("Vendors", "Nav.VendorCatalog", "fa-address-book", activation: false),
				I("SupportPlan", "Nav.SupportPlan", "fa-map-signs", activation: false),
				I("IncidentAuthority", "Nav.Authority", "fa-gavel", activation: false),
				I("IncidentBackOffice", "Nav.Settings", "fa-sliders", activation: false, visible: () => ClaimsAuthorizationHelper.IsUserDepartmentAdmin(), action: "Settings")
			} }
		};

		public static Section For(string key) => Sections.FirstOrDefault(s => s.Key == key) ?? Sections[0];

		public static bool Allowed(Item item, Resgrid.Model.IncidentBackOfficeAccess access) => item.Visible() && item.Area switch
		{
			"Business" => access.BusinessEnabled,
			"Coordination" => access.CoordinationEnabled,
			_ => true
		};
	}
}
