using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Services.Dashboard;
using static Resgrid.Tests.Dashboard.Widgets.ReadinessRecordsWidgetTestSupport;

namespace Resgrid.Tests.Dashboard.Widgets
{
	/// <summary>Readiness and Records widget gates mirror the module pages and the sidebar (plan §7.7, §7.8, §9).</summary>
	[TestFixture]
	public class ReadinessRecordsWidgetGateTests
	{
		private static readonly string[] ReadinessKeys =
		{
			"checklists.due", "checklists.compliance", "workOrders.stats", "workOrders.mine", "inventory.alerts", "inventory.valuation",
			"certifications.expiry", "certifications.mine"
		};

		private static readonly string[] RecordsKeys =
		{
			"records.overview", "records.myWork", "records.neris", "records.qualityReview", "records.prevention", "records.hydrants", "records.crr",
			"records.investigations", "records.analytics", "records.legalHolds", "records.deployments"
		};

		[Test]
		public void Every_widget_of_the_slice_is_catalogued_in_its_category()
		{
			foreach (var key in ReadinessKeys)
				DashboardWidgetCatalog.Find(key)?.Category.Should().Be(DashboardWidgetCategories.Readiness, key);
			foreach (var key in RecordsKeys.Append("logs.recent"))
				DashboardWidgetCatalog.Find(key)?.Category.Should().Be(DashboardWidgetCategories.Records, key);
		}

		[Test]
		public void Checklists_need_the_checklists_access_check_and_compliance_the_results_policy()
		{
			Visible("checklists.due", Viewer()).Should().BeFalse();
			Visible("checklists.due", Viewer(access: new[] { DashboardAccessChecks.ChecklistsUsable })).Should().BeTrue();

			Visible("checklists.compliance", Viewer(access: new[] { DashboardAccessChecks.ChecklistsUsable })).Should().BeFalse();
			Visible("checklists.compliance", Viewer(policies: new[] { "ChecklistResults_View" }, access: new[] { DashboardAccessChecks.ChecklistsUsable })).Should().BeTrue();
		}

		[Test]
		public void Work_orders_need_readiness_pro_and_show_admins_a_locked_card_when_it_is_offered()
		{
			Visible("workOrders.mine", Viewer(access: new[] { DashboardAccessChecks.MaintenanceUsable })).Should().BeTrue();
			Visible("workOrders.mine", Viewer()).Should().BeFalse();
			Visible("workOrders.stats", Viewer(access: new[] { DashboardAccessChecks.MaintenanceUsable })).Should().BeFalse("department-wide stats need WorkOrder_View");
			Visible("workOrders.stats", Viewer(policies: new[] { "WorkOrder_View" }, access: new[] { DashboardAccessChecks.MaintenanceUsable })).Should().BeTrue();

			var admin = Viewer(policies: new[] { "WorkOrder_View" }, admin: true);
			admin.OfferedAddons.Add(PlanAddonTypes.ReadinessPro);
			LockedForAdmin("workOrders.stats", admin).Should().BeTrue();
			LockedForAdmin("workOrders.mine", admin).Should().BeTrue();
			admin.HeldAddons.Add(PlanAddonTypes.ReadinessPro);
			LockedForAdmin("workOrders.mine", admin).Should().BeFalse();
			LockedForAdmin("workOrders.mine", Viewer()).Should().BeFalse("members never see locked cards");
		}

		[Test]
		public void Inventory_needs_the_module_and_valuation_is_for_department_admins()
		{
			Visible("inventory.alerts", Viewer()).Should().BeFalse();
			Visible("inventory.alerts", Viewer(access: new[] { DashboardAccessChecks.InventoryEnabled })).Should().BeTrue();
			Visible("inventory.valuation", Viewer(access: new[] { DashboardAccessChecks.InventoryEnabled })).Should().BeFalse();
			Visible("inventory.valuation", Viewer(access: new[] { DashboardAccessChecks.InventoryEnabled }, admin: true)).Should().BeTrue();
		}

		[Test]
		public void Certifications_follow_the_nav_and_profile_rules()
		{
			var shown = new[] { DashboardAccessChecks.CertificationsEnabled };
			Visible("certifications.expiry", Viewer(policies: new[] { "Profile_View" }, access: shown)).Should().BeFalse();
			Visible("certifications.expiry", Viewer(policies: new[] { "Certifications_View" }, access: shown)).Should().BeTrue();
			Visible("certifications.mine", Viewer(policies: new[] { "Profile_View" }, access: shown)).Should().BeTrue();
			// The department's Certifications visibility switch hides both, like the menu entry.
			Visible("certifications.expiry", Viewer(policies: new[] { "Certifications_View" })).Should().BeFalse();
			Visible("certifications.mine", Viewer(policies: new[] { "Profile_View" })).Should().BeFalse();
		}

		private static DashboardViewer RecordsViewer(params string[] extraPolicies) => Viewer(
			policies: new[] { "Record_View" }.Concat(extraPolicies).ToArray(),
			flags: new[] { FeatureFlagKeys.RecordsSystem },
			access: new[] { DashboardAccessChecks.RecordsUsable });

		[Test]
		public void Records_widgets_need_the_records_group_rule()
		{
			foreach (var key in new[] { "records.overview", "records.myWork", "records.deployments" })
			{
				Visible(key, RecordsViewer()).Should().BeTrue(key);
				Visible(key, Viewer(policies: new[] { "Record_View" }, access: new[] { DashboardAccessChecks.RecordsUsable })).Should().BeFalse($"{key} needs Records.System");
				Visible(key, Viewer(policies: new[] { "Record_View" }, flags: new[] { FeatureFlagKeys.RecordsSystem })).Should().BeFalse($"{key} needs the cutover finished");
				Visible(key, Viewer(flags: new[] { FeatureFlagKeys.RecordsSystem }, access: new[] { DashboardAccessChecks.RecordsUsable })).Should().BeFalse($"{key} needs Record_View");
			}

			foreach (var key in RecordsKeys)
				DashboardWidgetCatalog.Find(key).Gates.Flags.Should().Contain(FeatureFlagKeys.RecordsSystem, key);
		}

		[Test]
		public void Legacy_logs_are_the_inverse_of_records()
		{
			var legacy = Viewer(policies: new[] { "Log_View", "Record_View" }, flags: new[] { FeatureFlagKeys.RecordsSystem }, access: new[] { DashboardAccessChecks.LegacyLogsOnly });
			Visible("logs.recent", legacy).Should().BeTrue();
			Visible("records.overview", legacy).Should().BeFalse();

			var records = RecordsViewer("Log_View");
			Visible("logs.recent", records).Should().BeFalse();
			Visible("records.overview", records).Should().BeTrue();

			Visible("logs.recent", Viewer(access: new[] { DashboardAccessChecks.LegacyLogsOnly })).Should().BeFalse("Log_View is the Logs page policy");
		}

		[Test]
		public void Records_sub_modules_need_their_own_flags_policies_and_permissions()
		{
			Visible("records.neris", RecordsViewer()).Should().BeFalse();
			var neris = RecordsViewer(); neris.Access[DashboardAccessChecks.NerisWorkflowEnabled] = true;
			Visible("records.neris", neris).Should().BeTrue();

			var quality = RecordsViewer(); quality.EnabledFlags.Add(FeatureFlagKeys.RecordsQualityReview);
			Visible("records.qualityReview", quality).Should().BeFalse("the Quality pages require Record_Review");
			quality.GrantedPolicies.Add("Record_Review");
			Visible("records.qualityReview", quality).Should().BeTrue();

			Visible("records.prevention", RecordsViewer()).Should().BeFalse("no prevention module is on");
			foreach (var flag in new[] { FeatureFlagKeys.RecordsPreventionOccupancy, FeatureFlagKeys.RecordsPreventionHydrants, FeatureFlagKeys.RecordsPreventionCrr })
			{
				var prevention = RecordsViewer(); prevention.EnabledFlags.Add(flag);
				Visible("records.prevention", prevention).Should().BeTrue(flag);
			}

			var hydrants = RecordsViewer(); hydrants.EnabledFlags.Add(FeatureFlagKeys.RecordsPreventionHydrants);
			Visible("records.hydrants", hydrants).Should().BeTrue();
			Visible("records.crr", hydrants).Should().BeFalse();

			var investigations = RecordsViewer(); investigations.EnabledFlags.Add(FeatureFlagKeys.RecordsInvestigations);
			Visible("records.investigations", investigations).Should().BeFalse("cases need RecordRestricted_View");
			investigations.GrantedPolicies.Add("RecordRestricted_View");
			Visible("records.investigations", investigations).Should().BeTrue();

			var analytics = RecordsViewer(); analytics.EnabledFlags.Add(FeatureFlagKeys.RecordsAnalytics);
			Visible("records.analytics", analytics).Should().BeTrue();

			var holds = RecordsViewer();
			Visible("records.legalHolds", holds).Should().BeFalse();
			holds.Permissions.Add(PermissionTypes.ManageRecordLegalHold);
			Visible("records.legalHolds", holds).Should().BeTrue();
		}

		[Test]
		public void Readiness_and_records_widgets_declare_no_protected_field()
		{
			foreach (var key in ReadinessKeys.Concat(RecordsKeys).Append("logs.recent"))
				DashboardWidgetCatalog.Find(key).ProtectedFields.Should().BeEmpty($"{key} shows counts, numbers, states and dates only");
		}

		[Test]
		public void Records_dtos_carry_no_narrative_or_protected_columns()
		{
			var forbidden = new[] { "Narrative", "Notes", "Note", "Reason", "ReferenceNumber", "Title", "Findings", "IncidentSummary", "CauseDetail", "OriginDescription",
				"ClosureReason", "DisplaySummary", "AddressText", "OutOfServiceReason", "ContactName", "ContactNumber", "Description", "Content" };

			var types = typeof(Resgrid.Model.Dashboard.Widgets.Records.RecordsOverviewWidgetData).Assembly.GetTypes()
				.Where(t => t.Namespace == "Resgrid.Model.Dashboard.Widgets.Records").ToList();
			types.Should().NotBeEmpty();

			foreach (var type in types)
				type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Intersect(forbidden, StringComparer.Ordinal)
					.Should().BeEmpty($"{type.Name} must not carry free text");
		}
	}
}
