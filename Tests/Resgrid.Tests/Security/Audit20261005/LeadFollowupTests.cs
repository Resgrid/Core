using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.DistributionLists;
using Resgrid.Web.Areas.User.Models.Training;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Follow-ups the area agents handed back (audit 2026-10-05): department-scoped status-history clearing, map layer and
	/// POI management for department admins only, unit status changes as POST + antiforgery, training and distribution-list
	/// recipients confined to the department, custom-field values filtered on the v4 unit list, and the scheduled Personnel
	/// report showing contact details only to a subscriber who holds View Personal Info.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class LeadFollowupTests
	{
		private const int DepartmentId = 12;
		private const string UserId = "member-1";

		private Dictionary<Type, Mock> _mocks;

		[SetUp]
		public void SetUp() => _mocks = new Dictionary<Type, Mock>();

		[TearDown]
		public void TearDown()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
			Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		private Mock<T> M<T>() where T : class
		{
			if (!_mocks.TryGetValue(typeof(T), out var mock))
				_mocks[typeof(T)] = mock = new Mock<T>();
			return (Mock<T>)mock;
		}

		private static readonly Claim DepartmentAdmin = new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update);
		private static readonly Claim PreventionAdmin = new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.PreventionAdmin);

		private DefaultHttpContext Context(params Claim[] claims)
		{
			var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) }.Concat(claims), "test");
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			return http;
		}

		/// <summary>Builds a controller whose constructor dependencies are the fixture's loose mocks.</summary>
		private T Build<T>(params Claim[] claims) where T : ControllerBase
		{
			var constructor = typeof(T).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
			{
				if (!_mocks.TryGetValue(p.ParameterType, out var mock))
					_mocks[p.ParameterType] = mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType));
				return mock.Object;
			}).ToArray();
			var controller = (T)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = Context(claims) };
			return controller;
		}

		private static void ShouldBeRefused(IActionResult result) =>
			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");

		// ── status history ─────────────────────────────────────────────────────────────────────────

		[Test]
		public async Task Clearing_a_members_status_history_touches_only_this_departments_logs()
		{
			var repository = new Mock<IActionLogsRepository>();
			var here = new ActionLog { ActionLogId = 1, UserId = UserId, DepartmentId = DepartmentId };
			var elsewhere = new ActionLog { ActionLogId = 2, UserId = UserId, DepartmentId = 99 };
			repository.Setup(r => r.GetAllActionLogsForUser(UserId)).ReturnsAsync(new List<ActionLog> { here, elsewhere });
			var service = new ActionLogsService(repository.Object, Mock.Of<IUsersService>(), Mock.Of<IDepartmentMembersRepository>(), Mock.Of<IDepartmentGroupsService>(),
				Mock.Of<IDepartmentsService>(), Mock.Of<IDepartmentSettingsService>(), Mock.Of<Resgrid.Model.Providers.IEventAggregator>(), Mock.Of<IGeoService>(),
				Mock.Of<ICustomStateService>(), Mock.Of<Resgrid.Model.Providers.ICacheProvider>(), Mock.Of<ICallStatusAttributionService>());

			(await service.DeleteActionLogsForUserAsync(UserId, DepartmentId)).Should().BeTrue();

			repository.Verify(r => r.DeleteAsync(here, It.IsAny<CancellationToken>()), Times.Once);
			repository.Verify(r => r.DeleteAsync(elsewhere, It.IsAny<CancellationToken>()), Times.Never);
		}

		// ── map layers and POIs ────────────────────────────────────────────────────────────────────

		[Test]
		public async Task Map_layer_and_poi_management_refuses_members_who_are_not_department_admins()
		{
			var controller = Build<MappingController>();

			ShouldBeRefused(await controller.Layers());
			ShouldBeRefused(await controller.POIs());
			ShouldBeRefused(await controller.AddPOIType());
			ShouldBeRefused(await controller.DeleteLayer("layer-1"));
			ShouldBeRefused(await controller.DeletePOIType(5, CancellationToken.None));
			ShouldBeRefused(await controller.DeletePOI(7, CancellationToken.None));
			ShouldBeRefused(await controller.ImportPOIs(5));
			M<IMappingService>().Verify(x => x.DeletePOITypeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
			M<IMappingService>().Verify(x => x.DeletePOIAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Department_admins_manage_pois_and_prevention_admins_may_import_them()
		{
			(await Build<MappingController>(DepartmentAdmin).AddPOIType()).Should().BeOfType<ViewResult>();
			(await Build<MappingController>(PreventionAdmin).ImportPOIs(5)).Should().BeOfType<ViewResult>();
			ShouldBeRefused(await Build<MappingController>(PreventionAdmin).AddPOIType());
		}

		// ── unit status changes ────────────────────────────────────────────────────────────────────

		[TestCase(nameof(UnitsController.SetUnitState))]
		[TestCase(nameof(UnitsController.SetUnitStateWithDest))]
		[TestCase(nameof(UnitsController.SetUnitStateForMultiple))]
		[TestCase(nameof(UnitsController.SetUnitStateWithDestForMultiple))]
		public void Unit_status_changes_are_posts_with_an_antiforgery_token(string action)
		{
			var method = typeof(UnitsController).GetMethods().Single(m => m.Name == action);

			method.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull(action);
			method.GetCustomAttribute<HttpGetAttribute>().Should().BeNull(action);
			method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull(action);
		}

		[Test]
		public async Task Unit_status_menus_post_their_target_instead_of_linking_to_it()
		{
			var result = await Build<UnitsController>().GetUnitOptionsDropdownForStates(0, "4|5");

			var html = result.Should().BeOfType<ContentResult>().Subject.Content;
			html.Should().Contain("data-post-url='/User/Units/SetUnitStateForMultiple?");
			html.Should().NotContain("href='/User/Units/");
		}

		// ── recipients confined to the department ──────────────────────────────────────────────────

		private void DepartmentUsers(params string[] userIds) =>
			M<IDepartmentsService>().Setup(x => x.GetAllUsersForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync(userIds.Select(id => new IdentityUser { Id = id }).ToList());

		private static IFormCollection Form(Dictionary<string, string> values) =>
			new FormCollection(values.ToDictionary(kv => kv.Key, kv => new StringValues(kv.Value)));

		[Test]
		public async Task A_training_is_never_assigned_to_users_groups_or_roles_of_another_department()
		{
			DepartmentUsers("member-1", "member-2");
			M<IDepartmentGroupsService>().Setup(x => x.GetGroupByIdAsync(30, It.IsAny<bool>())).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 30, DepartmentId = 99 });
			M<IDepartmentGroupsService>().Setup(x => x.GetAllMembersForGroupAsync(30)).ReturnsAsync(new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = "outsider-group" } });
			M<IPersonnelRolesService>().Setup(x => x.GetRoleByIdAsync(40)).ReturnsAsync(new PersonnelRole { PersonnelRoleId = 40, DepartmentId = 99 });
			M<IPersonnelRolesService>().Setup(x => x.GetAllMembersOfRoleAsync(40)).ReturnsAsync(new List<PersonnelRoleUser> { new PersonnelRoleUser { UserId = "outsider-role" } });
			Training saved = null;
			M<ITrainingService>().Setup(x => x.SaveAsync(It.IsAny<Training>(), It.IsAny<CancellationToken>())).Callback<Training, CancellationToken>((t, _) => saved = t).ReturnsAsync((Training t, CancellationToken _) => t);

			var form = Form(new Dictionary<string, string> { ["usersToAdd"] = "member-2,outsider", ["groupsToAdd"] = "30", ["rolesToAdd"] = "40" });
			await Build<TrainingsController>(DepartmentAdmin).New(new NewTrainingModel { Training = new Training { Name = "Pump ops" } }, form, new List<IFormFile>());

			saved.Should().NotBeNull();
			saved.Users.Select(u => u.UserId).Should().BeEquivalentTo(new[] { "member-2" });
		}

		[Test]
		public async Task A_distribution_list_never_takes_members_of_another_department()
		{
			DepartmentUsers("member-1", "member-2");
			DistributionList saved = null;
			M<IDistributionListsService>().Setup(x => x.SaveDistributionListAsync(It.IsAny<DistributionList>(), It.IsAny<CancellationToken>()))
				.Callback<DistributionList, CancellationToken>((l, _) => saved = l).ReturnsAsync((DistributionList l, CancellationToken _) => l);

			var model = new NewListView { List = new DistributionList { Name = "Officers", EmailAddress = "officers" } };
			await Build<DistributionListsController>(DepartmentAdmin).NewList(model, Form(new Dictionary<string, string> { ["listMembers"] = "member-1,outsider" }), CancellationToken.None);

			saved.Should().NotBeNull();
			saved.Members.Select(m => m.UserId).Should().BeEquivalentTo(new[] { "member-1" });
		}

		// ── scheduled Personnel report ────────────────────────────────────────────────────────────

		private Task<bool> SubscriberMayViewPersonalInfo(string userId)
		{
			var controller = Build<ReportsController>();
			var method = typeof(ReportsController).GetMethod("CanSubscriberViewPersonalInfoAsync", BindingFlags.NonPublic | BindingFlags.Instance);
			return (Task<bool>)method.Invoke(controller, new object[] { userId, DepartmentId });
		}

		[Test]
		public async Task The_scheduled_personnel_report_shows_contact_details_only_to_a_current_member_with_PII_rights()
		{
			M<IDepartmentsService>().Setup(x => x.GetDepartmentMemberAsync("reader", DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentMember { UserId = "reader", DepartmentId = DepartmentId });
			M<IDepartmentsService>().Setup(x => x.GetDepartmentMemberAsync("restricted", DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentMember { UserId = "restricted", DepartmentId = DepartmentId });
			M<IDepartmentsService>().Setup(x => x.GetDepartmentMemberAsync("removed", DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentMember { UserId = "removed", DepartmentId = DepartmentId, IsDeleted = true });
			M<IAuthorizationService>().Setup(x => x.CanUserViewPIIAsync("reader", DepartmentId)).ReturnsAsync(true);
			M<IAuthorizationService>().Setup(x => x.CanUserViewPIIAsync("removed", DepartmentId)).ReturnsAsync(true);
			M<IAuthorizationService>().Setup(x => x.CanUserViewPIIAsync("restricted", DepartmentId)).ReturnsAsync(false);

			(await SubscriberMayViewPersonalInfo("reader")).Should().BeTrue();
			(await SubscriberMayViewPersonalInfo("restricted")).Should().BeFalse();
			(await SubscriberMayViewPersonalInfo("removed")).Should().BeFalse();
			(await SubscriberMayViewPersonalInfo(null)).Should().BeFalse("an older worker that does not pass the subscriber gets no contact details");
		}

		// ── custom-field values on the v4 unit list ───────────────────────────────────────────────

		[Test]
		public async Task The_v4_unit_list_returns_only_custom_field_values_the_caller_may_see()
		{
			var activity = new Activity(nameof(LeadFollowupTests)).Start();
			try
			{
				M<IUnitsService>().Setup(x => x.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit> { new Unit { UnitId = 4, DepartmentId = DepartmentId, Name = "Engine 4" } });
				M<IUnitsService>().Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<UnitState>());
				M<IAuthorizationService>().Setup(x => x.CanUserViewUnitViaMatrixAsync(4, UserId, DepartmentId)).ReturnsAsync(true);
				var stored = new List<UdfFieldValue> { new UdfFieldValue { UdfFieldId = "admin-only", EntityId = "4", Value = "secret" } };
				M<IUserDefinedFieldsService>().Setup(x => x.GetFieldValuesForEntitiesAsync(DepartmentId, (int)UdfEntityType.Unit, It.IsAny<IEnumerable<string>>())).ReturnsAsync(stored);
				M<IUserDefinedFieldsService>().Setup(x => x.FilterValuesVisibleToUserAsync(DepartmentId, (int)UdfEntityType.Unit, It.IsAny<IEnumerable<UdfFieldValue>>(), false, false, false))
					.ReturnsAsync(new List<UdfFieldValue>());

				var result = await Build<Resgrid.Web.Services.Controllers.v4.UnitsController>().GetAllUnits();

				M<IUserDefinedFieldsService>().Verify(x => x.FilterValuesVisibleToUserAsync(DepartmentId, (int)UdfEntityType.Unit, It.IsAny<IEnumerable<UdfFieldValue>>(), false, false, false), Times.Once);
				var units = result.Value?.Data ?? ((result.Result as ObjectResult)?.Value as Resgrid.Web.Services.Models.v4.Units.UnitsResult)?.Data;
				units.Should().NotBeNull();
				units.Should().OnlyContain(u => u.UdfValues == null || u.UdfValues.Count == 0);
			}
			finally
			{
				activity.Stop();
			}
		}
	}
}
