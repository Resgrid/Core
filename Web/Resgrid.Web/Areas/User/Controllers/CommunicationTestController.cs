using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.CommunicationTests;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	[Authorize]
	public class CommunicationTestController : Resgrid.Web.SecureBaseController
	{
		private readonly ICommunicationTestService _communicationTestService;
		private readonly IUserProfileService _userProfileService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IDepartmentsService _departmentsService;

		public CommunicationTestController(
			ICommunicationTestService communicationTestService,
			IUserProfileService userProfileService,
			IEventAggregator eventAggregator,
			IDepartmentGroupsService departmentGroupsService,
			IDepartmentsService departmentsService)
		{
			_communicationTestService = communicationTestService;
			_userProfileService = userProfileService;
			_eventAggregator = eventAggregator;
			_departmentGroupsService = departmentGroupsService;
			_departmentsService = departmentsService;
		}

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
			bool isGroupAdmin = false;

			if (!isDeptAdmin)
			{
				var group = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);
				isGroupAdmin = group != null && group.IsUserGroupAdmin(UserId);
			}

			if (!isDeptAdmin && !isGroupAdmin)
				return Unauthorized();

			var model = new CommunicationTestIndexView();
			model.IsDepartmentAdmin = isDeptAdmin;

			var tests = await _communicationTestService.GetTestsByDepartmentIdAsync(DepartmentId);
			if (tests != null)
				model.Tests = tests.ToList();

			var runs = await _communicationTestService.GetRunsByDepartmentIdAsync(DepartmentId);
			if (runs != null)
			{
				model.RecentRuns = runs.OrderByDescending(r => r.StartedOn).Take(20).ToList();
				foreach (var test in model.Tests)
				{
					model.TestNames[test.CommunicationTestId.ToString()] = test.Name;
				}
			}

			return View(model);
		}

		[HttpGet]
		public IActionResult New()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			var model = new NewCommunicationTestView
			{
				Test = new CommunicationTest
				{
					Active = true,
					ResponseWindowMinutes = 60,
					TestSms = true,
					TestEmail = true,
					TestPush = true
				}
			};

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> New(NewCommunicationTestView model, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			if (string.IsNullOrWhiteSpace(model.Test.Name))
			{
				ModelState.AddModelError("Test.Name", "Name is required.");
				return View(model);
			}

			if (!await _communicationTestService.CanCreateScheduledTestAsync(DepartmentId, model.Test.ScheduleType))
			{
				var typeLabel = model.Test.ScheduleType == (int)CommunicationTestScheduleType.Weekly ? "weekly" : "monthly";
				model.Message = $"Only one {typeLabel} test is allowed per department. Please edit the existing one instead.";
				return View(model);
			}

			model.Test.DepartmentId = DepartmentId;
			model.Test.CreatedByUserId = UserId;
			model.Test.CreatedOn = DateTime.UtcNow;
			if (model.Test.ResponseWindowMinutes <= 0)
				model.Test.ResponseWindowMinutes = 60;

			var saved = await _communicationTestService.SaveTestAsync(model.Test, cancellationToken);

			_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Type = AuditLogTypes.CommunicationTestCreated,
				After = saved.CloneJsonToString(),
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});

			return RedirectToAction("Index");
		}

		[HttpGet]
		public async Task<IActionResult> Edit(string testId)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			if (!Guid.TryParse(testId, out var id))
				return RedirectToAction("Index");

			var test = await _communicationTestService.GetTestByIdAsync(id);
			if (test == null || test.DepartmentId != DepartmentId)
				return Unauthorized();

			var model = new EditCommunicationTestView { Test = test };
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Edit(EditCommunicationTestView model, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			if (model.Test == null || model.Test.CommunicationTestId == Guid.Empty)
				return RedirectToAction("Index");

			var existing = await _communicationTestService.GetTestByIdAsync(model.Test.CommunicationTestId);
			if (existing == null || existing.DepartmentId != DepartmentId)
				return Unauthorized();

			if (model.Test.ScheduleType != existing.ScheduleType &&
				!await _communicationTestService.CanCreateScheduledTestAsync(DepartmentId, model.Test.ScheduleType, existing.CommunicationTestId))
			{
				var typeLabel = model.Test.ScheduleType == (int)CommunicationTestScheduleType.Weekly ? "weekly" : "monthly";
				model.Message = $"Only one {typeLabel} test is allowed per department.";
				model.Test = existing;
				return View(model);
			}

			var beforeJson = existing.CloneJsonToString();

			existing.Name = model.Test.Name;
			existing.Description = model.Test.Description;
			existing.ScheduleType = model.Test.ScheduleType;
			existing.Sunday = model.Test.Sunday;
			existing.Monday = model.Test.Monday;
			existing.Tuesday = model.Test.Tuesday;
			existing.Wednesday = model.Test.Wednesday;
			existing.Thursday = model.Test.Thursday;
			existing.Friday = model.Test.Friday;
			existing.Saturday = model.Test.Saturday;
			existing.DayOfMonth = model.Test.DayOfMonth;
			existing.Time = model.Test.Time;
			existing.TestSms = model.Test.TestSms;
			existing.TestEmail = model.Test.TestEmail;
			existing.TestVoice = model.Test.TestVoice;
			existing.TestPush = model.Test.TestPush;
			existing.Active = model.Test.Active;
			existing.ResponseWindowMinutes = model.Test.ResponseWindowMinutes > 0 ? model.Test.ResponseWindowMinutes : 60;
			existing.UpdatedOn = DateTime.UtcNow;

			await _communicationTestService.SaveTestAsync(existing, cancellationToken);

			_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Type = AuditLogTypes.CommunicationTestUpdated,
				Before = beforeJson,
				After = existing.CloneJsonToString(),
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});

			return RedirectToAction("Index");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Delete(string testId, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			if (!Guid.TryParse(testId, out var id))
				return RedirectToAction("Index");

			var test = await _communicationTestService.GetTestByIdAsync(id);
			if (test == null || test.DepartmentId != DepartmentId)
				return Unauthorized();

			var beforeJson = test.CloneJsonToString();

			await _communicationTestService.DeleteTestAsync(id, cancellationToken);

			_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Type = AuditLogTypes.CommunicationTestDeleted,
				Before = beforeJson,
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});

			return RedirectToAction("Index");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> StartRun(string testId, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			if (!Guid.TryParse(testId, out var id))
				return RedirectToAction("Index");

			var test = await _communicationTestService.GetTestByIdAsync(id);
			if (test == null || test.DepartmentId != DepartmentId)
				return Unauthorized();

			if (test.ScheduleType != (int)CommunicationTestScheduleType.OnDemand)
			{
				TempData["Error"] = "Only on-demand tests can be started manually. Scheduled tests run automatically.";
				return RedirectToAction("Index");
			}

			// Check 48-hour rate limit for on-demand tests
			if (!await _communicationTestService.CanStartOnDemandRunAsync(id))
			{
				TempData["Error"] = "An on-demand test can only be run once every 48 hours. Please try again later.";
				return RedirectToAction("Index");
			}

			var run = await _communicationTestService.StartTestRunAsync(id, DepartmentId, UserId, cancellationToken);
			if (run == null)
			{
				TempData["Error"] = "Unable to start the test run. Rate limit may apply.";
				return RedirectToAction("Index");
			}

			_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Type = AuditLogTypes.CommunicationTestRunStarted,
				After = run.CloneJsonToString(),
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});

			return RedirectToAction("Report", new { runId = run.CommunicationTestRunId.ToString() });
		}

		[HttpGet]
		public async Task<IActionResult> Report(string runId)
		{
			bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
			bool isGroupAdmin = false;
			DepartmentGroup userGroup = null;

			if (!isDeptAdmin)
			{
				userGroup = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);
				isGroupAdmin = userGroup != null && userGroup.IsUserGroupAdmin(UserId);
			}

			if (!isDeptAdmin && !isGroupAdmin)
				return Unauthorized();

			if (!Guid.TryParse(runId, out var id))
				return RedirectToAction("Index");

			var run = await _communicationTestService.GetRunByIdAsync(id);
			if (run == null || run.DepartmentId != DepartmentId)
				return Unauthorized();

			var test = await _communicationTestService.GetTestByIdAsync(run.CommunicationTestId);
			var results = await _communicationTestService.GetResultsByRunIdAsync(id);
			var resultList = results?.ToList() ?? new List<CommunicationTestResult>();
			var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId);

			// Group admins only see results for members in their group and child groups
			if (!isDeptAdmin && isGroupAdmin && userGroup != null)
			{
				var allowedUserIds = new HashSet<string>();

				// Add members of the admin's own group
				var groupMembers = await _departmentGroupsService.GetAllMembersForGroupAsync(userGroup.DepartmentGroupId);
				foreach (var m in groupMembers)
					allowedUserIds.Add(m.UserId);

				// Add members of child groups
				var childGroups = await _departmentGroupsService.GetAllChildDepartmentGroupsAsync(userGroup.DepartmentGroupId);
				if (childGroups != null)
				{
					foreach (var childGroup in childGroups)
					{
						var childMembers = await _departmentGroupsService.GetAllMembersForGroupAsync(childGroup.DepartmentGroupId);
						foreach (var m in childMembers)
							allowedUserIds.Add(m.UserId);
					}
				}

				resultList = resultList.Where(r => allowedUserIds.Contains(r.UserId)).ToList();
			}

			var model = new CommunicationTestReportView
			{
				Run = run,
				Test = test,
				Results = resultList,
				Profiles = profiles ?? new Dictionary<string, UserProfile>()
			};

			return View(model);
		}
	}
}
