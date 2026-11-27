using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Security;
using Resgrid.Web.Attributes;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Security → Step-up verification (owner decision 2026-11-26): per area whether members must verify again, and whether the password
	/// or a provider sign-in may be used besides a second factor; with the members who could not complete each step-up. Department
	/// administrators; changing protected data's methods is the managing member's. A security change, so it needs a fresh second factor.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class StepUpSettingsController : SecureBaseController
	{
		private readonly IStepUpPolicyService _policy;
		private readonly IStepUpCapabilityService _capabilities;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentDataProtectionService _dataProtection;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Security.Security> _localizer;

		public StepUpSettingsController(IStepUpPolicyService policy, IStepUpCapabilityService capabilities, IDepartmentsService departments,
			IDepartmentDataProtectionService dataProtection, IStringLocalizer<Resgrid.Localization.Areas.User.Security.Security> localizer)
		{
			_policy = policy;
			_capabilities = capabilities;
			_departments = departments;
			_dataProtection = dataProtection;
			_localizer = localizer;
		}

		[HttpGet]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5, MethodScope = MfaMethodScope.SecurityChange)]
		public async Task<IActionResult> Index(CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Index", "Security", new { area = "User" });

			var model = await BuildAsync(null, cancellationToken);
			if (TempData["StepUpMessage"] is string message)
			{
				model.Message = message;
				model.MessageIsError = TempData["StepUpMessageIsError"] as bool? == true;
			}

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5, MethodScope = MfaMethodScope.SecurityChange)]
		public async Task<IActionResult> Index(StepUpSettingsView model, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Index", "Security", new { area = "User" });

			var rows = (model?.Areas ?? new List<StepUpAreaRowView>()).Where(r => r != null && StepUpAreas.IsKnown((StepUpArea)r.Area)).ToList();
			var inputs = rows.GroupBy(r => r.Area).Select(g => g.Last()).Select(r => new StepUpRuleInput
			{
				Area = (StepUpArea)r.Area,
				Required = r.Required,
				AllowSso = r.AllowSso,
				AllowPassword = r.AllowPassword
			}).ToList();

			var department = await _departments.GetDepartmentByIdAsync(DepartmentId);
			var isManaging = department != null && string.Equals(department.ManagingUserId, UserId, System.StringComparison.OrdinalIgnoreCase);
			if (!isManaging)
			{
				// Protected data's methods are posted back unchanged by everyone else; keep them exactly as saved.
				var current = (await _policy.GetRulesAsync(DepartmentId, cancellationToken)).First(r => r.Area == StepUpArea.ProtectedData);
				inputs.RemoveAll(i => i.Area == StepUpArea.ProtectedData);
				inputs.Add(new StepUpRuleInput
				{
					Area = StepUpArea.ProtectedData,
					Required = true,
					AllowSso = current.Allows(StepUpMethodClasses.Sso),
					AllowPassword = current.Allows(StepUpMethodClasses.Password)
				});
			}

			var outcome = await _policy.SaveAsync(DepartmentId, inputs, UserId, isManaging, cancellationToken);
			TempData["StepUpMessage"] = outcome switch
			{
				StepUpPolicySaveOutcome.Saved => _localizer["StepUpSaved"].Value,
				StepUpPolicySaveOutcome.Unchanged => _localizer["StepUpUnchanged"].Value,
				StepUpPolicySaveOutcome.ManagingMemberRequired => _localizer["StepUpManagingMemberRequired"].Value,
				_ => _localizer["StepUpSaveFailed"].Value
			};
			TempData["StepUpMessageIsError"] = outcome is not (StepUpPolicySaveOutcome.Saved or StepUpPolicySaveOutcome.Unchanged);
			return RedirectToAction("Index");
		}

		/// <summary>
		/// The page: every area's saved rule, and per area the members who could not complete it with its current methods were it required,
		/// so the warning shows before an administrator turns a requirement on as well as while it is on.
		/// </summary>
		private async Task<StepUpSettingsView> BuildAsync(string message, CancellationToken cancellationToken)
		{
			var rules = await _policy.GetRulesAsync(DepartmentId, cancellationToken);
			var asRequired = rules.Select(r => new StepUpRule { Area = r.Area, Required = true, Methods = r.Methods, WindowMinutes = r.WindowMinutes }).ToList();
			var readiness = await _capabilities.GetReadinessAsync(DepartmentId, asRequired, 25, cancellationToken);
			var department = await _departments.GetDepartmentByIdAsync(DepartmentId);

			return new StepUpSettingsView
			{
				IsManagingMember = department != null && string.Equals(department.ManagingUserId, UserId, System.StringComparison.OrdinalIgnoreCase),
				HasDataProtection = await _dataProtection.GetPolicyByDepartmentIdAsync(DepartmentId) != null,
				Message = message,
				Areas = rules.Select(r =>
				{
					var ready = readiness.FirstOrDefault(x => x.Rule?.Area == r.Area);
					return new StepUpAreaRowView
					{
						Area = (int)r.Area,
						Required = r.Required,
						AllowSso = r.Allows(StepUpMethodClasses.Sso),
						AllowPassword = r.Allows(StepUpMethodClasses.Password),
						UnableCount = ready?.MemberCount ?? 0,
						UnableMembers = ready?.Members ?? new List<StepUpMemberGap>()
					};
				}).ToList()
			};
		}
	}
}
