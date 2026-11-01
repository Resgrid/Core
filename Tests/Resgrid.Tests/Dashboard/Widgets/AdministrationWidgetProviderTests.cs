using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Administration;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard.Widgets.Administration;
using Resgrid.Services.Onboarding;
using static Resgrid.Tests.Dashboard.Widgets.AgentDWidgetTestSupport;
using Catalog = Resgrid.Services.Dashboard.DashboardWidgetCatalog;
using Labels = Resgrid.Localization.Areas.User.AdminAssist.AdminAssist;

namespace Resgrid.Tests.Dashboard.Widgets
{
	/// <summary>Administration widgets (plan §7.11, §9A): admin-only gates, the pages' own checks, the null-plan fallback, no protected text.</summary>
	[TestFixture]
	public class AdministrationWidgetProviderTests
	{
		[Test]
		public void Admin_widgets_are_department_admin_only()
		{
			foreach (var key in new[] { "admin.setup", "admin.plan", "admin.dataProtection", "admin.import", "admin.security" })
				Catalog.Find(key).Gates.Role.Should().Be(DashboardRoles.DepartmentAdmin, key);

			Visible("admin.security", Viewer()).Should().BeFalse();
			Visible("admin.security", Viewer(admin: true)).Should().BeTrue();
			Visible("admin.plan", Viewer(admin: true)).Should().BeFalse("the Subscription page needs Department_Update");
			Visible("admin.import", Viewer(admin: true)).Should().BeFalse("Onboarding.MigrationWizard is required");
			Visible("admin.workflows", Viewer(policies: new[] { "Workflow_View" })).Should().BeFalse("run data needs WorkflowRun_View");
		}

		[Test]
		public void Workflow_health_is_not_reveal_capable()
		{
			Catalog.Find("admin.workflows").ProtectedFields.Should().BeEmpty("the Workflows module cannot reveal run errors, so neither can the widget");
			typeof(AdminWorkflowsWidgetData).GetProperties().Concat(typeof(AdminWorkflowHealthRow).GetProperties())
				.Select(p => p.Name).Should().NotContain(n => n.Contains("Error") && n != nameof(AdminWorkflowsWidgetData.ErrorDetailsProtected));
		}

		#region admin.setup

		private static Mock<IStringLocalizer<Labels>> LabelStrings()
		{
			var labels = new Mock<IStringLocalizer<Labels>>();
			labels.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, "Label for " + key, resourceNotFound: false));
			return labels;
		}

		private static AdminAssistOverview Overview(params SetupTask[] tasks)
		{
			var findings = new[]
			{
				new ConfigurationFinding("r1", "a", FindingSeverity.Warning, RuleResult.Pass, "t", "e", "n", "/x", Array.Empty<string>(), "rev", Now),
				new ConfigurationFinding("r2", "a", FindingSeverity.Warning, RuleResult.Fail, "t", "e", "n", "/x", Array.Empty<string>(), "rev", Now)
			};
			var report = new ConfigurationReport(null, findings, new[] { "a" });
			var plan = new SetupPlan(tasks, Array.Empty<SetupEffort>(), false, null, "rev");
			var assessments = new[] { new CapabilitySetupAssessment("cap.calls", CapabilitySetupState.NeedsAttention, null, null, Array.Empty<string>(), "rev", Now) };
			return new AdminAssistOverview("v1", null, report, Array.Empty<CapabilityAccess>(), assessments, plan);
		}

		[Test]
		public async Task Setup_is_refused_when_neither_the_wizard_nor_the_workspace_is_available()
		{
			var access = new Mock<IAdminAssistAccessService>();
			access.Setup(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
			var provider = new AdminSetupWidgetProvider(new Mock<IAdminAssistService>(MockBehavior.Strict).Object, access.Object, new Mock<IAdminAssistCatalog>().Object, LabelStrings().Object);

			(await StateAsync(() => provider.GetDataAsync(Context("admin.setup", Viewer(admin: true)), CancellationToken.None))).Code.Should().Be(DashboardWidgetErrorCodes.Forbidden);
		}

		[Test]
		public async Task Setup_reports_progress_and_the_next_pending_task_localized()
		{
			var access = new Mock<IAdminAssistAccessService>();
			access.Setup(a => a.CanAccessAsync(It.Is<AdminAssistActor>(x => x.DepartmentId == Dept && x.UserId == User), true, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var service = new Mock<IAdminAssistService>(MockBehavior.Strict);
			service.Setup(s => s.GetOverviewAsync(It.IsAny<AdminAssistActor>(), true, It.IsAny<CancellationToken>())).ReturnsAsync(Overview(
				new SetupTask("cap.calls.Learn", "cap.calls", "Ui.SetupTask.Learn", "g", "Learned", null, Array.Empty<string>()),
				new SetupTask("cap.calls.Configure", "cap.calls", "Ui.SetupTask.Configure", "g", "Pending", "https://evil.example/x", Array.Empty<string>()),
				new SetupTask("cap.calls.Verify", "cap.calls", "Ui.SetupTask.Verify", "g", "Unknown", null, Array.Empty<string>())));
			var catalog = new Mock<IAdminAssistCatalog>();
			catalog.Setup(c => c.Capabilities).Returns(new[] { new ProductCapability("cap.calls", "a", "Cap.Calls", "p", null, null, null, "Released", null, Array.Empty<CapabilityRequirement>(), Array.Empty<string>(), Array.Empty<string>()) });

			var data = (AdminSetupWidgetData)await new AdminSetupWidgetProvider(service.Object, access.Object, catalog.Object, LabelStrings().Object)
				.GetDataAsync(Context("admin.setup", Viewer(admin: true)), CancellationToken.None);

			data.SetupMode.Should().BeTrue();
			data.ChecksRequired.Should().Be(2);
			data.ChecksVerified.Should().Be(1);
			data.PercentComplete.Should().Be(50);
			data.TasksTotal.Should().Be(3);
			data.TasksDone.Should().Be(1);
			data.NeedsAttention.Should().Be(1);
			data.NextLabel.Should().Be("Label for Cap.Calls");
			data.NextUrl.Should().Be(AdminSetupWidgetProvider.WizardUrl, "a destination that is not app-relative is never handed to the client");
		}

		#endregion

		#region admin.plan

		private static Mock<ISubscriptionsService> Subscriptions(Plan plan, Payment payment = null, List<PlanAddon> addonPlans = null, List<PaymentAddon> held = null, bool billingFails = false)
		{
			var subscriptions = new Mock<ISubscriptionsService>(MockBehavior.Strict);
			subscriptions.Setup(s => s.GetCurrentPlanForDepartmentAsync(Dept, false)).ReturnsAsync(plan);
			subscriptions.Setup(s => s.GetCurrentPaymentForDepartmentAsync(Dept, false)).ReturnsAsync(payment);
			if (billingFails)
				subscriptions.Setup(s => s.GetAllAddonPlansAsync()).ThrowsAsync(new InvalidOperationException("billing down"));
			else
				subscriptions.Setup(s => s.GetAllAddonPlansAsync()).ReturnsAsync(addonPlans ?? new List<PlanAddon>());
			subscriptions.Setup(s => s.GetCurrentPaymentAddonsForDepartmentAsync(Dept, It.IsAny<List<string>>())).ReturnsAsync(held ?? new List<PaymentAddon>());
			return subscriptions;
		}

		private static AdminPlanWidgetProvider Plan(Mock<ISubscriptionsService> subscriptions, bool canManage, DepartmentLimits limits = null, params string[] enabledFlags)
		{
			var limitsService = new Mock<ILimitsService>();
			limitsService.Setup(l => l.GetLimitsForEntityPlanWithFallbackAsync(Dept, false)).ReturnsAsync(limits ?? new DepartmentLimits { PersonnelLimit = 10, PersonnelCount = 4 });
			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(a => a.CanUserManageSubscriptionAsync(User, Dept)).ReturnsAsync(canManage);
			var flags = new Mock<IFeatureToggleService>();
			flags.Setup(f => f.IsEnabledAsync(It.IsAny<string>(), Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>()))
				.ReturnsAsync((string flag, int d, bool fallback, IDictionary<string, string> c) => enabledFlags.Contains(flag));
			return new AdminPlanWidgetProvider(subscriptions.Object, limitsService.Object, authorization.Object, flags.Object);
		}

		[Test]
		public async Task A_null_plan_shows_the_free_fallback_and_no_billing_figures_for_a_non_manager()
		{
			var subscriptions = Subscriptions(null);
			var data = (AdminPlanWidgetData)await Plan(subscriptions, canManage: false).GetDataAsync(Context("admin.plan", Viewer(admin: true)), CancellationToken.None);

			data.PlanUnavailable.Should().BeTrue("GetCurrentPlanForDepartmentAsync can return null (CLAUDE.md gotcha 2)");
			data.PlanId.Should().Be(1);
			data.IsFree.Should().BeTrue();
			data.UsedPercent.Should().Be(40);
			data.CanManageBilling.Should().BeFalse();
			data.Cost.Should().BeNull();
			data.RenewsOn.Should().BeNull();
			subscriptions.Verify(s => s.GetCurrentPaymentForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task A_subscription_manager_sees_cost_and_renewal_and_add_ons_held_or_offered()
		{
			var plan = new Plan { PlanId = 40, Name = "Pro", Cost = 0, Frequency = (int)PlanFrequency.Yearly };
			var payment = new Payment { Amount = 1200, EndingOn = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
			var addonPlans = new List<PlanAddon>
			{
				new PlanAddon { PlanAddonId = "ptt-1", AddonType = (int)PlanAddonTypes.PTT },
				new PlanAddon { PlanAddonId = "bo-1", AddonType = (int)PlanAddonTypes.BusinessOperations }
			};
			var held = new List<PaymentAddon> { new PaymentAddon { PlanAddonId = "bo-1", IsCancelled = true, EndingOn = new DateTime(2026, 12, 1) } };
			var subscriptions = Subscriptions(plan, payment, addonPlans, held);
			var limits = new DepartmentLimits { IsEntityPlan = true, EntityTotal = 50, PersonnelCount = 20, UnitsCount = 5 };

			var data = (AdminPlanWidgetData)await Plan(subscriptions, true, limits, FeatureFlagKeys.BusinessOperations).GetDataAsync(Context("admin.plan", Viewer(admin: true)), CancellationToken.None);

			data.PlanName.Should().Be("Pro");
			data.Cost.Should().Be(1200m, "a paid plan with no catalog cost shows the payment amount, as the page does");
			data.Frequency.Should().Be("Yearly");
			data.RenewsOn.Should().Be(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
			data.EntityCount.Should().Be(25);
			data.UsedPercent.Should().Be(50);
			data.Addons.Select(a => a.Key).Should().Equal("PTT", "ReadinessPro", "BusinessOperations", "IncidentBackOffice", "EnhancedAi", "ADP");
			data.Addons.Single(a => a.Key == "BusinessOperations").Should().BeEquivalentTo(new AdminPlanAddon { Key = "BusinessOperations", Offered = true, Held = true, Cancelled = true, EndsOn = new DateTime(2026, 12, 1) });
			data.Addons.Single(a => a.Key == "PTT").Offered.Should().BeTrue("Push-To-Talk is always offered");
			data.Addons.Single(a => a.Key == "EnhancedAi").Offered.Should().BeFalse();
			subscriptions.Verify(s => s.GetCurrentPaymentAddonsForDepartmentAsync(Dept, It.Is<List<string>>(ids => ids.Contains("ptt-1") && ids.Contains("bo-1"))), Times.Once,
				"one held-add-on read covers every type, Push-To-Talk included");
		}

		[Test]
		public async Task A_never_expiring_payment_has_no_renewal_date_and_a_billing_failure_still_renders_the_plan()
		{
			var plan = new Plan { PlanId = 20, Name = "Standard", Cost = 99, Frequency = (int)PlanFrequency.Monthly };
			var subscriptions = Subscriptions(plan, new Payment { EndingOn = DateTime.MaxValue }, billingFails: true);

			var data = (AdminPlanWidgetData)await Plan(subscriptions, true).GetDataAsync(Context("admin.plan", Viewer(admin: true)), CancellationToken.None);

			data.RenewsOn.Should().BeNull();
			data.Cost.Should().Be(99m);
			data.Addons.Should().HaveCount(6).And.OnlyContain(a => !a.Held);
		}

		#endregion

		#region admin.workflows

		private static Mock<IPermissionsService> WorkflowPermissions(bool allowed)
		{
			var permissions = new Mock<IPermissionsService>(MockBehavior.Strict);
			permissions.Setup(p => p.GetAllPermissionsForDepartmentAsync(Dept)).ReturnsAsync(new List<Permission>());
			permissions.Setup(p => p.IsUserAllowed(It.IsAny<Permission>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<List<PersonnelRole>>())).Returns(allowed);
			return permissions;
		}

		[Test]
		public async Task Workflow_health_is_refused_without_the_workflow_permission_rows()
		{
			var provider = new AdminWorkflowsWidgetProvider(new Mock<IWorkflowService>(MockBehavior.Strict).Object, WorkflowPermissions(false).Object);
			(await StateAsync(() => provider.GetDataAsync(Context("admin.workflows"), CancellationToken.None))).Code.Should().Be(DashboardWidgetErrorCodes.Forbidden);
		}

		[Test]
		public async Task Workflow_health_counts_runs_and_names_failing_workflows_of_the_department_only()
		{
			var workflows = new Mock<IWorkflowService>(MockBehavior.Strict);
			workflows.Setup(w => w.GetWorkflowsByDepartmentIdAsync(Dept, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Workflow>
			{
				new Workflow { WorkflowId = "w1", DepartmentId = Dept, Name = "Page on fire", IsEnabled = true },
				new Workflow { WorkflowId = "w2", DepartmentId = Dept, Name = "Nightly export", IsEnabled = false }
			});
			workflows.Setup(w => w.GetDepartmentRunStatusCountsAsync(Dept, Now.AddHours(-24), It.IsAny<CancellationToken>())).ReturnsAsync(new List<WorkflowRunStatusCount>
			{
				new WorkflowRunStatusCount { WorkflowId = "w1", Status = (int)WorkflowRunStatus.Completed, Total = 8, LastOn = Now },
				new WorkflowRunStatusCount { WorkflowId = "w1", Status = (int)WorkflowRunStatus.Failed, Total = 2, LastOn = Now.AddHours(-1) },
				new WorkflowRunStatusCount { WorkflowId = "w1", Status = (int)WorkflowRunStatus.Pending, Total = 3, LastOn = Now.AddDays(-4) },
				new WorkflowRunStatusCount { WorkflowId = "foreign", Status = (int)WorkflowRunStatus.Failed, Total = 9, LastOn = Now }
			});

			var data = (AdminWorkflowsWidgetData)await new AdminWorkflowsWidgetProvider(workflows.Object, WorkflowPermissions(true).Object)
				.GetDataAsync(Context("admin.workflows", Viewer(adpEnforced: true)), CancellationToken.None);

			data.Workflows.Should().Be(2);
			data.ActiveWorkflows.Should().Be(1);
			data.Pending.Should().Be(3, "an open run counts whenever it started");
			data.Completed24h.Should().Be(8);
			data.Failed24h.Should().Be(2, "a run of a workflow that is not the department's never counts");
			data.Total24h.Should().Be(10);
			data.SuccessPercent24h.Should().Be(80);
			data.ErrorDetailsProtected.Should().BeTrue();
			data.Unhealthy.Should().ContainSingle().Which.Should().BeEquivalentTo(new AdminWorkflowHealthRow
			{
				WorkflowId = "w1", Name = "Page on fire", IsEnabled = true, Failed24h = 2, Total24h = 10, LastFailureOn = Now.AddHours(-1)
			});
		}

		#endregion

		[Test]
		public async Task Data_protection_reads_migration_progress_only_while_a_migration_runs()
		{
			var disabled = new Mock<IDepartmentDataProtectionService>(MockBehavior.Strict);
			disabled.Setup(d => d.GetPolicyByDepartmentIdAsync(Dept, false)).ReturnsAsync((DepartmentDataProtectionPolicy)null);
			var off = (AdminDataProtectionWidgetData)await new AdminDataProtectionWidgetProvider(disabled.Object).GetDataAsync(Context("admin.dataProtection", Viewer(admin: true)), CancellationToken.None);
			off.State.Should().Be("Disabled");
			off.MigrationRunning.Should().BeFalse();

			var encrypting = new Mock<IDepartmentDataProtectionService>(MockBehavior.Strict);
			encrypting.Setup(d => d.GetPolicyByDepartmentIdAsync(Dept, false)).ReturnsAsync(new DepartmentDataProtectionPolicy { State = (int)DepartmentDataProtectionState.Encrypting, StepUpWindowMinutes = 15, CatalogVersion = 36 });
			encrypting.Setup(d => d.IsCatalogUpgradePendingAsync(Dept)).ReturnsAsync(false);
			encrypting.Setup(d => d.GetMigrationProgressAsync(Dept, It.IsAny<CancellationToken>())).ReturnsAsync(new AdpMigrationProgress { IsRunning = true, PercentComplete = 40, RowsTotal = 100, RowsCompleted = 40 });

			var on = (AdminDataProtectionWidgetData)await new AdminDataProtectionWidgetProvider(encrypting.Object).GetDataAsync(Context("admin.dataProtection", Viewer(admin: true)), CancellationToken.None);
			on.State.Should().Be("Encrypting");
			on.MigrationRunning.Should().BeTrue();
			on.PercentComplete.Should().Be(40);
			on.StepUpWindowMinutes.Should().Be(15);
		}

		#region admin.import

		[Test]
		public async Task Import_is_refused_unless_the_viewer_can_manage_imports()
		{
			var access = new Mock<IDepartmentImportAccessService>();
			access.Setup(a => a.CanManageAsync(Dept, User, false)).ReturnsAsync(false);
			var provider = new AdminImportWidgetProvider(access.Object, new Mock<IDepartmentImportService>(MockBehavior.Strict).Object);
			(await StateAsync(() => provider.GetDataAsync(Context("admin.import", Viewer(admin: true)), CancellationToken.None))).Code.Should().Be(DashboardWidgetErrorCodes.Forbidden);
		}

		[Test]
		public async Task The_import_summary_counts_workspaces_and_the_current_workspaces_datasets_and_runs()
		{
			var workspaces = new Mock<IImportWorkspacesRepository>(MockBehavior.Strict);
			workspaces.Setup(w => w.GetAllForDepartmentAsync(Dept, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportWorkspace>
			{
				new ImportWorkspace { Id = "old", DepartmentId = Dept, Status = (int)ImportWorkspaceStatus.Reviewing, LastActivityOn = Now.AddDays(-9) },
				new ImportWorkspace { Id = "cur", DepartmentId = Dept, Name = "CAD move", Status = (int)ImportWorkspaceStatus.Importing, LastActivityOn = Now },
				new ImportWorkspace { Id = "done", DepartmentId = Dept, Status = (int)ImportWorkspaceStatus.Active, LastActivityOn = Now.AddDays(-30) },
				new ImportWorkspace { Id = "gone", DepartmentId = Dept, Status = (int)ImportWorkspaceStatus.Cancelled, LastActivityOn = Now.AddDays(-40) }
			});
			var datasets = new Mock<IImportDatasetsRepository>(MockBehavior.Strict);
			datasets.Setup(d => d.GetAllForWorkspaceAsync(Dept, "cur", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportDataset>
			{
				new ImportDataset { Status = (int)ImportDatasetStatus.NeedsAttention, RowCount = 10, WarningCount = 2, BlockedCount = 1 },
				new ImportDataset { Status = (int)ImportDatasetStatus.Imported, RowCount = 20, CreatedCount = 18, UpdatedCount = 2 },
				new ImportDataset { Status = (int)ImportDatasetStatus.Excluded, RowCount = 99 }
			});
			var runs = new Mock<IImportRunsRepository>(MockBehavior.Strict);
			runs.Setup(r => r.GetAllForWorkspaceAsync(Dept, "cur", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportRun>
			{
				new ImportRun { Status = (int)ImportRunStatus.Running }, new ImportRun { Status = (int)ImportRunStatus.Failed }, new ImportRun { Status = (int)ImportRunStatus.Completed }
			});
			var service = new DepartmentImportService(workspaces.Object, null, null, datasets.Object, runs.Object, null, null, null, null, null, null, null, null, null, null);

			var summary = await service.GetImportSummaryAsync(Dept);

			summary.OpenWorkspaces.Should().Be(2);
			summary.ActivatedWorkspaces.Should().Be(1);
			summary.FinishedWorkspaces.Should().Be(1);
			summary.Current.Id.Should().Be("cur");
			summary.Datasets.Should().Be(2, "an excluded data set is no longer part of the import");
			summary.DatasetsNeedingReview.Should().Be(1);
			summary.RowsTotal.Should().Be(30);
			summary.RowsImported.Should().Be(20);
			summary.RowsBlocked.Should().Be(1);
			summary.RunsActive.Should().Be(1);
			summary.RunsFailed.Should().Be(1);

			var access = new Mock<IDepartmentImportAccessService>();
			access.Setup(a => a.CanManageAsync(Dept, User, false)).ReturnsAsync(true);
			var imports = new Mock<IDepartmentImportService>();
			imports.Setup(i => i.GetImportSummaryAsync(Dept, It.IsAny<CancellationToken>())).ReturnsAsync(summary);
			var data = (AdminImportWidgetData)await new AdminImportWidgetProvider(access.Object, imports.Object).GetDataAsync(Context("admin.import", Viewer(admin: true)), CancellationToken.None);
			data.Current.Status.Should().Be("Importing");
			data.Current.NeedingReview.Should().Be(1);
		}

		#endregion

		[Test]
		public async Task Security_posture_reports_adoption_sso_and_policy_switches_as_counts()
		{
			var sessions = new Mock<IUserSessionService>(MockBehavior.Strict);
			sessions.Setup(s => s.GetDepartmentSecurityPostureAsync(Dept, It.IsAny<CancellationToken>())).ReturnsAsync(new DepartmentSecurityPosture
			{
				Members = 8, MfaEnabled = 4, PasskeyUsers = 3, StrongAuthentication = 6, ActiveSessions = 11, UsersWithSessions = 5
			});
			var sso = new Mock<IDepartmentSsoService>(MockBehavior.Strict);
			sso.Setup(s => s.GetSsoConfigsForDepartmentAsync(Dept, It.IsAny<CancellationToken>())).ReturnsAsync(new[]
			{
				new DepartmentSsoConfig { DepartmentId = Dept, IsEnabled = true, ScimEnabled = true },
				new DepartmentSsoConfig { DepartmentId = OtherDept, IsEnabled = true }
			});
			sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(Dept, It.IsAny<CancellationToken>())).ReturnsAsync(new DepartmentSecurityPolicy { RequireMfa = true, MaxConcurrentSessions = 0 });

			var data = (AdminSecurityWidgetData)await new AdminSecurityWidgetProvider(sessions.Object, sso.Object).GetDataAsync(Context("admin.security", Viewer(admin: true)), CancellationToken.None);

			data.StrongAuthenticationPercent.Should().Be(75);
			data.SsoProviders.Should().Be(1, "another department's configuration never counts");
			data.ScimEnabled.Should().BeTrue();
			data.RequireMfa.Should().BeTrue();
			data.MaxConcurrentSessions.Should().BeNull("0 means unlimited");
			data.ActiveSessions.Should().Be(11);
		}

		[Test]
		public async Task Security_posture_survives_an_empty_department()
		{
			var sessions = new Mock<IUserSessionService>();
			var sso = new Mock<IDepartmentSsoService>();
			var data = (AdminSecurityWidgetData)await new AdminSecurityWidgetProvider(sessions.Object, sso.Object).GetDataAsync(Context("admin.security", Viewer(admin: true)), CancellationToken.None);
			data.Members.Should().Be(0);
			data.StrongAuthenticationPercent.Should().Be(0);
		}
	}
}
