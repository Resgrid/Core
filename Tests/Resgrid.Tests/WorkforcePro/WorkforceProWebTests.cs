using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Tests.Wellness;
using Resgrid.Web.Areas.User.Models.Subscription;
using Resgrid.Web.Areas.User.Models.Wellness;
using WebClaims = Resgrid.Web.Helpers.ClaimsAuthorizationHelper;
using File = System.IO.File;

namespace Resgrid.Tests.WorkforcePro
{
	/// <summary>
	/// The web side of Workforce Pro: the wellness and member health pages answer 404 while the Workforce.Pro flag is off and carry the
	/// read-only upsell banner while it is not licensed (a lapsed or never-bought department); the seats page lists active members with the
	/// switch and reports refusals; the shared wellness shell, the personnel page, the call page and the add-ons page wire Workforce Pro in.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class WorkforceProWebTests
	{
		private const int DepartmentId = 7;
		private const string UserId = "admin-user";
		private IHttpContextAccessor _previous;
		private DefaultHttpContext _http;

		[SetUp]
		public void SetUp()
		{
			_previous = WebClaims._httpContextAccessor;
			_http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
					new Claim(Resgrid.Providers.Claims.ResgridClaimTypes.Resources.Department, Resgrid.Providers.Claims.ResgridClaimTypes.Actions.Update)
				}, "Test"))
			};
			WebClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };
		}

		[TearDown]
		public void TearDown() => WebClaims._httpContextAccessor = _previous;

		private static IStringLocalizer<T> Echo<T>()
		{
			var localizer = new Mock<IStringLocalizer<T>>();
			localizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			return localizer.Object;
		}

		private T WithContext<T>(T controller) where T : Controller
		{
			controller.ControllerContext = new ControllerContext { HttpContext = _http };
			controller.TempData = new TempDataDictionary(_http, Mock.Of<ITempDataProvider>());
			return controller;
		}

		private Resgrid.Web.Areas.User.Controllers.WellnessController Wellness(FakeWorkforcePro workforcePro, Mock<IMemberHealthService> health) =>
			WithContext(new Resgrid.Web.Areas.User.Controllers.WellnessController(Mock.Of<IMemberWellnessService>(), health.Object, Mock.Of<IDepartmentsService>(),
				Mock.Of<ICallsService>(), Mock.Of<IPersonnelRolesService>(), Mock.Of<ICertificationService>(), Mock.Of<IPdfProvider>(), Mock.Of<ILmsAccessService>(),
				workforcePro, Echo<Resgrid.Localization.Areas.User.Wellness.Wellness>()));

		private Resgrid.Web.Areas.User.Controllers.MemberHealthController MemberHealth(FakeWorkforcePro workforcePro, Mock<IMemberHealthService> health) =>
			WithContext(new Resgrid.Web.Areas.User.Controllers.MemberHealthController(health.Object, Mock.Of<IMemberWellnessService>(), Mock.Of<IDepartmentsService>(),
				Mock.Of<ICallsService>(), Mock.Of<IPdfProvider>(), workforcePro, Echo<Resgrid.Localization.Areas.User.Wellness.MemberHealth>()));

		private ActionExecutingContext Executing(Controller controller) =>
			new ActionExecutingContext(new ActionContext(_http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>(), new Dictionary<string, object>(), controller);

		private static Mock<IMemberHealthService> Health()
		{
			var health = new Mock<IMemberHealthService>();
			health.Setup(h => h.GetAccessAsync(DepartmentId, UserId)).ReturnsAsync(new MemberHealthAccess { IsMember = true, IsAdmin = true });
			return health;
		}

		[Test]
		public async Task The_wellness_pages_answer_404_while_the_flag_is_off_without_reading_anything()
		{
			var workforcePro = new FakeWorkforcePro();
			workforcePro.SwitchOff();
			var health = Health();
			foreach (var controller in new Controller[] { Wellness(workforcePro, health), MemberHealth(workforcePro, health) })
			{
				var context = Executing(controller);
				var ran = false;
				await controller.OnActionExecutionAsync(context, () => { ran = true; return Task.FromResult<ActionExecutedContext>(null); });

				context.Result.Should().BeOfType<NotFoundResult>(controller.GetType().Name);
				ran.Should().BeFalse("the action never runs");
			}
			health.Verify(h => h.GetAccessAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}

		[TestCase(false, true, false)]
		[TestCase(true, false, true)]
		public async Task The_wellness_pages_carry_the_read_only_banner_while_not_licensed(bool lapsed, bool expectLicensed, bool expectReadOnly)
		{
			var workforcePro = new FakeWorkforcePro();
			if (lapsed)
				workforcePro.Lapse();
			foreach (var controller in new Controller[] { Wellness(workforcePro, Health()), MemberHealth(workforcePro, Health()) })
			{
				var context = Executing(controller);
				var ran = false;
				await controller.OnActionExecutionAsync(context, () => { ran = true; return Task.FromResult<ActionExecutedContext>(null); });

				ran.Should().BeTrue();
				var view = controller.ViewData["WellnessAccess"].Should().BeOfType<WellnessAccessView>().Subject;
				view.WorkforceProReadOnly.Should().Be(expectReadOnly);
				view.WorkforceProLicensed.Should().Be(expectLicensed);
				view.ShowWorkforceProLinks.Should().BeTrue("an administrator gets the link to the add-on page");
			}
		}

		[Test]
		public async Task The_seats_page_lists_active_members_with_their_switch_and_a_refusal_is_reported()
		{
			var workforcePro = new FakeWorkforcePro();
			var seats = new Mock<IWorkforceProSeatService>();
			seats.Setup(s => s.GetSummaryAsync(DepartmentId)).ReturnsAsync(new WorkforceProSeatSummary
			{
				Offered = true, State = WorkforceProSubscriptionStates.Active, PurchasedPacks = 1, PackSize = 25, EnabledSeats = 1,
				EnabledUserIds = new HashSet<string>(new[] { "member-user" }, StringComparer.OrdinalIgnoreCase),
				LicensedUserIds = new HashSet<string>(new[] { "member-user" }, StringComparer.OrdinalIgnoreCase)
			});
			seats.Setup(s => s.SetSeatAsync(DepartmentId, "other-user", true, UserId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new WorkforceProSeatChangeResult { Outcome = WorkforceProSeatOutcomes.SeatsFull, PacksNeeded = 1, Summary = new WorkforceProSeatSummary { State = WorkforceProSubscriptionStates.Active, PurchasedPacks = 1, PackSize = 25 } });
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetActiveMemberUserIdsAsync(DepartmentId)).ReturnsAsync(new HashSet<string>(new[] { "member-user", "other-user" }, StringComparer.OrdinalIgnoreCase));
			departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonName>
			{
				new PersonName { UserId = "member-user", FirstName = "Mia", LastName = "Member" }, new PersonName { UserId = "other-user", FirstName = "Olly", LastName = "Other" },
				new PersonName { UserId = "gone-user", FirstName = "Gil", LastName = "Gone" }
			});
			var controller = WithContext(new Resgrid.Web.Areas.User.Controllers.WorkforceProSeatsController(seats.Object, workforcePro, departments.Object, Echo<Resgrid.Localization.Areas.User.Addons.Addons>()));

			var page = (await controller.Index()).Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<WorkforceProSeatsView>().Subject;
			page.Members.Select(m => m.UserId).Should().Equal("member-user", "other-user");
			page.Members.First().Enabled.Should().BeTrue();
			page.Members.First().Licensed.Should().BeTrue();

			(await controller.SetSeat("other-user", true, CancellationToken.None)).Should().BeOfType<RedirectToActionResult>();
			controller.TempData[Resgrid.Web.Areas.User.Controllers.WorkforceProSeatsController.MessageKey].Should().Be("WorkforceProSeatsFull");

			workforcePro.SwitchOff();
			_http.Items.Clear();
			(await controller.Index()).Should().BeOfType<NotFoundResult>();
		}

		private static string Root()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;
			return directory!.FullName;
		}

		[Test]
		public void The_shell_personnel_call_and_add_on_views_wire_Workforce_Pro_in()
		{
			var user = Path.Combine(Root(), "Web", "Resgrid.Web", "Areas", "User", "Views");
			var shell = File.ReadAllText(Path.Combine(user, "Wellness", "_Shell.cshtml"));
			shell.Should().Contain("WorkforcePro.ReadOnly").And.Contain("WorkforceProBilling").And.Contain("WorkforcePro.SeatMissing");
			File.ReadAllText(Path.Combine(user, "Personnel", "Index.cshtml")).Should().Contain("WorkforceProRequestGate.IsOfferedAsync", "the My exposures button hides with the flag");
			File.ReadAllText(Path.Combine(user, "Dispatch", "ViewCall.cshtml")).Should().Contain("WorkforceProRequestGate.IsOfferedAsync", "the wellness follow-up button hides with the flag");
			File.ReadAllText(Path.Combine(user, "Shared", "_AddonCard.cshtml")).Should().Contain("\"WorkforceProBilling\"");
			File.Exists(Path.Combine(user, "WorkforceProBilling", "Index.cshtml")).Should().BeTrue("the Billing API's checkout returns to /User/WorkforceProBilling");
			AddonCatalog.All.Select(a => a.Type).Should().Contain(PlanAddonTypes.WorkforcePro);
		}
	}
}
