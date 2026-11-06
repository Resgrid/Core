using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.CostRecovery.CalOesMars;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Business;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Model.Workforce;
using Resgrid.Services.Dashboard.Widgets.Business;
using Resgrid.Services.Invoicing;
using static Resgrid.Tests.Dashboard.Widgets.AgentDWidgetTestSupport;

namespace Resgrid.Tests.Dashboard.Widgets
{
	/// <summary>Workforce &amp; Business widgets (plan §7.9): gates mirror the nav and pages; scopes mirror each page's manager rule.</summary>
	[TestFixture]
	public class BusinessWidgetProviderTests
	{
		private static Deployment Deployment(string id, int department = Dept, DateTime? start = null, int status = (int)DeploymentStatuses.Active) =>
			new Deployment { DeploymentId = id, DepartmentId = department, Name = "Deployment " + id, Status = status, StartOn = start ?? Now.AddDays(-3), AddedOn = Now.AddDays(-10) };

		#region Gates

		[Test]
		public void Gates_mirror_the_nav_and_the_pages()
		{
			var deploymentsShown = new[] { DashboardAccessChecks.DeploymentsEnabled };
			Visible("deployments.active", Viewer(access: deploymentsShown)).Should().BeFalse("the Operations.Deployments flag is required");
			Visible("deployments.active", Viewer(flags: new[] { FeatureFlagKeys.Deployments }, access: deploymentsShown)).Should().BeTrue("the Deployments list has no policy: members see their own");
			Visible("deployments.active", Viewer(flags: new[] { FeatureFlagKeys.Deployments })).Should().BeFalse("the department's Deployments visibility switch hides it, like the menu entry");

			Visible("deployments.timeReports", Viewer(flags: new[] { FeatureFlagKeys.Deployments }, access: deploymentsShown)).Should().BeFalse("approving needs TimeReports_Approve");
			Visible("deployments.timeReports", Viewer(flags: new[] { FeatureFlagKeys.Deployments }, policies: new[] { "TimeReports_Approve" }, access: deploymentsShown)).Should().BeTrue();
			Visible("deployments.timeReports", Viewer(flags: new[] { FeatureFlagKeys.Deployments }, policies: new[] { "TimeReports_Approve" })).Should().BeFalse();

			var invoicing = Viewer(policies: new[] { "Invoicing_View" }, flags: new[] { FeatureFlagKeys.BusinessOperations }, access: new[] { DashboardAccessChecks.InvoicingVisible });
			Visible("invoicing.receivables", invoicing).Should().BeTrue("Invoicing is visible on flag + module; a lapsed add-on only makes it read-only");
			Visible("invoicing.receivables", Viewer(policies: new[] { "Invoicing_View" }, flags: new[] { FeatureFlagKeys.BusinessOperations })).Should().BeFalse();
			Visible("invoicing.onlinePayments", invoicing).Should().BeFalse("online payments is a department-admin panel");

			Visible("bids.pipeline", Viewer(policies: new[] { "Bids_View" })).Should().BeFalse("bids need the contractor billing entitlement");
			Visible("bids.pipeline", Viewer(policies: new[] { "Bids_View" }, access: new[] { DashboardAccessChecks.ContractorBillingUsable })).Should().BeTrue();
			Visible("contracts.expiring", Viewer(access: new[] { DashboardAccessChecks.ContractorBillingUsable })).Should().BeFalse("contracts need ServiceContracts_View");
			Visible("costRecovery.queue", Viewer(access: new[] { DashboardAccessChecks.CostRecoveryUsable })).Should().BeTrue("the MARS queue is a field action every member reaches");
			Visible("workforce.fieldCosting", Viewer(access: new[] { DashboardAccessChecks.WorkforceUsable })).Should().BeFalse("cost runs need InternalCosts_View");
			Visible("workforce.payData", Viewer(policies: new[] { "PayDataReporting_View" }, access: new[] { DashboardAccessChecks.PayDataReportingUsable })).Should().BeTrue();
		}

		[Test]
		public void An_admin_gets_a_locked_card_for_an_offered_business_operations_widget_the_department_does_not_hold()
		{
			var admin = Viewer(admin: true, policies: new[] { "Bids_View" });
			admin.OfferedAddons.Add(PlanAddonTypes.BusinessOperations);
			LockedForAdmin("bids.pipeline", admin).Should().BeTrue();

			admin.HeldAddons.Add(PlanAddonTypes.BusinessOperations);
			LockedForAdmin("bids.pipeline", admin).Should().BeFalse();
		}

		#endregion

		#region deployments.active

		[Test]
		public async Task A_member_sees_only_the_deployments_they_are_rostered_on()
		{
			var deployments = new Mock<IDeploymentService>(MockBehavior.Strict);
			deployments.Setup(d => d.GetDeploymentsForUserAsync(Dept, User, true)).ReturnsAsync(new List<Deployment> { Deployment("mine") });

			var data = (DeploymentsActiveWidgetData)await new DeploymentsActiveWidgetProvider(deployments.Object, Permissions().Object)
				.GetDataAsync(Context("deployments.active"), CancellationToken.None);

			data.CanViewAll.Should().BeFalse("ManageDeployments defaults to department admins");
			data.OpenCount.Should().Be(1);
			data.Rows.Should().ContainSingle().Which.IsMine.Should().BeTrue();
			data.Rows[0].StatusName.Should().Be("Active");
			data.Rows[0].DaysDeployed.Should().Be(3);
			deployments.Verify(d => d.GetDeploymentsForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task A_manager_sees_the_department_count_with_their_own_deployments_first_and_never_another_departments()
		{
			var deployments = new Mock<IDeploymentService>(MockBehavior.Strict);
			deployments.Setup(d => d.GetDeploymentsForUserAsync(Dept, User, true)).ReturnsAsync(new List<Deployment> { Deployment("mine", start: Now.AddDays(-30)) });
			deployments.Setup(d => d.CountDeploymentsForDepartmentAsync(Dept, true)).ReturnsAsync(7);
			deployments.Setup(d => d.GetDeploymentsForDepartmentAsync(Dept, true, 0, 6)).ReturnsAsync(new List<Deployment>
			{
				Deployment("newer", start: Now.AddDays(-1)), Deployment("mine", start: Now.AddDays(-30)), Deployment("foreign", department: OtherDept)
			});
			var permissions = Permissions(Row(PermissionTypes.ManageDeployments, PermissionActions.DepartmentAdminsAndSelectRoles, "9"));

			var data = (DeploymentsActiveWidgetData)await new DeploymentsActiveWidgetProvider(deployments.Object, permissions.Object)
				.GetDataAsync(Context("deployments.active", Viewer(roles: new[] { 9 })), CancellationToken.None);

			data.CanViewAll.Should().BeTrue("a selected role holds ManageDeployments");
			data.OpenCount.Should().Be(7);
			data.MineCount.Should().Be(1);
			data.Rows.Select(r => r.DeploymentId).Should().Equal("mine", "newer");
		}

		#endregion

		[Test]
		public async Task Time_reports_map_the_aggregate_and_survive_a_null_answer()
		{
			var time = new Mock<ITimeTrackingService>();
			time.Setup(t => t.GetTimeReportCountsAsync(Dept)).ReturnsAsync(new DeploymentTimeReportCounts { AwaitingApproval = 3, ApprovedUnbilled = 2, OldestAwaitingOn = Now.AddDays(-2) });
			var data = (DeploymentTimeReportsWidgetData)await new DeploymentTimeReportsWidgetProvider(time.Object).GetDataAsync(Context("deployments.timeReports"), CancellationToken.None);
			data.AwaitingApproval.Should().Be(3);
			data.ApprovedUnbilled.Should().Be(2);
			data.OldestAwaitingOn.Should().Be(Now.AddDays(-2));

			time.Setup(t => t.GetTimeReportCountsAsync(Dept)).ReturnsAsync((DeploymentTimeReportCounts)null);
			((DeploymentTimeReportsWidgetData)await new DeploymentTimeReportsWidgetProvider(time.Object).GetDataAsync(Context("deployments.timeReports"), CancellationToken.None))
				.AwaitingApproval.Should().Be(0);
		}

		[Test]
		public async Task Receivables_keep_currencies_apart_and_read_only_when_the_add_on_lapsed()
		{
			var report = new InvoiceAgingReport { TotalCount = 3 };
			report.BalancesByCurrency["USD"] = 300m;
			report.BalancesByCurrency["CAD"] = 50m;
			var current = new InvoiceAgingBucket { Label = "Current", Count = 1 };
			current.BalancesByCurrency["USD"] = 100m;
			var late = new InvoiceAgingBucket { Label = "31-60", Count = 2 };
			late.BalancesByCurrency["USD"] = 200m;
			late.BalancesByCurrency["CAD"] = 50m;
			late.Invoices.Add(new InvoiceAgingRow());
			report.Buckets.AddRange(new[] { current, late });

			var invoicing = new Mock<IInvoicingService>(MockBehavior.Strict);
			invoicing.Setup(i => i.GetAccountsReceivableAgingAsync(Dept, null)).ReturnsAsync(report);
			invoicing.Setup(i => i.CountInvoicesForDepartmentAsync(Dept, It.Is<InvoiceListFilter>(f => f.Statuses.Single() == (int)InvoiceStatus.Draft))).ReturnsAsync(4);
			var access = new Mock<IBusinessOperationsAccessService>(MockBehavior.Strict);
			access.Setup(a => a.CanUseInvoicingAsync(Dept)).ReturnsAsync(false);

			var data = (InvoicingReceivablesWidgetData)await new InvoicingReceivablesWidgetProvider(invoicing.Object, access.Object)
				.GetDataAsync(Context("invoicing.receivables"), CancellationToken.None);

			data.ReadOnly.Should().BeTrue("a lapsed add-on still reads");
			data.Buckets.Select(b => b.Key).Should().Equal("Current", "31-60");
			data.OverdueCount.Should().Be(2);
			data.Overdue.Should().BeEquivalentTo(new[] { new DashboardCurrencyAmount { Currency = "CAD", Amount = 50m }, new DashboardCurrencyAmount { Currency = "USD", Amount = 200m } });
			data.Outstanding.Should().HaveCount(2);
			data.DraftCount.Should().Be(4);
			data.OpenCount.Should().Be(3);
		}

		[Test]
		public async Task Online_payments_never_carry_account_ids_tokens_or_the_provider_error()
		{
			var payments = new Mock<IInvoicePaymentsService>();
			payments.Setup(p => p.GetStatusAsync(Dept)).ReturnsAsync(new OnlinePaymentsStatus
			{
				AvailableInCluster = true, FlagEnabled = true, EnabledByDepartment = true, BlockedReason = "payments_connection_not_ready",
				Connection = new DepartmentPaymentConnection { DisplayName = "Acme Fire", ExternalAccountId = "acct_secret", AccessTokenCiphertext = "cipher", LastError = "token revoked", Status = 0 },
				Capabilities = new Dictionary<string, bool> { ["charges_enabled"] = false, ["payouts_enabled"] = true }
			});

			var data = (InvoicingOnlinePaymentsWidgetData)await new InvoicingOnlinePaymentsWidgetProvider(payments.Object)
				.GetDataAsync(Context("invoicing.onlinePayments", Viewer(admin: true)), CancellationToken.None);

			data.BlockedReason.Should().Be("payments_connection_not_ready");
			data.ConnectionName.Should().Be("Acme Fire");
			data.ConnectionHasError.Should().BeTrue();
			data.ChargesEnabled.Should().BeFalse();
			data.PayoutsEnabled.Should().BeTrue();
			typeof(InvoicingOnlinePaymentsWidgetData).GetProperties().Select(p => p.Name).Should().NotContain(new[] { "ExternalAccountId", "AccessTokenCiphertext", "LastError" });
		}

		[Test]
		public async Task Bids_count_by_status_with_the_expiry_window_from_the_setting()
		{
			var bids = new Mock<IBidsService>(MockBehavior.Strict);
			var pipeline = new BidPipelineSummary { ExpiringSoon = 2 };
			pipeline.ByStatus[(int)BidStatuses.Submitted] = 5;
			pipeline.ByStatus[(int)BidStatuses.Accepted] = 1;
			bids.Setup(b => b.GetBidPipelineAsync(Dept, Now, Now.AddDays(30))).ReturnsAsync(pipeline);

			var data = (BidsPipelineWidgetData)await new BidsPipelineWidgetProvider(bids.Object)
				.GetDataAsync(Context("bids.pipeline", settings: new Dictionary<string, object> { ["expiringDays"] = 30 }), CancellationToken.None);

			data.Submitted.Should().Be(5);
			data.Accepted.Should().Be(1);
			data.Draft.Should().Be(0);
			data.ExpiringSoon.Should().Be(2);
			data.ExpiringDays.Should().Be(30);
		}

		[Test]
		public async Task Bid_pipeline_only_counts_submitted_bids_as_expiring()
		{
			var repository = new Mock<IBidRepository>(MockBehavior.Strict);
			repository.Setup(r => r.GetStatusCountsAsync(Dept, Now, Now.AddDays(14))).ReturnsAsync(new[]
			{
				new BidStatusCount { Status = (int)BidStatuses.Draft, Total = 4, Expiring = 3 },
				new BidStatusCount { Status = (int)BidStatuses.Submitted, Total = 6, Expiring = 2 }
			});
			var service = new BidsService(repository.Object, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);

			var pipeline = await service.GetBidPipelineAsync(Dept, Now, Now.AddDays(14));

			pipeline.Count(BidStatuses.Draft).Should().Be(4);
			pipeline.Count(BidStatuses.Submitted).Should().Be(6);
			pipeline.ExpiringSoon.Should().Be(2, "a draft has not gone out, so it cannot lapse");
		}

		[Test]
		public async Task Contracts_list_live_contracts_ending_inside_the_window_and_lapsing_compliance_documents()
		{
			var contracts = new Mock<IServiceContractService>(MockBehavior.Strict);
			contracts.Setup(c => c.GetContractsForDepartmentAsync(Dept, ServiceContractStatuses.Active)).ReturnsAsync(new List<ServiceContract>
			{
				new ServiceContract { ServiceContractId = "soon", DepartmentId = Dept, Name = "County", Status = (int)ServiceContractStatuses.Active, StartOn = Now.AddYears(-1), EndOn = Now.AddDays(10) },
				new ServiceContract { ServiceContractId = "later", DepartmentId = Dept, Name = "State", Status = (int)ServiceContractStatuses.Active, StartOn = Now.AddYears(-1), EndOn = Now.AddDays(200) },
				new ServiceContract { ServiceContractId = "open", DepartmentId = Dept, Name = "City", Status = (int)ServiceContractStatuses.Active, StartOn = Now.AddYears(-1) },
				new ServiceContract { ServiceContractId = "foreign", DepartmentId = OtherDept, Name = "X", Status = (int)ServiceContractStatuses.Active, StartOn = Now.AddYears(-1), EndOn = Now.AddDays(5) }
			});
			contracts.Setup(c => c.GetComplianceDocumentsAsync(Dept)).ReturnsAsync(new List<DepartmentComplianceDocument>
			{
				new DepartmentComplianceDocument { DepartmentId = Dept, ExpiresOn = Now.AddDays(-1) },
				new DepartmentComplianceDocument { DepartmentId = Dept, ExpiresOn = Now.AddDays(20) },
				new DepartmentComplianceDocument { DepartmentId = Dept, ExpiresOn = Now.AddDays(400) },
				new DepartmentComplianceDocument { DepartmentId = Dept, ExpiresOn = Now.AddDays(-5), IsDeleted = true }
			});

			var data = (ContractsExpiringWidgetData)await new ContractsExpiringWidgetProvider(contracts.Object).GetDataAsync(Context("contracts.expiring"), CancellationToken.None);

			data.Active.Should().Be(3);
			data.ExpiringCount.Should().Be(1);
			data.Rows.Should().ContainSingle().Which.DaysLeft.Should().Be(10);
			data.ComplianceDocumentsExpired.Should().Be(1);
			data.ComplianceDocumentsExpiring.Should().Be(1);
		}

		#region costRecovery.queue

		private static CalOesMarsQueueItem QueueItem(string id, bool mine, int age, int errors = 0) => new CalOesMarsQueueItem
		{
			WorkItem = new CalOesMarsWorkItem { CalOesMarsWorkItemId = id, DepartmentId = Dept, RecordType = (int)CalOesMarsRecordTypes.F42, LocalState = (int)CalOesMarsLocalStates.Draft },
			IsMine = mine, AgeDays = age, ErrorCount = errors
		};

		[Test]
		public async Task A_member_works_their_own_mars_items_without_the_readiness_check()
		{
			var mars = new Mock<ICalOesMarsService>(MockBehavior.Strict);
			mars.Setup(m => m.GetActionQueueAsync(Dept, User, false)).ReturnsAsync(new List<CalOesMarsQueueItem> { QueueItem("a", true, 2, errors: 1) });

			var data = (CostRecoveryQueueWidgetData)await new CostRecoveryQueueWidgetProvider(mars.Object, Permissions().Object)
				.GetDataAsync(Context("costRecovery.queue"), CancellationToken.None);

			data.ManagerScope.Should().BeFalse();
			data.Readiness.Should().BeNull("the readiness dashboard needs the manager's view claim");
			data.Rows.Should().ContainSingle().Which.RecordType.Should().Be("F42");
			data.WithErrors.Should().Be(1);
		}

		[Test]
		public async Task A_manager_works_the_agency_queue_oldest_first_with_readiness_on_the_department_date()
		{
			var mars = new Mock<ICalOesMarsService>(MockBehavior.Strict);
			mars.Setup(m => m.GetActionQueueAsync(Dept, User, true)).ReturnsAsync(new List<CalOesMarsQueueItem> { QueueItem("new", false, 1), QueueItem("old", true, 9) });
			mars.Setup(m => m.GetAgencyReadinessAsync(Dept, Now.Date)).ReturnsAsync(new CalOesMarsReadiness
			{
				OpenWorkItems = 2,
				Items = new List<CalOesMarsReadinessItem> { new CalOesMarsReadinessItem { Severity = (int)CalOesMarsReadinessSeverities.Blocker }, new CalOesMarsReadinessItem { Severity = (int)CalOesMarsReadinessSeverities.Warning } }
			});

			var data = (CostRecoveryQueueWidgetData)await new CostRecoveryQueueWidgetProvider(mars.Object, Permissions().Object)
				.GetDataAsync(Context("costRecovery.queue", Viewer(admin: true)), CancellationToken.None);

			data.ManagerScope.Should().BeTrue();
			data.Rows.Select(r => r.WorkItemId).Should().Equal("old", "new");
			data.Readiness.IsReady.Should().BeFalse();
			data.Readiness.Blockers.Should().Be(1);
			data.Readiness.Warnings.Should().Be(1);
		}

		#endregion

		[Test]
		public async Task Field_costing_lists_the_newest_runs_with_their_totals()
		{
			var costing = new Mock<IFieldCostingService>(MockBehavior.Strict);
			costing.Setup(c => c.GetRunsAsync(Dept, 0, 5)).ReturnsAsync(new List<FieldCostRun>
			{
				new FieldCostRun { FieldCostRunId = "r1", DepartmentId = Dept, Status = (int)FieldCostRunStatuses.Frozen, ContextType = (int)FieldCostContextTypes.Deployment, TotalLoadedCost = 1000m, AddedOn = Now.AddDays(-1) },
				new FieldCostRun { FieldCostRunId = "r2", DepartmentId = Dept, Status = (int)FieldCostRunStatuses.NeedsReview, ContextType = (int)FieldCostContextTypes.Bid, MissingInputCount = 3, AddedOn = Now },
				new FieldCostRun { FieldCostRunId = "foreign", DepartmentId = OtherDept, AddedOn = Now }
			});

			var data = (WorkforceFieldCostingWidgetData)await new WorkforceFieldCostingWidgetProvider(costing.Object).GetDataAsync(Context("workforce.fieldCosting"), CancellationToken.None);

			data.Runs.Select(r => r.FieldCostRunId).Should().Equal("r2", "r1");
			data.Runs[1].ContextType.Should().Be("Deployment");
			data.OpenRuns.Should().Be(1);
			data.MissingInputs.Should().Be(3);
		}

		[Test]
		public async Task Pay_data_reports_last_year_in_the_department_time_zone_as_counts_only()
		{
			var reporting = new Mock<ICaPayDataReportingService>(MockBehavior.Strict);
			reporting.Setup(r => r.GetReadinessAsync(Dept, 2025)).ReturnsAsync(new PayDataReadiness { ReportingYear = 2025, OpenRuns = 1, ProfileAvailable = true });
			var demographics = new Mock<IPayDataDemographicsService>(MockBehavior.Strict);
			demographics.Setup(d => d.GetCompletenessAsync(Dept, new DateTime(2025, 12, 31))).ReturnsAsync(new DemographicCompleteness { ActiveWorkers = 8, WithResponse = 6 });

			var data = (WorkforcePayDataWidgetData)await new WorkforcePayDataWidgetProvider(reporting.Object, demographics.Object)
				.GetDataAsync(Context("workforce.payData"), CancellationToken.None);

			data.ReportingYear.Should().Be(2025);
			data.CompletenessPercent.Should().Be(75);
			data.Missing.Should().Be(2);
			data.OpenRuns.Should().Be(1);
		}

		[Test]
		public async Task Pay_data_with_no_active_workers_is_complete()
		{
			var reporting = new Mock<ICaPayDataReportingService>();
			var demographics = new Mock<IPayDataDemographicsService>();
			demographics.Setup(d => d.GetCompletenessAsync(Dept, It.IsAny<DateTime>())).ReturnsAsync(new DemographicCompleteness());

			var data = (WorkforcePayDataWidgetData)await new WorkforcePayDataWidgetProvider(reporting.Object, demographics.Object)
				.GetDataAsync(Context("workforce.payData"), CancellationToken.None);

			data.CompletenessPercent.Should().Be(100);
		}
	}
}
