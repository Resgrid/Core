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

		private static TrainingUser Me(bool viewed = false, bool complete = false, double score = 0, bool? passed = null) =>
			new TrainingUser { UserId = UserId.ToUpperInvariant(), Viewed = viewed, Complete = complete, Score = score, Passed = passed };

		[Test]
		public async Task Outstanding_follows_the_stored_result_and_never_recomputes_from_the_current_quiz()
		{
			var utcNow = DateTime.UtcNow;
			var archived = Training(8, Me());
			archived.ArchivedOn = utcNow.AddDays(-1);
			var exempt = Me();
			exempt.ExemptOn = utcNow.AddDays(-1);
			var scheduled = Training(10, Me());
			scheduled.StartsOn = utcNow.AddDays(3);

			var trainings = new Mock<ITrainingRepository>(MockBehavior.Strict);
			trainings.Setup(x => x.GetTrainingsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<Training>
			{
				Training(1, Me()),                                                                 // not viewed: outstanding
				Training(2, Me(viewed: true, complete: true)),                                      // done, no quiz
				Training(3, Me(viewed: true, score: 40, passed: false), minimum: 80),               // failed quiz (stored): outstanding
				Training(4, Me(viewed: true, complete: true, score: 40, passed: true), minimum: 80), // passed when taken: done whatever the pass mark is now
				Training(5, null),                                                                  // not assigned
				Training(6, Me(viewed: true), due: utcNow.AddDays(2)),                              // viewed, not complete: outstanding, due first
				Training(7, Me(), departmentId: OtherDepartmentId),
				archived,                                                                           // archived: hidden
				Training(9, exempt),                                                                // exempt: not outstanding
				scheduled                                                                           // not started yet: hidden
			});
			trainings.Setup(x => x.GetQuestionCountsForDepartmentAsync(DepartmentId)).ReturnsAsync(new Dictionary<int, int> { [3] = 1, [4] = 2 });
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" });
			var service = new TrainingService(trainings.Object, null, null, Mock.Of<ITrainingQuestionRepository>(MockBehavior.Strict), null, departments.Object, null, Mock.Of<IKnowledgeIndexQueue>(), Mock.Of<IUserProfileService>());

			var outstanding = await service.GetOutstandingTrainingsForUserAsync(DepartmentId, UserId);

			outstanding.Select(x => x.Training.TrainingId).Should().Equal(6, 1, 3);
			outstanding.Single(x => x.Training.TrainingId == 3).QuizFailed.Should().BeTrue();
			outstanding.Single(x => x.Training.TrainingId == 3).State.Should().Be(TrainingAssignmentStates.Failed);
			outstanding.Single(x => x.Training.TrainingId == 6).DueUtc.Should().NotBeNull();
		}

		[Test]
		public async Task The_widget_counts_overdue_trainings_and_links_each_one()
		{
			var service = new Mock<ITrainingService>(MockBehavior.Strict);
			service.Setup(x => x.GetOutstandingTrainingsForUserAsync(DepartmentId, UserId)).ReturnsAsync(new List<TrainingAssignmentStatus>
			{
				new TrainingAssignmentStatus { Training = Training(1, Me()), Assignment = Me(), DueUtc = Now.AddDays(-1), State = TrainingAssignmentStates.Overdue },
				new TrainingAssignmentStatus { Training = Training(2, Me(viewed: true)), Assignment = Me(viewed: true), DueUtc = Now.AddDays(3), State = TrainingAssignmentStates.InProgress },
				new TrainingAssignmentStatus { Training = Training(3, Me(), departmentId: OtherDepartmentId), Assignment = Me() }
			});
			var provider = new TrainingsMineWidgetProvider(service.Object);

			var data = (TrainingsMineWidgetData)await provider.GetDataAsync(For("trainings.mine"), CancellationToken.None);

			data.OutstandingCount.Should().Be(2);
			data.OverdueCount.Should().Be(1);
			data.Items[0].Should().BeEquivalentTo(new { TrainingId = 1, IsOverdue = true, Viewed = false, Url = "/User/Trainings/View?trainingId=1" });
			data.Items[0].DueUtc.Should().Be(Now.AddDays(-1));
			data.Items[1].Viewed.Should().BeTrue();
			data.Items[1].IsOverdue.Should().BeFalse();
		}
	}
}
