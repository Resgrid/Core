using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using CommunicationTestMessages = Resgrid.Localization.Areas.User.CommunicationTest.CommunicationTestMessageCatalog;
using CommunicationTestResources = Resgrid.Localization.Areas.User.CommunicationTest.CommunicationTestResources;
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
		private readonly IPersonnelRolesService _personnelRolesService;

		public CommunicationTestController(
			ICommunicationTestService communicationTestService,
			IUserProfileService userProfileService,
			IEventAggregator eventAggregator,
			IDepartmentGroupsService departmentGroupsService,
			IDepartmentsService departmentsService,
			IPersonnelRolesService personnelRolesService)
		{
			_communicationTestService = communicationTestService;
			_userProfileService = userProfileService;
			_eventAggregator = eventAggregator;
			_departmentGroupsService = departmentGroupsService;
			_departmentsService = departmentsService;
			_personnelRolesService = personnelRolesService;
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
			{
				model.Tests = tests.ToList();

				foreach (var test in model.Tests)
				{
					var targets = await _communicationTestService.GetTargetsByTestIdAsync(test.CommunicationTestId);
					model.TestScopes[test.CommunicationTestId.ToString()] = BuildScopeLabel(targets);
				}
			}

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
		public async Task<IActionResult> New()
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

			model.TargetOptions = await BuildTargetOptionsAsync();
			model.Preview = await BuildPreviewAsync();

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
				ModelState.AddModelError("Test.Name", CommunicationTestResources.GetCurrent("NameRequired"));
				model.TargetOptions = await BuildTargetOptionsAsync();
				model.Preview = await BuildPreviewAsync();
				return View(model);
			}

			if (!await _communicationTestService.CanCreateScheduledTestAsync(DepartmentId, model.Test.ScheduleType))
			{
				model.Message = CommunicationTestResources.GetCurrent(
					model.Test.ScheduleType == (int)CommunicationTestScheduleType.Weekly ? "OnlyOneWeeklyTest" : "OnlyOneMonthlyTest");
				model.TargetOptions = await BuildTargetOptionsAsync();
				model.Preview = await BuildPreviewAsync();
				return View(model);
			}

			model.Test.DepartmentId = DepartmentId;
			model.Test.CreatedByUserId = UserId;
			model.Test.CreatedOn = DateTime.UtcNow;
			if (model.Test.ResponseWindowMinutes <= 0)
				model.Test.ResponseWindowMinutes = 60;

			var saved = await _communicationTestService.SaveTestWithTargetsAsync(model.Test, DepartmentId,
				BuildTargets(model.Test.CommunicationTestId, model.SelectedGroupIds, model.SelectedRoleIds, model.SelectedUserIds), cancellationToken);

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
			model.TargetOptions = await BuildTargetOptionsAsync();
			model.Preview = await BuildPreviewAsync();

			var targets = await _communicationTestService.GetTargetsByTestIdAsync(id);
			if (targets != null)
			{
				foreach (var target in targets)
				{
					switch ((CommunicationTestTargetType)target.TargetType)
					{
						case CommunicationTestTargetType.Group:
							if (int.TryParse(target.TargetId, out var groupId))
								model.SelectedGroupIds.Add(groupId);
							break;
						case CommunicationTestTargetType.Role:
							if (int.TryParse(target.TargetId, out var roleId))
								model.SelectedRoleIds.Add(roleId);
							break;
						case CommunicationTestTargetType.User:
							model.SelectedUserIds.Add(target.TargetId);
							break;
					}
				}
			}

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
				model.Message = CommunicationTestResources.GetCurrent(
					model.Test.ScheduleType == (int)CommunicationTestScheduleType.Weekly ? "OnlyOneWeeklyTest" : "OnlyOneMonthlyTest");
				model.Test = existing;
				model.TargetOptions = await BuildTargetOptionsAsync();
				model.Preview = await BuildPreviewAsync();
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

			await _communicationTestService.SaveTestWithTargetsAsync(existing, DepartmentId,
				BuildTargets(existing.CommunicationTestId, model.SelectedGroupIds, model.SelectedRoleIds, model.SelectedUserIds), cancellationToken);

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
				TempData["Error"] = CommunicationTestResources.GetCurrent("OnlyOnDemandCanRun");
				return RedirectToAction("Index");
			}

			// Check 48-hour rate limit for on-demand tests
			if (!await _communicationTestService.CanStartOnDemandRunAsync(id))
			{
				TempData["Error"] = CommunicationTestResources.GetCurrent("RateLimited");
				return RedirectToAction("Index");
			}

			var run = await _communicationTestService.StartTestRunAsync(id, DepartmentId, UserId, cancellationToken);
			if (run == null)
			{
				TempData["Error"] = CommunicationTestResources.GetCurrent("UnableToStart");
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

		private static string BuildScopeLabel(IEnumerable<CommunicationTestTarget> targets)
		{
			if (targets == null)
				return CommunicationTestResources.GetCurrent("ScopeEveryone");

			var targetList = targets.ToList();
			if (targetList.Count == 0)
				return CommunicationTestResources.GetCurrent("ScopeEveryone");

			var parts = new List<string>();

			var groups = targetList.Count(t => t.TargetType == (int)CommunicationTestTargetType.Group);
			if (groups > 0)
				parts.Add(groups == 1 ? CommunicationTestResources.GetCurrent("ScopeGroup") : CommunicationTestResources.GetCurrent("ScopeGroups", groups));

			var roles = targetList.Count(t => t.TargetType == (int)CommunicationTestTargetType.Role);
			if (roles > 0)
				parts.Add(roles == 1 ? CommunicationTestResources.GetCurrent("ScopeRole") : CommunicationTestResources.GetCurrent("ScopeRoles", roles));

			var users = targetList.Count(t => t.TargetType == (int)CommunicationTestTargetType.User);
			if (users > 0)
				parts.Add(users == 1 ? CommunicationTestResources.GetCurrent("ScopePerson") : CommunicationTestResources.GetCurrent("ScopePeople", users));

			return parts.Count == 0 ? CommunicationTestResources.GetCurrent("ScopeEveryone") : string.Join(", ", parts);
		}

		/// <summary>
		/// Renders the real per-channel message text for the "what your people will see" panel. The
		/// test name is left as a placeholder the screen substitutes live, and the sample values
		/// (name, run code, confirm link) stand in for what a real run generates per recipient.
		/// </summary>
		private async Task<CommunicationTestPreview> BuildPreviewAsync()
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var departmentName = string.IsNullOrWhiteSpace(department?.Name) ? "Your department" : department.Name;

			var placeholder = CommunicationTestPreview.NamePlaceholder;
			var sampleConfirmUrl = $"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/v4/CommunicationTestResponse/EmailConfirm?token=...";

			// Previewed in the administrator's own language. Each recipient receives it in theirs, which
			// the note under the panel says out loud so nobody assumes everyone gets this exact text.
			var culture = System.Globalization.CultureInfo.CurrentUICulture.Name;

			return new CommunicationTestPreview
			{
				SampleRunCode = CommunicationTestMessages.SampleRunCode,
				SmsBody = CommunicationTestMessages.BuildSmsBody(placeholder, CommunicationTestMessages.SampleRunCode, culture),
				EmailSubject = CommunicationTestMessages.BuildEmailSubject(placeholder, culture),
				EmailBody = CommunicationTestMessages.BuildEmailBody("Alex", departmentName, placeholder, sampleConfirmUrl, culture),
				VoicePrompts = CommunicationTestMessages.GetVoicePrompts(culture).ToList(),
				PushTitle = CommunicationTestMessages.BuildPushTitle(culture),
				PushBody = CommunicationTestMessages.BuildPushBody(placeholder, culture)
			};
		}

		private async Task<CommunicationTestTargetOptions> BuildTargetOptionsAsync()
		{
			var options = new CommunicationTestTargetOptions();

			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			if (groups != null)
				options.Groups = groups.OrderBy(g => g.Name).ToList();

			var roles = await _personnelRolesService.GetAllRolesForDepartmentAsync(DepartmentId);
			if (roles != null)
				options.Roles = roles.OrderBy(r => r.Name).ToList();

			var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId);
			if (profiles != null)
			{
				options.Personnel = profiles.Values
					.Select(p => new CommunicationTestPersonnelOption
					{
						UserId = p.UserId,
						Name = $"{p.LastName}, {p.FirstName}".Trim(' ', ',')
					})
					.Where(p => !string.IsNullOrWhiteSpace(p.UserId))
					.OrderBy(p => p.Name)
					.ToList();
			}

			return options;
		}

		private List<CommunicationTestTarget> BuildTargets(Guid communicationTestId, List<int> groupIds, List<int> roleIds, List<string> userIds)
		{
			var targets = new List<CommunicationTestTarget>();

			if (groupIds != null)
			{
				foreach (var groupId in groupIds)
					targets.Add(NewTarget(communicationTestId, CommunicationTestTargetType.Group, groupId.ToString()));
			}

			if (roleIds != null)
			{
				foreach (var roleId in roleIds)
					targets.Add(NewTarget(communicationTestId, CommunicationTestTargetType.Role, roleId.ToString()));
			}

			if (userIds != null)
			{
				foreach (var userId in userIds.Where(x => !string.IsNullOrWhiteSpace(x)))
					targets.Add(NewTarget(communicationTestId, CommunicationTestTargetType.User, userId));
			}

			return targets;
		}

		private CommunicationTestTarget NewTarget(Guid communicationTestId, CommunicationTestTargetType type, string targetId)
		{
			return new CommunicationTestTarget
			{
				CommunicationTestId = communicationTestId,
				DepartmentId = DepartmentId,
				TargetType = (int)type,
				TargetId = targetId
			};
		}
	}
}
