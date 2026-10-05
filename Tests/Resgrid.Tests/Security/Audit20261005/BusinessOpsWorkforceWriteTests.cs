using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;
using Resgrid.Model.Workforce;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Workforce;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Audit 2026-10-05 items 2.6 and 3.20 (Workforce MVC): ViewInternalCosts (74) reads cost data; writing it — someone
	/// else's usage reading, approvals, running, freezing or deleting a cost run — also needs Workforce_Update (75), the
	/// resource-cost profiles' rule. A caller still files and changes their own unapproved readings. ExportPayDataReporting
	/// (78) reaches a run and its artifacts but not the per-worker snapshots, which stay with ManagePayDataReporting (77).
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class BusinessOpsWorkforceWriteTests
	{
		private const int Dept = 41;
		private const string Me = "analyst-1";

		private DefaultHttpContext _http;
		private Mock<IFieldCostingService> _costing;
		private Mock<ICaPayDataReportingService> _reporting;
		private Mock<IWorkforceService> _workforce;
		private Mock<IDeploymentService> _deployments;

		[SetUp]
		public void SetUp()
		{
			_http = new DefaultHttpContext { Connection = { RemoteIpAddress = System.Net.IPAddress.Loopback }, User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, Me), new Claim(ClaimTypes.PrimaryGroupSid, Dept.ToString()) }, "test")) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };
			_costing = new Mock<IFieldCostingService>();
			_costing.Setup(x => x.GetUsageForDeploymentAsync("dep-1", Dept)).ReturnsAsync(new List<ResourceUsageEntry>
			{
				new ResourceUsageEntry { ResourceUsageEntryId = "mine", DepartmentId = Dept, DeploymentId = "dep-1", AddedByUserId = Me },
				new ResourceUsageEntry { ResourceUsageEntryId = "theirs", DepartmentId = Dept, DeploymentId = "dep-1", AddedByUserId = "crew-9" },
				new ResourceUsageEntry { ResourceUsageEntryId = "approved", DepartmentId = Dept, DeploymentId = "dep-1", AddedByUserId = Me, IsApproved = true }
			});
			_costing.Setup(x => x.FreezeCostRunAsync("run-1", Dept, Me, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new FieldCostRun { FieldCostRunId = "run-1" });
			_reporting = new Mock<ICaPayDataReportingService>();
			_reporting.Setup(x => x.GetRunAsync("pd-1", Dept)).ReturnsAsync(new PayDataReportRun { PayDataReportRunId = "pd-1", DepartmentId = Dept, ReportingYear = 2025 });
			_reporting.Setup(x => x.GetSnapshotsAsync("pd-1", Dept)).ReturnsAsync(new List<PayDataReportEmployeeSnapshot> { new PayDataReportEmployeeSnapshot() });
			_reporting.Setup(x => x.GetRowsAsync("pd-1", Dept)).ReturnsAsync(new List<PayDataReportRow>());
			_reporting.Setup(x => x.GetArtifactsAsync("pd-1", Dept)).ReturnsAsync(new List<PayDataExportArtifact>());
			_workforce = new Mock<IWorkforceService>();
			_workforce.Setup(x => x.GetEstablishmentsAsync(Dept)).ReturnsAsync(new List<WorkforceEstablishment>());
			_deployments = new Mock<IDeploymentService>();
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		private void Grant(string resource, string action) => _http.User.AddIdentity(new ClaimsIdentity(new[] { new Claim(resource, action) }));

		private WorkforceController Controller()
		{
			var constructor = typeof(WorkforceController).GetConstructors().Single();
			var arguments = constructor.GetParameters().Select(p =>
				p.ParameterType == typeof(IFieldCostingService) ? _costing.Object :
				p.ParameterType == typeof(ICaPayDataReportingService) ? _reporting.Object :
				p.ParameterType == typeof(IWorkforceService) ? _workforce.Object :
				p.ParameterType == typeof(IDeploymentService) ? (object)_deployments.Object :
				((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object).ToArray();
			var controller = (WorkforceController)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = _http };
			controller.TempData = new TempDataDictionary(_http, Mock.Of<ITempDataProvider>());
			return controller;
		}

		private static void ShouldBeUnauthorized(IActionResult result) => result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");

		[Test]
		public async Task ViewInternalCosts_alone_cannot_run_freeze_or_delete_cost_runs()
		{
			Grant(ResgridClaimTypes.Resources.InternalCosts, ResgridClaimTypes.Actions.View);
			var controller = Controller();

			ShouldBeUnauthorized(await controller.FreezeCostRun("run-1"));
			ShouldBeUnauthorized(await controller.DeleteCostRun("run-1"));
			ShouldBeUnauthorized(await controller.RunDeploymentCost("dep-1", null, 0));
			ShouldBeUnauthorized(await controller.RunCallCost(9));
			ShouldBeUnauthorized(await controller.RunBidEstimate("bid-1"));
			_costing.Verify(x => x.FreezeCostRunAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_costing.Verify(x => x.DeleteRunAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_costing.Verify(x => x.CalculateDeploymentCostAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<RevenueSources>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task ViewInternalCosts_with_Workforce_Update_freezes_a_cost_run()
		{
			Grant(ResgridClaimTypes.Resources.InternalCosts, ResgridClaimTypes.Actions.View);
			Grant(ResgridClaimTypes.Resources.Workforce, ResgridClaimTypes.Actions.Update);

			(await Controller().FreezeCostRun("run-1")).Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("CostRun");
			_costing.Verify(x => x.FreezeCostRunAsync("run-1", Dept, Me, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_viewer_cannot_change_or_delete_someone_elses_usage_reading_or_an_approved_one()
		{
			Grant(ResgridClaimTypes.Resources.InternalCosts, ResgridClaimTypes.Actions.View);
			var controller = Controller();

			ShouldBeUnauthorized(await controller.SaveUsage(new ResourceUsageEntry { ResourceUsageEntryId = "theirs", DeploymentId = "dep-1", UnitId = 3, Phase = 0 }));
			ShouldBeUnauthorized(await controller.SaveUsage(new ResourceUsageEntry { ResourceUsageEntryId = "approved", DeploymentId = "dep-1", UnitId = 3 }));
			ShouldBeUnauthorized(await controller.DeleteUsage("theirs", "dep-1", null));
			_costing.Verify(x => x.SaveUsageEntryAsync(It.IsAny<ResourceUsageEntry>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_costing.Verify(x => x.DeleteUsageEntryAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_viewer_still_files_and_removes_their_own_reading_but_never_approves_it()
		{
			Grant(ResgridClaimTypes.Resources.InternalCosts, ResgridClaimTypes.Actions.View);
			var controller = Controller();

			await controller.SaveUsage(new ResourceUsageEntry { DeploymentId = "dep-1", UnitId = 3, IsApproved = true });
			_costing.Verify(x => x.SaveUsageEntryAsync(It.Is<ResourceUsageEntry>(u => !u.IsApproved && u.DepartmentId == Dept), Me, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

			await controller.DeleteUsage("mine", "dep-1", null);
			_costing.Verify(x => x.DeleteUsageEntryAsync("mine", Dept, Me, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task The_cost_data_manager_approves_and_edits_anyones_reading()
		{
			Grant(ResgridClaimTypes.Resources.InternalCosts, ResgridClaimTypes.Actions.View);
			Grant(ResgridClaimTypes.Resources.Workforce, ResgridClaimTypes.Actions.Update);

			await Controller().SaveUsage(new ResourceUsageEntry { ResourceUsageEntryId = "theirs", DeploymentId = "dep-1", UnitId = 3, IsApproved = true });
			_costing.Verify(x => x.SaveUsageEntryAsync(It.Is<ResourceUsageEntry>(u => u.IsApproved && u.ResourceUsageEntryId == "theirs"), Me, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Export_alone_sees_the_run_but_not_the_per_worker_snapshots()
		{
			Grant(ResgridClaimTypes.Resources.PayDataReporting, ResgridClaimTypes.Actions.View);
			Grant(ResgridClaimTypes.Resources.PayDataReporting, ResgridClaimTypes.Actions.Export);

			var view = (WorkforcePayDataRunView)(await Controller().PayDataRun("pd-1")).Should().BeOfType<ViewResult>().Subject.Model;

			view.Snapshots.Should().BeEmpty();
			view.Tab.Should().Be("rows");
			_reporting.Verify(x => x.GetSnapshotsAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task ManagePayDataReporting_still_reads_the_snapshots()
		{
			Grant(ResgridClaimTypes.Resources.PayDataReporting, ResgridClaimTypes.Actions.View);
			Grant(ResgridClaimTypes.Resources.PayDataReporting, ResgridClaimTypes.Actions.Update);

			var view = (WorkforcePayDataRunView)(await Controller().PayDataRun("pd-1")).Should().BeOfType<ViewResult>().Subject.Model;

			view.Snapshots.Should().ContainSingle();
			view.Tab.Should().Be("snapshots");
		}
	}
}
