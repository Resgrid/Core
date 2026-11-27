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
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard;
using Resgrid.Services.Dashboard.Widgets.Planning;
using static Resgrid.Tests.Dashboard.Widgets.CommsPlanningWidgetContexts;

namespace Resgrid.Tests.Dashboard.Widgets
{
	/// <summary>trainings.bulletin (Training T0 item 6): new assignments and newly published open trainings, nothing archived, unstarted or done.</summary>
	[TestFixture]
	public class TrainingsBulletinWidgetProviderTests
	{
		private static Training Training(int id, DateTime createdOn, bool open = false, DateTime? startsOn = null, DateTime? archivedOn = null, int departmentId = DepartmentId) => new Training
		{
			TrainingId = id, DepartmentId = departmentId, Name = "Training " + id, CreatedOn = createdOn, OpenToAllMembers = open, StartsOn = startsOn, ArchivedOn = archivedOn,
			Users = new List<TrainingUser>()
		};

		private static TrainingAssignmentStatus Assigned(Training training, DateTime assignedOn, TrainingAssignmentStates state, DateTime? due = null) => new TrainingAssignmentStatus
		{
			Training = training,
			Assignment = new TrainingUser { TrainingId = training.TrainingId, UserId = UserId, AssignedOn = assignedOn, Complete = state == TrainingAssignmentStates.Complete },
			State = state,
			DueUtc = due
		};

		private static TrainingsBulletinWidgetProvider Provider(List<TrainingAssignmentStatus> assignments, List<Training> catalog)
		{
			var service = new Mock<ITrainingService>(MockBehavior.Strict);
			service.Setup(x => x.GetAssignmentsForUserAsync(DepartmentId, UserId, Now)).ReturnsAsync(assignments);
			service.Setup(x => x.GetAllTrainingsForDepartmentAsync(DepartmentId)).ReturnsAsync(catalog);
			return new TrainingsBulletinWidgetProvider(service.Object);
		}

		[Test]
		public void The_bulletin_is_in_the_catalog_with_its_settings()
		{
			var definition = DashboardWidgetCatalog.Find("trainings.bulletin");

			definition.Should().NotBeNull();
			definition.Gates.Policies.Should().Contain("Training_View");
			definition.Settings.Select(s => s.Name).Should().BeEquivalentTo(new[] { "days", "maxItems" });
		}

		[Test]
		public async Task New_assignments_and_newly_published_open_trainings_show_newest_first()
		{
			var assignedNew = Training(1, Now.AddDays(-30));
			var assignedOld = Training(2, Now.AddDays(-60));
			var assignedDone = Training(3, Now.AddDays(-2));
			var startedYesterday = Training(4, Now.AddDays(-40), startsOn: Now.AddDays(-1));
			var overdue = Training(5, Now.AddDays(-5));
			var catalogNew = Training(6, Now.AddDays(-3), open: true);
			catalogNew.AllowSelfEnroll = true;
			var catalogOld = Training(7, Now.AddDays(-45), open: true);
			var catalogArchived = Training(8, Now.AddDays(-1), open: true, archivedOn: Now.AddHours(-1));
			var notOpen = Training(9, Now.AddDays(-1));
			var catalogFuture = Training(10, Now.AddDays(-1), open: true, startsOn: Now.AddDays(3));
			var catalogForeign = Training(11, Now.AddDays(-1), open: true, departmentId: OtherDepartmentId);

			var provider = Provider(new List<TrainingAssignmentStatus>
			{
				Assigned(assignedNew, Now.AddDays(-2), TrainingAssignmentStates.NotStarted, Now.AddDays(10)),
				Assigned(assignedOld, Now.AddDays(-40), TrainingAssignmentStates.InProgress),
				Assigned(assignedDone, Now.AddDays(-1), TrainingAssignmentStates.Complete),
				Assigned(startedYesterday, Now.AddDays(-40), TrainingAssignmentStates.NotStarted),
				Assigned(overdue, Now.AddDays(-4), TrainingAssignmentStates.Overdue, Now.AddDays(-1))
			}, new List<Training> { assignedNew, catalogNew, catalogOld, catalogArchived, notOpen, catalogFuture, catalogForeign });

			var data = (TrainingsBulletinWidgetData)await provider.GetDataAsync(For("trainings.bulletin", Settings(("maxItems", 20))), CancellationToken.None);

			data.Items.Select(x => x.TrainingId).Should().Equal(4, 1, 6, 5);
			data.NewAssignedCount.Should().Be(3);
			data.CatalogCount.Should().Be(1);
			data.Items.Single(x => x.TrainingId == 6).Kind.Should().Be(TrainingsBulletinItem.CatalogKind);
			data.Items.Single(x => x.TrainingId == 6).CanSelfEnroll.Should().BeTrue();
			data.Items.Single(x => x.TrainingId == 5).IsOverdue.Should().BeTrue();
			data.Items.Single(x => x.TrainingId == 1).DueUtc.Should().Be(Now.AddDays(10));
			data.Items.Single(x => x.TrainingId == 1).Url.Should().Be("/User/Trainings/View?trainingId=1");
		}

		[Test]
		public async Task The_window_and_the_item_limit_follow_the_settings()
		{
			var trainings = Enumerable.Range(1, 6).Select(i => Training(i, Now.AddDays(-i), open: true)).ToList();
			var provider = Provider(new List<TrainingAssignmentStatus>(), trainings);

			var data = (TrainingsBulletinWidgetData)await provider.GetDataAsync(For("trainings.bulletin", Settings(("days", 4), ("maxItems", 2))), CancellationToken.None);

			data.CatalogCount.Should().Be(4);
			data.Items.Select(x => x.TrainingId).Should().Equal(1, 2);
		}
	}
}
