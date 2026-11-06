using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard;

namespace Resgrid.Tests.Dashboard
{
	/// <summary>Widget visibility (home-dashboard-redesign-plan.md §9, D8): every gate must pass; locked cards for admins only.</summary>
	[TestFixture]
	public class DashboardEntitlementServiceTests
	{
		private const int DepartmentId = 3;

		private Mock<IFeatureToggleService> _flags;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IReadinessAccessService> _readiness;
		private Mock<IRecordsCutoverService> _records;
		private Mock<IBusinessOperationsAccessService> _business;
		private Mock<IIncidentBackOfficeAccessService> _ibo;
		private Mock<IEnhancedAiAccessService> _ai;
		private Mock<IVoiceService> _voice;
		private Mock<IAiDispatchAdminService> _aiDispatch;
		private Mock<INerisProfileService> _neris;
		private Mock<IRecordsAuthorizationService> _recordsAuthorization;
		private Mock<IPermissionsService> _permissions;
		private Mock<IPersonnelRolesService> _roles;
		private Mock<IDepartmentDataProtectionService> _protection;

		[SetUp]
		public void SetUp()
		{
			_flags = new Mock<IFeatureToggleService>();
			_settings = new Mock<IDepartmentSettingsService>();
			_readiness = new Mock<IReadinessAccessService>();
			_records = new Mock<IRecordsCutoverService>();
			_business = new Mock<IBusinessOperationsAccessService>();
			_ibo = new Mock<IIncidentBackOfficeAccessService>();
			_ai = new Mock<IEnhancedAiAccessService>();
			_voice = new Mock<IVoiceService>();
			_aiDispatch = new Mock<IAiDispatchAdminService>();
			_neris = new Mock<INerisProfileService>();
			_recordsAuthorization = new Mock<IRecordsAuthorizationService>();
			_permissions = new Mock<IPermissionsService>();
			_roles = new Mock<IPersonnelRolesService>();
			_protection = new Mock<IDepartmentDataProtectionService>();

			_settings.Setup(s => s.GetDepartmentModuleSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentModuleSettings());
			_roles.Setup(r => r.GetRolesForUserAsync(It.IsAny<string>(), DepartmentId)).ReturnsAsync(new List<PersonnelRole>());
		}

		private DashboardEntitlementService Service() => new DashboardEntitlementService(_flags.Object, _settings.Object, _readiness.Object, _records.Object,
			_business.Object, _ibo.Object, _ai.Object, _voice.Object, _aiDispatch.Object, _neris.Object, _recordsAuthorization.Object, _permissions.Object,
			_roles.Object, _protection.Object);

		private static DashboardViewer Viewer(bool admin = false, params string[] policies) => new DashboardViewer
		{
			DepartmentId = DepartmentId,
			UserId = "u",
			IsDepartmentAdmin = admin,
			GrantedPolicies = new HashSet<string>(policies)
		};

		private static DashboardWidgetDefinition Widget(DashboardWidgetGates gates) => new DashboardWidgetDefinition { Key = "t.w", Gates = gates };

		[Test]
		public void Every_gate_must_pass()
		{
			var service = Service();
			var widget = Widget(new DashboardWidgetGates
			{
				Policies = new[] { "Call_View" },
				Flags = new[] { "A.Flag" },
				Access = new[] { DashboardAccessChecks.MappingEnabled }
			});

			var viewer = Viewer(false, "Call_View");
			viewer.EnabledFlags.Add("A.Flag");
			viewer.Access[DashboardAccessChecks.MappingEnabled] = true;
			service.IsVisible(widget, viewer).Should().BeTrue();

			viewer.Access[DashboardAccessChecks.MappingEnabled] = false;
			service.IsVisible(widget, viewer).Should().BeFalse("a module switch off hides the widget");

			viewer.Access[DashboardAccessChecks.MappingEnabled] = true;
			viewer.GrantedPolicies.Clear();
			service.IsVisible(widget, viewer).Should().BeFalse("a missing policy hides the widget");
		}

		[Test]
		public void AnyFlags_needs_one_and_roles_are_enforced()
		{
			var service = Service();
			var anyFlag = Widget(new DashboardWidgetGates { AnyFlags = new[] { "X", "Y" } });
			var viewer = Viewer();

			service.IsVisible(anyFlag, viewer).Should().BeFalse();
			viewer.EnabledFlags.Add("Y");
			service.IsVisible(anyFlag, viewer).Should().BeTrue();

			var adminOnly = Widget(new DashboardWidgetGates { Role = DashboardRoles.DepartmentAdmin });
			service.IsVisible(adminOnly, Viewer()).Should().BeFalse();
			service.IsVisible(adminOnly, Viewer(admin: true)).Should().BeTrue();

			var groupAdmin = Widget(new DashboardWidgetGates { Role = DashboardRoles.GroupAdmin });
			var member = Viewer();
			service.IsVisible(groupAdmin, member).Should().BeFalse();
			member.IsGroupAdmin = true;
			service.IsVisible(groupAdmin, member).Should().BeTrue();
		}

		[Test]
		public void Locked_cards_are_for_admins_when_the_addon_is_offered_and_not_held()
		{
			var service = Service();
			var paid = Widget(new DashboardWidgetGates
			{
				Access = new[] { DashboardAccessChecks.IncidentBackOfficeEnabled },
				Addon = PlanAddonTypes.IncidentBackOffice
			});

			var admin = Viewer(admin: true);
			admin.Access[DashboardAccessChecks.IncidentBackOfficeEnabled] = false;
			admin.OfferedAddons.Add(PlanAddonTypes.IncidentBackOffice);
			service.IsLockedForAdmin(paid, admin).Should().BeTrue();

			admin.HeldAddons.Add(PlanAddonTypes.IncidentBackOffice);
			service.IsLockedForAdmin(paid, admin).Should().BeFalse("a held add-on is never upsold");

			var member = Viewer();
			member.OfferedAddons.Add(PlanAddonTypes.IncidentBackOffice);
			service.IsLockedForAdmin(paid, member).Should().BeFalse("members never see upsell");
		}

		[Test]
		public async Task Completion_evaluates_module_switches_flags_and_audience()
		{
			_settings.Setup(s => s.GetDepartmentModuleSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentModuleSettings { MappingDisabled = true });
			_flags.Setup(f => f.IsEnabledAsync("A.Flag", DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);

			var widget = Widget(new DashboardWidgetGates { Flags = new[] { "A.Flag" }, Access = new[] { DashboardAccessChecks.MappingEnabled } });
			var viewer = await Service().CompleteViewerForWidgetAsync(Viewer(false, "Call_Create"), widget);

			viewer.HasFlag("A.Flag").Should().BeTrue();
			viewer.Passes(DashboardAccessChecks.MappingEnabled).Should().BeFalse();
			viewer.Audience.Should().Be(DashboardAudiences.Dispatcher);

			(await Service().CompleteViewerForWidgetAsync(Viewer(admin: true), widget)).Audience.Should().Be(DashboardAudiences.Administrator);
			(await Service().CompleteViewerForWidgetAsync(Viewer(), widget)).Audience.Should().Be(DashboardAudiences.Member);
		}

		[Test]
		public async Task Records_widgets_follow_the_records_switch_not_the_logs_switch()
		{
			_records.Setup(r => r.GetModuleStateAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new RecordsModuleState
			{
				DepartmentId = DepartmentId, FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Active
			});
			var widget = Widget(new DashboardWidgetGates { Access = new[] { DashboardAccessChecks.RecordsUsable } });

			_settings.Setup(s => s.GetDepartmentModuleSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentModuleSettings { LogsDisabled = true });
			(await Service().CompleteViewerForWidgetAsync(Viewer(), widget)).Passes(DashboardAccessChecks.RecordsUsable).Should().BeTrue("Logs off no longer hides Records");

			_settings.Setup(s => s.GetDepartmentModuleSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentModuleSettings { RecordsDisabled = true });
			(await Service().CompleteViewerForWidgetAsync(Viewer(), widget)).Passes(DashboardAccessChecks.RecordsUsable).Should().BeFalse();
		}

		[TestCase(DashboardAccessChecks.ContactsEnabled, "contacts.preplans")]
		[TestCase(DashboardAccessChecks.CertificationsEnabled, "certifications.expiry")]
		[TestCase(DashboardAccessChecks.CertificationsEnabled, "certifications.mine")]
		[TestCase(DashboardAccessChecks.DeploymentsEnabled, "deployments.active")]
		[TestCase(DashboardAccessChecks.DeploymentsEnabled, "deployments.timeReports")]
		[TestCase(DashboardAccessChecks.ChatEnabled, "chat.unread")]
		[TestCase(DashboardAccessChecks.ChatEnabled, "chat.moderation")]
		public async Task Newer_module_widgets_follow_their_module_switch(DashboardAccessChecks check, string widgetKey)
		{
			DashboardWidgetCatalog.All.Single(w => w.Key == widgetKey).Gates.Access.Should().Contain(check);
			var widget = Widget(new DashboardWidgetGates { Access = new[] { check } });

			(await Service().CompleteViewerForWidgetAsync(Viewer(), widget)).Passes(check).Should().BeTrue("every module is shown by default");

			var hidden = new DepartmentModuleSettings
			{
				ContactsDisabled = check == DashboardAccessChecks.ContactsEnabled,
				CertificationsDisabled = check == DashboardAccessChecks.CertificationsEnabled,
				DeploymentsDisabled = check == DashboardAccessChecks.DeploymentsEnabled,
				ChatDisabled = check == DashboardAccessChecks.ChatEnabled
			};
			_settings.Setup(s => s.GetDepartmentModuleSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(hidden);
			(await Service().CompleteViewerForWidgetAsync(Viewer(), widget)).Passes(check).Should().BeFalse();
		}

		[Test]
		public async Task Adp_view_permissions_resolve_missing_rows_through_the_adp_defaults()
		{
			_protection.Setup(p => p.IsProtectionEnforcedAsync(DepartmentId)).ReturnsAsync(true);
			var seen = new List<Permission>();
			_permissions.Setup(p => p.IsUserAllowed(It.IsAny<Permission>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<List<PersonnelRole>>()))
				.Callback((Permission permission, bool admin, bool groupAdmin, List<PersonnelRole> roles) => seen.Add(permission))
				.Returns((Permission permission, bool admin, bool groupAdmin, List<PersonnelRole> roles) => permission.Action == (int)PermissionActions.Everyone);

			var viewer = await Service().CompleteViewerForWidgetAsync(Viewer(), Widget(new DashboardWidgetGates()));

			viewer.AdpEnforced.Should().BeTrue();
			seen.Should().Contain(p => p.PermissionType == (int)PermissionTypes.ViewProtectedOperationalData &&
				p.Action == (int)AdpPermissionDefaults.For(PermissionTypes.ViewProtectedOperationalData));
			viewer.CanRevealAdp(PermissionTypes.ViewProtectedCallData).Should().BeTrue("call data defaults to everyone");
			viewer.CanRevealAdp(PermissionTypes.ViewProtectedOperationalData).Should().BeFalse("operational data defaults to department and group admins");
		}
	}
}
