using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Services;

namespace Resgrid.Tests.LmsBilling
{
	/// <summary>
	/// Lapse rule 12 (§17.3, R-L7), the reactivation re-base the requirement engine (T3) will call: time spent lapsed does not count
	/// toward lateness (due dates shift by the lapse) and nothing becomes overdue or enforceable until the reactivation grace (14 days)
	/// has passed. Exercised against the assignment due dates that exist today: TrainingUser.DueOn made by TrainingDueDates (a fixed
	/// date, a relative rule, and the member's leave), in a department's own zone.
	/// </summary>
	[TestFixture]
	public class LmsReactivationRebaseTests
	{
		private static readonly Department Eastern = new Department { DepartmentId = 70, TimeZone = "Eastern Standard Time" };
		private static readonly TimeSpan Grace = TimeSpan.FromDays(LmsAddonConfig.ReactivationGraceDays);

		private static DateTime Utc(int y, int mo, int d, int h = 0) => new DateTime(y, mo, d, h, 0, 0, DateTimeKind.Utc);

		/// <summary>The add-on lapsed on 1 March and came back on 1 April 2027 (31 days).</summary>
		private static readonly LmsAddonLapse March = new LmsAddonLapse
		{
			LmsAddonLapseId = 1, DepartmentId = 70, StartedOn = Utc(2027, 3, 1, 12), EndedOn = Utc(2027, 4, 1, 12), Reason = (int)LmsLapseReasons.PaymentFailed
		};

		private static LmsDueItem Item(TrainingUser assignment) => new LmsDueItem
		{
			Key = assignment.TrainingUserId.ToString(), DueUtc = assignment.DueOn.Value, AssignedOnUtc = assignment.AssignedOn
		};

		private static TrainingUser Assigned(int id, Training training, DateTime assignedOnUtc) => new TrainingUser
		{
			TrainingUserId = id, TrainingId = training.TrainingId, UserId = "member", AssignedOn = assignedOnUtc,
			DueOn = TrainingDueDates.DueForAssignment(training, assignedOnUtc, Eastern)
		};

		[Test]
		public void A_due_date_inside_the_lapse_shifts_by_the_lapse_length_plus_the_grace()
		{
			// Fixed date 10 March, assigned 1 February: due at the end of 10 March local, 9 days into the lapse.
			var training = new Training { TrainingId = 1, ToBeCompletedBy = new DateTime(2027, 3, 10) };
			var assignment = Assigned(11, training, Utc(2027, 2, 1, 15));

			var rebased = LmsLapseRebase.Rebase(Item(assignment), new[] { March }, Grace);

			rebased.Shift.Should().BeGreaterThanOrEqualTo(TimeSpan.FromDays(31), "time spent lapsed does not count toward lateness");
			rebased.DueUtc.Should().Be(Utc(2027, 4, 15, 12), "shifted by the 31-day lapse it would fall inside the grace, so it moves to reactivation + 14 days");
			rebased.HoldUntilUtc.Should().Be(Utc(2027, 4, 15, 12));
			rebased.Key.Should().Be("11");
			TrainingDueDates.IsOverdue(rebased.DueUtc, Utc(2027, 4, 10)).Should().BeFalse("nothing becomes overdue inside the grace");
		}

		[Test]
		public void A_later_due_date_shifts_by_the_lapse_length_only()
		{
			// Relative rule: due 90 days after assignment on 15 February, so due mid-May, well after the grace.
			var training = new Training { TrainingId = 2, DueDays = 90 };
			var assignment = Assigned(12, training, Utc(2027, 2, 15, 15));

			var rebased = LmsLapseRebase.Rebase(Item(assignment), new[] { March }, Grace);

			rebased.DueUtc.Should().Be(assignment.DueOn.Value.AddDays(31));
			rebased.Shift.Should().Be(TimeSpan.FromDays(31));
			rebased.HoldUntilUtc.Should().Be(Utc(2027, 4, 15, 12), "enforcement waits for the grace even where the due date is later");
		}

		[Test]
		public void A_member_assigned_during_the_lapse_is_shifted_only_by_the_part_after_the_assignment()
		{
			var training = new Training { TrainingId = 3, DueDays = 30 };
			var assignment = Assigned(13, training, Utc(2027, 3, 21, 12));

			var rebased = LmsLapseRebase.Rebase(Item(assignment), new[] { March }, Grace);

			rebased.Shift.Should().Be(TimeSpan.FromDays(11), "only the 11 lapsed days after the assignment count");
		}

		[Test]
		public void An_item_already_overdue_before_the_lapse_stays_overdue_but_is_held_from_enforcement()
		{
			var training = new Training { TrainingId = 4, ToBeCompletedBy = new DateTime(2027, 2, 10) };
			var assignment = Assigned(14, training, Utc(2027, 1, 5, 15));

			var rebased = LmsLapseRebase.Rebase(Item(assignment), new[] { March }, Grace);

			rebased.DueUtc.Should().Be(assignment.DueOn.Value, "lateness earned before the lapse is not forgiven");
			rebased.HoldUntilUtc.Should().Be(Utc(2027, 4, 15, 12), "but nothing becomes enforceable until the grace has passed");
		}

		[Test]
		public void An_item_assigned_after_the_reactivation_was_never_paused()
		{
			var training = new Training { TrainingId = 5, DueDays = 10 };
			var assignment = Assigned(15, training, Utc(2027, 4, 5, 12));

			var rebased = LmsLapseRebase.Rebase(Item(assignment), new[] { March }, Grace);

			rebased.DueUtc.Should().Be(assignment.DueOn.Value);
			rebased.HoldUntilUtc.Should().BeNull();
		}

		[Test]
		public void Several_lapses_add_up_and_an_open_lapse_moves_nothing_yet()
		{
			var training = new Training { TrainingId = 6, DueDays = 200 };
			var assignment = Assigned(16, training, Utc(2027, 1, 2, 15));
			var june = new LmsAddonLapse { LmsAddonLapseId = 2, DepartmentId = 70, StartedOn = Utc(2027, 6, 1), EndedOn = Utc(2027, 6, 11) };
			var open = new LmsAddonLapse { LmsAddonLapseId = 3, DepartmentId = 70, StartedOn = Utc(2027, 7, 1) };

			var rebased = LmsLapseRebase.Rebase(Item(assignment), new[] { March, june, open }, Grace);

			rebased.Shift.Should().Be(TimeSpan.FromDays(41), "31 + 10 days; the open lapse is still paused");
			rebased.HoldUntilUtc.Should().Be(Utc(2027, 6, 25), "the grace runs from the last reactivation");
		}

		[Test]
		public void The_rebase_composes_with_the_members_leave()
		{
			var training = new Training { TrainingId = 7, ToBeCompletedBy = new DateTime(2027, 5, 31) };
			var assignment = Assigned(17, training, Utc(2027, 2, 1, 15));
			var leave = new[] { new MemberLeavePeriod { StartsOn = Utc(2027, 2, 10), EndsOn = Utc(2027, 2, 20) } };
			var effective = TrainingDueDates.EffectiveDueUtc(training, assignment, Eastern, leave).Value;

			var rebased = LmsLapseRebase.Rebase(new LmsDueItem { Key = "17", DueUtc = effective, AssignedOnUtc = assignment.AssignedOn }, new[] { March }, Grace);

			rebased.DueUtc.Should().Be(assignment.DueOn.Value.AddDays(10).AddDays(31), "ten days of leave, then the 31-day lapse");
		}

		[Test]
		public void Lapsed_time_between_two_moments_is_what_does_not_count_toward_lateness()
		{
			LmsLapseRebase.LapsedBetween(new[] { March }, Utc(2027, 3, 11, 12), Utc(2027, 5, 1)).Should().Be(TimeSpan.FromDays(21));
			LmsLapseRebase.LapsedBetween(new[] { new LmsAddonLapse { StartedOn = Utc(2027, 3, 1) } }, Utc(2027, 2, 1), Utc(2027, 3, 4)).Should().Be(TimeSpan.FromDays(3), "an open lapse counts up to now");
		}

		[Test]
		public async Task The_service_reads_the_departments_closed_lapses_and_rebases_each_item_in_order()
		{
			var lapses = new Mock<ILmsAddonLapsesRepository>();
			lapses.Setup(l => l.GetForDepartmentAsync(70)).ReturnsAsync(new List<LmsAddonLapse>
			{
				March, new LmsAddonLapse { LmsAddonLapseId = 9, DepartmentId = 70, StartedOn = Utc(2027, 9, 1) },
				new LmsAddonLapse { LmsAddonLapseId = 8, DepartmentId = 71, StartedOn = Utc(2027, 1, 1), EndedOn = Utc(2027, 12, 1) }
			});
			var service = new LmsReactivationService(lapses.Object);

			(await service.GetLapsesAsync(70)).Select(l => l.LmsAddonLapseId).Should().Equal(1);
			(await service.GetLastReactivationAsync(70)).Should().Be(Utc(2027, 4, 1, 12));

			var results = await service.RebaseAsync(70, new[]
			{
				new LmsDueItem { Key = "a", DueUtc = Utc(2027, 3, 10), AssignedOnUtc = Utc(2027, 2, 1) },
				new LmsDueItem { Key = "b", DueUtc = Utc(2027, 8, 1), AssignedOnUtc = Utc(2027, 2, 1) }
			});

			results.Select(r => r.Key).Should().Equal("a", "b");
			results[0].DueUtc.Should().Be(Utc(2027, 4, 15, 12));
			results[1].DueUtc.Should().Be(Utc(2027, 9, 1));
			(await service.RebaseAsync(0, new[] { new LmsDueItem { Key = "x", DueUtc = Utc(2027, 1, 1) } })).Single().DueUtc.Should().Be(Utc(2027, 1, 1));
		}
	}
}
