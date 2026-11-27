using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using CommonServiceLocator;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Dispatch;
using Resgrid.Web.Areas.User.Models.Units;
using Resgrid.Web.Helpers;
using Resgrid.WebCore.Areas.User.Models.Links;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;
using PersonnelViewModel = Resgrid.Web.Areas.User.Models.BigBoardX.PersonnelViewModel;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The Department Links data page reads the OTHER side's calls, units and personnel through the link. Each list is
	/// gated on its own per-direction share flag over an enabled link: the linked department reads the creator through
	/// DepartmentShare*, the creator reads the linked department through LinkedDepartmentShare*. Before this, the units
	/// and personnel lists only checked that the caller was a party, so a linked department read unit status and staff
	/// status/ETA the creator never shared — even over a disabled link.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class LinksControllerSharingTests
	{
		private const int Creator = 100;
		private const int Linked = 200;
		private const int Outsider = 300;
		private const int LinkId = 9;

		public enum ListKind { Calls, Units, Personnel }

		private IHttpContextAccessor _previousAccessor;
		private IServiceLocator _previousLocator;

		[SetUp]
		public void SetUp()
		{
			_previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor;
			_previousLocator = ServiceLocator.IsLocationProviderSet ? ServiceLocator.Current : null;

			// The personnel board resolves custom staffing through the static CustomStatesHelper (service locator).
			var customStates = new Mock<ICustomStateService>();
			customStates.Setup(x => x.GetAllCustomStatesForDepartmentAsync(It.IsAny<int>())).ReturnsAsync(new List<CustomState>());
			var locator = new Mock<IServiceLocator>();
			locator.Setup(x => x.GetInstance<ICustomStateService>()).Returns(customStates.Object);
			ServiceLocator.SetLocatorProvider(() => locator.Object);
		}

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;
			var previous = _previousLocator;
			ServiceLocator.SetLocatorProvider(previous == null ? null : () => previous);
		}

		/// <summary>SecureBaseController.Unauthorized() redirects to the public unauthorized page.</summary>
		private static void ShouldBeRefused(IActionResult result) =>
			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");

		private static readonly ListKind[] AllLists = { ListKind.Calls, ListKind.Units, ListKind.Personnel };

		private static string CallName(int departmentId) => $"Call of {departmentId}";
		private static string UnitName(int departmentId) => $"Engine of {departmentId}";
		private static string PersonFirstName(int departmentId) => $"Member{departmentId}";

		private sealed class Harness
		{
			public Mock<IDepartmentLinksService> Links { get; } = new Mock<IDepartmentLinksService>();
			public Mock<IDepartmentsService> Departments { get; } = new Mock<IDepartmentsService>();
			public Mock<ICallsService> Calls { get; } = new Mock<ICallsService>();
			public Mock<IUnitsService> Units { get; } = new Mock<IUnitsService>();
			public Mock<IActionLogsService> ActionLogs { get; } = new Mock<IActionLogsService>();
			public Mock<IDepartmentGroupsService> Groups { get; } = new Mock<IDepartmentGroupsService>();
			public Mock<IUserStateService> UserStates { get; } = new Mock<IUserStateService>();
			public Mock<IPersonnelRolesService> Roles { get; } = new Mock<IPersonnelRolesService>();
			public Mock<ILimitsService> Limits { get; } = new Mock<ILimitsService>();
			public Mock<IMappingService> Mapping { get; } = new Mock<IMappingService>();
			public Mock<IEmailService> Email { get; } = new Mock<IEmailService>();

			public Harness(DepartmentLink link)
			{
				Links.Setup(x => x.GetLinkByIdAsync(LinkId)).ReturnsAsync(link);

				foreach (var departmentId in new[] { Creator, Linked, Outsider })
				{
					var id = departmentId;
					var userId = $"USER-{id}";

					Departments.Setup(x => x.GetDepartmentByIdAsync(id, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = id, Name = $"Dept {id}" });

					Calls.Setup(x => x.GetActiveCallsByDepartmentAsync(id)).ReturnsAsync(new List<Call>
					{
						new Call { CallId = id, DepartmentId = id, Number = id.ToString(), Name = CallName(id), LoggedOn = DateTime.UtcNow }
					});

					Units.Setup(x => x.GetUnitsForDepartmentAsync(id)).ReturnsAsync(new List<Unit>
					{
						new Unit { UnitId = id, DepartmentId = id, Name = UnitName(id), Type = "Engine" }
					});
					// No status rows: the status lookup goes through a static service-locator helper.
					Units.Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(id)).ReturnsAsync(new List<UnitState>());

					Departments.Setup(x => x.GetAllUsersForDepartmentAsync(id, It.IsAny<bool>(), It.IsAny<bool>()))
						.ReturnsAsync(new List<IdentityUser> { new IdentityUser { Id = userId, UserName = userId } });
					Departments.Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(id))
						.ReturnsAsync(new List<PersonName> { new PersonName { UserId = userId, FirstName = PersonFirstName(id), LastName = "Smith" } });
					ActionLogs.Setup(x => x.GetAllActionLogsForDepartmentAsync(id)).ReturnsAsync(new List<ActionLog>());
					Groups.Setup(x => x.GetAllGroupsForDepartmentAsync(id)).ReturnsAsync(new List<DepartmentGroup>());
					Groups.Setup(x => x.GetAllStationGroupsForDepartmentAsync(id)).ReturnsAsync(new List<DepartmentGroup>());
					UserStates.Setup(x => x.GetLatestStatesForDepartmentAsync(id, It.IsAny<bool>())).ReturnsAsync(new List<UserState>());
					// The service never returns null: with no stored state it synthesizes "Available".
					UserStates.Setup(x => x.GetLastUserStateByUserIdAsync(userId)).ReturnsAsync(new UserState { UserId = userId, State = 0 });
					Mapping.Setup(x => x.GetPOIsForDepartmentAsync(id)).ReturnsAsync(new List<Poi>());
					Roles.Setup(x => x.GetRolesForUserAsync(userId, id)).ReturnsAsync(new List<PersonnelRole>());
				}
			}

			public LinksController Build(int callerDepartmentId)
			{
				var known = new Dictionary<Type, object>
				{
					[typeof(IDepartmentLinksService)] = Links.Object,
					[typeof(IDepartmentsService)] = Departments.Object,
					[typeof(ICallsService)] = Calls.Object,
					[typeof(IUnitsService)] = Units.Object,
					[typeof(IActionLogsService)] = ActionLogs.Object,
					[typeof(IDepartmentGroupsService)] = Groups.Object,
					[typeof(IUserStateService)] = UserStates.Object,
					[typeof(IPersonnelRolesService)] = Roles.Object,
					[typeof(ILimitsService)] = Limits.Object,
					[typeof(IMappingService)] = Mapping.Object,
					[typeof(IEmailService)] = Email.Object
				};

				var constructor = typeof(LinksController).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
				var arguments = constructor.GetParameters().Select(p => known.TryGetValue(p.ParameterType, out var value) ? value :
					((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object).ToArray();
				var controller = (LinksController)constructor.Invoke(arguments);

				var claims = new List<Claim>
				{
					new Claim(ClaimTypes.PrimarySid, $"VIEWER-{callerDepartmentId}"),
					new Claim(ClaimTypes.PrimaryGroupSid, callerDepartmentId.ToString())
				};
				var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
				http.Connection.RemoteIpAddress = IPAddress.Loopback;
				ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
				controller.ControllerContext = new ControllerContext { HttpContext = http };
				return controller;
			}

			/// <summary>True when the list's data was read for <paramref name="departmentId"/> at all.</summary>
			public bool Queried(ListKind kind, int departmentId)
			{
				switch (kind)
				{
					case ListKind.Calls:
						return Calls.Invocations.Any(i => i.Method.Name == nameof(ICallsService.GetActiveCallsByDepartmentAsync) && (int)i.Arguments[0] == departmentId);
					case ListKind.Units:
						return Units.Invocations.Any(i => i.Method.Name == nameof(IUnitsService.GetUnitsForDepartmentAsync) && (int)i.Arguments[0] == departmentId);
					default:
						return Departments.Invocations.Any(i => i.Method.Name == nameof(IDepartmentsService.GetAllUsersForDepartmentAsync) && (int)i.Arguments[0] == departmentId);
				}
			}
		}

		private static DepartmentLink Link(bool enabled = true) =>
			new DepartmentLink { DepartmentLinkId = LinkId, DepartmentId = Creator, LinkedDepartmentId = Linked, LinkEnabled = enabled };

		private static void CreatorShares(DepartmentLink link, ListKind kind, bool value)
		{
			switch (kind)
			{
				case ListKind.Calls: link.DepartmentShareCalls = value; break;
				case ListKind.Units: link.DepartmentShareUnits = value; break;
				default: link.DepartmentSharePersonnel = value; break;
			}
		}

		private static void LinkedShares(DepartmentLink link, ListKind kind, bool value)
		{
			switch (kind)
			{
				case ListKind.Calls: link.LinkedDepartmentShareCalls = value; break;
				case ListKind.Units: link.LinkedDepartmentShareUnits = value; break;
				default: link.LinkedDepartmentSharePersonnel = value; break;
			}
		}

		private static DepartmentLink EverythingSharedBothWays(bool enabled = true)
		{
			var link = Link(enabled);
			link.DepartmentShareOrders = link.LinkedDepartmentShareOrders = true;
			foreach (var kind in AllLists)
			{
				CreatorShares(link, kind, true);
				LinkedShares(link, kind, true);
			}
			return link;
		}

		private static Task<IActionResult> Get(LinksController controller, ListKind kind)
		{
			switch (kind)
			{
				case ListKind.Calls: return controller.GetActiveCallsList(LinkId);
				case ListKind.Units: return controller.GetUnitsList(LinkId);
				default: return controller.GetPersonnelList(LinkId);
			}
		}

		/// <summary>The listed names, or what identifies each row as one department's data.</summary>
		private static IEnumerable<string> Listed(IActionResult result, ListKind kind)
		{
			var value = result.Should().BeOfType<JsonResult>().Subject.Value.Should().BeAssignableTo<IEnumerable>().Subject;
			switch (kind)
			{
				case ListKind.Calls: return value.Cast<CallListJson>().Select(c => c.Name).ToList();
				case ListKind.Units: return value.Cast<UnitForListJson>().Select(u => u.Name).ToList();
				default: return value.Cast<PersonnelViewModel>().Select(p => p.Name).ToList();
			}
		}

		private static string ExpectedRow(ListKind kind, int departmentId)
		{
			switch (kind)
			{
				case ListKind.Calls: return CallName(departmentId);
				case ListKind.Units: return UnitName(departmentId);
				default: return $"{PersonFirstName(departmentId)} Smith";
			}
		}

		[TestCaseSource(nameof(AllLists))]
		public async Task The_linked_department_sees_the_creators_list_when_the_creator_shares_it(ListKind kind)
		{
			var link = Link();
			CreatorShares(link, kind, true);
			var harness = new Harness(link);

			var result = await Get(harness.Build(Linked), kind);

			Listed(result, kind).Should().Equal(ExpectedRow(kind, Creator));
		}

		[TestCaseSource(nameof(AllLists))]
		public async Task The_linked_department_sees_nothing_the_creator_does_not_share(ListKind kind)
		{
			// Every other kind is shared both ways, and the linked department shares this kind back; none of it counts.
			var link = EverythingSharedBothWays();
			CreatorShares(link, kind, false);
			var harness = new Harness(link);

			var result = await Get(harness.Build(Linked), kind);

			Listed(result, kind).Should().BeEmpty();
			harness.Queried(kind, Creator).Should().BeFalse("unshared data must not even be read");
			harness.Queried(kind, Linked).Should().BeFalse();
		}

		[TestCaseSource(nameof(AllLists))]
		public async Task A_disabled_link_shows_nothing_even_when_shared(ListKind kind)
		{
			var harness = new Harness(EverythingSharedBothWays(enabled: false));

			Listed(await Get(harness.Build(Linked), kind), kind).Should().BeEmpty();
			Listed(await Get(harness.Build(Creator), kind), kind).Should().BeEmpty();
			harness.Queried(kind, Creator).Should().BeFalse();
			harness.Queried(kind, Linked).Should().BeFalse();
		}

		[TestCaseSource(nameof(AllLists))]
		public async Task The_creator_sees_the_linked_departments_list_only_when_it_shares_back(ListKind kind)
		{
			var link = Link();
			LinkedShares(link, kind, true);
			var harness = new Harness(link);

			Listed(await Get(harness.Build(Creator), kind), kind).Should().Equal(ExpectedRow(kind, Linked));
		}

		[TestCaseSource(nameof(AllLists))]
		public async Task The_creator_sees_nothing_through_its_own_share_flags(ListKind kind)
		{
			var link = EverythingSharedBothWays();
			LinkedShares(link, kind, false);
			var harness = new Harness(link);

			Listed(await Get(harness.Build(Creator), kind), kind).Should().BeEmpty();
			harness.Queried(kind, Linked).Should().BeFalse();
		}

		[TestCaseSource(nameof(AllLists))]
		public async Task A_department_outside_the_link_is_refused(ListKind kind)
		{
			var harness = new Harness(EverythingSharedBothWays());

			ShouldBeRefused(await Get(harness.Build(Outsider), kind));
			harness.Queried(kind, Creator).Should().BeFalse();
			harness.Queried(kind, Linked).Should().BeFalse();
		}

		[TestCaseSource(nameof(AllLists))]
		public async Task A_missing_link_is_refused(ListKind kind)
		{
			var harness = new Harness(null);

			ShouldBeRefused(await Get(harness.Build(Linked), kind));
		}

		[Test]
		public async Task A_shared_call_is_never_deletable_from_the_linked_department()
		{
			var link = Link();
			link.DepartmentShareCalls = true;
			var harness = new Harness(link);

			var result = (JsonResult)await harness.Build(Linked).GetActiveCallsList(LinkId);

			((IEnumerable<CallListJson>)result.Value).Should().OnlyContain(c => !c.CanDeleteCall);
		}

		[Test]
		public void The_data_page_requests_the_lists_by_link_id_not_department_id()
		{
			// The page passed the creator's department id as linkId, so every list loaded some other link (or was refused).
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !System.IO.File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;
			directory.Should().NotBeNull("the repository root holds Resgrid.sln");

			var view = System.IO.File.ReadAllText(Path.Combine(directory.FullName, "Web", "Resgrid.Web", "Areas", "User", "Views", "Links", "View.cshtml"));

			view.Should().Contain("var linkId = @Model.Link.DepartmentLinkId;");
			view.Should().NotContain("var linkId = @Model.Link.DepartmentId;");
		}

		[Test]
		public async Task Creating_a_link_ignores_a_posted_row_id_acceptance_and_the_linked_departments_share_flags()
		{
			var harness = new Harness(null);
			harness.Limits.Setup(x => x.CanDepartmentUseLinksAsync(It.IsAny<int>())).ReturnsAsync(true);
			harness.Links.Setup(x => x.GetDepartmentByLinkCodeAsync("LINKCODE")).ReturnsAsync(new Department { DepartmentId = Linked });
			DepartmentLink saved = null;
			harness.Links.Setup(x => x.SaveAsync(It.IsAny<DepartmentLink>(), It.IsAny<CancellationToken>()))
				.Callback<DepartmentLink, CancellationToken>((l, _) => saved = l)
				.ReturnsAsync((DepartmentLink l, CancellationToken _) => l);

			var posted = new NewLinksView
			{
				LinkCode = "LINKCODE",
				Link = new DepartmentLink
				{
					DepartmentLinkId = 55,
					DepartmentId = Outsider,
					LinkedDepartmentId = Outsider,
					DepartmentColor = "#ff0000",
					DepartmentShareCalls = true,
					DepartmentShareUnits = false,
					DepartmentSharePersonnel = true,
					DepartmentShareOrders = false,
					LinkedDepartmentShareCalls = true,
					LinkedDepartmentShareUnits = true,
					LinkedDepartmentSharePersonnel = true,
					LinkedDepartmentShareOrders = true,
					LinkedDepartmentColor = "#00ff00",
					LinkEnabled = true,
					LinkAccepted = DateTime.UtcNow
				}
			};

			var result = await harness.Build(Creator).New(posted, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			saved.Should().NotBeNull();
			saved.DepartmentLinkId.Should().Be(0, "a posted id would overwrite another department's link");
			saved.DepartmentId.Should().Be(Creator);
			saved.LinkedDepartmentId.Should().Be(Linked);
			saved.DepartmentColor.Should().Be("#ff0000");
			saved.DepartmentShareCalls.Should().BeTrue();
			saved.DepartmentShareUnits.Should().BeFalse();
			saved.DepartmentSharePersonnel.Should().BeTrue();
			saved.DepartmentShareOrders.Should().BeFalse();
			saved.LinkedDepartmentShareCalls.Should().BeFalse("only the linked department may share its own data");
			saved.LinkedDepartmentShareUnits.Should().BeFalse();
			saved.LinkedDepartmentSharePersonnel.Should().BeFalse();
			saved.LinkedDepartmentShareOrders.Should().BeFalse();
			saved.LinkedDepartmentColor.Should().BeNull();
			saved.LinkEnabled.Should().BeFalse();
			saved.LinkAccepted.Should().BeNull();
		}
	}
}
