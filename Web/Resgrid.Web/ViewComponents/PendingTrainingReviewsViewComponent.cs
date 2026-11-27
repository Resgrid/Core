using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Training;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.ViewComponents
{
	/// <summary>
	/// Round 9B: the home dashboards' and My Training's rgw-next-action item for training hours submissions: the count waiting for the member's
	/// review (with the oldest three), and the member's own submissions a reviewer sent back for more information. Renders nothing when there
	/// is neither or the Trainings module is off; a failure never takes the host page down.
	/// </summary>
	public sealed class PendingTrainingReviewsViewComponent(ITrainingSubmissionService submissions, IDepartmentsService departments, IDepartmentSettingsService settings)
		: ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync()
		{
			try
			{
				var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
				var userId = ClaimsAuthorizationHelper.GetUserId();
				var modules = await settings.GetDepartmentModuleSettingsAsync(departmentId);
				if (modules != null && modules.TrainingDisabled)
					return Content(string.Empty);

				var view = new PendingTrainingReviewsView();
				var inbox = await submissions.GetInboxAsync(new TrainingReviewInboxQuery { DepartmentId = departmentId, ReviewerUserId = userId, Take = 3 });
				if (inbox != null && inbox.IsReviewer)
				{
					view.AwaitingCount = inbox.AwaitingCount;
					view.Waiting = inbox.Items ?? new List<LearningRecord>();
				}

				view.InfoNeeded = (await submissions.GetMySubmissionsAsync(departmentId, userId) ?? new List<LearningRecord>())
					.Where(TrainingSubmissionRules.IsInfoRequested).Take(3).ToList();
				if (view.AwaitingCount == 0 && view.InfoNeeded.Count == 0)
					return Content(string.Empty);

				view.Department = await departments.GetDepartmentByIdAsync(departmentId, false);
				if (view.Waiting.Count > 0)
					view.Names = (await departments.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>())
						.Where(n => n != null && !string.IsNullOrWhiteSpace(n.UserId))
						.GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
						.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
				return View("~/Areas/User/Views/Shared/_PendingTrainingReviews.cshtml", view);
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex)
			{
				Logging.LogException(ex, "Pending training reviews could not be shown.");
				return Content(string.Empty);
			}
		}
	}
}
