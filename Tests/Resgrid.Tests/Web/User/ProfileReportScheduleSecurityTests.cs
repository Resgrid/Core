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
			_controller = new ProfileController(
				departmentsService: null, usersService: null, authorizationService: null,
				userProfileService: null, scheduledTasksService: _tasks.Object, certificationService: null,
				customStateService: null, imageService: null, appOptionsAccessor: null,
				emailService: null, userManager: null, signInManager: null,
				departmentSsoService: null, secLocalizer: null, deleteService: null,
				externalIdentityLinkService: null, userSessionService: null, systemAuditsService: null,
				departmentGroupsService: null, departmentSettingsService: null, passwordRecoveryService: null,
				eventAggregator: null, protectedReadService: null)
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
