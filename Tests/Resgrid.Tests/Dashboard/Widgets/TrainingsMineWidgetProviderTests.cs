using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard.Widgets.Planning;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.Dashboard.Widgets.Planning;
using static Resgrid.Tests.Dashboard.Widgets.CommsPlanningWidgetContexts;

namespace Resgrid.Tests.Dashboard.Widgets
{
	[TestFixture]
	public class TrainingsMineWidgetProviderTests
	{
		private static Training Training(int id, TrainingUser user, double minimum = 0, DateTime? due = null, int departmentId = DepartmentId) => new Training
		{
			TrainingId = id, DepartmentId = departmentId, Name = "Training " + id, MinimumScore = minimum, ToBeCompletedBy = due,
			Users = user == null ? new List<TrainingUser>() : new List<TrainingUser> { user }
		};

		private static TrainingUser Me(bool viewed = false, bool complete = false, double score = 0) =>
			new TrainingUser { UserId = UserId.ToUpperInvariant(), Viewed = viewed, Complete = complete, Score = score };

		[Test]
		public async Task Outstanding_follows_the_trainings_page_rule()
		{
			var trainings = new Mock<ITrainingRepository>(MockBehavior.Strict);
			trainings.Setup(x => x.GetTrainingsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<Training>
			{
				Training(1, Me()),                                                   // not viewed: outstanding
				Training(2, Me(viewed: true, complete: true)),                        // done, no quiz
				Training(3, Me(viewed: true, complete: true, score: 40), minimum: 80), // failed quiz: outstanding
				Training(4, Me(viewed: true, complete: true, score: 40), minimum: 80), // below minimum but no quiz: done
				Training(5, null),                                                    // not assigned
				Training(6, Me(viewed: true), due: Now.AddDays(2)),                   // viewed, not complete: outstanding, due first
				Training(7, Me(), departmentId: OtherDepartmentId)
			});
			var questions = new Mock<ITrainingQuestionRepository>(MockBehavior.Strict);
			questions.Setup(x => x.GetTrainingQuestionsByTrainingIdAsync(3)).ReturnsAsync(new List<TrainingQuestion> { new TrainingQuestion() });
			questions.Setup(x => x.GetTrainingQuestionsByTrainingIdAsync(4)).ReturnsAsync(new List<TrainingQuestion>());
			var service = new TrainingService(trainings.Object, null, null, questions.Object, null, null, null);

			var outstanding = await service.GetOutstandingTrainingsForUserAsync(DepartmentId, UserId);

			outstanding.Select(x => x.Training.TrainingId).Should().Equal(6, 1, 3);
			outstanding.Single(x => x.Training.TrainingId == 3).QuizFailed.Should().BeTrue();
		}

		[Test]
		public async Task The_widget_counts_overdue_trainings_and_links_each_one()
		{
			var service = new Mock<ITrainingService>(MockBehavior.Strict);
			service.Setup(x => x.GetOutstandingTrainingsForUserAsync(DepartmentId, UserId)).ReturnsAsync(new List<TrainingAssignmentStatus>
			{
				new TrainingAssignmentStatus { Training = Training(1, Me(), due: Now.AddDays(-1)), Assignment = Me() },
				new TrainingAssignmentStatus { Training = Training(2, Me(viewed: true)), Assignment = Me(viewed: true) },
				new TrainingAssignmentStatus { Training = Training(3, Me(), departmentId: OtherDepartmentId), Assignment = Me() }
			});
			var provider = new TrainingsMineWidgetProvider(service.Object);

			var data = (TrainingsMineWidgetData)await provider.GetDataAsync(For("trainings.mine"), CancellationToken.None);

			data.OutstandingCount.Should().Be(2);
			data.OverdueCount.Should().Be(1);
			data.Items[0].Should().BeEquivalentTo(new { TrainingId = 1, IsOverdue = true, Viewed = false, Url = "/User/Trainings/View?trainingId=1" });
			data.Items[1].Viewed.Should().BeTrue();
		}
	}
}
