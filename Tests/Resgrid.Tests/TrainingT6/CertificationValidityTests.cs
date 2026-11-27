using System;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Helpers;

namespace Resgrid.Tests.TrainingT6
{
	/// <summary>
	/// Round 9A: the validity rules' expiry maths (rolling months, calendar and fiscal year ends, a fixed date), the renewal window, the
	/// grace period, leap years, the department's time zone, and the counting window that keeps a renewal's own completions from renewing
	/// it again.
	/// </summary>
	[TestFixture]
	public class CertificationValidityTests
	{
		private static DepartmentCertificationType Type(CertificationValidityRules rule, int? months = 12, int? endMonth = null, int? endDay = null, DateTime? fixedDate = null,
			int? grace = null, int? window = null) => new DepartmentCertificationType
		{
			DepartmentCertificationTypeId = 1, DepartmentId = 1, Type = "Operator", ValidityRule = (int)rule, DefaultValidityMonths = months, ValidityYearEndMonth = endMonth,
			ValidityYearEndDay = endDay, ValidityFixedDate = fixedDate, RenewalGraceDays = grace, RenewalWindowMonths = window
		};

		private static PersonnelCertification Record(DateTime expires, int status = (int)PersonnelCertificationStatuses.Active) =>
			new PersonnelCertification { PersonnelCertificationId = 9, UserId = "m", DepartmentCertificationTypeId = 1, ExpiresOn = expires, Status = status };

		[TestCase(2027, 1, 31, 1, 2027, 2, 28, TestName = "Rolling_months_clamp_to_the_end_of_a_short_month")]
		[TestCase(2028, 1, 31, 1, 2028, 2, 29, TestName = "Rolling_months_land_on_29_February_in_a_leap_year")]
		[TestCase(2026, 3, 15, 24, 2028, 3, 15, TestName = "Rolling_24_months_from_the_issue_date")]
		[TestCase(2028, 2, 29, 12, 2029, 2, 28, TestName = "Rolling_from_29_February_lands_on_28_February")]
		public void Rolling_months_run_from_the_issue_date(int y, int m, int d, int months, int ey, int em, int ed)
		{
			CertificationValidity.ExpiryFor(Type(CertificationValidityRules.RollingMonths, months), new DateTime(y, m, d), null)
				.Should().Be(new DateTime(ey, em, ed));
		}

		[Test]
		public void A_calendar_year_issue_runs_to_31_December_and_a_renewal_to_the_next_one()
		{
			var type = Type(CertificationValidityRules.CalendarYearEnd);
			CertificationValidity.ExpiryFor(type, new DateTime(2026, 3, 3), null).Should().Be(new DateTime(2026, 12, 31));
			CertificationValidity.ExpiryFor(type, new DateTime(2026, 12, 31), null).Should().Be(new DateTime(2026, 12, 31), "issued on the year end itself");
			CertificationValidity.ExpiryFor(type, new DateTime(2026, 11, 20), Record(new DateTime(2026, 12, 31))).Should().Be(new DateTime(2027, 12, 31));
		}

		[Test]
		public void A_first_issue_inside_the_renewal_window_already_covers_the_next_year()
		{
			var type = Type(CertificationValidityRules.CalendarYearEnd, window: 3);
			CertificationValidity.ExpiryFor(type, new DateTime(2026, 11, 15), null).Should().Be(new DateTime(2027, 12, 31));
			CertificationValidity.ExpiryFor(type, new DateTime(2026, 9, 15), null).Should().Be(new DateTime(2026, 12, 31));
		}

		[Test]
		public void A_renewal_inside_the_grace_period_continues_the_cycle()
		{
			var type = Type(CertificationValidityRules.CalendarYearEnd, grace: 45);
			var expired = Record(new DateTime(2026, 12, 31), (int)PersonnelCertificationStatuses.Expired);
			CertificationValidity.IsInGrace(type, expired, new DateTime(2027, 1, 20)).Should().BeTrue();
			CertificationValidity.IsInGrace(type, expired, new DateTime(2027, 2, 15)).Should().BeFalse("45 days after 31 December is 14 February");
			CertificationValidity.ExpiryFor(type, new DateTime(2027, 1, 20), expired).Should().Be(new DateTime(2027, 12, 31), "the cycle stays anchored on the old year end");
		}

		[TestCase(6, 30, 2026, 7, 1, 2027, 6, 30)]
		[TestCase(6, 30, 2026, 6, 30, 2026, 6, 30)]
		[TestCase(3, 31, 2027, 2, 1, 2027, 3, 31)]
		[TestCase(2, 29, 2027, 1, 10, 2027, 2, 28)]
		[TestCase(2, 29, 2027, 3, 1, 2028, 2, 29)]
		public void A_fiscal_year_end_is_the_first_one_on_or_after_the_issue(int endMonth, int endDay, int y, int m, int d, int ey, int em, int ed)
		{
			CertificationValidity.ExpiryFor(Type(CertificationValidityRules.FiscalYearEnd, endMonth: endMonth, endDay: endDay), new DateTime(y, m, d), null)
				.Should().Be(new DateTime(ey, em, ed));
		}

		[Test]
		public void A_fiscal_renewal_of_a_29_February_year_end_moves_to_the_next_clamped_year_end()
		{
			var type = Type(CertificationValidityRules.FiscalYearEnd, endMonth: 2, endDay: 29);
			CertificationValidity.ExpiryFor(type, new DateTime(2027, 2, 1), Record(new DateTime(2027, 2, 28))).Should().Be(new DateTime(2028, 2, 29));
			CertificationValidity.ExpiryFor(type, new DateTime(2028, 1, 5), Record(new DateTime(2028, 2, 29))).Should().Be(new DateTime(2029, 2, 28));
		}

		[Test]
		public void A_fixed_date_is_the_expiry_whatever_the_issue_date()
		{
			var type = Type(CertificationValidityRules.FixedDate, fixedDate: new DateTime(2027, 6, 30, 13, 0, 0));
			CertificationValidity.ExpiryFor(type, new DateTime(2026, 1, 1), null).Should().Be(new DateTime(2027, 6, 30));
			CertificationValidity.ExpiryFor(type, new DateTime(2027, 5, 1), Record(new DateTime(2027, 6, 30))).Should().Be(new DateTime(2027, 6, 30));
		}

		[Test]
		public void A_type_that_never_expires_or_has_no_months_gets_no_expiry()
		{
			var never = Type(CertificationValidityRules.CalendarYearEnd);
			never.NeverExpires = true;
			CertificationValidity.ExpiryFor(never, new DateTime(2026, 5, 1), null).Should().BeNull();
			CertificationValidity.ExpiryFor(Type(CertificationValidityRules.RollingMonths, months: null), new DateTime(2026, 5, 1), null).Should().BeNull();
		}

		[Test]
		public void The_counting_window_is_anchored_on_the_expiry_so_a_renewals_own_completions_never_count_again()
		{
			var yearly = Type(CertificationValidityRules.CalendarYearEnd);
			CertificationValidity.CountingWindow(yearly, Record(new DateTime(2026, 12, 31)), new DateTime(2026, 11, 25))
				.Should().Be((new DateTime(2026, 1, 1), new DateTime(2026, 11, 25)));
			// After the renewal to 31 December 2027 the 2026 completions fall outside the window.
			CertificationValidity.CountingWindow(yearly, Record(new DateTime(2027, 12, 31)), new DateTime(2026, 11, 26)).Start.Should().Be(new DateTime(2027, 1, 1));

			var rolling = Type(CertificationValidityRules.RollingMonths, 12);
			CertificationValidity.CountingWindow(rolling, Record(new DateTime(2027, 3, 15)), new DateTime(2026, 11, 25)).Start.Should().Be(new DateTime(2026, 3, 16));
			CertificationValidity.CountingWindow(Type(CertificationValidityRules.CalendarYearEnd, window: 3), Record(new DateTime(2026, 12, 31)), new DateTime(2026, 11, 25))
				.Start.Should().Be(new DateTime(2026, 10, 1), "a three-month renewal window");
			CertificationValidity.CountingWindow(rolling, null, new DateTime(2026, 11, 25)).Should().Be((new DateTime(2025, 11, 26), new DateTime(2026, 11, 25)), "a first issue counts the last cycle");
		}

		[Test]
		public void The_grace_period_extends_the_window_end()
		{
			var type = Type(CertificationValidityRules.CalendarYearEnd, grace: 30);
			CertificationValidity.CountingWindow(type, Record(new DateTime(2026, 12, 31)), new DateTime(2027, 1, 10)).End.Should().Be(new DateTime(2027, 1, 10));
			CertificationValidity.CountingWindow(type, Record(new DateTime(2026, 12, 31)), new DateTime(2027, 3, 10)).End.Should().Be(new DateTime(2027, 1, 30));
		}

		[Test]
		public void The_department_time_zone_decides_which_year_a_late_evening_completion_belongs_to()
		{
			// 23:30 on 31 December in New York is 04:30 UTC on 1 January.
			var newYork = new Department { DepartmentId = 1, TimeZone = "Eastern Standard Time" };
			var completedUtc = new DateTime(2027, 1, 1, 4, 30, 0, DateTimeKind.Utc);
			var local = TrainingDueDates.LocalDate(completedUtc, newYork);
			local.Should().Be(new DateTime(2026, 12, 31));
			CertificationValidity.ExpiryFor(Type(CertificationValidityRules.CalendarYearEnd), local, null).Should().Be(new DateTime(2026, 12, 31));

			// The same moment in Berlin is already 1 January.
			var berlin = new Department { DepartmentId = 2, TimeZone = "W. Europe Standard Time" };
			CertificationValidity.ExpiryFor(Type(CertificationValidityRules.CalendarYearEnd), TrainingDueDates.LocalDate(completedUtc, berlin), null).Should().Be(new DateTime(2027, 12, 31));
		}

		[Test]
		public void Validation_refuses_impossible_year_ends_and_out_of_range_grace_and_windows()
		{
			CertificationValidity.Validate(Type(CertificationValidityRules.FiscalYearEnd, endMonth: 2, endDay: 30)).Should().Be("certifications_type_year_end_invalid");
			CertificationValidity.Validate(Type(CertificationValidityRules.FiscalYearEnd, endMonth: 2, endDay: 29)).Should().BeNull("29 February is a valid year end (clamped in common years)");
			CertificationValidity.Validate(Type(CertificationValidityRules.FiscalYearEnd, endMonth: 13, endDay: 1)).Should().Be("certifications_type_year_end_invalid");
			CertificationValidity.Validate(Type(CertificationValidityRules.FixedDate)).Should().Be("certifications_type_fixed_date_required");
			CertificationValidity.Validate(Type(CertificationValidityRules.CalendarYearEnd, grace: 400)).Should().Be("certifications_type_grace_invalid");
			CertificationValidity.Validate(Type(CertificationValidityRules.CalendarYearEnd, window: 0)).Should().Be("certifications_type_window_invalid");
			var unknown = Type(CertificationValidityRules.CalendarYearEnd);
			unknown.ValidityRule = 99;
			CertificationValidity.Validate(unknown).Should().Be("certifications_type_validity_rule_invalid");
		}

		[Test]
		public void Normalizing_clears_what_the_rule_does_not_use()
		{
			var type = Type(CertificationValidityRules.CalendarYearEnd, endMonth: 6, endDay: 30, fixedDate: new DateTime(2027, 1, 1), grace: 0);
			CertificationValidity.Normalize(type);
			type.ValidityYearEndMonth.Should().BeNull();
			type.ValidityFixedDate.Should().BeNull();
			type.RenewalGraceDays.Should().BeNull();

			var never = Type(CertificationValidityRules.FiscalYearEnd, endMonth: 6, endDay: 30, grace: 10, window: 2);
			never.NeverExpires = true;
			CertificationValidity.Normalize(never);
			never.ValidityRule.Should().Be((int)CertificationValidityRules.RollingMonths);
			never.RenewalGraceDays.Should().BeNull();
			never.RenewalWindowMonths.Should().BeNull();
		}

		[Test]
		public void The_cycle_auto_renew_sizes_a_year_end_cycle_to_the_following_year_end()
		{
			var type = Type(CertificationValidityRules.FiscalYearEnd, months: null, endMonth: 3, endDay: 31);
			type.RenewalCreditHoursRequired = 20m;
			Resgrid.Model.Lms.LmsCertificationCycleMath.CanAutoRenew(type).Should().BeTrue();
			Resgrid.Model.Lms.LmsCertificationCycleMath.NextCycle(Record(new DateTime(2027, 3, 31)), type).Should().Be((new DateTime(2027, 4, 1), new DateTime(2028, 3, 31)));
			var fixedDate = Type(CertificationValidityRules.FixedDate, fixedDate: new DateTime(2027, 3, 31));
			fixedDate.RenewalCreditHoursRequired = 20m;
			Resgrid.Model.Lms.LmsCertificationCycleMath.CanAutoRenew(fixedDate).Should().BeFalse("a fixed date has no next cycle");
		}
	}
}
