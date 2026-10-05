using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Deployments;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Audit 2026-10-05 items 2.13, 3.24 and the invoicing read gate: generating an invoice from a deployment needs
	/// Invoicing_Update as on v4; the invoice packet (compliance documents, DTRs, receipts) needs it too; the deployment
	/// Billing tab and the contract page only show invoices, charges and the compliance checklist to holders of those
	/// families' view permissions; and the invoicing pages honour the department's Business Operations switches.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class BusinessOpsBillingAccessTests
	{
		private const int Dept = 61;
		private const string Me = "deploy-manager";

		private DefaultHttpContext _http;
		private Mock<IDeploymentService> _deployments;
		private Mock<ITimeTrackingService> _timeTracking;
		private Mock<IContractorBillingEngine> _engine;
		private Mock<IServiceContractService> _contracts;
		private Mock<IInvoicingService> _invoicing;
		private Mock<IBusinessOperationsAccessService> _access;
		private Mock<IFeatureToggleService> _flags;
		private Deployment _deployment;

		[SetUp]
		public void SetUp()
		{
			_http = new DefaultHttpContext { Connection = { RemoteIpAddress = System.Net.IPAddress.Loopback }, User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, Me), new Claim(ClaimTypes.PrimaryGroupSid, Dept.ToString()) }, "test")) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };
			_deployment = new Deployment { DeploymentId = "dep-1", DepartmentId = Dept, Name = "Ridge", FinanceMode = (int)DeploymentFinanceModes.Billable, ContactId = "c-1", Status = (int)DeploymentStatuses.Active };
			_deployments = new Mock<IDeploymentService>();
			_deployments.Setup(x => x.GetDeploymentByIdAsync("dep-1", Dept)).ReturnsAsync(_deployment);
			_deployments.Setup(x => x.GetTimeAccessAsync(_deployment, Me, It.IsAny<bool>())).ReturnsAsync((Deployment _, string __, bool manage) => new DeploymentTimeAccess { CanManage = manage });
			_deployments.Setup(x => x.GetAttachmentsAsync("dep-1", Dept)).ReturnsAsync(new List<DeploymentAttachment>());
			_timeTracking = new Mock<ITimeTrackingService>();
			_timeTracking.Setup(x => x.GetTimeReportsAsync("dep-1", Dept)).ReturnsAsync(new List<DeploymentTimeReport>());
			_timeTracking.Setup(x => x.GetExpensesAsync("dep-1", Dept)).ReturnsAsync(new List<DeploymentExpense>());
			_engine = new Mock<IContractorBillingEngine>();
			_engine.Setup(x => x.GenerateInvoiceFromDeploymentAsync("dep-1", Dept, It.IsAny<DateTime?>(), Me, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Invoice { InvoiceId = "inv-1" });
			_contracts = new Mock<IServiceContractService>();
			_contracts.Setup(x => x.GetContractByIdAsync("sc-1", Dept)).ReturnsAsync(new ServiceContract { ServiceContractId = "sc-1", DepartmentId = Dept, ContactId = "c-1", Name = "MOU" });
			_invoicing = new Mock<IInvoicingService>();
			_invoicing.Setup(x => x.GetInvoicesForDepartmentAsync(Dept, It.IsAny<InvoiceListFilter>())).ReturnsAsync(new List<Invoice>());
			_access = new Mock<IBusinessOperationsAccessService>();
			_access.Setup(x => x.CanUseContractorBillingAsync(Dept)).ReturnsAsync(true);
			_flags = new Mock<IFeatureToggleService>();
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		private void Grant(string resource, string action) => _http.User.AddIdentity(new ClaimsIdentity(new[] { new Claim(resource, action) }));

		private T Build<T>() where T : Controller
		{
			var constructor = typeof(T).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var supplied = new object[] { _deployments.Object, _timeTracking.Object, _engine.Object, _contracts.Object, _invoicing.Object, _access.Object, _flags.Object };
			var arguments = constructor.GetParameters().Select(p =>
				supplied.FirstOrDefault(p.ParameterType.IsInstanceOfType) ??
				(p.ParameterType.IsInterface ? ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object : null)).ToArray();
			var controller = (T)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = _http };
			controller.TempData = new TempDataDictionary(_http, Mock.Of<ITempDataProvider>());
			return controller;
		}

		[Test]
		public async Task Generating_an_invoice_from_a_deployment_needs_ManageInvoicing_as_on_v4()
		{
			Grant(ResgridClaimTypes.Resources.Deployments, ResgridClaimTypes.Actions.Update);

			(await Build<DeploymentsController>().GenerateInvoice("dep-1", null, CancellationToken.None))
				.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			_engine.Verify(x => x.GenerateInvoiceFromDeploymentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			Grant(ResgridClaimTypes.Resources.Invoicing, ResgridClaimTypes.Actions.Update);
			(await Build<DeploymentsController>().GenerateInvoice("dep-1", null, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ControllerName.Should().Be("Invoicing");
		}

		[Test]
		public async Task The_billing_tab_shows_charges_invoices_and_compliance_only_to_their_view_permissions()
		{
			Grant(ResgridClaimTypes.Resources.Deployments, ResgridClaimTypes.Actions.Update);

			var view = (DeploymentDetailView)(await Build<DeploymentsController>().View("dep-1", "billing")).Should().BeOfType<ViewResult>().Subject.Model;

			view.ContractorBilling.Should().BeTrue("the tab itself stays the deployment manager's");
			view.Charges.Should().BeNull();
			view.Compliance.Should().BeNull();
			view.Invoices.Should().BeEmpty();
			_engine.Verify(x => x.CalculateDeploymentChargesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime?>()), Times.Never);
			_contracts.Verify(x => x.GetContractComplianceAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
			_invoicing.Verify(x => x.GetInvoicesForDepartmentAsync(It.IsAny<int>(), It.IsAny<InvoiceListFilter>()), Times.Never);

			Grant(ResgridClaimTypes.Resources.Invoicing, ResgridClaimTypes.Actions.View);
			Grant(ResgridClaimTypes.Resources.ServiceContracts, ResgridClaimTypes.Actions.View);
			await Build<DeploymentsController>().View("dep-1", "billing");
			_engine.Verify(x => x.CalculateDeploymentChargesAsync("dep-1", Dept, It.IsAny<DateTime?>()), Times.Once);
			_contracts.Verify(x => x.GetContractComplianceAsync("dep-1", Dept), Times.Once);
			_invoicing.Verify(x => x.GetInvoicesForDepartmentAsync(Dept, It.IsAny<InvoiceListFilter>()), Times.Once);
		}

		[Test]
		public async Task The_contract_page_lists_invoices_only_for_ViewInvoicing()
		{
			Grant(ResgridClaimTypes.Resources.ServiceContracts, ResgridClaimTypes.Actions.View);

			(await Build<ContractsController>().View("sc-1")).Should().BeOfType<ViewResult>();
			_invoicing.Verify(x => x.GetInvoicesForDepartmentAsync(It.IsAny<int>(), It.IsAny<InvoiceListFilter>()), Times.Never);

			Grant(ResgridClaimTypes.Resources.Invoicing, ResgridClaimTypes.Actions.View);
			await Build<ContractsController>().View("sc-1");
			_invoicing.Verify(x => x.GetInvoicesForDepartmentAsync(Dept, It.Is<InvoiceListFilter>(f => f.ServiceContractId == "sc-1")), Times.Once);
		}

		[Test]
		public void The_invoice_packet_takes_the_same_Invoicing_Update_as_sending_it()
		{
			Policy(nameof(InvoicingController.Packet)).Should().Be(ResgridResources.Invoicing_Update);
			Policy(nameof(InvoicingController.SendPacket)).Should().Be(ResgridResources.Invoicing_Update);
			Policy(nameof(InvoicingController.Pdf)).Should().Be(ResgridResources.Invoicing_View, "the invoice itself stays readable with Invoicing_View");
		}

		private static string Policy(string action) =>
			typeof(InvoicingController).GetMethods().Where(m => m.Name == action).SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>()).Single().Policy;

		[Test]
		public async Task Invoicing_pages_are_absent_when_the_departments_Business_Operations_switches_are_off()
		{
			var settingsField = typeof(SettingsHelper).GetField("_departmentSettingsService", BindingFlags.Static | BindingFlags.NonPublic);
			var previous = settingsField.GetValue(null);
			try
			{
				var settings = new Mock<IDepartmentSettingsService>();
				settings.Setup(x => x.GetDepartmentModuleSettingsAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(new DepartmentModuleSettings());
				settingsField.SetValue(null, settings.Object);
				_flags.Setup(x => x.IsEnabledAsync(FeatureFlagKeys.CustomerInvoicing, Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);
				_access.Setup(x => x.IsEnabledAsync(Dept)).ReturnsAsync(false);
				_http.Request.Method = "GET";

				var controller = Build<InvoicingController>();
				var context = new ActionExecutingContext(new ActionContext(_http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>(), new Dictionary<string, object>(), controller);
				var reached = false;
				await controller.OnActionExecutionAsync(context, () => { reached = true; return Task.FromResult<ActionExecutedContext>(null); });

				context.Result.Should().BeOfType<NotFoundResult>("v4 hides the module the same way when the master flag or the module switch is off");
				reached.Should().BeFalse();
			}
			finally
			{
				settingsField.SetValue(null, previous);
			}
		}
	}
}
