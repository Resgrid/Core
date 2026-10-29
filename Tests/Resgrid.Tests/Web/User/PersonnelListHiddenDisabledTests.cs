using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Personnel;
using Resgrid.Web.Helpers;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// GitHub #311: the personnel list showed the department's hidden and disabled people to every member. Department admins
	/// see all of them, a group admin sees the ones in their group, and nobody else sees them.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class PersonnelListHiddenDisabledTests
	{
		private const int DepartmentId = 311;
		private const string Owner = "OWNER-311";
		private const string DepartmentAdmin = "ADMIN-311";
		private const string StationOneAdmin = "S1-ADMIN-311";
		private const string StationTwoAdmin = "S2-ADMIN-311";
		private const string StationOneMember = "S1-MEMBER-311";
		private const string HiddenInStationOne = "S1-HIDDEN-311";
		private const string DisabledInStationTwo = "S2-DISABLED-311";
		private const string HiddenUngrouped = "NOGROUP-HIDDEN-311";

		private IHttpContextAccessor _previousAccessor;

		[SetUp]
		public void SetUp() => _previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor;

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;

		private static readonly string[] Everyone =
			{ Owner, DepartmentAdmin, StationOneAdmin, StationTwoAdmin, StationOneMember, HiddenInStationOne, DisabledInStationTwo, HiddenUngrouped };

		private static readonly string[] NeitherHiddenNorDisabled =
			{ Owner, DepartmentAdmin, StationOneAdmin, StationTwoAdmin, StationOneMember };

		private static PersonnelController Build(string caller)
		{
			var department = new Department { DepartmentId = DepartmentId, ManagingUserId = Owner, AdminUsers = new List<string> { DepartmentAdmin } };

			var members = Everyone.Select(id => new DepartmentMember
			{
				DepartmentId = DepartmentId,
				UserId = id,
				IsAdmin = id == DepartmentAdmin,
				IsHidden = id == HiddenInStationOne || id == HiddenUngrouped,
				IsDisabled = id == DisabledInStationTwo
			}).ToList();

			var stationOne = new DepartmentGroup
			{
				DepartmentGroupId = 1, DepartmentId = DepartmentId, Name = "Station 1",
				Members = new List<DepartmentGroupMember>
				{
					new DepartmentGroupMember { DepartmentGroupId = 1, UserId = StationOneAdmin, IsAdmin = true },
					new DepartmentGroupMember { DepartmentGroupId = 1, UserId = StationOneMember },
					new DepartmentGroupMember { DepartmentGroupId = 1, UserId = HiddenInStationOne }
				}
			};
			var stationTwo = new DepartmentGroup
			{
				DepartmentGroupId = 2, DepartmentId = DepartmentId, Name = "Station 2",
				Members = new List<DepartmentGroupMember>
				{
					new DepartmentGroupMember { DepartmentGroupId = 2, UserId = StationTwoAdmin, IsAdmin = true },
					new DepartmentGroupMember { DepartmentGroupId = 2, UserId = DisabledInStationTwo }
				}
			};

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(department);
			departments.Setup(x => x.GetAllUsersForDepartmentUnlimitedAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(Everyone.Select(id => new IdentityUser { Id = id, UserName = id, Email = id + "@example.com" }).ToList());
			departments.Setup(x => x.GetAllMembersForDepartmentUnlimitedAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(members);

			var groups = new Mock<IDepartmentGroupsService>();
			foreach (var groupMember in stationOne.Members)
				groups.Setup(x => x.GetGroupForUserAsync(groupMember.UserId, DepartmentId)).ReturnsAsync(stationOne);
			foreach (var groupMember in stationTwo.Members)
				groups.Setup(x => x.GetGroupForUserAsync(groupMember.UserId, DepartmentId)).ReturnsAsync(stationTwo);

			// Security > View Group Users lets everyone see everyone, so only the hidden/disabled rule narrows the list.
			var authorization = new Mock<Resgrid.Model.Services.IAuthorizationService>();
			authorization.Setup(x => x.CanUserViewPersonViaMatrixAsync(It.IsAny<string>(), caller, DepartmentId)).ReturnsAsync(true);

			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(x => x.GetAllProfilesForDepartmentIncDisabledDeletedAsync(DepartmentId)).ReturnsAsync(new Dictionary<string, UserProfile>());
			var users = new Mock<IUsersService>();
			users.Setup(x => x.GetUserGroupAndRolesByDepartmentIdInLimitAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync(new List<UserGroupRole>());
			var externalIdentity = new Mock<IExternalIdentityLinkService>();
			externalIdentity.Setup(x => x.GetSsoManagementStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SsoManagementState());

			var known = new Dictionary<Type, object>
			{
				[typeof(IDepartmentsService)] = departments.Object,
				[typeof(IDepartmentGroupsService)] = groups.Object,
				[typeof(Resgrid.Model.Services.IAuthorizationService)] = authorization.Object,
				[typeof(IUserProfileService)] = profiles.Object,
				[typeof(IUsersService)] = users.Object,
				[typeof(IExternalIdentityLinkService)] = externalIdentity.Object,
				[typeof(UserManager<IdentityUser>)] = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null).Object
			};

			var constructor = typeof(PersonnelController).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p => known.TryGetValue(p.ParameterType, out var value) ? value :
				((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object).ToArray();
			var controller = (PersonnelController)constructor.Invoke(arguments);

			var claims = new List<Claim> { new Claim(ClaimTypes.PrimarySid, caller), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) };
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
			http.Connection.RemoteIpAddress = IPAddress.Loopback;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			return controller;
		}

		private static async Task<IEnumerable<string>> ListedFor(string caller)
		{
			var result = await Build(caller).GetPersonnelList();

			var json = result.Should().BeOfType<JsonResult>().Subject;
			return json.Value.Should().BeAssignableTo<IEnumerable<PersonnelForListJson>>().Subject.Select(p => p.UserId);
		}

		[TestCase(DepartmentAdmin)]
		[TestCase(Owner)]
		public async Task A_department_admin_sees_every_hidden_and_disabled_member(string admin)
		{
			(await ListedFor(admin)).Should().BeEquivalentTo(Everyone);
		}

		[Test]
		public async Task A_group_admin_sees_the_hidden_and_disabled_members_of_their_group_only()
		{
			(await ListedFor(StationOneAdmin)).Should().BeEquivalentTo(NeitherHiddenNorDisabled.Append(HiddenInStationOne));
			(await ListedFor(StationTwoAdmin)).Should().BeEquivalentTo(NeitherHiddenNorDisabled.Append(DisabledInStationTwo));
		}

		[Test]
		public async Task An_ordinary_member_sees_no_hidden_or_disabled_member_even_in_their_own_group()
		{
			(await ListedFor(StationOneMember)).Should().BeEquivalentTo(NeitherHiddenNorDisabled);
		}

		[Test]
		public async Task The_state_column_still_tells_an_admin_which_members_are_hidden_or_disabled()
		{
			var result = (JsonResult)await Build(DepartmentAdmin).GetPersonnelList();
			var listed = ((IEnumerable<PersonnelForListJson>)result.Value).ToDictionary(p => p.UserId, p => p.State);

			listed[HiddenInStationOne].Should().Be("Normal, Hidden");
			listed[DisabledInStationTwo].Should().Be("Normal, Disabled");
			listed[StationOneMember].Should().Be("Normal");
		}
	}
}
