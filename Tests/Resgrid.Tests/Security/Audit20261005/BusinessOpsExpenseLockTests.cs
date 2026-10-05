using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Invoicing;
using Resgrid.Web.Areas.User.Models.Deployments;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Audit 2026-10-05 item 2.7: an expense cannot be added to, moved onto or changed on a daily time report that is past
	/// approval (Approved, Billed, Void), nor added unlinked on a day whose report is Approved or Billed — the billing engine
	/// would bill it without anyone approving it. Pre-approval is a manager's statement on both the MVC and v4 paths.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class BusinessOpsExpenseLockTests
	{
		private const int Dept = 51;
		private const string Member = "crew-1";
		private static readonly DateTime Day = new DateTime(2026, 9, 21);

		private List<DeploymentTimeReport> _reports;
		private List<DeploymentExpense> _expenses;
		private TimeTrackingService _service;

		[SetUp]
		public void SetUp()
		{
			_reports = new List<DeploymentTimeReport>
			{
				new DeploymentTimeReport { DeploymentTimeReportId = "approved", DeploymentId = "dep-1", DepartmentId = Dept, ReportDate = Day, Status = (int)DeploymentTimeReportStatuses.Approved },
				new DeploymentTimeReport { DeploymentTimeReportId = "billed", DeploymentId = "dep-1", DepartmentId = Dept, ReportDate = Day.AddDays(1), Status = (int)DeploymentTimeReportStatuses.Billed },
				new DeploymentTimeReport { DeploymentTimeReportId = "void", DeploymentId = "dep-1", DepartmentId = Dept, ReportDate = Day.AddDays(2), Status = (int)DeploymentTimeReportStatuses.Void },
				new DeploymentTimeReport { DeploymentTimeReportId = "draft", DeploymentId = "dep-1", DepartmentId = Dept, ReportDate = Day.AddDays(3), Status = (int)DeploymentTimeReportStatuses.Draft }
			};
			_expenses = new List<DeploymentExpense>();

			var deployments = new Mock<IDeploymentRepository>();
			deployments.Setup(r => r.GetByIdForDepartmentAsync("dep-1", Dept)).ReturnsAsync(new Deployment { DeploymentId = "dep-1", DepartmentId = Dept, Currency = "USD", Status = (int)DeploymentStatuses.Active });
			var reports = new Mock<IDeploymentTimeReportRepository>();
			reports.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), Dept)).ReturnsAsync((string id, int _) => _reports.FirstOrDefault(t => t.DeploymentTimeReportId == id));
			reports.Setup(r => r.GetByDeploymentAsync("dep-1")).ReturnsAsync(() => _reports.ToList());
			var expenses = new Mock<IDeploymentExpenseRepository>();
			expenses.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), Dept)).ReturnsAsync((string id, int _) => _expenses.FirstOrDefault(e => e.DeploymentExpenseId == id));
			expenses.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DeploymentExpense>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DeploymentExpense e, CancellationToken _, bool __) => { e.DeploymentExpenseId ??= Guid.NewGuid().ToString(); _expenses.RemoveAll(x => x.DeploymentExpenseId == e.DeploymentExpenseId); _expenses.Add(e); return e; });

			_service = new TimeTrackingService(deployments.Object, Mock.Of<IDeploymentPersonnelRepository>(), Mock.Of<IDeploymentUnitRepository>(), Mock.Of<IDeploymentEquipmentRepository>(), reports.Object,
				Mock.Of<IDeploymentTimeEntryRepository>(), expenses.Object, Mock.Of<IDeploymentAttachmentRepository>(), Mock.Of<ITimeReportNumberSequenceRepository>(), Mock.Of<IDeploymentService>(),
				Mock.Of<IDepartmentsService>(), Mock.Of<IUserProfileService>(), Mock.Of<IUnitsService>(), Mock.Of<IEventAggregator>(), Mock.Of<IPdfProvider>(), null);
		}

		private static DeploymentExpense Expense(string reportId, DateTime date, string id = null) => new DeploymentExpense
		{
			DeploymentExpenseId = id, DeploymentId = "dep-1", DepartmentId = Dept, DeploymentTimeReportId = reportId, ExpenseDate = date, ExpenseType = (int)DeploymentExpenseTypes.Fuel, Amount = 40m, Billable = true
		};

		private Task<DeploymentExpense> Save(DeploymentExpense expense) => _service.SaveExpenseAsync(expense, null, null, null, Member, null, null);

		[TestCase("approved")]
		[TestCase("billed")]
		[TestCase("void")]
		public async Task A_new_expense_cannot_be_linked_to_a_report_past_approval(string reportId)
		{
			var date = _reports.Single(r => r.DeploymentTimeReportId == reportId).ReportDate;
			(await FluentActions.Awaiting(() => Save(Expense(reportId, date))).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("timereports_locked");
			_expenses.Should().BeEmpty();
		}

		[Test]
		public async Task An_unlinked_expense_cannot_be_dated_on_an_approved_or_billed_day_but_a_void_or_open_day_is_fine()
		{
			(await FluentActions.Awaiting(() => Save(Expense(null, Day))).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("timereports_locked");
			(await FluentActions.Awaiting(() => Save(Expense(null, Day.AddDays(1)))).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("timereports_locked");

			(await Save(Expense(null, Day.AddDays(2)))).Should().NotBeNull("a void day bills nothing; its replacement report picks the expense up");
			(await Save(Expense(null, Day.AddDays(10)))).Should().NotBeNull();
			(await Save(Expense("draft", Day.AddDays(3)))).Should().NotBeNull();
		}

		[Test]
		public async Task An_expense_on_a_report_that_was_approved_since_can_neither_change_nor_move()
		{
			var saved = await Save(Expense("draft", Day.AddDays(3)));
			_reports.Single(r => r.DeploymentTimeReportId == "draft").Status = (int)DeploymentTimeReportStatuses.Approved;
			_reports.Add(new DeploymentTimeReport { DeploymentTimeReportId = "draft-2", DeploymentId = "dep-1", DepartmentId = Dept, ReportDate = Day.AddDays(4), Status = (int)DeploymentTimeReportStatuses.Draft });

			var change = Expense("draft", Day.AddDays(3), saved.DeploymentExpenseId);
			change.Amount = 400m;
			(await FluentActions.Awaiting(() => Save(change)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("timereports_locked");
			(await FluentActions.Awaiting(() => Save(Expense("draft-2", Day.AddDays(4), saved.DeploymentExpenseId))).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("timereports_locked");
			_expenses.Single().Amount.Should().Be(40m);
		}

		[Test]
		public async Task A_member_cannot_pre_approve_an_expense_through_the_web_page()
		{
			var http = MemberContext();
			var deploymentService = MemberDeployment();
			var timeTracking = new Mock<ITimeTrackingService>();
			var constructor = typeof(Resgrid.Web.Areas.User.Controllers.DeploymentsController).GetConstructors().Single();
			var controller = (Resgrid.Web.Areas.User.Controllers.DeploymentsController)constructor.Invoke(constructor.GetParameters().Select(p =>
				p.ParameterType == typeof(IDeploymentService) ? deploymentService.Object :
				p.ParameterType == typeof(ITimeTrackingService) ? (object)timeTracking.Object :
				p.ParameterType.IsInterface ? ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object : null).ToArray());
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());

			await controller.SaveExpense(new ExpenseInput { DeploymentId = "dep-1", ExpenseDate = Day.AddDays(10), ExpenseType = (int)DeploymentExpenseTypes.Fuel, Amount = 40m, PreApproved = true }, null, CancellationToken.None);

			timeTracking.Verify(x => x.SaveExpenseAsync(It.Is<DeploymentExpense>(e => !e.PreApproved), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), Member, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_member_cannot_pre_approve_an_expense_through_the_api()
		{
			var http = MemberContext(services: true);
			var deploymentService = MemberDeployment();
			var timeTracking = new Mock<ITimeTrackingService>();
			timeTracking.Setup(x => x.SaveExpenseAsync(It.IsAny<DeploymentExpense>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DeploymentExpense e, byte[] _, string __, string ___, string ____, string _____, string ______, CancellationToken _______) => e);
			var flags = new Mock<IFeatureToggleService>();
			flags.Setup(x => x.IsEnabledAsync(FeatureFlagKeys.Deployments, Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);
			var controller = new Resgrid.Web.Services.Controllers.v4.TimeReportsController(timeTracking.Object, deploymentService.Object, flags.Object, Mock.Of<IDepartmentsService>())
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
			using var activity = new Activity(nameof(BusinessOpsExpenseLockTests)).Start();

			await controller.SaveExpense(new Resgrid.Web.Services.Models.v4.Deployments.SaveExpenseInput { DeploymentId = "dep-1", ExpenseDate = Day.AddDays(10), ExpenseType = (int)DeploymentExpenseTypes.Fuel, Amount = 40m, PreApproved = true }, CancellationToken.None);

			timeTracking.Verify(x => x.SaveExpenseAsync(It.Is<DeploymentExpense>(e => !e.PreApproved), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), Member, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
			Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		private static DefaultHttpContext MemberContext(bool services = false)
		{
			var http = new DefaultHttpContext { Connection = { RemoteIpAddress = System.Net.IPAddress.Loopback }, User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, Member), new Claim(ClaimTypes.PrimaryGroupSid, Dept.ToString()) }, "test")) };
			if (services) Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			else Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			return http;
		}

		/// <summary>The member is rostered on dep-1 and writes their own time there; they hold no deployment permission.</summary>
		private static Mock<IDeploymentService> MemberDeployment()
		{
			var deployment = new Deployment { DeploymentId = "dep-1", DepartmentId = Dept, Status = (int)DeploymentStatuses.Active, Personnel = { new DeploymentPersonnel { DeploymentPersonnelId = "p1", UserId = Member, DepartmentId = Dept } } };
			var access = new DeploymentTimeAccess { IsRostered = true, PersonnelId = "p1" };
			access.WritableSubjectIds.Add("p1");
			var service = new Mock<IDeploymentService>();
			service.Setup(x => x.GetDeploymentByIdAsync("dep-1", Dept)).ReturnsAsync(deployment);
			service.Setup(x => x.GetTimeAccessAsync(deployment, Member, false)).ReturnsAsync(access);
			return service;
		}
	}
}
