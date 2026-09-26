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
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Profile;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	[TestFixture, NonParallelizable]
	public sealed class ProfileReportScheduleSecurityTests
	{
		private const int DepartmentId = 77;
		private const int ScheduleId = 410;
		private const string UserId = "report-owner";
		private Mock<IScheduledTasksService> _tasks;
		private Mock<Resgrid.Model.Services.IAuthorizationService> _authorization;
		private Mock<ICustomStateService> _customStates;
		private Mock<IDepartmentsService> _departments;
		private Mock<IUsersService> _users;
		private ProfileController _controller;
		private IHttpContextAccessor _previousAccessor;

		[SetUp]
		public void SetUp()
		{
			_previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor;
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "Test"))
			};
			http.Request.Method = "POST";
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			_tasks = new Mock<IScheduledTasksService>(MockBehavior.Strict);
			_authorization = new Mock<Resgrid.Model.Services.IAuthorizationService>(MockBehavior.Strict);
			_authorization.Setup(a => a.CanUserEditProfileAsync(UserId, DepartmentId, UserId)).ReturnsAsync(true);
			_customStates = new Mock<ICustomStateService>();
			_departments = new Mock<IDepartmentsService>();
			_users = new Mock<IUsersService>();
			_controller = new ProfileController(
				departmentsService: _departments.Object, usersService: _users.Object, authorizationService: _authorization.Object,
				userProfileService: null, scheduledTasksService: _tasks.Object, certificationService: null,
				customStateService: _customStates.Object, imageService: null, appOptionsAccessor: null,
				emailService: null, userManager: null, signInManager: null,
				departmentSsoService: null, secLocalizer: null, deleteService: null,
				externalIdentityLinkService: null, userSessionService: null, systemAuditsService: null,
				departmentGroupsService: null, departmentSettingsService: null, passwordRecoveryService: null,
				eventAggregator: null, protectedReadService: null, businessOperationsAccess: Mock.Of<IBusinessOperationsAccessService>(),
				limitsService: null, profileLocalizer: null)
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;

		private static IEnumerable<TestCaseData> RejectedReportTargets()
		{
			foreach (var action in new[] { nameof(ProfileController.ActivateScheduledReport), nameof(ProfileController.DeactivateScheduledReport), nameof(ProfileController.DeleteScheduledReport) })
				foreach (var target in new[] { "missing", "department", "owner", "staffing" })
					yield return new TestCaseData(action, target).SetName($"{action}_rejects_{target}_without_mutation");
		}

		[TestCaseSource(nameof(RejectedReportTargets))]
		public async Task Report_actions_reject_inaccessible_or_non_report_schedules_without_mutation(string action, string target)
		{
			var schedule = OwnedReport();
			switch (target)
			{
				case "missing": schedule = null; break;
				case "department": schedule.DepartmentId = DepartmentId + 1; break;
				case "owner": schedule.UserId = "another-owner"; break;
				case "staffing": schedule.TaskType = (int)TaskTypes.UserStaffingLevel; break;
			}
			_tasks.Setup(s => s.GetScheduledTaskByIdAsync(ScheduleId)).ReturnsAsync(schedule);

			(await InvokeAsync(action)).Should().BeOfType<NotFoundResult>();

			_tasks.Verify(s => s.GetScheduledTaskByIdAsync(ScheduleId), Times.Once);
			VerifyNoMutation();
		}

		[TestCase(nameof(ProfileController.ActivateScheduledReport))]
		[TestCase(nameof(ProfileController.DeactivateScheduledReport))]
		[TestCase(nameof(ProfileController.DeleteScheduledReport))]
		public async Task Report_owner_can_mutate_the_reloaded_schedule_and_passes_cancellation(string action)
		{
			using var cancellation = new CancellationTokenSource();
			var schedule = OwnedReport();
			_tasks.Setup(s => s.GetScheduledTaskByIdAsync(ScheduleId)).ReturnsAsync(schedule);
			ExpectMutation(action, schedule, cancellation.Token);

			(await InvokeAsync(action, cancellation.Token)).Should().BeOfType<EmptyResult>();

			_tasks.VerifyAll();
			_tasks.Invocations.Select(i => i.Method.Name).Should().Equal(nameof(IScheduledTasksService.GetScheduledTaskByIdAsync), MutationMethod(action));
		}

		[TestCase(nameof(ProfileController.ActivateScheduledReport))]
		[TestCase(nameof(ProfileController.DeactivateScheduledReport))]
		[TestCase(nameof(ProfileController.DeleteScheduledReport))]
		public void Report_mutations_require_post_csrf_and_profile_update(string action)
		{
			var method = typeof(ProfileController).GetMethod(action, new[] { typeof(int), typeof(CancellationToken) });
			method.Should().NotBeNull();
			method.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull();
			method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull();
			method.GetCustomAttributes<AuthorizeAttribute>().Should().Contain(a => a.Policy == ResgridResources.Profile_Update);
			method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
		}

		[TestCase(nameof(ProfileController.ActivateSchedule))]
		[TestCase(nameof(ProfileController.DeactivateSchedule))]
		[TestCase(nameof(ProfileController.DeleteSchedule))]
		public async Task Generic_routes_cannot_bypass_report_mutation_guards_even_for_the_owner(string action)
		{
			_tasks.Setup(s => s.GetScheduledTaskByIdAsync(ScheduleId)).ReturnsAsync(OwnedReport());

			(await InvokeAsync(action)).Should().BeOfType<NotFoundResult>();

			_tasks.Verify(s => s.GetScheduledTaskByIdAsync(ScheduleId), Times.Once);
			VerifyNoMutation();
		}

		[TestCase(nameof(ProfileController.ActivateSchedule))]
		[TestCase(nameof(ProfileController.DeactivateSchedule))]
		[TestCase(nameof(ProfileController.DeleteSchedule))]
		public async Task Generic_routes_preserve_staffing_schedule_mutations(string action)
		{
			var schedule = OwnedReport();
			schedule.TaskType = (int)TaskTypes.UserStaffingLevel;
			_tasks.Setup(s => s.GetScheduledTaskByIdAsync(ScheduleId)).ReturnsAsync(schedule);
			ExpectMutation(action, schedule, CancellationToken.None);

			(await InvokeAsync(action)).Should().BeOfType<EmptyResult>();

			_tasks.VerifyAll();
			_tasks.Invocations.Select(i => i.Method.Name).Should().Equal(nameof(IScheduledTasksService.GetScheduledTaskByIdAsync), MutationMethod(action));
		}

		private static IEnumerable<TestCaseData> RejectedStaffingTargets()
		{
			foreach (var action in new[] { nameof(ProfileController.ActivateSchedule), nameof(ProfileController.DeactivateSchedule), nameof(ProfileController.DeleteSchedule), EditStaffingGet, EditStaffingPost })
				foreach (var target in new[] { "missing", "department", "owner", "empty-owner", "task-type" })
					yield return new TestCaseData(action, target);
		}

		[TestCaseSource(nameof(RejectedStaffingTargets))]
		public async Task Staffing_mutations_reject_inaccessible_schedules(string action, string target)
		{
			var schedule = OwnedReport(); schedule.TaskType = (int)TaskTypes.UserStaffingLevel;
			if (target == "missing") schedule = null;
			if (target == "department") schedule.DepartmentId++;
			if (target == "empty-owner") schedule.UserId = "";
			if (target == "task-type") schedule.TaskType = -1;
			if (target == "owner")
			{
				schedule.UserId = "another-member";
				_authorization.Setup(a => a.CanUserEditProfileAsync(UserId, DepartmentId, schedule.UserId)).ReturnsAsync(false);
			}
			_tasks.Setup(s => s.GetScheduledTaskByIdAsync(ScheduleId)).ReturnsAsync(schedule);
			(await InvokeAsync(action)).Should().BeOfType<NotFoundResult>();
			VerifyNoMutation();
		}

		[TestCase(nameof(ProfileController.ActivateSchedule))]
		[TestCase(nameof(ProfileController.DeactivateSchedule))]
		[TestCase(nameof(ProfileController.DeleteSchedule))]
		public async Task Staffing_mutations_preserve_delegated_member_management(string action)
		{
			var schedule = OwnedReport(); schedule.TaskType = (int)TaskTypes.UserStaffingLevel; schedule.UserId = "managed-member";
			_authorization.Setup(a => a.CanUserEditProfileAsync(UserId, DepartmentId, schedule.UserId)).ReturnsAsync(true);
			_tasks.Setup(s => s.GetScheduledTaskByIdAsync(ScheduleId)).ReturnsAsync(schedule);
			ExpectMutation(action, schedule, CancellationToken.None);
			(await InvokeAsync(action)).Should().BeOfType<EmptyResult>();
			_authorization.Verify(a => a.CanUserEditProfileAsync(UserId, DepartmentId, schedule.UserId), Times.Once);
			_tasks.VerifyAll();
		}

		[Test]
		public async Task Edit_staffing_schedule_get_loads_a_delegated_members_schedule()
		{
			var schedule = OwnedReport(); schedule.TaskType = (int)TaskTypes.UserStaffingLevel; schedule.UserId = "managed-member";
			schedule.ScheduleType = (int)ScheduleTypes.Weekly; schedule.Time = "07:30"; schedule.Tuesday = true; schedule.Data = "2";
			_authorization.Setup(a => a.CanUserEditProfileAsync(UserId, DepartmentId, schedule.UserId)).ReturnsAsync(true);
			_tasks.Setup(s => s.GetScheduledTaskByIdAsync(ScheduleId)).ReturnsAsync(schedule);

			var model = (await InvokeAsync(EditStaffingGet)).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<EditStaffingLevelView>().Which;

			model.ScheduleId.Should().Be(ScheduleId);
			model.Time.Should().Be("07:30");
			model.Tuesday.Should().BeTrue();
			model.StaffingLevel.Should().Be(2);
			VerifyNoMutation();
		}

		[Test]
		public async Task Edit_staffing_schedule_post_saves_a_delegated_members_schedule_and_passes_cancellation()
		{
			using var cancellation = new CancellationTokenSource();
			var schedule = OwnedReport(); schedule.TaskType = (int)TaskTypes.UserStaffingLevel; schedule.UserId = "managed-member";
			_authorization.Setup(a => a.CanUserEditProfileAsync(UserId, DepartmentId, schedule.UserId)).ReturnsAsync(true);
			_tasks.Setup(s => s.GetScheduledTaskByIdAsync(ScheduleId)).ReturnsAsync(schedule);
			_tasks.Setup(s => s.SaveScheduledTaskAsync(schedule, cancellation.Token)).ReturnsAsync(schedule);

			var redirect = (await InvokeAsync(EditStaffingPost, cancellation.Token)).Should().BeOfType<RedirectToActionResult>().Which;

			redirect.ActionName.Should().Be(nameof(ProfileController.ViewSchedules));
			redirect.RouteValues["userId"].Should().Be("managed-member");
			schedule.UserId.Should().Be("managed-member");
			schedule.DepartmentId.Should().Be(DepartmentId);
			schedule.Time.Should().Be("08:00");
			schedule.Data.Should().Be("3");
			_tasks.VerifyAll();
		}

		[TestCase(nameof(ProfileController.EditStaffingSchedule), typeof(EditStaffingLevelView))]
		[TestCase(nameof(ProfileController.AddNewStaffingSchedule), typeof(NewStaffingLevelView))]
		public void Staffing_form_posts_require_csrf_and_profile_update(string action, Type modelType)
		{
			var method = typeof(ProfileController).GetMethod(action, new[] { modelType, typeof(CancellationToken) });
			method.Should().NotBeNull();
			method.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull();
			method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull();
			method.GetCustomAttributes<AuthorizeAttribute>().Should().Contain(a => a.Policy == ResgridResources.Profile_Update);
		}

		private static readonly string[] StaffingSubjectActions =
		{
			nameof(ProfileController.ViewSchedules), nameof(ProfileController.GetScheduledStaffingTasksForGrid), AddStaffingGet, AddStaffingPost
		};

		[TestCaseSource(nameof(StaffingSubjectActions))]
		public async Task Staffing_subject_actions_reject_members_the_caller_cannot_manage_before_touching_data(string action)
		{
			_authorization.Setup(a => a.CanUserEditProfileAsync(UserId, DepartmentId, "another-member")).ReturnsAsync(false);

			var result = await InvokeForSubjectAsync(action, "another-member");

			if (action == nameof(ProfileController.GetScheduledStaffingTasksForGrid))
				result.Should().BeOfType<NotFoundResult>();
			else
				result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			_authorization.Verify(a => a.CanUserEditProfileAsync(UserId, DepartmentId, "another-member"), Times.Once);
			_tasks.Invocations.Should().BeEmpty();
			_customStates.Invocations.Should().BeEmpty();
			_departments.Invocations.Should().BeEmpty();
			_users.Invocations.Should().BeEmpty();
		}

		[TestCaseSource(nameof(StaffingSubjectActions))]
		public async Task Staffing_subject_actions_treat_a_missing_user_as_the_caller(string action)
		{
			// The task mock is strict, so reading or saving schedules for anyone but the caller throws.
			_tasks.Setup(s => s.GetScheduledStaffingTasksForUserAsync(UserId)).ReturnsAsync(new List<ScheduledTask>());
			_tasks.Setup(s => s.SaveScheduledTaskAsync(It.Is<ScheduledTask>(t => t.UserId == UserId), CancellationToken.None)).ReturnsAsync(new ScheduledTask());

			(await InvokeForSubjectAsync(action, null)).Should().BeOfType(action switch
			{
				nameof(ProfileController.GetScheduledStaffingTasksForGrid) => typeof(JsonResult),
				AddStaffingPost => typeof(RedirectToActionResult),
				_ => typeof(ViewResult)
			});

			_authorization.Verify(a => a.CanUserEditProfileAsync(UserId, DepartmentId, UserId), Times.Once);
		}

		// ViewSchedules is left out of the delegated cases: naming another member goes through the static
		// UserHelper, which resolves its services from the container rather than the controller.
		[Test]
		public async Task Staffing_grid_lists_a_delegated_members_schedules()
		{
			var schedule = OwnedReport(); schedule.TaskType = (int)TaskTypes.UserStaffingLevel; schedule.UserId = ManagedMember;
			schedule.ScheduleType = (int)ScheduleTypes.Weekly; schedule.Monday = true; schedule.Time = "06:00"; schedule.Data = "2";
			_authorization.Setup(a => a.CanUserEditProfileAsync(UserId, DepartmentId, ManagedMember)).ReturnsAsync(true);
			_tasks.Setup(s => s.GetScheduledStaffingTasksForUserAsync(ManagedMember)).ReturnsAsync(new List<ScheduledTask> { schedule });

			var rows = (await InvokeForSubjectAsync(nameof(ProfileController.GetScheduledStaffingTasksForGrid), ManagedMember))
				.Should().BeOfType<JsonResult>().Which.Value.Should().BeAssignableTo<IEnumerable<ScheduledTasksForJson>>().Which;

			rows.Select(r => r.ScheduleId).Should().Equal(ScheduleId);
			_tasks.VerifyAll();
		}

		[Test]
		public async Task Add_staffing_schedule_get_prepares_the_form_for_a_delegated_member()
		{
			_authorization.Setup(a => a.CanUserEditProfileAsync(UserId, DepartmentId, ManagedMember)).ReturnsAsync(true);

			var model = (await InvokeForSubjectAsync(AddStaffingGet, ManagedMember)).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<NewStaffingLevelView>().Which;

			model.UserId.Should().Be(ManagedMember);
		}

		[Test]
		public async Task Add_staffing_schedule_post_creates_a_delegated_members_schedule_in_the_callers_department()
		{
			using var cancellation = new CancellationTokenSource();
			ScheduledTask saved = null;
			_authorization.Setup(a => a.CanUserEditProfileAsync(UserId, DepartmentId, ManagedMember)).ReturnsAsync(true);
			_tasks.Setup(s => s.SaveScheduledTaskAsync(It.IsAny<ScheduledTask>(), cancellation.Token))
				.Callback<ScheduledTask, CancellationToken>((t, _) => saved = t).ReturnsAsync(new ScheduledTask());

			var redirect = (await InvokeForSubjectAsync(AddStaffingPost, ManagedMember, cancellation.Token)).Should().BeOfType<RedirectToActionResult>().Which;

			redirect.ActionName.Should().Be(nameof(ProfileController.ViewSchedules));
			redirect.RouteValues["userId"].Should().Be(ManagedMember);
			saved.UserId.Should().Be(ManagedMember);
			saved.DepartmentId.Should().Be(DepartmentId);
			saved.TaskType.Should().Be((int)TaskTypes.UserStaffingLevel);
			saved.Time.Should().Be("08:00");
			saved.Data.Should().Be("3");
		}

		private const string EditStaffingGet = "EditStaffingSchedule(GET)";
		private const string EditStaffingPost = "EditStaffingSchedule(POST)";
		private const string AddStaffingGet = "AddNewStaffingSchedule(GET)";
		private const string AddStaffingPost = "AddNewStaffingSchedule(POST)";
		private const string ManagedMember = "managed-member";

		private static NewStaffingLevelView ValidWeeklyAdd(string userId) => new()
		{
			UserId = userId,
			Time = "08:00",
			Monday = true,
			StaffingLevel = 3
		};

		private static EditStaffingLevelView ValidWeeklyEdit() => new()
		{
			ScheduleId = ScheduleId,
			Time = "08:00",
			Monday = true,
			StaffingLevel = 3
		};

		private static ScheduledTask OwnedReport() => new()
		{
			ScheduledTaskId = ScheduleId,
			DepartmentId = DepartmentId,
			UserId = UserId,
			TaskType = (int)TaskTypes.ReportDelivery,
			Data = ((int)ReportTypes.ControlledSubstanceLog).ToString(),
			Active = true
		};

		private Task<IActionResult> InvokeAsync(string action, CancellationToken cancellation = default) => action switch
		{
			nameof(ProfileController.ActivateScheduledReport) => _controller.ActivateScheduledReport(ScheduleId, cancellation),
			nameof(ProfileController.DeactivateScheduledReport) => _controller.DeactivateScheduledReport(ScheduleId, cancellation),
			nameof(ProfileController.DeleteScheduledReport) => _controller.DeleteScheduledReport(ScheduleId, cancellation),
			nameof(ProfileController.ActivateSchedule) => _controller.ActivateSchedule(ScheduleId, cancellation),
			nameof(ProfileController.DeactivateSchedule) => _controller.DeactivateSchedule(ScheduleId, cancellation),
			nameof(ProfileController.DeleteSchedule) => _controller.DeleteSchedule(ScheduleId, cancellation),
			EditStaffingGet => _controller.EditStaffingSchedule(ScheduleId),
			EditStaffingPost => _controller.EditStaffingSchedule(ValidWeeklyEdit(), cancellation),
			_ => throw new ArgumentOutOfRangeException(nameof(action))
		};

		private Task<IActionResult> InvokeForSubjectAsync(string action, string userId, CancellationToken cancellation = default) => action switch
		{
			nameof(ProfileController.ViewSchedules) => _controller.ViewSchedules(userId),
			nameof(ProfileController.GetScheduledStaffingTasksForGrid) => _controller.GetScheduledStaffingTasksForGrid(userId),
			AddStaffingGet => _controller.AddNewStaffingSchedule(userId),
			AddStaffingPost => _controller.AddNewStaffingSchedule(ValidWeeklyAdd(userId), cancellation),
			_ => throw new ArgumentOutOfRangeException(nameof(action))
		};

		private void ExpectMutation(string action, ScheduledTask schedule, CancellationToken cancellation)
		{
			switch (MutationMethod(action))
			{
				case nameof(IScheduledTasksService.EnableScheduledTaskByIdAsync):
					_tasks.Setup(s => s.EnableScheduledTaskByIdAsync(ScheduleId, cancellation)).ReturnsAsync(schedule); break;
				case nameof(IScheduledTasksService.DisabledScheduledTaskByIdAsync):
					_tasks.Setup(s => s.DisabledScheduledTaskByIdAsync(ScheduleId, cancellation)).ReturnsAsync(schedule); break;
				case nameof(IScheduledTasksService.DeleteScheduledTask):
					_tasks.Setup(s => s.DeleteScheduledTask(ScheduleId, cancellation)).ReturnsAsync(true); break;
			}
		}

		private static string MutationMethod(string action) => action switch
		{
			nameof(ProfileController.ActivateScheduledReport) or nameof(ProfileController.ActivateSchedule) => nameof(IScheduledTasksService.EnableScheduledTaskByIdAsync),
			nameof(ProfileController.DeactivateScheduledReport) or nameof(ProfileController.DeactivateSchedule) => nameof(IScheduledTasksService.DisabledScheduledTaskByIdAsync),
			nameof(ProfileController.DeleteScheduledReport) or nameof(ProfileController.DeleteSchedule) => nameof(IScheduledTasksService.DeleteScheduledTask),
			_ => throw new ArgumentOutOfRangeException(nameof(action))
		};

		private void VerifyNoMutation()
		{
			_tasks.Verify(s => s.EnableScheduledTaskByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
			_tasks.Verify(s => s.DisabledScheduledTaskByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
			_tasks.Verify(s => s.DeleteScheduledTask(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
			_tasks.Verify(s => s.SaveScheduledTaskAsync(It.IsAny<ScheduledTask>(), It.IsAny<CancellationToken>()), Times.Never);
		}
	}
}
