using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Lms;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.ViewComponents
{
	/// <summary>
	/// My Training's requirements and paths panel (Training T3 item 8): the member's learning requirements with their state and the next
	/// action first (rgw-next-action), and their learning paths with progress. Rendered by the baseline My Training page by name, so the
	/// page never depends on LMS types (§17.3). Costs nothing for a member with no requirement and no path (two small reads, no billing
	/// call); otherwise one gate read per request. An optional panel never takes the page down.
	/// </summary>
	public sealed class LmsMyTrainingViewComponent(ILmsRequirementService requirements, ILmsPathService paths, ILmsPathAssignmentsRepository assignments,
		ILmsGateService gate, IDepartmentsService departments) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync(TrainingManagementScope scope = null)
		{
			try
			{
				var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
				var userId = ClaimsAuthorizationHelper.GetUserId();
				if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
					return Content(string.Empty);

				var now = DateTime.UtcNow;
				var mine = await requirements.GetMyRequirementsAsync(departmentId, userId, now) ?? new List<LmsMyRequirement>();
				var myPaths = new List<LmsMyPath>();
				LmsActor actor = null;
				if ((await assignments.GetForUserAsync(departmentId, userId) ?? new List<LmsPathAssignment>()).Count > 0)
				{
					actor = await gate.GetActorAsync(departmentId, userId);
					myPaths = await paths.GetMyPathsAsync(actor, now) ?? new List<LmsMyPath>();
				}

				var canSeeCompliance = scope != null && (scope.AssignRight.ManagesAll || (scope.AssignRight.AdministeredMemberUserIds?.Count ?? 0) > 0);
				if (mine.Count == 0 && myPaths.Count == 0)
					return Content(string.Empty);

				return View("~/Areas/User/Views/LmsRequirements/_MyTraining.cshtml", new LmsMyTrainingPanel
				{
					Requirements = mine,
					Paths = myPaths,
					CanSeeCompliance = canSeeCompliance,
					Actor = actor,
					Department = await departments.GetDepartmentByIdAsync(departmentId, false)
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The My Training requirements panel could not be shown.");
				return Content(string.Empty);
			}
		}
	}

	/// <summary>
	/// My Training's "Recommended" (K3 path recommendations): what to take next and why, ranked deterministically from the member's open
	/// requirements, the paths they are on, their competency gaps and the courses offered to their roles, with an optional AI explanation.
	/// Rendered by the baseline My Training page by name (§17.3). Nothing while the LMS flag is off (no billing call); otherwise one gate
	/// read. Needs an active add-on. An optional panel never takes the page down.
	/// </summary>
	public sealed class LmsRecommendationsViewComponent(ILmsRecommendationService recommendations, ILmsAccessService access, ILmsGateService gate,
		IDepartmentsService departments) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync(TrainingManagementScope scope = null)
		{
			try
			{
				var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
				var userId = ClaimsAuthorizationHelper.GetUserId();
				if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || !await access.IsFlagEnabledAsync(departmentId))
					return Content(string.Empty);

				var actor = await gate.GetActorAsync(departmentId, userId);
				var set = await recommendations.GetForMemberAsync(actor, scope, userId, DateTime.UtcNow);
				if (set == null || set.Error != LmsErrors.None || set.Items.Count == 0)
					return Content(string.Empty);

				var department = await departments.GetDepartmentByIdAsync(departmentId, false);
				return View("~/Areas/User/Views/LmsRequirements/_Recommended.cshtml", new LmsRecommendedPanel
				{
					Actor = actor,
					Department = department,
					CanSeeOthers = actor.MayManage || scope != null && (scope.AssignRight.ManagesAll || (scope.AssignRight.AdministeredMemberUserIds?.Count ?? 0) > 0),
					List = new LmsRecommendationListModel { Set = set, IsSelf = true, CanOpenPaths = actor.MayManage || actor.MayAssignAcademy, Actor = actor, Department = department }
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The My Training recommendations panel could not be shown.");
				return Content(string.Empty);
			}
		}
	}
}
