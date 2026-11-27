using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Services;

namespace Resgrid.Tests.TrainingT4
{
	/// <summary>
	/// Training T4 part 1 (round 5B) pure rules: enrolment (open / closed, cutoff, groups, approval, capacity, waitlist, self-unenrol),
	/// waitlist order and the inactive-member rule, visibility, rotating-QR (an old code is refused) and optional GPS, hours, retention,
	/// the sign-off drafts (none for the absent), the shift-calendar conflict flag, series, cost roll-ups and the meeting report parser.
	/// </summary>
	[TestFixture]
	public class LmsClassRulesTests
	{
		private static readonly DateTime Now = new DateTime(2027, 3, 1, 12, 0, 0, DateTimeKind.Utc);

		private static LmsClassSession Session(int capacity = 2, bool waitlist = true, bool open = true, bool approval = false, int cutoff = 0, string restrict = null,
			string visible = null) => new LmsClassSession
		{
			LmsClassSessionId = 10,
			DepartmentId = 7,
			CalendarItemId = 500,
			Title = "Bloodborne pathogens refresher",
			StartsOn = Now.AddDays(10),
			EndsOn = Now.AddDays(10).AddHours(2),
			Capacity = capacity,
			WaitlistEnabled = waitlist,
			EnrollmentOpen = open,
			RequiresApproval = approval,
			CutoffDays = cutoff,
			RestrictGroupIds = restrict,
			VisibleGroupIds = visible,
			QrEnabled = true,
			QrSecret = LmsCrewCodes.NewSecret(),
			AllowSelfUnenroll = true
		};

		private static LmsClassEnrollment Row(int id, string user, LmsClassEnrollmentStatuses status, int? position = null, LmsClassAttendanceStates attendance = LmsClassAttendanceStates.Unmarked) =>
			new LmsClassEnrollment { LmsClassEnrollmentId = id, LmsClassSessionId = 10, DepartmentId = 7, UserId = user, Status = (int)status, WaitlistPosition = position, Attendance = (int)attendance, EnrolledOn = Now.AddMinutes(id) };

		[Test]
		public void Self_enrolment_follows_open_cutoff_group_and_visibility_rules()
		{
			LmsClassRules.SelfEnrollRefusal(Session(), Now, null).Should().BeNull();
			LmsClassRules.SelfEnrollRefusal(Session(open: false), Now, null).Should().Be("lms_class_enrollment_closed");
			LmsClassRules.SelfEnrollRefusal(Session(cutoff: 14), Now, null).Should().Be("lms_class_enrollment_cutoff", "the class starts in 10 days and closes 14 days before");
			LmsClassRules.SelfEnrollRefusal(Session(cutoff: 7), Now, null).Should().BeNull();
			LmsClassRules.SelfEnrollRefusal(Session(restrict: "3,4"), Now, 5).Should().Be("lms_class_not_eligible");
			LmsClassRules.SelfEnrollRefusal(Session(restrict: "3,4"), Now, 4).Should().BeNull();
			LmsClassRules.SelfEnrollRefusal(Session(restrict: "3"), Now, null).Should().Be("lms_class_not_eligible", "a member with no group is not in a restricted group");
			LmsClassRules.SelfEnrollRefusal(Session(visible: "9"), Now, 4).Should().Be("lms_class_not_eligible");
			LmsClassRules.IsVisibleTo(Session(visible: "9"), 4, true).Should().BeTrue("class managers see every session");

			var past = Session();
			past.StartsOn = Now.AddMinutes(-1);
			LmsClassRules.SelfEnrollRefusal(past, Now, null).Should().Be("lms_class_not_open");
			var cancelled = Session();
			cancelled.Status = (int)LmsClassSessionStatuses.Cancelled;
			LmsClassRules.SelfEnrollRefusal(cancelled, Now, null).Should().Be("lms_class_not_open");
		}

		[Test]
		public void Placement_puts_members_in_a_seat_on_the_waitlist_pending_or_refuses()
		{
			LmsClassRules.Placement(Session(capacity: 2), 1, false).Should().Be(LmsClassEnrollmentStatuses.Enrolled);
			LmsClassRules.Placement(Session(capacity: 2), 2, false).Should().Be(LmsClassEnrollmentStatuses.Waitlisted);
			LmsClassRules.Placement(Session(capacity: 2, waitlist: false), 2, false).Should().BeNull("full without a waitlist is refused");
			LmsClassRules.Placement(Session(capacity: 2, waitlist: false), 2, true).Should().Be(LmsClassEnrollmentStatuses.Enrolled, "a class manager may go over capacity");
			LmsClassRules.Placement(Session(capacity: 0), 500, false).Should().Be(LmsClassEnrollmentStatuses.Enrolled, "0 is no limit");
			LmsClassRules.Placement(Session(approval: true), 0, false).Should().Be(LmsClassEnrollmentStatuses.PendingApproval);
			LmsClassRules.Placement(Session(approval: true), 0, true).Should().Be(LmsClassEnrollmentStatuses.Enrolled, "a manager's enrolment needs no approval");
		}

		[Test]
		public void The_waitlist_is_promoted_in_order_into_free_seats_and_inactive_members_are_passed_over()
		{
			var roster = new List<LmsClassEnrollment>
			{
				Row(1, "a", LmsClassEnrollmentStatuses.Enrolled),
				Row(2, "gone", LmsClassEnrollmentStatuses.Waitlisted, 1),
				Row(3, "c", LmsClassEnrollmentStatuses.Waitlisted, 3),
				Row(4, "b", LmsClassEnrollmentStatuses.Waitlisted, 2)
			};
			var active = new HashSet<string>(new[] { "a", "b", "c" }, StringComparer.OrdinalIgnoreCase);

			LmsClassRules.PromotionOrder(Session(capacity: 2), roster, active.Contains, Now).Select(e => e.UserId).Should().Equal("b");
			LmsClassRules.PromotionOrder(Session(capacity: 3), roster, active.Contains, Now).Select(e => e.UserId).Should().Equal("b", "c");
			LmsClassRules.PromotionOrder(Session(capacity: 1), roster, active.Contains, Now).Should().BeEmpty("no seat is free");
			LmsClassRules.PromotionOrder(Session(capacity: 3), roster, active.Contains, Now.AddDays(11)).Should().BeEmpty("never after the start");
			LmsClassRules.NextWaitlistPosition(roster).Should().Be(4);
		}

		[Test]
		public void A_member_may_leave_the_waitlist_but_needs_self_unenrol_to_give_up_a_seat()
		{
			var session = Session();
			session.AllowSelfUnenroll = false;
			LmsClassRules.SelfUnenrollRefusal(session, Row(1, "a", LmsClassEnrollmentStatuses.Enrolled), Now).Should().Be("lms_class_unenroll_closed");
			LmsClassRules.SelfUnenrollRefusal(session, Row(2, "b", LmsClassEnrollmentStatuses.Waitlisted, 1), Now).Should().BeNull();
			LmsClassRules.SelfUnenrollRefusal(Session(), Row(1, "a", LmsClassEnrollmentStatuses.Enrolled), Now).Should().BeNull();
			LmsClassRules.SelfUnenrollRefusal(Session(), Row(1, "a", LmsClassEnrollmentStatuses.Withdrawn), Now).Should().Be("lms_class_not_enrolled");
		}

		[Test]
		public void The_rotating_code_refuses_an_old_code_and_a_code_from_another_secret()
		{
			var session = Session();
			session.StartsOn = Now.AddMinutes(30);
			session.EndsOn = Now.AddHours(2);

			var current = LmsClassRules.CurrentCode(session, Now);
			current.Should().NotBeNullOrEmpty();
			LmsClassRules.SignInRefusal(session, current, Now, null, null).Should().BeNull();
			LmsClassRules.SignInRefusal(session, current, Now.AddSeconds(LmsCrewCodes.SliceSeconds), null, null).Should().BeNull("the previous slice is still accepted");
			LmsClassRules.SignInRefusal(session, current, Now.AddSeconds(LmsCrewCodes.SliceSeconds * 3), null, null).Should().Be("lms_class_code_expired",
				"a photo of the QR taken a minute and a half ago no longer works");
			LmsClassRules.SignInRefusal(session, LmsCrewCodes.CurrentCode(LmsCrewCodes.NewSecret(), Now), Now, null, null).Should().Be("lms_class_code_expired");

			var rotated = Session();
			rotated.StartsOn = session.StartsOn;
			rotated.EndsOn = session.EndsOn;
			LmsClassRules.SignInRefusal(rotated, current, Now, null, null).Should().Be("lms_class_code_expired", "a new secret retires every earlier code");
		}

		[Test]
		public void Sign_in_opens_an_hour_before_and_closes_two_hours_after()
		{
			var session = Session();
			LmsClassRules.IsSignInOpen(session, session.StartsOn.AddHours(-2)).Should().BeFalse();
			LmsClassRules.IsSignInOpen(session, session.StartsOn.AddMinutes(-30)).Should().BeTrue();
			LmsClassRules.IsSignInOpen(session, session.EndsOn.AddHours(3)).Should().BeFalse();
			session.QrEnabled = false;
			LmsClassRules.CurrentCode(session, session.StartsOn).Should().BeNull();
		}

		[Test]
		public void Gps_is_optional_unless_the_session_requires_it_and_then_the_radius_counts()
		{
			var session = Session();
			session.StartsOn = Now.AddMinutes(10);
			session.EndsOn = Now.AddHours(2);
			var code = LmsClassRules.CurrentCode(session, Now);

			LmsClassRules.SignInRefusal(session, code, Now, null, null).Should().BeNull("GPS is optional");

			session.GpsRequired = true;
			session.GpsLatitude = 39.7392m;
			session.GpsLongitude = -104.9903m;
			session.GpsRadiusMeters = 200;
			LmsClassRules.SignInRefusal(session, code, Now, null, null).Should().Be("lms_class_gps_required");
			LmsClassRules.SignInRefusal(session, code, Now, 39.7395m, -104.9900m).Should().BeNull("about 40 m away");
			LmsClassRules.SignInRefusal(session, code, Now, 39.7500m, -104.9903m).Should().Be("lms_class_gps_too_far", "about 1.2 km away");
			LmsClassRules.DistanceMeters(39.7392m, -104.9903m, 39.7500m, -104.9903m).Should().BeApproximately(1201, 10);
		}

		[Test]
		public void Hours_come_from_the_override_then_check_in_and_out_then_the_meeting_report_then_the_session()
		{
			var session = Session();
			var settings = new TrainingRecordSettings { HoursRoundingMinutes = 15 };
			var row = Row(1, "a", LmsClassEnrollmentStatuses.Enrolled, attendance: LmsClassAttendanceStates.Attended);

			LmsClassRules.HoursFor(session, row, null, settings).Should().Be(2m, "no times: the session's length");
			row.ImportedMinutes = 95;
			LmsClassRules.HoursFor(session, row, null, settings).Should().Be(1.5m, "95 minutes rounded to the nearest 15");
			row.CheckInOn = session.StartsOn.AddMinutes(10);
			row.CheckOutOn = session.StartsOn.AddMinutes(100);
			LmsClassRules.HoursFor(session, row, null, settings).Should().Be(1.5m);
			row.CheckOutOn = null;
			row.ImportedMinutes = null;
			LmsClassRules.HoursFor(session, row, null, settings).Should().Be(1.75m, "a check-in without a check-out counts to the end");
			row.HoursOverride = 3.25m;
			LmsClassRules.HoursFor(session, row, null, settings).Should().Be(3.25m);

			var calendar = new CalendarItemCheckIn { CheckInTime = session.StartsOn, CheckOutTime = session.StartsOn.AddMinutes(45) };
			LmsClassRules.HoursFor(session, Row(2, "b", LmsClassEnrollmentStatuses.Enrolled), calendar, new TrainingRecordSettings()).Should().Be(0.75m, "a calendar check-in counts when there is no QR one");
		}

		[Test]
		public void Sign_off_drafts_one_record_per_attendee_and_member_instructor_and_none_for_the_absent()
		{
			var session = Session();
			session.CategoryId = 4;
			session.Tags = "osha";
			var roster = new List<LmsClassEnrollment>
			{
				Row(1, "a", LmsClassEnrollmentStatuses.Enrolled, attendance: LmsClassAttendanceStates.Attended),
				Row(2, "b", LmsClassEnrollmentStatuses.Enrolled, attendance: LmsClassAttendanceStates.Absent),
				Row(3, "c", LmsClassEnrollmentStatuses.Enrolled, attendance: LmsClassAttendanceStates.Excused),
				Row(4, "d", LmsClassEnrollmentStatuses.Waitlisted, 1, LmsClassAttendanceStates.Attended),
				Row(5, "e", LmsClassEnrollmentStatuses.Enrolled, attendance: LmsClassAttendanceStates.Attended)
			};
			var instructors = new List<LmsClassInstructor>
			{
				new LmsClassInstructor { LmsClassInstructorId = 1, UserId = "lead", Name = "Lead Instructor", Qualifications = "Instructor I" },
				new LmsClassInstructor { LmsClassInstructorId = 2, Name = "Outside Nurse", Qualifications = "RN" }
			};

			var drafts = LmsClassRules.SignOffDrafts(session, roster, new Dictionary<int, decimal> { { 1, 2m }, { 5, 1.5m } }, instructors, new[] { "Explain exposure control" }, "lead");

			drafts.Where(d => d.ComponentKey == string.Empty).Select(d => d.UserId).Should().BeEquivalentTo("a", "e");
			drafts.Should().NotContain(d => d.UserId == "b" || d.UserId == "c" || d.UserId == "d", "the absent, the excused and the waitlisted earn nothing");
			drafts.Single(d => d.UserId == "a").Hours.Should().Be(2m);
			drafts.Single(d => d.UserId == "a").SessionKey.Should().Be(LearningRecordRules.CalendarSession(500), "one class counted once with its calendar item (R-T2)");
			drafts.Single(d => d.UserId == "a").InstructorNames.Should().Be("Lead Instructor, Outside Nurse");
			drafts.Single(d => d.UserId == "a").Objectives.Should().Be("Explain exposure control");
			drafts.Single(d => d.UserId == "a").CategoryId.Should().Be(4);
			var teacher = drafts.Single(d => d.ComponentKey == LmsClassRules.InstructorComponent);
			teacher.UserId.Should().Be("lead");
			teacher.Hours.Should().Be(2m, "hours taught default to the session's length");
			drafts.Should().HaveCount(3, "the outside instructor has no transcript");
			LmsClassRules.SignOffDrafts(session, roster, new Dictionary<int, decimal> { { 1, 2m }, { 5, 1.5m } }, instructors, new[] { "Explain exposure control" }, "lead")
				.Select(d => (d.UserId, d.ComponentKey, d.Hours)).Should().Equal(drafts.Select(d => (d.UserId, d.ComponentKey, d.Hours)), "the same roster drafts the same records");
		}

		[Test]
		public void A_roster_cannot_be_purged_before_its_retention()
		{
			var session = Session();
			session.RetainUntil = LmsClassRules.RetainUntil(session.EndsOn, 3);
			session.RetainUntil.Should().Be(session.EndsOn.AddYears(3), "OSHA 1910.1030: 3 years");
			LmsClassRules.CanPurge(session, Now.AddYears(5)).Should().BeFalse("only a deleted session is purged");
			session.IsDeleted = true;
			LmsClassRules.CanPurge(session, Now.AddYears(2)).Should().BeFalse("the retention holds");
			LmsClassRules.CanPurge(session, session.EndsOn.AddYears(3).AddDays(1)).Should().BeTrue();
			LmsClassRules.RetainUntil(session.EndsOn, null).Should().BeNull();
		}

		[Test]
		public void The_shift_conflict_flag_marks_enrolled_members_on_an_overlapping_shift()
		{
			var shift = new Shift { ShiftId = 1, StartTime = "07:00", EndTime = "19:00" };
			var day = new ShiftDay { ShiftDayId = 1, ShiftId = 1, Day = new DateTime(2027, 3, 11), Shift = shift };
			var schedule = new ShiftDaySchedule
			{
				Day = day,
				Shift = shift,
				Roster = new List<ShiftDayRosterEntry>
				{
					new ShiftDayRosterEntry { UserId = "A" },
					new ShiftDayRosterEntry { UserId = "pending", ApprovalPending = true },
					new ShiftDayRosterEntry { UserId = "other" }
				}
			};

			var conflicts = LmsClassRules.ShiftConflicts(new DateTime(2027, 3, 11, 9, 0, 0), new DateTime(2027, 3, 11, 11, 0, 0), new[] { schedule }, new[] { "a", "pending", "b" });
			conflicts.Should().BeEquivalentTo(new[] { "A" });
			LmsClassRules.ShiftConflicts(new DateTime(2027, 3, 11, 20, 0, 0), new DateTime(2027, 3, 11, 22, 0, 0), new[] { schedule }, new[] { "a" }).Should().BeEmpty();
		}

		[Test]
		public void A_weekly_series_repeats_the_same_hours()
		{
			var start = new DateTime(2027, 3, 2, 19, 0, 0);
			var occurrences = LmsClassRules.Occurrences(start, start.AddHours(2), LmsClassRepeatKinds.Weekly, 4);
			occurrences.Should().HaveCount(4);
			occurrences[3].Start.Should().Be(start.AddDays(21));
			occurrences[3].End.Should().Be(start.AddDays(21).AddHours(2));
			LmsClassRules.Occurrences(start, start.AddHours(2), LmsClassRepeatKinds.None, 9).Should().HaveCount(1);
		}

		[Test]
		public void Class_costs_roll_up_per_course_member_and_year_beside_training_request_costs()
		{
			var s1 = Session();
			s1.LmsClassSessionId = 1;
			s1.CourseCode = "BBP";
			var s2 = Session();
			s2.LmsClassSessionId = 2;
			s2.CourseCode = "bbp";
			var lines = new List<LmsClassCostLine>
			{
				new LmsClassCostLine { LmsClassSessionId = 1, Amount = 300m, Kind = (int)LmsClassCostKinds.Instructor },
				new LmsClassCostLine { LmsClassSessionId = 1, Amount = 100m, Kind = (int)LmsClassCostKinds.Materials },
				new LmsClassCostLine { LmsClassSessionId = 2, Amount = 50m, Kind = (int)LmsClassCostKinds.BackfillOvertime }
			};
			var attendees = new List<LmsClassEnrollment>
			{
				new LmsClassEnrollment { LmsClassSessionId = 1, UserId = "a", Status = (int)LmsClassEnrollmentStatuses.Enrolled, Attendance = (int)LmsClassAttendanceStates.Attended, Hours = 2m },
				new LmsClassEnrollment { LmsClassSessionId = 1, UserId = "b", Status = (int)LmsClassEnrollmentStatuses.Enrolled, Attendance = (int)LmsClassAttendanceStates.Attended, Hours = 2m },
				new LmsClassEnrollment { LmsClassSessionId = 1, UserId = "c", Status = (int)LmsClassEnrollmentStatuses.Enrolled, Attendance = (int)LmsClassAttendanceStates.Absent },
				new LmsClassEnrollment { LmsClassSessionId = 2, UserId = "a", Status = (int)LmsClassEnrollmentStatuses.Enrolled, Attendance = (int)LmsClassAttendanceStates.Attended, Hours = 1m }
			};
			var requests = new[] { new LmsTrainingRequestCostRow { Key = "BBP", Committed = 450m }, new LmsTrainingRequestCostRow { Key = "A", Committed = 75m } };

			var byCourse = LmsClassCostMath.Rollup(new[] { s1, s2 }, lines, attendees, LmsClassCostGroupings.Course, requests);
			var bbp = byCourse.Single(r => r.Key == "BBP");
			bbp.ClassCost.Should().Be(450m);
			bbp.Sessions.Should().Be(2);
			bbp.Attendees.Should().Be(3);
			bbp.Hours.Should().Be(5m);
			bbp.RequestCost.Should().Be(450m);

			var byMember = LmsClassCostMath.Rollup(new[] { s1, s2 }, lines, attendees, LmsClassCostGroupings.Member, requests);
			byMember.Single(r => r.Key == "A").ClassCost.Should().Be(250m, "half of session 1 and all of session 2");
			byMember.Single(r => r.Key == "A").RequestCost.Should().Be(75m);
			byMember.Single(r => r.Key == "B").ClassCost.Should().Be(200m);
			byMember.Should().NotContain(r => r.Key == "C", "the absent share no cost");

			var cancelled = Session();
			cancelled.LmsClassSessionId = 3;
			cancelled.Status = (int)LmsClassSessionStatuses.Cancelled;
			LmsClassCostMath.Rollup(new[] { s1, cancelled }, lines.Append(new LmsClassCostLine { LmsClassSessionId = 3, Amount = 999m }), attendees, LmsClassCostGroupings.Year, null)
				.Single().ClassCost.Should().Be(400m, "a cancelled class costs nothing in the roll-up");
		}

		[Test]
		public void Lesson_plan_content_is_normalized_and_round_trips_as_json()
		{
			var content = new LmsLessonPlanContent { Objectives = { "  Advance a line ", "", "Search" }, StudentsPerInstructor = -3, CompetencyItemIds = { 4, 4, 0, 7 } };
			var parsed = LmsLessonPlanContent.Parse(content.ToJson());
			parsed.Objectives.Should().Equal("Advance a line", "Search");
			parsed.StudentsPerInstructor.Should().Be(0);
			parsed.CompetencyItemIds.Should().Equal(4, 7);
			LmsLessonPlanContent.Parse("not json").Objectives.Should().BeEmpty();
			LmsLessonPlanStarterCatalog.All.Select(s => s.Key).Should().OnlyHaveUniqueItems().And.Contain(new[] { "live-fire", "evoc", "scba", "hose-evolutions", "ladder-evolutions", "pump-evolutions", "physical-ability" });
			LmsLessonPlanStarterCatalog.All.Should().OnlyContain(s => s.Content.Objectives.Count > 0 && s.Content.PreBriefChecklist.Count > 0 && !string.IsNullOrWhiteSpace(s.Content.SafetyPlan));
			LmsLessonPlanStarterCatalog.All.SelectMany(s => s.Content.PreBriefChecklist.Concat(s.Content.Objectives)).Should().NotContain(t => t.Contains("NFPA") || t.Contains("shall"),
				"starter plans are Resgrid's own plain-language checklists, never a standard's text");
		}

		[Test]
		public void Teams_zoom_and_meet_attendance_reports_are_read()
		{
			var teams = "1. Summary\tMeeting title\tBBP refresher\n\n2. Participants\nName\tFirst Join\tLast Leave\tIn-Meeting Duration\tEmail\tRole\n" +
				"Jane Doe\t3/1/27, 9:00:01 AM\t3/1/27, 10:05:00 AM\t1h 5m\tjane@example.org\tAttendee\nJohn Roe\t3/1/27, 9:10:00 AM\t3/1/27, 9:40:00 AM\t30m 10s\tjohn@example.org\tAttendee\n\n3. In-Meeting Activities\n";
			var rows = LmsAttendanceImport.Parse(teams);
			rows.Should().HaveCount(2);
			rows[0].Email.Should().Be("jane@example.org");
			rows[0].Minutes.Should().Be(65);
			rows[1].Minutes.Should().Be(30);

			var zoom = "Name (Original Name),User Email,Join Time,Leave Time,Duration (Minutes),Guest\n" +
				"Janie (Jane Doe),jane@example.org,03/01/2027 09:00:00 AM,03/01/2027 09:30:00 AM,30,No\n" +
				"Janie (Jane Doe),jane@example.org,03/01/2027 09:35:00 AM,03/01/2027 10:00:00 AM,25,No\n" +
				"\"Roe, John\",,03/01/2027 09:00:00 AM,03/01/2027 09:45:00 AM,45,Yes\n";
			rows = LmsAttendanceImport.Parse(zoom);
			rows.Should().HaveCount(2);
			rows.Single(r => r.Email == "jane@example.org").Minutes.Should().Be(55, "a rejoin adds up");
			rows.Single(r => r.Email == null).Name.Should().Be("Roe, John");

			var meet = "First name,Last name,Email,Duration,Time joined,Time exited\nJane,Doe,jane@example.org,1 hr 5 min,9:00 AM,10:05 AM\n";
			rows = LmsAttendanceImport.Parse(meet);
			rows.Single().Name.Should().Be("Jane Doe");
			rows.Single().Minutes.Should().Be(65);

			LmsAttendanceImport.ParseMinutes("01:30:00").Should().Be(90);
			LmsAttendanceImport.Parse("nothing useful here").Should().BeEmpty();
		}
	}
}
