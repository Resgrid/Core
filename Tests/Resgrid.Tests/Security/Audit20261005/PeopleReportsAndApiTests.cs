using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models;
using Resgrid.Web.Areas.User.Models.Departments.ActionLogs;
using Resgrid.Web.Areas.User.Models.Reports.Personnel;
using Resgrid.Web.Services.Models.v4.Personnel;
using Resgrid.Web.Services.Models.v4.PersonnelStatuses;
using Resgrid.Web.Services.Models.v4.Sync;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;
using MvcClaims = Resgrid.Web.Helpers.ClaimsAuthorizationHelper;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using V4 = Resgrid.Web.Services.Controllers.v4;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Permission audit 2026-10-05: the personnel report behind View Personal Info (3.1), the View Group Users lock on the
	/// status reports (3.4), v4 personnel/status positions behind See Personnel Locations (3.2), the Sync roster (3.4) and
	/// the Sync board delta behind the commander gate (3.8).
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class PeopleReportsAndApiTests
	{
		private const int DepartmentId = 41;
		private const string Caller = "CALLER-2";
		private const string Visible = "VISIBLE-2";
		private const string Hidden = "HIDDEN-2";

		private Mock<IAuthorizationService> _authorization;
		private Mock<IDepartmentsService> _departments;
		private Mock<IUserProfileService> _profiles;
		private Mock<IActionLogsService> _actionLogs;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IDepartmentMemberSensitiveDataService> _sensitive;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_authorization = new Mock<IAuthorizationService>();
			_authorization.Setup(x => x.CanUserViewUserAsync(Caller, It.IsAny<string>())).ReturnsAsync(true);
			_authorization.Setup(x => x.CanUserViewPersonViaMatrixAsync(It.IsAny<string>(), Caller, DepartmentId))
				.ReturnsAsync((string target, string _, int __) => target != Hidden);
			_authorization.Setup(x => x.CanUserViewPersonLocationViaMatrixAsync(It.IsAny<string>(), Caller, DepartmentId))
				.ReturnsAsync((string target, string _, int __) => target == Caller);

			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = "owner" });
			_departments.Setup(x => x.GetDepartmentMemberAsync(It.IsAny<string>(), DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync((string userId, int departmentId, bool _) => new DepartmentMember { DepartmentId = departmentId, UserId = userId });
			_departments.Setup(x => x.GetDepartmentByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId });

			_profiles = new Mock<IUserProfileService>();
			_profiles.Setup(x => x.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>()))
				.ReturnsAsync((string userId, bool _) => new UserProfile { UserId = userId, FirstName = "First", LastName = userId, MobileNumber = "+15550002222" });

			_actionLogs = new Mock<IActionLogsService>();
			_groups = new Mock<IDepartmentGroupsService>();
			_groups.Setup(x => x.GetAllDepartmentGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new Dictionary<string, DepartmentGroup>());

			_sensitive = new Mock<IDepartmentMemberSensitiveDataService>();
			_sensitive.Setup(x => x.GetResolvedForDepartmentAsync(DepartmentId, It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync(new Dictionary<string, DepartmentMemberSensitiveData>
				{
					[Visible] = new DepartmentMemberSensitiveData { UserId = Visible, MailingAddress1 = "<b>1 Main</b>", MailingCity = "Reno" }
				});

			_activity = new Activity(nameof(PeopleReportsAndApiTests)).Start();
		}

		[TearDown]
		public void TearDown()
		{
			MvcClaims._httpContextAccessor = null;
			ApiClaims._httpContextAccessor = null;
			_activity?.Stop();
		}

		private static DefaultHttpContext Http(params Claim[] extra)
		{
			var claims = new List<Claim> { new Claim(ClaimTypes.PrimarySid, Caller), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) };
			claims.AddRange(extra);
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
			MvcClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			ApiClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			return http;
		}

		private T Build<T>(Dictionary<Type, object> extra, params Claim[] claims) where T : ControllerBase
		{
			var known = new Dictionary<Type, object>
			{
				[typeof(IAuthorizationService)] = _authorization.Object,
				[typeof(IDepartmentsService)] = _departments.Object,
				[typeof(IUserProfileService)] = _profiles.Object,
				[typeof(IActionLogsService)] = _actionLogs.Object,
				[typeof(IDepartmentGroupsService)] = _groups.Object,
				[typeof(IDepartmentMemberSensitiveDataService)] = _sensitive.Object
			};
			foreach (var item in extra ?? new Dictionary<Type, object>())
				known[item.Key] = item.Value;

			var constructor = typeof(T).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
				known.TryGetValue(p.ParameterType, out var value) ? value :
				((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object).ToArray();
			var controller = (T)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = Http(claims) };
			return controller;
		}

		private static Claim PersonalInfoClaim => new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Create);

		#region MVC reports

		private ReportsController Reports(params Claim[] claims)
		{
			_departments.Setup(x => x.GetAllUsersForDepartmentUnlimitedMinusDisabledAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new List<IdentityUser> { new IdentityUser { Id = Visible, Email = "visible@example.com", UserName = "visible" } });
			var roles = new Mock<IPersonnelRolesService>();
			roles.Setup(x => x.GetRolesForUserAsync(It.IsAny<string>(), DepartmentId)).ReturnsAsync(new List<PersonnelRole>());

			return Build<ReportsController>(new Dictionary<Type, object> { [typeof(IPersonnelRolesService)] = roles.Object }, claims);
		}

		[Test]
		public async Task The_personnel_report_leaves_out_contact_details_without_View_Personal_Info()
		{
			var result = await Reports().PersonnelReport();

			var row = result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<PersonnelReportView>().Which.Rows.Single();
			row.Name.Should().NotBeNullOrWhiteSpace();
			row.Email.Should().BeNull();
			row.MobilePhoneNumber.Should().BeNull();
			row.MailingAddress.Should().BeNull();
		}

		[Test]
		public async Task The_personnel_report_shows_contact_details_with_View_Personal_Info_and_encodes_the_address()
		{
			var result = await Reports(PersonalInfoClaim).PersonnelReport();

			var row = result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<PersonnelReportView>().Which.Rows.Single();
			row.Email.Should().Be("visible@example.com");
			row.MobilePhoneNumber.Should().Be("+15550002222");
			row.MailingAddress.Should().Contain("&lt;b&gt;1 Main&lt;/b&gt;").And.NotContain("<b>");
		}

		[Test]
		public async Task The_hours_detail_report_refuses_a_person_the_report_picker_hides()
		{
			var result = await Reports().PersonnelHoursDetailReport(Hidden, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
		}

		[Test]
		public async Task The_status_history_report_without_a_person_lists_only_visible_people()
		{
			_profiles.Setup(x => x.GetAllProfilesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Dictionary<string, UserProfile>
			{
				[Visible] = new UserProfile { UserId = Visible, FirstName = "Vis", LastName = "Ible" },
				[Hidden] = new UserProfile { UserId = Hidden, FirstName = "Hid", LastName = "Den" }
			});
			_actionLogs.Setup(x => x.GetAllActionLogsInDateRangeAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(new List<ActionLog>
			{
				new ActionLog { UserId = Visible, DepartmentId = DepartmentId, ActionTypeId = (int)ActionTypes.Responding, Timestamp = DateTime.UtcNow },
				new ActionLog { UserId = Hidden, DepartmentId = DepartmentId, ActionTypeId = (int)ActionTypes.Responding, Timestamp = DateTime.UtcNow }
			});

			var result = await Reports().ActionLogs(false, 0, null, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

			var people = result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<PersonnelStatusHistoryView>().Which.Personnel;
			people.Select(x => x.Name).Should().BeEquivalentTo(new[] { "Vis Ible" });
		}

		[Test]
		public async Task The_action_log_feed_lists_only_visible_people()
		{
			_actionLogs.Setup(x => x.GetAllActionLogsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<ActionLog>
			{
				new ActionLog { UserId = Visible, ActionTypeId = (int)ActionTypes.Responding, Timestamp = DateTime.UtcNow },
				new ActionLog { UserId = Hidden, ActionTypeId = (int)ActionTypes.Responding, Timestamp = DateTime.UtcNow }
			});
			_departments.Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonName>
			{
				new PersonName { UserId = Visible, FirstName = "Vis", LastName = "Ible" }, new PersonName { UserId = Hidden, FirstName = "Hid", LastName = "Den" }
			});

			var result = await Reports().GetActionLogs();

			result.Should().BeOfType<JsonResult>().Which.Value.Should().BeAssignableTo<List<ActionLogForJson>>()
				.Which.Select(x => x.Name).Should().BeEquivalentTo(new[] { "Vis Ible" });
		}

		#endregion MVC reports

		#region v4 personnel and statuses

		[Test]
		public async Task Personnel_info_leaves_out_the_status_position_without_See_Personnel_Locations()
		{
			var users = new Mock<IUsersService>();
			users.Setup(x => x.GetUserById(It.IsAny<string>(), It.IsAny<bool>())).Returns((string userId, bool _) => new IdentityUser { Id = userId });
			_actionLogs.Setup(x => x.GetLastActionLogForUserAsync(It.IsAny<string>(), DepartmentId))
				.ReturnsAsync((string userId, int? departmentId) => new ActionLog { UserId = userId, DepartmentId = departmentId ?? 0, ActionTypeId = (int)ActionTypes.Responding, GeoLocationData = "39.5,-119.8", Timestamp = DateTime.UtcNow });
			var controller = Build<V4.PersonnelController>(new Dictionary<Type, object> { [typeof(IUsersService)] = users.Object });

			var other = await controller.GetPersonnelInfo(Visible);
			var self = await controller.GetPersonnelInfo(Caller);

			((PersonnelInfoResult)((OkObjectResult)other.Result).Value).Data.Location.Should().BeNull();
			((PersonnelInfoResult)((OkObjectResult)self.Result).Value).Data.Location.Should().Be("39.5,-119.8");
		}

		[Test]
		public async Task Current_status_of_a_hidden_person_is_not_found_and_a_visible_one_has_no_position_without_the_location_right()
		{
			_actionLogs.Setup(x => x.GetLastActionLogForUserAsync(It.IsAny<string>(), DepartmentId))
				.ReturnsAsync((string userId, int? departmentId) => new ActionLog { UserId = userId, DepartmentId = departmentId ?? 0, ActionTypeId = (int)ActionTypes.Responding, GeoLocationData = "39.5,-119.8", Timestamp = DateTime.UtcNow });
			var controller = Build<V4.PersonnelStatusesController>(null);

			var hidden = (await controller.GetCurrentStatus(Hidden)).Value;
			var visible = (await controller.GetCurrentStatus(Visible)).Value;
			var self = (await controller.GetCurrentStatus(null)).Value;

			hidden.Data.Should().BeNull();
			hidden.Status.Should().Be(Resgrid.Web.Services.Helpers.ResponseHelper.NotFound);
			visible.Data.GeoLocationData.Should().BeNull();
			self.Data.GeoLocationData.Should().Be("39.5,-119.8");
		}

		#endregion v4 personnel and statuses

		#region v4 sync

		[Test]
		public async Task Sync_changes_and_bundle_are_empty_for_a_caller_the_commander_gate_refuses()
		{
			var commands = new Mock<IIncidentCommandService>();
			var access = new Mock<ICommandAccessService>();
			access.Setup(x => x.CanUseCommandAsync(DepartmentId, Caller)).ReturnsAsync(false);
			var controller = Build<V4.SyncController>(new Dictionary<Type, object>
			{
				[typeof(IIncidentCommandService)] = commands.Object, [typeof(ICommandAccessService)] = access.Object
			});

			var changes = (await controller.Changes(1700000000000)).Value;
			var bundle = (await controller.Bundle()).Value;

			changes.Data.Commands.Should().BeEmpty();
			changes.Data.ServerTimestampMs.Should().Be(1700000000000, "the cursor stays where it was");
			bundle.Data.Boards.Should().BeEmpty();
			commands.Verify(x => x.GetChangesSinceAsync(It.IsAny<int>(), It.IsAny<DateTime>()), Times.Never);
			commands.Verify(x => x.GetBundleForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Sync_changes_still_reach_a_commander()
		{
			var commands = new Mock<IIncidentCommandService>();
			commands.Setup(x => x.GetChangesSinceAsync(DepartmentId, It.IsAny<DateTime>())).ReturnsAsync(new IncidentCommandChanges
			{
				ServerTimestampMs = 5, Commands = new List<IncidentCommand> { new IncidentCommand() }
			});
			var resources = new Mock<IIncidentResourcesService>();
			resources.Setup(x => x.GetAdHocChangesSinceAsync(DepartmentId, It.IsAny<DateTime>()))
				.ReturnsAsync((new List<IncidentAdHocUnit>(), new List<IncidentAdHocPersonnel>()));
			var access = new Mock<ICommandAccessService>();
			access.Setup(x => x.CanUseCommandAsync(DepartmentId, Caller)).ReturnsAsync(true);
			var controller = Build<V4.SyncController>(new Dictionary<Type, object>
			{
				[typeof(IIncidentCommandService)] = commands.Object, [typeof(ICommandAccessService)] = access.Object,
				[typeof(IIncidentResourcesService)] = resources.Object
			});

			var changes = (await controller.Changes(0)).Value;

			changes.Data.Commands.Should().ContainSingle();
		}

		[Test]
		public async Task Sync_reference_roster_lists_only_the_people_the_caller_may_see()
		{
			var sync = new Mock<ISyncService>();
			sync.Setup(x => x.GetReferenceDataAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new SyncReferenceData
			{
				Personnel = new List<ReferencePersonnel>
				{
					new ReferencePersonnel { UserId = Visible, MobilePhone = "+1" },
					new ReferencePersonnel { UserId = Hidden, MobilePhone = "+2" }
				}
			});
			_authorization.Setup(x => x.CanUserViewPIIAsync(Caller, DepartmentId)).ReturnsAsync(true);
			var controller = Build<V4.SyncController>(new Dictionary<Type, object> { [typeof(ISyncService)] = sync.Object });

			var reference = (await controller.Reference()).Value;

			reference.Data.Personnel.Select(x => x.UserId).Should().BeEquivalentTo(new[] { Visible });
		}

		#endregion v4 sync
	}
}
