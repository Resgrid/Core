using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;

namespace Resgrid.Tests.TrainingT3
{
	/// <summary>
	/// Training T3 pure rules: due dates from audience entry (join, role add, any entry), grace, at risk, leave pause, recurrences,
	/// calendar-year pro-rating, evaluations, not licensed, exemptions, the lapse pause and re-base; the blast-radius guard (R-L11); and
	/// learning-path prerequisites, electives and validation.
	/// </summary>
	[TestFixture]
	public class LmsRequirementRulesTests
	{
		private static readonly Department Utc = new Department { DepartmentId = 7, TimeZone = "UTC" };
		private static readonly DateTime Now = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
		private const string Member = "a1b2c3d4-0000-4000-8000-000000000001";

		private static LmsRequirement Course(int dueDays = 30, int grace = 0, int atRisk = 14, LmsRequirementRecurrences recurrence = LmsRequirementRecurrences.OneTime,
			int? interval = null) => new LmsRequirement
		{
			LmsRequirementId = 1, DepartmentId = 7, Name = "SCBA refresher", Kind = (int)LmsRequirementKinds.Course, TargetRefId = 31, DueDays = dueDays, GraceDays = grace,
			AtRiskDays = atRisk, Recurrence = (int)recurrence, IntervalMonths = interval
		};

		private static LmsRequirementMember Entered(DateTime enteredOn) => new LmsRequirementMember { LmsRequirementMemberId = 9, DepartmentId = 7, LmsRequirementId = 1, UserId = Member, EnteredOn = enteredOn };

		private static LmsRequirementFact Done(DateTime on, decimal amount = 1m, int? record = null) => new LmsRequirementFact { UserId = Member, RefId = 31, CompletedOn = on, Amount = amount, LearningRecordId = record };

		private static LmsRequirementContext Context(DateTime? now = null, bool licensed = true, IEnumerable<MemberLeavePeriod> leaves = null, IEnumerable<LmsAddonLapse> lapses = null,
			DateTime? openLapse = null) => new LmsRequirementContext
		{
			Department = Utc, UtcNow = now ?? Now, Licensed = licensed, Leaves = leaves, Lapses = lapses, OpenLapseSinceUtc = openLapse, ReactivationGrace = TimeSpan.FromDays(14)
		};

		private static DateTime EndOfDay(int year, int month, int day) => new DateTime(year, month, day, 23, 59, 59, DateTimeKind.Utc);

		#region Due dates from audience entry

		[Test]
		public void A_new_member_is_due_DueDays_after_joining_at_the_end_of_the_local_day()
		{
			var status = LmsRequirementEvaluator.Evaluate(Course(dueDays: 30), Entered(new DateTime(2026, 6, 10, 8, 0, 0, DateTimeKind.Utc)), null, Context());

			status.EffectiveDueUtc.Should().Be(EndOfDay(2026, 7, 10));
			status.State.Should().Be(LmsRequirementStates.Pending);
			status.Satisfied.Should().BeFalse();
		}

		[Test]
		public void A_member_given_the_role_later_gets_their_own_later_due_date()
		{
			// Both entered the audience (one joined in May, one was given the role in June): each due date runs from their own entry.
			var joined = LmsRequirementEvaluator.Evaluate(Course(dueDays: 30), Entered(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)), null, Context());
			var roleAdded = LmsRequirementEvaluator.Evaluate(Course(dueDays: 30), Entered(new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc)), null, Context());

			joined.EffectiveDueUtc.Should().Be(EndOfDay(2026, 5, 31));
			joined.State.Should().Be(LmsRequirementStates.Overdue);
			roleAdded.EffectiveDueUtc.Should().Be(EndOfDay(2026, 7, 14));
			roleAdded.State.Should().Be(LmsRequirementStates.Pending);
		}

		[Test]
		public void Within_the_at_risk_window_the_member_is_at_risk()
		{
			var status = LmsRequirementEvaluator.Evaluate(Course(dueDays: 30, atRisk: 14), Entered(new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc)), null, Context());
			status.EffectiveDueUtc.Should().Be(EndOfDay(2026, 6, 24));
			status.State.Should().Be(LmsRequirementStates.AtRisk);
		}

		[Test]
		public void Past_due_within_grace_is_at_risk_and_after_grace_overdue_and_enforceable()
		{
			var entered = Entered(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
			var inGrace = LmsRequirementEvaluator.Evaluate(Course(dueDays: 30, grace: 30), entered, null, Context());
			inGrace.State.Should().Be(LmsRequirementStates.AtRisk);
			inGrace.Enforceable.Should().BeFalse();
			inGrace.GraceEndsUtc.Should().Be(EndOfDay(2026, 5, 31).AddDays(30));

			var past = LmsRequirementEvaluator.Evaluate(Course(dueDays: 30, grace: 7), entered, null, Context());
			past.State.Should().Be(LmsRequirementStates.Overdue);
			past.Enforceable.Should().BeTrue();
		}

		[Test]
		public void Leave_during_the_period_shifts_the_due_date_and_a_running_leave_shows_on_leave()
		{
			var entered = Entered(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
			var leave = new MemberLeavePeriod { UserId = Member, StartsOn = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc), EndsOn = new DateTime(2026, 5, 30, 0, 0, 0, DateTimeKind.Utc) };

			var status = LmsRequirementEvaluator.Evaluate(Course(dueDays: 30), entered, null, Context(leaves: new[] { leave }));
			status.EffectiveDueUtc.Should().Be(EndOfDay(2026, 5, 31).AddDays(20), "twenty days of leave pause the due date");
			status.State.Should().Be(LmsRequirementStates.AtRisk);

			var running = new MemberLeavePeriod { UserId = Member, StartsOn = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), EndsOn = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc) };
			var onLeave = LmsRequirementEvaluator.Evaluate(Course(dueDays: 30), entered, null, Context(leaves: new[] { running }));
			onLeave.State.Should().Be(LmsRequirementStates.OnLeave);
			onLeave.Enforceable.Should().BeFalse();
		}

		[Test]
		public void An_extension_moves_the_due_date_later_only()
		{
			var member = Entered(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
			member.ExtendedDueOn = EndOfDay(2026, 8, 1);
			LmsRequirementEvaluator.Evaluate(Course(), member, null, Context()).EffectiveDueUtc.Should().Be(EndOfDay(2026, 8, 1));

			member.ExtendedDueOn = EndOfDay(2026, 5, 2);
			LmsRequirementEvaluator.Evaluate(Course(), member, null, Context()).EffectiveDueUtc.Should().Be(EndOfDay(2026, 5, 31));
		}

		#endregion Due dates from audience entry

		#region Recurrence, counted requirements

		[Test]
		public void A_one_time_completion_satisfies_for_good_even_before_entry()
		{
			var status = LmsRequirementEvaluator.Evaluate(Course(), Entered(Now.AddDays(-1)), new[] { Done(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), record: 77) }, Context());
			status.State.Should().Be(LmsRequirementStates.Compliant);
			status.LastLearningRecordId.Should().Be(77);
			status.ShownDueUtc.Should().BeNull();
		}

		[Test]
		public void A_rolling_requirement_is_due_again_interval_months_after_the_last_completion()
		{
			var requirement = Course(dueDays: 30, grace: 0, atRisk: 30, recurrence: LmsRequirementRecurrences.RollingMonths, interval: 12);
			var entered = Entered(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

			var current = LmsRequirementEvaluator.Evaluate(requirement, entered, new[] { Done(new DateTime(2025, 9, 1, 10, 0, 0, DateTimeKind.Utc)) }, Context());
			current.State.Should().Be(LmsRequirementStates.Compliant);
			current.NextDueUtc.Should().Be(EndOfDay(2026, 9, 1));

			var soon = LmsRequirementEvaluator.Evaluate(requirement, entered, new[] { Done(new DateTime(2025, 7, 1, 10, 0, 0, DateTimeKind.Utc)) }, Context());
			soon.State.Should().Be(LmsRequirementStates.AtRisk, "the renewal is within the at-risk window");
			soon.Satisfied.Should().BeTrue();

			var lapsed = LmsRequirementEvaluator.Evaluate(requirement, entered, new[] { Done(new DateTime(2025, 5, 1, 10, 0, 0, DateTimeKind.Utc)) }, Context());
			lapsed.State.Should().Be(LmsRequirementStates.Overdue);
			lapsed.EffectiveDueUtc.Should().Be(EndOfDay(2026, 5, 1));
		}

		[Test]
		public void A_member_who_joins_with_an_expired_completion_still_gets_DueDays()
		{
			var requirement = Course(dueDays: 30, recurrence: LmsRequirementRecurrences.RollingMonths, interval: 12);
			var status = LmsRequirementEvaluator.Evaluate(requirement, Entered(new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc)),
				new[] { Done(new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc)) }, Context());
			status.EffectiveDueUtc.Should().Be(EndOfDay(2026, 7, 10));
			status.State.Should().Be(LmsRequirementStates.Pending);
		}

		[Test]
		public void Calendar_year_hours_are_pro_rated_for_the_joining_year_and_counted_from_the_year_start()
		{
			var hours = new LmsRequirement
			{
				LmsRequirementId = 1, DepartmentId = 7, Name = "Company training", Kind = (int)LmsRequirementKinds.CategoryHours, TargetRefId = 4, RequiredHours = 24m,
				Recurrence = (int)LmsRequirementRecurrences.CalendarYear, DueDays = 30, AtRiskDays = 14
			};
			var veteran = LmsRequirementEvaluator.Evaluate(hours, Entered(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
				new[] { Done(new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc), 30m), Done(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), 6m) },
				Context(now: new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc)));
			veteran.Required.Should().Be(24m);
			veteran.Progress.Should().Be(6m, "last year's hours do not count");
			veteran.EffectiveDueUtc.Should().Be(EndOfDay(2026, 12, 31));
			veteran.State.Should().Be(LmsRequirementStates.AtRisk, "half the year is gone and a quarter of the hours are done");

			var joiner = LmsRequirementEvaluator.Evaluate(hours, Entered(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)), null, Context(now: new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc)));
			joiner.Required.Should().Be(6.5m, "a quarter of the year is left, rounded up to half an hour");
		}

		[Test]
		public void Passed_evaluations_count_toward_N_and_satisfy_once_reached()
		{
			var evaluations = new LmsRequirement
			{
				LmsRequirementId = 1, DepartmentId = 7, Name = "Intubation", Kind = (int)LmsRequirementKinds.Evaluations, TargetCode = "INT-1", RequiredCount = 3,
				Recurrence = (int)LmsRequirementRecurrences.OneTime, DueDays = 90, AtRiskDays = 14
			};
			var entered = Entered(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
			var two = LmsRequirementEvaluator.Evaluate(evaluations, entered, new[] { Done(Now.AddDays(-3)), Done(Now.AddDays(-2)) }, Context());
			two.Progress.Should().Be(2m);
			two.Satisfied.Should().BeFalse();
			var three = LmsRequirementEvaluator.Evaluate(evaluations, entered, new[] { Done(Now.AddDays(-3)), Done(Now.AddDays(-2)), Done(Now.AddDays(-1)) }, Context());
			three.State.Should().Be(LmsRequirementStates.Compliant);
		}

		#endregion Recurrence, counted requirements

		#region Licensing, exemption and the lapse rules

		[Test]
		public void A_member_without_a_licensed_seat_is_not_licensed_and_never_overdue_or_enforceable()
		{
			var status = LmsRequirementEvaluator.Evaluate(Course(dueDays: 1), Entered(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)), null, Context(licensed: false));
			status.State.Should().Be(LmsRequirementStates.NotLicensed);
			status.Enforceable.Should().BeFalse();
		}

		[Test]
		public void An_exempt_member_shows_exempt_never_complete_until_the_exemption_ends()
		{
			var member = Entered(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
			member.ExemptOn = Now.AddDays(-5);
			member.ExemptUntil = Now.AddDays(5);
			LmsRequirementEvaluator.Evaluate(Course(), member, null, Context()).State.Should().Be(LmsRequirementStates.Exempt);
			LmsRequirementEvaluator.Evaluate(Course(), member, null, Context(now: Now.AddDays(6))).State.Should().Be(LmsRequirementStates.Overdue);
		}

		[Test]
		public void While_the_add_on_is_lapsed_nothing_new_becomes_overdue_and_nothing_is_enforceable()
		{
			// Due at the end of June 20; the add-on lapsed on June 1, so as of the lapse the member was not even at risk... and stays so.
			var member = Entered(new DateTime(2026, 5, 21, 0, 0, 0, DateTimeKind.Utc));
			var paused = LmsRequirementEvaluator.Evaluate(Course(dueDays: 30, atRisk: 0), member, null,
				Context(now: new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), openLapse: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)));
			paused.State.Should().Be(LmsRequirementStates.Pending);
			paused.Enforceable.Should().BeFalse();
		}

		[Test]
		public void After_reactivation_due_dates_shift_by_the_lapse_and_are_held_through_the_grace()
		{
			var member = Entered(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
			var lapse = new LmsAddonLapse { DepartmentId = 7, StartedOn = new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc), EndedOn = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc) };

			var status = LmsRequirementEvaluator.Evaluate(Course(dueDays: 30, atRisk: 0), member, null, Context(lapses: new[] { lapse }));
			status.HoldUntilUtc.Should().Be(new DateTime(2026, 6, 24, 0, 0, 0, DateTimeKind.Utc));
			status.EffectiveDueUtc.Should().Be(new DateTime(2026, 6, 24, 0, 0, 0, DateTimeKind.Utc), "21 days of lapse shift June 1 to June 21, inside the grace, so it moves to the grace end");
			status.State.Should().NotBe(LmsRequirementStates.Overdue);
			status.Enforceable.Should().BeFalse();
		}

		#endregion Licensing, exemption and the lapse rules

		#region Blast-radius guard (R-L11)

		private static LmsRoleImpact Loses(int roleId, string user) => new LmsRoleImpact { PersonnelRoleId = roleId, RoleName = "Engineer", UserId = user };

		[Test]
		public void A_run_that_would_remove_more_than_the_share_of_a_role_is_refused_and_one_at_the_share_is_not()
		{
			var holders = new Dictionary<int, int> { [5] = 10, [6] = 10 };
			LmsEnforcementGuard.Check(new[] { Loses(5, "a"), Loses(5, "b") }, holders, 20).Should().BeEmpty("2 of 10 is exactly 20%");
			var breach = LmsEnforcementGuard.Check(new[] { Loses(5, "a"), Loses(5, "b"), Loses(5, "c"), Loses(6, "a") }, holders, 20).Single();
			breach.PersonnelRoleId.Should().Be(5);
			breach.Losing.Should().Be(3);
			breach.SharePercent.Should().Be(30m);
			LmsEnforcementGuard.Check(new[] { Loses(5, "a"), Loses(5, "A") }, new Dictionary<int, int> { [5] = 10 }, 10).Should().BeEmpty("the same member counts once");
		}

		[Test]
		public void Roles_lost_follow_the_certification_rule_an_any_of_alternative_keeps_the_role()
		{
			var types = new Dictionary<int, DepartmentCertificationType>
			{
				[1] = new DepartmentCertificationType { DepartmentCertificationTypeId = 1, DepartmentId = 7, Type = "SCBA", DefaultValidityMonths = 12 },
				[2] = new DepartmentCertificationType { DepartmentCertificationTypeId = 2, DepartmentId = 7, Type = "SCBA (alt)", DefaultValidityMonths = 12 }
			};
			var requirements = new List<PersonnelRoleCertificationRequirement>
			{
				new PersonnelRoleCertificationRequirement { PersonnelRoleId = 5, DepartmentId = 7, DepartmentCertificationTypeId = 1, IsMandatory = true },
				new PersonnelRoleCertificationRequirement { PersonnelRoleId = 6, DepartmentId = 7, DepartmentCertificationTypeId = 1, IsMandatory = true, AnyOfGroup = 1 },
				new PersonnelRoleCertificationRequirement { PersonnelRoleId = 6, DepartmentId = 7, DepartmentCertificationTypeId = 2, IsMandatory = true, AnyOfGroup = 1 }
			};
			var records = new List<PersonnelCertification>
			{
				new PersonnelCertification { PersonnelCertificationId = 11, DepartmentId = 7, UserId = Member, DepartmentCertificationTypeId = 1, Status = 0, ExpiresOn = Now.AddMonths(6) },
				new PersonnelCertification { PersonnelCertificationId = 12, DepartmentId = 7, UserId = Member, DepartmentCertificationTypeId = 2, Status = 0, ExpiresOn = Now.AddMonths(6) }
			};

			var lost = LmsEnforcementGuard.RolesLost(Member, new[] { 5, 6, 9 }, requirements, records, types, new DepartmentCertificationSettings(), 11, Now);
			lost.Should().Equal(new[] { 5 }, "role 6 is still met by the alternative, role 9 asks for nothing");
		}

		#endregion Blast-radius guard (R-L11)

		#region Learning paths

		private static LmsPathStep Step(int order, int target, string needs = null, int? elective = null, int electiveRequired = 0, LmsPathStepKinds kind = LmsPathStepKinds.Course) =>
			new LmsPathStep { SortOrder = order, Kind = (int)kind, TargetRefId = target, PrerequisiteOrders = needs, ElectiveGroup = elective, ElectiveRequired = electiveRequired };

		[Test]
		public void A_step_unlocks_when_its_prerequisites_are_done_and_the_path_completes_with_enough_electives()
		{
			var steps = new[] { Step(1, 100), Step(2, 101, "1"), Step(3, 102, "1", 1, 1), Step(4, 103, "1", 1, 1), Step(5, 7, "2", kind: LmsPathStepKinds.Training) };
			var none = LmsPathRules.Compute(steps, new Dictionary<(int, int), DateTime>());
			none.Steps.Select(s => s.State).Should().Equal(LmsPathStepStates.Available, LmsPathStepStates.Locked, LmsPathStepStates.Locked, LmsPathStepStates.Locked, LmsPathStepStates.Locked);
			none.Needed.Should().Be(4, "three required steps and one elective of two");

			var first = new Dictionary<(int, int), DateTime> { [(0, 100)] = Now.AddDays(-10) };
			LmsPathRules.Compute(steps, first).Steps.Select(s => s.State).Should().Equal(LmsPathStepStates.Done, LmsPathStepStates.Available, LmsPathStepStates.Available,
				LmsPathStepStates.Available, LmsPathStepStates.Locked);

			var all = new Dictionary<(int, int), DateTime> { [(0, 100)] = Now.AddDays(-10), [(0, 101)] = Now.AddDays(-8), [(0, 103)] = Now.AddDays(-6), [(1, 7)] = Now.AddDays(-2) };
			var complete = LmsPathRules.Compute(steps, all);
			complete.Complete.Should().BeTrue("one elective of the group is enough");
			complete.CompletedOn.Should().Be(Now.AddDays(-2));
			complete.Percent.Should().Be(100);
		}

		[Test]
		public void A_path_validates_its_order_prerequisites_and_electives()
		{
			LmsPathRules.Validate(new List<LmsPathStep>()).Should().Be("PathNoSteps");
			LmsPathRules.Validate(new[] { Step(1, 100), Step(2, 101, "2") }).Should().Be("PathPrerequisiteNotEarlier", "a step cannot need itself or a later step (no cycles)");
			LmsPathRules.Validate(new[] { Step(1, 100), Step(2, 100) }).Should().Be("PathDuplicateStep");
			LmsPathRules.Validate(new[] { Step(1, 100, null, 1, 3), Step(2, 101, null, 1, 3) }).Should().Be("PathElectiveCount");
			LmsPathRules.Validate(new[] { Step(1, 100), Step(2, 101, "1") }).Should().BeNull();
		}

		[Test]
		public void An_Academy_role_path_becomes_steps_in_manifest_order_each_needing_the_first()
		{
			var rolePath = AcademyCourseManifest.FindPath("dispatcher");
			var id = 500;
			var steps = LmsPathRules.AcademySteps(rolePath, key => new LmsCourse { LmsCourseId = ++id, SystemKey = key, DepartmentId = 0 });
			steps.Should().HaveCount(rolePath.LessonIds.Count);
			steps.Select(s => s.TargetSystemKey).Should().Equal(rolePath.LessonIds.Select(AcademyCourseManifest.SystemKeyFor));
			steps.First().PrerequisiteOrders.Should().BeNull();
			steps.Skip(1).Should().OnlyContain(s => s.PrerequisiteOrders == "1");
		}

		#endregion Learning paths
	}
}
