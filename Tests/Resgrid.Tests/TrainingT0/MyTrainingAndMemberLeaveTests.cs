using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.TrainingT0
{
	/// <summary>My Training (§18.3): assignments by state, the catalog, certifications due or pinned, and the catch-up re-evaluation.</summary>
	[TestFixture]
	public class MyTrainingServiceTests
	{
		private const int DepartmentId = 10;
		private const string Member = "member";
		private static readonly DateTime Now = DateTime.UtcNow;

		private Mock<ITrainingService> _trainings;
		private Mock<ITrainingAssignmentService> _assignments;
		private Mock<ICertificationService> _certifications;
		private Mock<IMemberPinnedItemsRepository> _pins;
		private Mock<IMemberLeaveService> _leave;
		private MyTrainingService _service;

		[SetUp]
		public void SetUp()
		{
			_trainings = new Mock<ITrainingService>();
			_assignments = new Mock<ITrainingAssignmentService>();
			_certifications = new Mock<ICertificationService>();
			_pins = new Mock<IMemberPinnedItemsRepository>();
			_leave = new Mock<IMemberLeaveService>();
			_leave.Setup(x => x.GetForMemberAsync(DepartmentId, Member)).ReturnsAsync(new List<MemberLeavePeriod>());
			_pins.Setup(x => x.GetForMemberAsync(DepartmentId, Member)).ReturnsAsync(new List<MemberPinnedItem>());
			_certifications.Setup(x => x.GetCertificationsByUserIdAsync(Member)).ReturnsAsync(new List<PersonnelCertification>());
			_trainings.Setup(x => x.GetAllTrainingsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Training>());
			_service = new MyTrainingService(_trainings.Object, _assignments.Object, _certifications.Object, _pins.Object, _leave.Object);
		}

		private static TrainingAssignmentStatus Status(int id, TrainingAssignmentStates state, DateTime? due = null, DateTime? completedOn = null) => new TrainingAssignmentStatus
		{
			Training = new Training { TrainingId = id, DepartmentId = DepartmentId, Name = "T" + id },
			Assignment = new TrainingUser { TrainingId = id, UserId = Member, CompletedOn = completedOn, Complete = state == TrainingAssignmentStates.Complete },
			State = state,
			DueUtc = due
		};

		[Test]
		public async Task Assignments_land_in_the_overdue_due_soon_in_progress_and_recent_buckets()
		{
			_trainings.Setup(x => x.GetAssignmentsForUserAsync(DepartmentId, Member, Now)).ReturnsAsync(new List<TrainingAssignmentStatus>
			{
				Status(1, TrainingAssignmentStates.Overdue, Now.AddDays(-1)),
				Status(2, TrainingAssignmentStates.NotStarted, Now.AddDays(3)),
				Status(3, TrainingAssignmentStates.InProgress, Now.AddDays(40)),
				Status(4, TrainingAssignmentStates.Failed, Now.AddDays(2)),
				Status(5, TrainingAssignmentStates.Complete, completedOn: Now.AddDays(-5)),
				Status(6, TrainingAssignmentStates.Complete, completedOn: Now.AddDays(-400)),
				Status(7, TrainingAssignmentStates.Scheduled, Now.AddDays(10)),
				Status(8, TrainingAssignmentStates.NotStarted)
			});

			var overview = await _service.GetOverviewAsync(DepartmentId, Member, Now);

			overview.Overdue.Select(x => x.Training.TrainingId).Should().Equal(1);
			overview.DueSoon.Select(x => x.Training.TrainingId).Should().BeEquivalentTo(new[] { 2, 4 });
			overview.InProgress.Select(x => x.Training.TrainingId).Should().Equal(3);
			overview.Other.Select(x => x.Training.TrainingId).Should().Equal(8);
			overview.RecentCompletions.Select(x => x.Training.TrainingId).Should().Equal(5);
			_assignments.Verify(x => x.ReevaluateMemberAsync(DepartmentId, Member, Now, It.IsAny<CancellationToken>()), Times.Once, "the page catches up a missed membership change");
		}

		[Test]
		public async Task The_catalog_lists_open_started_unarchived_trainings_the_member_is_not_assigned()
		{
			_trainings.Setup(x => x.GetAssignmentsForUserAsync(DepartmentId, Member, Now)).ReturnsAsync(new List<TrainingAssignmentStatus> { Status(1, TrainingAssignmentStates.NotStarted) });
			_trainings.Setup(x => x.GetAllTrainingsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Training>
			{
				new Training { TrainingId = 1, Name = "Assigned", OpenToAllMembers = true },
				new Training { TrainingId = 2, Name = "Open", OpenToAllMembers = true, AllowSelfEnroll = true },
				new Training { TrainingId = 3, Name = "Closed list" },
				new Training { TrainingId = 4, Name = "Archived", OpenToAllMembers = true, ArchivedOn = Now },
				new Training { TrainingId = 5, Name = "Later", OpenToAllMembers = true, StartsOn = Now.AddDays(5) }
			});

			var overview = await _service.GetOverviewAsync(DepartmentId, Member, Now);

			overview.Catalog.Select(t => t.TrainingId).Should().Equal(2);
		}

		[Test]
		public async Task A_failing_re_evaluation_never_breaks_the_page()
		{
			_assignments.Setup(x => x.ReevaluateMemberAsync(DepartmentId, Member, Now, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));
			_trainings.Setup(x => x.GetAssignmentsForUserAsync(DepartmentId, Member, Now)).ReturnsAsync(new List<TrainingAssignmentStatus> { Status(1, TrainingAssignmentStates.Overdue, Now.AddDays(-1)) });

			(await _service.GetOverviewAsync(DepartmentId, Member, Now)).Overdue.Should().ContainSingle();
		}

		[Test]
		public async Task Certifications_show_when_due_expired_or_pinned_and_never_another_departments()
		{
			_trainings.Setup(x => x.GetAssignmentsForUserAsync(DepartmentId, Member, Now)).ReturnsAsync(new List<TrainingAssignmentStatus>());
			_certifications.Setup(x => x.GetCertificationsByUserIdAsync(Member)).ReturnsAsync(new List<PersonnelCertification>
			{
				new PersonnelCertification { PersonnelCertificationId = 1, DepartmentId = DepartmentId, UserId = Member, Name = "EMT", ExpiresOn = Now.AddDays(20) },
				new PersonnelCertification { PersonnelCertificationId = 2, DepartmentId = DepartmentId, UserId = Member, Name = "CPR", ExpiresOn = Now.AddDays(-2) },
				new PersonnelCertification { PersonnelCertificationId = 3, DepartmentId = DepartmentId, UserId = Member, Name = "Pump", ExpiresOn = Now.AddDays(300) },
				new PersonnelCertification { PersonnelCertificationId = 4, DepartmentId = DepartmentId, UserId = Member, Name = "Pinned", ExpiresOn = Now.AddDays(300) },
				new PersonnelCertification { PersonnelCertificationId = 5, DepartmentId = 99, UserId = Member, Name = "Foreign", ExpiresOn = Now.AddDays(1) },
				new PersonnelCertification { PersonnelCertificationId = 6, DepartmentId = DepartmentId, UserId = Member, Name = "Gone", ExpiresOn = Now.AddDays(1), IsDeleted = true },
				new PersonnelCertification { PersonnelCertificationId = 7, DepartmentId = DepartmentId, UserId = Member, Name = "Revoked", ExpiresOn = Now.AddDays(1), Status = (int)PersonnelCertificationStatuses.Revoked }
			});
			_pins.Setup(x => x.GetForMemberAsync(DepartmentId, Member)).ReturnsAsync(new List<MemberPinnedItem>
			{
				new MemberPinnedItem { Kind = (int)MemberPinnedItemKinds.Certification, RefId = "4" }
			});

			var certifications = (await _service.GetOverviewAsync(DepartmentId, Member, Now)).Certifications;

			certifications.Select(c => c.Name).Should().Equal("Pinned", "CPR", "EMT");
			certifications.Single(c => c.Name == "CPR").IsExpired.Should().BeTrue();
			certifications.Single(c => c.Name == "EMT").DueSoon.Should().BeTrue();
			certifications.Single(c => c.Name == "Pinned").Pinned.Should().BeTrue();
		}

		[Test]
		public async Task A_member_pins_only_their_own_certification()
		{
			_certifications.Setup(x => x.GetCertificationByIdAsync(4)).ReturnsAsync(new PersonnelCertification { PersonnelCertificationId = 4, DepartmentId = DepartmentId, UserId = "MEMBER" });
			_certifications.Setup(x => x.GetCertificationByIdAsync(5)).ReturnsAsync(new PersonnelCertification { PersonnelCertificationId = 5, DepartmentId = DepartmentId, UserId = "someone-else" });

			(await _service.PinCertificationAsync(DepartmentId, Member, 4, true)).Should().BeTrue();
			(await _service.PinCertificationAsync(DepartmentId, Member, 5, true)).Should().BeFalse();
			(await _service.PinCertificationAsync(99, Member, 4, true)).Should().BeFalse();
			(await _service.PinCertificationAsync(DepartmentId, Member, 4, false)).Should().BeTrue();

			_pins.Verify(x => x.InsertAsync(It.Is<MemberPinnedItem>(p => p.RefId == "4" && p.Kind == (int)MemberPinnedItemKinds.Certification), It.IsAny<CancellationToken>(), true), Times.Once);
			_pins.Verify(x => x.DeleteForMemberAsync(DepartmentId, Member, (int)MemberPinnedItemKinds.Certification, "4", It.IsAny<CancellationToken>()), Times.Exactly(2));
		}
	}

	/// <summary>Leave of absence (R-C2): a department-local date range stored as UTC start and end of day, audited.</summary>
	[TestFixture]
	public class MemberLeaveServiceTests
	{
		private const int DepartmentId = 10;
		private static readonly Department Nepal = new Department { DepartmentId = DepartmentId, TimeZone = "Nepal Standard Time" };
		private Mock<IMemberLeavePeriodsRepository> _repository;
		private Mock<IDepartmentsService> _departments;
		private Mock<IEventAggregator> _events;
		private MemberLeaveService _service;

		[SetUp]
		public void SetUp()
		{
			_repository = new Mock<IMemberLeavePeriodsRepository>();
			_departments = new Mock<IDepartmentsService>();
			_events = new Mock<IEventAggregator>();
			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(Nepal);
			_departments.Setup(x => x.GetDepartmentMemberAsync(It.IsAny<string>(), DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync((string userId, int departmentId, bool _) => userId == "gone"
					? new DepartmentMember { DepartmentId = departmentId, UserId = userId, IsDeleted = true }
					: new DepartmentMember { DepartmentId = departmentId, UserId = userId.ToLowerInvariant() });
			_repository.Setup(x => x.SaveOrUpdateAsync(It.IsAny<MemberLeavePeriod>(), It.IsAny<CancellationToken>(), true))
				.ReturnsAsync((MemberLeavePeriod p, CancellationToken _, bool _) => p);
			_service = new MemberLeaveService(_repository.Object, _departments.Object, _events.Object);
		}

		[Test]
		public async Task A_leave_runs_from_the_start_of_its_first_local_day_to_the_end_of_its_last()
		{
			var period = await _service.SaveAsync(DepartmentId, "FF1", new DateTime(2026, 12, 1), new DateTime(2026, 12, 10), MemberLeaveKinds.Medical, " Knee ", "officer");

			period.UserId.Should().Be("ff1", "the stored id comes from the member row");
			period.StartsOn.Should().Be(TrainingDueDates.StartOfLocalDayUtc(new DateTime(2026, 12, 1), Nepal));
			period.EndsOn.Should().Be(TrainingDueDates.EndOfLocalDayUtc(new DateTime(2026, 12, 10), Nepal));
			period.Kind.Should().Be((int)MemberLeaveKinds.Medical);
			period.Note.Should().Be("Knee");
			period.CreatedByUserId.Should().Be("officer");
			_events.Verify(x => x.SendMessage(It.Is<AuditEvent>(e => e.Type == AuditLogTypes.MemberLeavePeriodChanged && e.Before == null)), Times.Once);
		}

		[Test]
		public async Task Backwards_overlong_or_departed_member_leaves_are_refused()
		{
			(await _service.SaveAsync(DepartmentId, "ff1", new DateTime(2026, 12, 10), new DateTime(2026, 12, 1), MemberLeaveKinds.Other, null, "officer")).Should().BeNull();
			(await _service.SaveAsync(DepartmentId, "ff1", new DateTime(2026, 1, 1), new DateTime(2029, 1, 1), MemberLeaveKinds.Other, null, "officer")).Should().BeNull();
			(await _service.SaveAsync(DepartmentId, "gone", new DateTime(2026, 12, 1), new DateTime(2026, 12, 2), MemberLeaveKinds.Other, null, "officer")).Should().BeNull();
			(await _service.SaveAsync(DepartmentId, "ff1", new DateTime(2026, 12, 1), new DateTime(2026, 12, 2), (MemberLeaveKinds)99, null, "officer")).Kind.Should().Be((int)MemberLeaveKinds.Other);
		}

		[Test]
		public async Task Another_departments_period_is_neither_edited_nor_deleted()
		{
			_repository.Setup(x => x.GetByIdAsync(5)).ReturnsAsync(new MemberLeavePeriod { MemberLeavePeriodId = 5, DepartmentId = 99, UserId = "ff1" });

			(await _service.SaveAsync(DepartmentId, "ff1", new DateTime(2026, 12, 1), new DateTime(2026, 12, 2), MemberLeaveKinds.Other, null, "officer", 5)).Should().BeNull();
			(await _service.DeleteAsync(DepartmentId, 5, "officer")).Should().BeFalse();
			_repository.Verify(x => x.DeleteForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Periods_are_grouped_by_member_without_regard_to_case()
		{
			_repository.Setup(x => x.GetForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<MemberLeavePeriod>
			{
				new MemberLeavePeriod { UserId = "FF1", StartsOn = new DateTime(2026, 12, 1) },
				new MemberLeavePeriod { UserId = "ff1", StartsOn = new DateTime(2026, 11, 1) },
				new MemberLeavePeriod { UserId = "ff2", StartsOn = new DateTime(2026, 10, 1) }
			});

			var byMember = await _service.GetByMemberAsync(DepartmentId);

			byMember["Ff1"].Select(l => l.StartsOn.Month).Should().Equal(11, 12);
			byMember.Should().HaveCount(2);
		}
	}
}
