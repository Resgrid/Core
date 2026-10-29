using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Personnel;
using Resgrid.Web.Areas.User.Models.Reports.Personnel;
using Resgrid.Web.Services.Models.v4.Personnel;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;
using MvcClaims = Resgrid.Web.Helpers.ClaimsAuthorizationHelper;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using V4 = Resgrid.Web.Services.Controllers.v4;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// GitHub #311, the pages about one person: taking a hidden or disabled person off the personnel list meant little while
	/// their profile, events and the v4 person lookup still opened by id. Those follow the list's rule (department admins,
	/// and the admins of the person's group), and a hidden or disabled person can still open their own.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class PersonnelProfileHiddenDisabledTests
	{
		private const int DepartmentId = 312;
		private const string Owner = "OWNER-312";
		private const string DepartmentAdmin = "ADMIN-312";
		private const string StationAdmin = "S1-ADMIN-312";
		private const string OtherStationAdmin = "S2-ADMIN-312";
		private const string StationMember = "S1-MEMBER-312";
		private const string HiddenPerson = "S1-HIDDEN-312";
		private const string DisabledPerson = "S1-DISABLED-312";
		private const string VisiblePerson = "S1-VISIBLE-312";

		private Activity _activity;

		[SetUp]
		public void SetUp() => _activity = new Activity(nameof(PersonnelProfileHiddenDisabledTests)).Start();

		[TearDown]
		public void TearDown()
		{
			MvcClaims._httpContextAccessor = null;
			ApiClaims._httpContextAccessor = null;
			_activity?.Stop();
		}

		private static T Build<T>(string caller) where T : ControllerBase
		{
			var department = new Department { DepartmentId = DepartmentId, ManagingUserId = Owner, AdminUsers = new List<string> { DepartmentAdmin } };

			var stationOne = new DepartmentGroup
			{
				DepartmentGroupId = 1, DepartmentId = DepartmentId, Name = "Station 1",
				Members = new List<DepartmentGroupMember>
				{
					new DepartmentGroupMember { DepartmentGroupId = 1, UserId = StationAdmin, IsAdmin = true },
					new DepartmentGroupMember { DepartmentGroupId = 1, UserId = StationMember },
					new DepartmentGroupMember { DepartmentGroupId = 1, UserId = HiddenPerson },
					new DepartmentGroupMember { DepartmentGroupId = 1, UserId = DisabledPerson },
					new DepartmentGroupMember { DepartmentGroupId = 1, UserId = VisiblePerson }
				}
			};
			var stationTwo = new DepartmentGroup
			{
				DepartmentGroupId = 2, DepartmentId = DepartmentId, Name = "Station 2",
				Members = new List<DepartmentGroupMember> { new DepartmentGroupMember { DepartmentGroupId = 2, UserId = OtherStationAdmin, IsAdmin = true } }
			};

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(department);
			departments.Setup(x => x.GetDepartmentByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync(department);
			departments.Setup(x => x.GetDepartmentMemberAsync(It.IsAny<string>(), DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync((string userId, int departmentId, bool _) => new DepartmentMember
				{
					DepartmentId = departmentId, UserId = userId, IsHidden = userId == HiddenPerson, IsDisabled = userId == DisabledPerson
				});

			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(x => x.GetGroupForUserAsync(It.IsAny<string>(), DepartmentId))
				.ReturnsAsync((string userId, int _) => stationTwo.Members.Any(m => m.UserId == userId) ? stationTwo : stationOne);

			// Same department and Security > View Group Users let everyone see everyone, so only the hidden/disabled rule narrows.
			var authorization = new Mock<Resgrid.Model.Services.IAuthorizationService>();
			authorization.Setup(x => x.CanUserViewUserAsync(caller, It.IsAny<string>())).ReturnsAsync(true);
			authorization.Setup(x => x.CanUserViewPersonViaMatrixAsync(It.IsAny<string>(), caller, DepartmentId)).ReturnsAsync(true);
			authorization.Setup(x => x.CanUserViewPersonAsync(caller, It.IsAny<string>(), DepartmentId)).ReturnsAsync(true);

			// Event 1 is the hidden person's, event 2 the disabled person's.
			var actionLogs = new Mock<IActionLogsService>();
			actionLogs.Setup(x => x.GetActionLogByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => id switch
			{
				1 => new ActionLog { ActionLogId = 1, DepartmentId = DepartmentId, UserId = HiddenPerson },
				2 => new ActionLog { ActionLogId = 2, DepartmentId = DepartmentId, UserId = DisabledPerson },
				_ => null
			});

			var users = new Mock<IUsersService>();
			users.Setup(x => x.GetUserById(It.IsAny<string>(), It.IsAny<bool>())).Returns((string userId, bool _) => new IdentityUser { Id = userId, UserName = userId });
			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(x => x.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>()))
				.ReturnsAsync((string userId, bool _) => new UserProfile { UserId = userId, FirstName = "First", LastName = userId });

			var known = new Dictionary<Type, object>
			{
				[typeof(IDepartmentsService)] = departments.Object,
				[typeof(IDepartmentGroupsService)] = groups.Object,
				[typeof(Resgrid.Model.Services.IAuthorizationService)] = authorization.Object,
				[typeof(IUsersService)] = users.Object,
				[typeof(IUserProfileService)] = profiles.Object,
				[typeof(IActionLogsService)] = actionLogs.Object,
				[typeof(UserManager<IdentityUser>)] = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null).Object
			};

			var constructor = typeof(T).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p => known.TryGetValue(p.ParameterType, out var value) ? value :
				((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object).ToArray();
			var controller = (T)constructor.Invoke(arguments);

			var claims = new List<Claim> { new Claim(ClaimTypes.PrimarySid, caller), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) };
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
			http.Connection.RemoteIpAddress = IPAddress.Loopback;
			MvcClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			ApiClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			return controller;
		}

		#region MVC person pages

		[TestCase(DepartmentAdmin, HiddenPerson)]
		[TestCase(Owner, DisabledPerson)]
		[TestCase(StationAdmin, HiddenPerson)]
		[TestCase(StationAdmin, DisabledPerson)]
		[TestCase(HiddenPerson, HiddenPerson)]
		[TestCase(DisabledPerson, DisabledPerson)]
		[TestCase(StationMember, VisiblePerson)]
		public async Task The_profile_opens_for_an_admin_their_group_admin_or_the_person_themselves(string caller, string person)
		{
			var result = await Build<PersonnelController>(caller).ViewPerson(person);

			result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<ViewPersonView>();
		}

		[TestCase(StationMember, HiddenPerson)]
		[TestCase(StationMember, DisabledPerson)]
		[TestCase(OtherStationAdmin, HiddenPerson)]
		[TestCase(HiddenPerson, DisabledPerson)]
		public async Task The_profile_is_refused_to_everyone_else(string caller, string person)
		{
			var result = await Build<PersonnelController>(caller).ViewPerson(person);

			IsRefused(result);
		}

		[Test]
		public async Task The_events_pages_follow_the_profile()
		{
			IsRefused(await Build<PersonnelController>(StationMember).ViewEvents(HiddenPerson));
			IsRefused(await Build<PersonnelController>(StationMember).GetPersonnelEvents(DisabledPerson));
			(await Build<PersonnelController>(StationAdmin).ViewEvents(HiddenPerson)).Should().BeOfType<ViewResult>();
		}

		[Test]
		public async Task The_events_report_leaves_out_events_of_people_whose_events_page_is_refused()
		{
			var form = new FormCollection(new Dictionary<string, StringValues> { ["selectEvent_1"] = "on", ["selectEvent_2"] = "on" });

			var result = await Build<PersonnelController>(StationMember).GeneratePersonnelEventsReport(form);

			result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<PersonnelEventsReportView>()
				.Which.Rows.Should().BeEmpty("posting a hidden or disabled person's event ids must not reach what their events page refuses");
		}

		/// <summary>SecureBaseController.Unauthorized sends the browser to the unauthorized page.</summary>
		private static void IsRefused(IActionResult result) =>
			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");

		#endregion MVC person pages

		#region v4 person lookup

		[TestCase(DepartmentAdmin, HiddenPerson)]
		[TestCase(StationAdmin, DisabledPerson)]
		[TestCase(HiddenPerson, HiddenPerson)]
		[TestCase(StationMember, VisiblePerson)]
		public async Task Personnel_info_answers_an_admin_their_group_admin_or_the_person_themselves(string caller, string person)
		{
			var result = await Build<V4.PersonnelController>(caller).GetPersonnelInfo(person);

			((PersonnelInfoResult)result.Result.Should().BeOfType<OkObjectResult>().Subject.Value).Data.UserId.Should().Be(person);
		}

		[TestCase(StationMember, HiddenPerson)]
		[TestCase(StationMember, DisabledPerson)]
		[TestCase(OtherStationAdmin, HiddenPerson)]
		public async Task Personnel_info_of_a_hidden_or_disabled_person_is_not_found_for_everyone_else(string caller, string person)
		{
			var result = await Build<V4.PersonnelController>(caller).GetPersonnelInfo(person);

			result.Result.Should().BeNull();
			result.Value.Data.Should().BeNull();
			result.Value.Status.Should().Be(Resgrid.Web.Services.Helpers.ResponseHelper.NotFound);
		}

		#endregion v4 person lookup
	}
}
