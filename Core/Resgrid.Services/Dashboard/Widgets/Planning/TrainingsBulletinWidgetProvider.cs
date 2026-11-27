using System;
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
	/// <summary>
	/// trainings.bulletin (Training T0 item 6): trainings newly assigned to the member (or whose scheduled start just passed) that
	/// they have not finished, and open trainings recently published to the catalog. Archived trainings and trainings still
	/// waiting for their scheduled start never show; due moments come from TrainingDueDates through the service.
	/// </summary>
	[DashboardWidget("trainings.bulletin")]
	public class TrainingsBulletinWidgetProvider : IDashboardWidgetDataProvider
	{
		private static readonly TrainingAssignmentStates[] Hidden =
		{
			TrainingAssignmentStates.Complete, TrainingAssignmentStates.Removed, TrainingAssignmentStates.Scheduled, TrainingAssignmentStates.Exempt
		};

		private readonly ITrainingService _trainingService;

		public TrainingsBulletinWidgetProvider(ITrainingService trainingService)
		{
			_trainingService = trainingService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var now = context.NowUtc;
			var since = now.AddDays(-context.GetInt("days", 14));
			var data = new TrainingsBulletinWidgetData();

			var assignments = (await _trainingService.GetAssignmentsForUserAsync(context.DepartmentId, context.UserId, now) ?? new List<TrainingAssignmentStatus>())
				.Where(x => x?.Training != null && x.Assignment != null && x.Training.DepartmentId == context.DepartmentId && !x.Training.IsArchived && x.Training.HasStarted(now))
				.ToList();

			var items = new List<TrainingsBulletinItem>();
			foreach (var status in assignments.Where(x => !Hidden.Contains(x.State)))
			{
				// It reached the member when it was assigned, or when its scheduled start passed if that came later.
				var assignedOn = status.Assignment.AssignedOn ?? status.Training.CreatedOn;
				var published = status.Training.StartsOn.HasValue && status.Training.StartsOn.Value > assignedOn ? status.Training.StartsOn.Value : assignedOn;
				if (published < since || published > now)
					continue;

				items.Add(new TrainingsBulletinItem
				{
					TrainingId = status.Training.TrainingId,
					Name = status.Training.Name,
					Kind = TrainingsBulletinItem.AssignedKind,
					PublishedUtc = published,
					DueUtc = status.DueUtc,
					IsOverdue = status.State == TrainingAssignmentStates.Overdue,
					Url = Url(status.Training.TrainingId)
				});
			}

			data.NewAssignedCount = items.Count;

			var assignedIds = new HashSet<int>(assignments.Where(x => !x.Assignment.RemovedOn.HasValue).Select(x => x.Training.TrainingId));
			foreach (var training in await _trainingService.GetAllTrainingsForDepartmentAsync(context.DepartmentId) ?? new List<Training>())
			{
				if (training == null || training.DepartmentId != context.DepartmentId || !training.OpenToAllMembers || training.IsArchived || !training.HasStarted(now) ||
					assignedIds.Contains(training.TrainingId))
					continue;

				var published = training.StartsOn.HasValue && training.StartsOn.Value > training.CreatedOn ? training.StartsOn.Value : training.CreatedOn;
				if (published < since || published > now)
					continue;

				items.Add(new TrainingsBulletinItem
				{
					TrainingId = training.TrainingId,
					Name = training.Name,
					Kind = TrainingsBulletinItem.CatalogKind,
					PublishedUtc = published,
					CanSelfEnroll = training.AllowSelfEnroll && !training.ClosedToNewAssignees,
					Url = Url(training.TrainingId)
				});
				data.CatalogCount++;
			}

			data.Items = items
				.OrderByDescending(x => x.PublishedUtc)
				.ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
				.Take(context.GetInt("maxItems", 5))
				.ToList();

			return data;
		}

		private static string Url(int trainingId) => "/User/Trainings/View?trainingId=" + trainingId.ToString(CultureInfo.InvariantCulture);
	}
}
