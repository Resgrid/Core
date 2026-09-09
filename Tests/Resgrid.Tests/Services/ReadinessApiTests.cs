using System.Diagnostics;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;

namespace Resgrid.Tests.Services
{
	[TestFixture, NonParallelizable]
	public class ReadinessApiTests
	{
		private HttpContextAccessor _context;
		private IHttpContextAccessor _previousAccessor;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_context = new HttpContextAccessor
			{
				HttpContext = new DefaultHttpContext
				{
					User = new ClaimsPrincipal(new ClaimsIdentity(new[]
					{
						new Claim(ClaimTypes.PrimarySid, "user-77"),
						new Claim(ClaimTypes.PrimaryGroupSid, "77")
					}, "test"))
				}
			};
			_previousAccessor = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper._httpContextAccessor;
			Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = _context;
			_activity = new Activity("ReadinessApiTests").Start();
		}

		[TearDown]
		public void TearDown()
		{
			_activity.Dispose();
			Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;
		}

		[Test]
		public async Task Catalog_requests_use_the_authenticated_department_and_standard_envelope()
		{
			var service = new Mock<IChecklistTemplateService>(MockBehavior.Strict);
			service.Setup(x => x.SearchAsync(77, "shelter opening")).ReturnsAsync(ChecklistTemplateCatalog.Search("shelter opening"));
			var controller = new ChecklistsController(service.Object, Mock.Of<IChecklistsService>(), null);
			var response = (await controller.GetChecklistTemplates("shelter opening")).Value;
			response.Status.Should().Be("success");
			response.Version.Should().Be("v4");
			response.PageSize.Should().Be(1);
			response.Data[0].Id.Should().Be("em-shelter");
			service.VerifyAll();
		}

		[Test]
		public async Task Disabled_catalog_and_unknown_template_return_not_found()
		{
			var service = new Mock<IChecklistTemplateService>();
			var controller = new ChecklistsController(service.Object, Mock.Of<IChecklistsService>(), null);
			(await controller.GetChecklistTemplates()).Result.Should().BeOfType<NotFoundResult>();
			(await controller.GetChecklistTemplate("unknown")).Result.Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task Invalid_requests_are_rejected_before_catalog_lookup()
		{
			var service = new Mock<IChecklistTemplateService>(MockBehavior.Strict);
			var controller = new ChecklistsController(service.Object, Mock.Of<IChecklistsService>(), null);
			(await controller.GetChecklistTemplates(new string('x', 257))).Result.Should().BeOfType<BadRequestObjectResult>();
			(await controller.GetChecklistTemplate(null)).Result.Should().BeOfType<BadRequestObjectResult>();
			service.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Access_contract_keeps_free_checklists_independent_and_publishes_monthly_regional_prices()
		{
			var access = new Mock<IReadinessAccessService>(MockBehavior.Strict);
			access.Setup(x => x.CanUseChecklistsAsync(77)).ReturnsAsync(true);
			access.Setup(x => x.CanUseMaintenanceAsync(77)).ReturnsAsync(false);
			var response = (await new ReadinessController(access.Object).GetAccess()).Value;
			response.Data.ChecklistsEnabled.Should().BeTrue();
			response.Data.MaintenanceEnabled.Should().BeFalse();
			response.Data.ProductName.Should().Be("Readiness Pro");
			response.Data.BillingInterval.Should().Be("month");
			response.Data.CheckoutAvailable.Should().BeFalse();
			response.Data.Offers.Should().ContainSingle(x => x.Region == "US" && x.Provider == "Stripe" && x.Currency == "USD" && x.MonthlyAmount == 150m);
			response.Data.Offers.Should().ContainSingle(x => x.Region == "EU" && x.Provider == "Paddle" && x.Currency == "EUR" && x.MonthlyAmount == 195m);
			access.VerifyAll();
		}
	}
}
