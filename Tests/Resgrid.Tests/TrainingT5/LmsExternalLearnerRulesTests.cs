using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>Training T5 external learners: the fair-use rule (§17.4), credentials and tokens, branding fallback, public class rules, the roster export and invoice grouping.</summary>
	[TestFixture]
	public class LmsExternalLearnerRulesTests
	{
		private static LmsFairUse Use(int packs, int members, int learners, bool active = true) => new LmsFairUse
		{
			AddonActive = active,
			PurchasedSeats = packs * 25,
			EnabledMemberSeats = members,
			CountedLearners = learners,
			Allowance = LmsExternalLearnerRules.Allowance(packs * 25)
		};

		[TestCase(0, 25)]
		[TestCase(25, 25)]
		[TestCase(100, 25)]
		[TestCase(200, 50)]
		[TestCase(1000, 250)]
		[TestCase(110, 27)]
		public void The_allowance_is_a_quarter_of_purchased_seats_with_a_minimum_of_25(int seats, int allowance) =>
			LmsExternalLearnerRules.Allowance(seats).Should().Be(allowance);

		[Test]
		public void Within_the_allowance_learners_are_free_even_when_member_seats_are_full()
		{
			var decision = LmsExternalLearnerRules.Decide(Use(1, 25, 24));
			decision.Allowed.Should().BeTrue();
			decision.CountsInPacks.Should().BeFalse();
		}

		[Test]
		public void Beyond_the_allowance_a_learner_takes_a_free_seat_like_a_member()
		{
			var decision = LmsExternalLearnerRules.Decide(Use(1, 20, 25));
			decision.Allowed.Should().BeTrue("five seats are free for the 26th learner");
			decision.CountsInPacks.Should().BeTrue();
			Use(1, 20, 27).Overflow.Should().Be(2);
			Use(1, 20, 27).FreeSeats.Should().Be(3);
		}

		[Test]
		public void Beyond_the_allowance_with_no_seat_left_the_refusal_names_the_packs_to_add()
		{
			var decision = LmsExternalLearnerRules.Decide(Use(1, 25, 25));
			decision.Allowed.Should().BeFalse();
			decision.Refusal.Should().Be("lms_xl_seats_full");
			decision.PacksNeeded.Should().Be(1);
		}

		[Test]
		public void Without_a_working_add_on_no_external_learner_is_added()
		{
			var decision = LmsExternalLearnerRules.Decide(Use(4, 0, 0, active: false));
			decision.Allowed.Should().BeFalse();
			decision.Refusal.Should().Be("lms_xl_addon_inactive");
			LmsExternalLearnerRules.Decide(null).Allowed.Should().BeFalse();
		}

		[Test]
		public void The_minimum_packs_seat_members_and_the_learners_beyond_fair_use()
		{
			LmsExternalLearnerRules.MinimumPacks(51, 0, 25, 1).Should().Be(3);
			LmsExternalLearnerRules.MinimumPacks(22, 28, 25, 1).Should().Be(1, "3 beyond the 25 allowance fit beside 22 members");
			LmsExternalLearnerRules.MinimumPacks(25, 30, 25, 1).Should().Be(2, "5 beyond the allowance need a second pack");
			LmsExternalLearnerRules.MinimumPacks(0, 0, 25, 1).Should().Be(1, "the floor");
			LmsExternalLearnerRules.SeatsTaken(30, 25).Should().Be(5);
			LmsExternalLearnerRules.SeatsTaken(10, 25).Should().Be(0);
		}

		[Test]
		public void Learner_ids_carry_the_prefix_the_ledger_filters_on_and_member_ids_never_do()
		{
			LmsExternalLearnerIds.Prefix.Should().Be(LearningRecordQuery.ExternalLearnerUserIdPrefix);
			var id = LmsExternalLearnerIds.New();
			LmsExternalLearnerIds.IsExternal(id).Should().BeTrue();
			LmsExternalLearnerIds.IsExternal(id.ToUpperInvariant()).Should().BeTrue();
			LmsExternalLearnerIds.IsExternal(Guid.NewGuid().ToString()).Should().BeFalse();
			LmsExternalLearnerIds.IsExternal(null).Should().BeFalse();
			id.Length.Should().BeLessThan(64);
		}

		[TestCase("casey@example.org", true)]
		[TestCase(" Casey@Example.ORG ", true)]
		[TestCase("casey", false)]
		[TestCase("@example.org", false)]
		[TestCase("a b@example.org", false)]
		[TestCase("", false)]
		public void Email_addresses_are_validated_and_normalized(string email, bool valid)
		{
			LmsExternalLearnerRules.IsValidEmail(email).Should().Be(valid);
			if (valid)
				LmsExternalLearnerRules.NormalizeEmail(email).Should().Be("casey@example.org");
		}

		[TestCase("Short1", "lms_xl_password_length")]
		[TestCase("alllowercase1", "lms_xl_password_weak")]
		[TestCase("NoDigitsHere", "lms_xl_password_weak")]
		[TestCase("Valid1Password", null)]
		public void Passwords_follow_the_member_rules(string password, string refusal) =>
			LmsExternalLearnerRules.PasswordRefusal(password).Should().Be(refusal);

		[Test]
		public void Tokens_are_random_url_safe_and_only_their_hash_is_kept()
		{
			var a = LmsExternalLearnerRules.NewToken();
			var b = LmsExternalLearnerRules.NewToken();
			a.Should().NotBe(b);
			a.Should().MatchRegex("^[A-Za-z0-9_-]{40,}$");
			LmsExternalLearnerRules.HashToken(a).Should().HaveLength(64).And.MatchRegex("^[0-9a-f]+$").And.NotContain(a);
			LmsExternalLearnerRules.HashToken(a).Should().Be(LmsExternalLearnerRules.HashToken(" " + a + " "));
			LmsExternalLearnerRules.HashToken(null).Should().BeNull();
		}

		[Test]
		public void Portal_keys_have_one_shape_and_cannot_be_guessed_from_the_department()
		{
			var key = LmsExternalLearnerRules.NewPortalKey();
			LmsExternalLearnerRules.IsPortalKeyShape(key).Should().BeTrue();
			LmsExternalLearnerRules.IsPortalKeyShape("4401").Should().BeFalse();
			LmsExternalLearnerRules.IsPortalKeyShape("../../User/Home").Should().BeFalse();
			LmsExternalLearnerRules.NewPortalKey().Should().NotBe(key);
		}

		[TestCase("#1AB394", "#1ab394")]
		[TestCase("1ab394", "#1ab394")]
		[TestCase("#abc", "#aabbcc")]
		[TestCase("red", null)]
		[TestCase("#12345g", null)]
		[TestCase("javascript:alert(1)", null)]
		public void Accent_colours_are_hex_or_nothing(string value, string expected) =>
			LmsExternalLearnerRules.NormalizeAccent(value).Should().Be(expected);

		[Test]
		public void Branding_falls_back_to_resgrid_for_every_missing_part()
		{
			var none = LmsExternalLearnerRules.Branding(null, null, null);
			none.DisplayName.Should().Be("Resgrid");
			none.Accent.Should().Be(LmsExternalLearnerRules.DefaultAccent);
			none.LogoUrl.Should().BeNull();
			none.IsDefaultAccent.Should().BeTrue();

			var bad = LmsExternalLearnerRules.Branding(new LmsLearnerPortal { AccentColor = "not-a-colour" }, "Station 9", "https://x/logo.png");
			bad.Accent.Should().Be(LmsExternalLearnerRules.DefaultAccent, "an invalid colour falls back");
			bad.DisplayName.Should().Be("Station 9");
			bad.LogoUrl.Should().Be("https://x/logo.png");

			var branded = LmsExternalLearnerRules.Branding(new LmsLearnerPortal { AccentColor = "#ffee00", HideLogo = true, WelcomeText = "  Welcome  " }, "Station 9", "https://x/logo.png");
			branded.Accent.Should().Be("#ffee00");
			branded.AccentText.Should().Be("#000000", "dark text on a light accent");
			branded.LogoUrl.Should().BeNull("the department hid the logo");
			branded.WelcomeText.Should().Be("Welcome");
			LmsExternalLearnerRules.TextOn("#001f3f").Should().Be("#ffffff");
		}

		[Test]
		public void Branding_shows_times_in_the_department_zone_or_utc()
		{
			var utc = new DateTime(2027, 1, 15, 12, 0, 0, DateTimeKind.Utc);
			new LmsLearnerPortalBranding().Local(utc).Should().Be(utc);
			new LmsLearnerPortalBranding { TimeZone = "No/Such_Zone" }.Local(utc).Should().Be(utc);
			new LmsLearnerPortalBranding { TimeZone = "Eastern Standard Time" }.Local(utc).Hour.Should().Be(7);
		}

		private static LmsClassSession Session(DateTime start) => new LmsClassSession
		{
			LmsClassSessionId = 5, DepartmentId = 1, Title = "CPR", StartsOn = start, EndsOn = start.AddHours(3), EnrollmentOpen = true, Status = (int)LmsClassSessionStatuses.Scheduled
		};

		[Test]
		public void Only_listed_open_future_classes_take_registrations()
		{
			var now = DateTime.UtcNow;
			var session = Session(now.AddDays(10));
			var listed = new LmsPublicClass { LmsClassSessionId = 5, IsPublic = true };

			LmsExternalLearnerRules.ClassRegistrationRefusal(session, listed, now).Should().BeNull();
			LmsExternalLearnerRules.ClassRegistrationRefusal(session, new LmsPublicClass { LmsClassSessionId = 5, IsPublic = false }, now).Should().Be("lms_class_not_found");
			LmsExternalLearnerRules.ClassRegistrationRefusal(session, null, now).Should().Be("lms_class_not_found");
			LmsExternalLearnerRules.ClassRegistrationRefusal(session, new LmsPublicClass { LmsClassSessionId = 6, IsPublic = true }, now).Should().Be("lms_class_not_found");
			LmsExternalLearnerRules.ClassRegistrationRefusal(Session(now.AddMinutes(-5)), listed, now).Should().Be("lms_class_not_open");
			session.CutoffDays = 14;
			LmsExternalLearnerRules.ClassRegistrationRefusal(session, listed, now).Should().Be("lms_class_enrollment_cutoff");
			session.CutoffDays = 0;
			session.EnrollmentOpen = false;
			LmsExternalLearnerRules.ClassRegistrationRefusal(session, listed, now).Should().Be("lms_class_enrollment_closed");
		}

		private static LmsClassBillingRow Row(string userId, string name, decimal fee, string sponsor = null, int status = (int)LmsClassEnrollmentStatuses.Enrolled,
			int attendance = (int)LmsClassAttendanceStates.Attended, bool waived = false, string invoice = null)
		{
			var enrollment = new LmsClassEnrollment { LmsClassEnrollmentId = Math.Abs(name.GetHashCode()), UserId = userId, Status = status, Attendance = attendance, Hours = 3m };
			var feeRow = LmsExternalLearnerIds.IsExternal(userId)
				? new LmsClassFee { LmsClassFeeId = enrollment.LmsClassEnrollmentId, LmsClassEnrollmentId = enrollment.LmsClassEnrollmentId, Amount = fee, Currency = "USD", SponsorContactId = sponsor, Waived = waived, InvoiceId = invoice }
				: null;
			return new LmsClassBillingRow
			{
				Enrollment = enrollment,
				Fee = feeRow,
				Name = name,
				IsExternal = LmsExternalLearnerIds.IsExternal(userId),
				Organization = sponsor == null ? null : "Acme",
				Billable = LmsExternalLearnerRules.IsBillable(enrollment, feeRow)
			};
		}

		[Test]
		public void A_fee_is_due_for_an_enrolled_external_learner_unless_waived_or_excused()
		{
			Row(LmsExternalLearnerIds.New(), "A", 45m).Billable.Should().BeTrue();
			Row(LmsExternalLearnerIds.New(), "B", 45m, waived: true).Billable.Should().BeFalse();
			Row(LmsExternalLearnerIds.New(), "C", 45m, attendance: (int)LmsClassAttendanceStates.Excused).Billable.Should().BeFalse();
			Row(LmsExternalLearnerIds.New(), "D", 45m, status: (int)LmsClassEnrollmentStatuses.Waitlisted).Billable.Should().BeFalse();
			Row(LmsExternalLearnerIds.New(), "E", 0m).Billable.Should().BeFalse();
			Row(Guid.NewGuid().ToString(), "Member", 45m).Billable.Should().BeFalse("members carry no fee");
		}

		[Test]
		public void The_roster_export_has_learner_organization_attendance_and_fee_and_defuses_formulas()
		{
			var rows = new List<LmsClassBillingRow>
			{
				Row(LmsExternalLearnerIds.New(), "=HYPERLINK(\"x\")", 45m, "sponsor-1"),
				Row(Guid.NewGuid().ToString(), "Pat, Doe", 0m)
			};
			var csv = LmsExternalLearnerRules.RosterCsv(rows, key => key);
			var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

			lines.Should().HaveCount(3);
			lines[0].Should().Be("Csv.Learner,Csv.Email,Csv.Organization,Csv.Kind,Csv.Roster,Csv.Attendance,Csv.Hours,Csv.Fee,Csv.Currency,Csv.FeeStatus,Csv.Invoice");
			lines[1].Should().StartWith("\"'=HYPERLINK(\"\"x\"\")\"").And.Contain("Kind.External").And.Contain("45.00").And.Contain("FeeStatus.Due").And.Contain("Attendance.Attended");
			lines[2].Should().StartWith("\"Pat, Doe\"").And.Contain("Kind.Member");
		}

		[Test]
		public void Invoices_group_per_sponsoring_organization_or_per_learner_and_skip_what_cannot_be_billed()
		{
			var first = Row(LmsExternalLearnerIds.New(), "A", 45m, "acme");
			var second = Row(LmsExternalLearnerIds.New(), "B", 45m, "ACME");
			var nosponsor = Row(LmsExternalLearnerIds.New(), "C", 45m);
			var invoiced = Row(LmsExternalLearnerIds.New(), "D", 45m, "acme", invoice: "INV-9");
			var claimed = Row(LmsExternalLearnerIds.New(), "E", 45m, "acme", invoice: LmsClassFee.ClaimPrefix + "x");
			var rows = new[] { first, second, nosponsor, invoiced, claimed };

			var (byOrganization, skipped) = LmsExternalLearnerRules.GroupForInvoices(rows, LmsClassInvoiceModes.PerOrganization);
			byOrganization.Should().ContainSingle().Which.Value.Should().BeEquivalentTo(new[] { first, second }, "sponsors compare case-insensitively");
			skipped.Should().ContainSingle().Which.Should().Be(nosponsor);

			var (byLearner, none) = LmsExternalLearnerRules.GroupForInvoices(rows, LmsClassInvoiceModes.PerLearner);
			byLearner.Should().HaveCount(3);
			none.Should().BeEmpty();
		}
	}
}
