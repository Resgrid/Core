using System;

namespace Resgrid.Model.Certifications
{
	/// <summary>
	/// How a record of a certification type expires when it is issued or renewed (round 9A, M0404). Persisted
	/// (DepartmentCertificationTypes.ValidityRule): append only. RollingMonths is the behaviour every type had before M0404.
	/// </summary>
	public enum CertificationValidityRules
	{
		/// <summary>The type's validity in months from the issue (or renewal) date, e.g. every 12 or 24 months.</summary>
		RollingMonths = 0,

		/// <summary>Every record expires on 31 December (a yearly operator or equipment authorization).</summary>
		CalendarYearEnd = 1,

		/// <summary>Every record expires at the department's own year end (a fiscal, training or license year, e.g. 31 March or 30 June).</summary>
		FiscalYearEnd = 2,

		/// <summary>Every record expires on one fixed date (a programme, a contract or a site authorization that ends then).</summary>
		FixedDate = 3
	}

	/// <summary>
	/// The one place a certification type's validity is computed (round 9A): the expiry an automatic issue or renewal gets, the window in
	/// which completions count toward the next renewal, and the grace after expiry. Works on department-local calendar dates only: the
	/// callers convert UTC moments with <c>TrainingDueDates.LocalDate</c> first, so a member who finishes at 23:30 local time on
	/// 31 December renews the year that is ending, wherever the server runs. No I/O.
	/// <list type="bullet">
	/// <item>Rolling: issue date + N months (the behaviour before M0404; 31 January + 1 month is 28 or 29 February).</item>
	/// <item>Year end (calendar or fiscal): a new issue expires at the first year end on or after the issue date (or the next one when the
	/// issue falls inside the renewal window of that year end); a renewal of a live or in-grace record moves its expiry to the following year
	/// end. A year end of 29 February falls on 28 February in other years.</item>
	/// <item>Fixed date: every record expires on that date.</item>
	/// <item>Counting window: completions count toward the renewal of a live (or in-grace) record when they fall after the start of the
	/// cycle that ends at its expiry (the expiry minus the cycle length, narrowed by the renewal window when one is set) and no later than
	/// the end of the grace period. With no live record the window is the last cycle length up to today. Because the window is anchored on
	/// the expiry, the completions that produced the current expiry can never renew it again.</item>
	/// </list>
	/// </summary>
	public static class CertificationValidity
	{
		public const int MaxGraceDays = 365;
		public const int MaxRenewalWindowMonths = 60;

		/// <summary>The type's rule (an unknown stored value reads as RollingMonths).</summary>
		public static CertificationValidityRules RuleOf(DepartmentCertificationType type) =>
			type != null && Enum.IsDefined(typeof(CertificationValidityRules), type.ValidityRule) ? (CertificationValidityRules)type.ValidityRule : CertificationValidityRules.RollingMonths;

		/// <summary>Whether records issued by the engine get an expiry at all (a rolling type without months never expires on its own).</summary>
		public static bool Expires(DepartmentCertificationType type)
		{
			if (type == null || type.NeverExpires)
				return false;
			switch (RuleOf(type))
			{
				case CertificationValidityRules.RollingMonths:
					return type.DefaultValidityMonths.HasValue && type.DefaultValidityMonths.Value > 0;
				case CertificationValidityRules.FixedDate:
					return type.ValidityFixedDate.HasValue;
				default:
					return true;
			}
		}

		/// <summary>The length of one cycle in months: the rolling validity, or a year for the year-end and fixed-date rules.</summary>
		public static int CycleMonths(DepartmentCertificationType type) =>
			RuleOf(type) == CertificationValidityRules.RollingMonths && type?.DefaultValidityMonths > 0 ? type.DefaultValidityMonths.Value : 12;

		/// <summary>The (month, day) of the type's year end: 31 December for a calendar year, the configured one for a fiscal year.</summary>
		public static (int Month, int Day) YearEnd(DepartmentCertificationType type)
		{
			if (RuleOf(type) == CertificationValidityRules.FiscalYearEnd && type.ValidityYearEndMonth is >= 1 and <= 12 && type.ValidityYearEndDay is >= 1 and <= 31)
				return (type.ValidityYearEndMonth.Value, type.ValidityYearEndDay.Value);
			return (12, 31);
		}

		/// <summary>The day <paramref name="day"/> of <paramref name="month"/> in <paramref name="year"/>, clamped to the month's last day (29 February in a common year is the 28th).</summary>
		public static DateTime Clamp(int year, int month, int day) => new DateTime(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

		/// <summary>The first year end (month, day) on or after <paramref name="localDate"/>.</summary>
		public static DateTime YearEndOnOrAfter(DateTime localDate, int month, int day)
		{
			var date = localDate.Date;
			var candidate = Clamp(date.Year, month, day);
			return candidate >= date ? candidate : Clamp(date.Year + 1, month, day);
		}

		/// <summary>The grace after expiry in days (0 when none).</summary>
		public static int GraceDays(DepartmentCertificationType type) => Math.Max(0, Math.Min(MaxGraceDays, type?.RenewalGraceDays ?? 0));

		/// <summary>The record expired, but completing the requirements still renews its cycle today (the grace period).</summary>
		public static bool IsInGrace(DepartmentCertificationType type, PersonnelCertification record, DateTime localToday) =>
			record?.ExpiresOn != null && record.ExpiresOn.Value.Date < localToday.Date && localToday.Date <= record.ExpiresOn.Value.Date.AddDays(GraceDays(type));

		/// <summary>The record's expiry has not passed, or it passed within the grace period: a completion renews this cycle.</summary>
		public static bool IsRenewable(DepartmentCertificationType type, PersonnelCertification record, DateTime localToday) =>
			record != null && (!record.ExpiresOn.HasValue || record.ExpiresOn.Value.Date >= localToday.Date || IsInGrace(type, record, localToday));

		/// <summary>
		/// The expiry (a local date) an issue or renewal completed on <paramref name="localCompletedOn"/> gets; null when the type does not
		/// expire. <paramref name="renewing"/> is the live or in-grace record being renewed, or null for a new issue.
		/// </summary>
		public static DateTime? ExpiryFor(DepartmentCertificationType type, DateTime localCompletedOn, PersonnelCertification renewing)
		{
			if (!Expires(type))
				return null;
			var completed = localCompletedOn.Date;
			switch (RuleOf(type))
			{
				case CertificationValidityRules.FixedDate:
					return type.ValidityFixedDate.Value.Date;
				case CertificationValidityRules.CalendarYearEnd:
				case CertificationValidityRules.FiscalYearEnd:
				{
					var (month, day) = YearEnd(type);
					if (renewing?.ExpiresOn != null)
					{
						// A renewal continues the cycle: the year end after the current one (never one that is already behind the completion).
						var next = YearEndOnOrAfter(renewing.ExpiresOn.Value.Date.AddDays(1), month, day);
						while (next < completed)
							next = YearEndOnOrAfter(next.AddDays(1), month, day);
						return next;
					}

					var first = YearEndOnOrAfter(completed, month, day);
					// A first issue inside the renewal window of that year end already covers the next year (no immediate second round).
					if (type.RenewalWindowMonths is > 0 && completed > first.AddMonths(-type.RenewalWindowMonths.Value))
						first = YearEndOnOrAfter(first.AddDays(1), month, day);
					return first;
				}
				default:
					return completed.AddMonths(type.DefaultValidityMonths.Value);
			}
		}

		/// <summary>
		/// The local dates (inclusive) in which completions count toward issuing or renewing on <paramref name="localToday"/>: for a renewable
		/// record the cycle that ends at its expiry (narrowed by the renewal window) through the end of its grace, else the last cycle length.
		/// </summary>
		public static (DateTime Start, DateTime End) CountingWindow(DepartmentCertificationType type, PersonnelCertification renewable, DateTime localToday)
		{
			var today = localToday.Date;
			var months = CycleMonths(type);
			if (renewable?.ExpiresOn != null)
			{
				var expiry = renewable.ExpiresOn.Value.Date;
				var window = type?.RenewalWindowMonths is > 0 ? Math.Min(months, type.RenewalWindowMonths.Value) : months;
				// The day after the expiry one cycle earlier (31 Dec → 1 Jan; a clamped 28 Feb year end → 1 Mar).
				var start = expiry.AddMonths(-window).AddDays(1);
				var end = expiry.AddDays(GraceDays(type));
				return (start, end < today ? end : today);
			}

			return (today.AddMonths(-months).AddDays(1), today);
		}

		/// <summary>Why a type's validity settings are invalid ("certifications_type_*"), or null.</summary>
		public static string Validate(DepartmentCertificationType type)
		{
			if (type == null)
				return null;
			if (!Enum.IsDefined(typeof(CertificationValidityRules), type.ValidityRule))
				return "certifications_type_validity_rule_invalid";
			var rule = (CertificationValidityRules)type.ValidityRule;
			if (rule == CertificationValidityRules.FiscalYearEnd)
			{
				if (type.ValidityYearEndMonth is not (>= 1 and <= 12) || type.ValidityYearEndDay is not (>= 1 and <= 31) ||
				    type.ValidityYearEndDay.Value > DateTime.DaysInMonth(2024, type.ValidityYearEndMonth.Value))
					return "certifications_type_year_end_invalid";
			}
			if (rule == CertificationValidityRules.FixedDate && !type.ValidityFixedDate.HasValue && !type.NeverExpires)
				return "certifications_type_fixed_date_required";
			if (type.RenewalGraceDays.HasValue && (type.RenewalGraceDays.Value < 0 || type.RenewalGraceDays.Value > MaxGraceDays))
				return "certifications_type_grace_invalid";
			if (type.RenewalWindowMonths.HasValue && (type.RenewalWindowMonths.Value < 1 || type.RenewalWindowMonths.Value > MaxRenewalWindowMonths))
				return "certifications_type_window_invalid";
			return null;
		}

		/// <summary>Clears the fields the type's rule does not use (a never-expiring or unit type keeps only the rule).</summary>
		public static void Normalize(DepartmentCertificationType type)
		{
			if (type == null)
				return;
			if (!Enum.IsDefined(typeof(CertificationValidityRules), type.ValidityRule))
				type.ValidityRule = (int)CertificationValidityRules.RollingMonths;
			var rule = (CertificationValidityRules)type.ValidityRule;
			if (rule != CertificationValidityRules.FiscalYearEnd)
			{
				type.ValidityYearEndMonth = null;
				type.ValidityYearEndDay = null;
			}
			if (rule != CertificationValidityRules.FixedDate)
				type.ValidityFixedDate = null;
			else if (type.ValidityFixedDate.HasValue)
				type.ValidityFixedDate = type.ValidityFixedDate.Value.Date;
			if (type.RenewalGraceDays == 0)
				type.RenewalGraceDays = null;
			if (type.NeverExpires)
			{
				type.ValidityRule = (int)CertificationValidityRules.RollingMonths;
				type.ValidityFixedDate = null;
				type.RenewalGraceDays = null;
				type.RenewalWindowMonths = null;
			}
		}
	}
}
