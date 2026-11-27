using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Planning;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Planning
{
	/// <summary>trainings.mine (plan §7.6): ITrainingService.GetOutstandingTrainingsForUserAsync — the Trainings page's rule per assignment.</summary>
	[DashboardWidget("trainings.mine")]
	public class TrainingsMineWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly ITrainingService _trainingService;

		public TrainingsMineWidgetProvider(ITrainingService trainingService)
		{
			_trainingService = trainingService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var outstanding = (await _trainingService.GetOutstandingTrainingsForUserAsync(context.DepartmentId, context.UserId) ?? new List<TrainingAssignmentStatus>())
				.Where(x => x?.Training != null && x.Training.DepartmentId == context.DepartmentId)
				.ToList();

			// Due moments and overdue come from TrainingDueDates (the end of the department-local day, leave applied), the
			// same helper as My Training, the search projection and the notifier (T-D19).
			bool IsOverdue(TrainingAssignmentStatus status) => status.State == TrainingAssignmentStates.Overdue || TrainingDueDates.IsOverdue(status.DueUtc, context.NowUtc);

			return new TrainingsMineWidgetData
			{
				OutstandingCount = outstanding.Count,
				OverdueCount = outstanding.Count(IsOverdue),
				Items = outstanding
					.Take(context.GetInt("maxItems", 5))
					.Select(x => new TrainingsMineItem
					{
						TrainingId = x.Training.TrainingId,
						Name = x.Training.Name,
						DueUtc = x.DueUtc,
						IsOverdue = IsOverdue(x),
						Viewed = x.Assignment?.Viewed ?? false,
						QuizFailed = x.QuizFailed,
						Url = "/User/Trainings/View?trainingId=" + x.Training.TrainingId.ToString(CultureInfo.InvariantCulture)
					})
					.ToList()
			};
		}
	}
}
