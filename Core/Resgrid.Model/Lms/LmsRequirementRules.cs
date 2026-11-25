using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.Certifications;
using Resgrid.Model.Helpers;

namespace Resgrid.Model.Lms
{
	/// <summary>Everything about the department and the moment that one evaluation needs (no I/O).</summary>
	public sealed class LmsRequirementContext
	{
		public Department Department { get; set; }

		public DateTime UtcNow { get; set; }

		/// <summary>The member holds a licensed LMS seat (§17.4). False: not licensed, never overdue, never enforced.</summary>
		public bool Licensed { get; set; } = true;

		public IEnumerable<MemberLeavePeriod> Leaves { get; set; }

		/// <summary>The department's closed LMS lapses (rule 12 re-base).</summary>
		public IEnumerable<LmsAddonLapse> Lapses { get; set; }

		public TimeSpan ReactivationGrace { get; set; } = TimeSpan.FromDays(14);

		/// <summary>The add-on is lapsed since this moment (rule 5): the engine is paused and nothing becomes overdue after it.</summary>
		public DateTime? OpenLapseSinceUtc { get; set; }
	}

	/// <summary>Where one member stands on one requirement now (a compliance matrix cell).</summary>
	public sealed class LmsRequirementStatus
	{
		public LmsRequirementStates State { get; set; }

		/// <summary>The current period is satisfied.</summary>
		public bool Satisfied { get; set; }

		/// <summary>The due moment of the open obligation before leave and lapse shifts (null when satisfied for good).</summary>
		public DateTime? DueUtc { get; set; }

		/// <summary>The due moment with the extension, the member's leave and the lapse re-base applied.</summary>
		public DateTime? EffectiveDueUtc { get; set; }

		public DateTime? GraceEndsUtc { get; set; }

		/// <summary>When a satisfied recurring requirement is due again.</summary>
		public DateTime? NextDueUtc { get; set; }

		public DateTime PeriodStartUtc { get; set; }

		public DateTime? PeriodEndUtc { get; set; }

		public decimal Progress { get; set; }

		public decimal Required { get; set; }

		public DateTime? LastCompletedUtc { get; set; }

		public int? LastLearningRecordId { get; set; }

		/// <summary>Rule 12: nothing becomes overdue or enforceable before this moment.</summary>
		public DateTime? HoldUntilUtc { get; set; }

		public bool OnLeave { get; set; }

		/// <summary>Overdue, licensed, not exempt, not on leave, not paused and past the reactivation hold.</summary>
		public bool Enforceable { get; set; }

		/// <summary>The due moment the matrix shows (the open obligation's, or the next renewal).</summary>
		public DateTime? ShownDueUtc => Satisfied ? NextDueUtc : EffectiveDueUtc;
	}

	/// <summary>
	/// The one place a requirement's due dates and states are computed (T3 item 1), shared by the compliance matrix, My Training, the v4
	/// API and worker 93. Due moments are the end of a department-local day, like <see cref="TrainingDueDates"/>:
	/// <list type="bullet">
	/// <item>A member's first due date is <see cref="LmsRequirement.DueDays"/> after they entered the audience (joined, were given the
	/// role, or entered it any other way: the anchor the engine stores).</item>
	/// <item>Rolling requirements are due again IntervalMonths after the last completion; a member who joins with an expired completion
	/// still gets DueDays. Calendar-year requirements are due at the end of the local year, pro-rated for the year a member joined.</item>
	/// <item>An extension moves the due date later; leave pauses it (shifted by the leave, R-C2); a lapse of the add-on shifts it by the
	/// lapse and holds it until the reactivation grace ends (rule 12). While a lapse runs nothing becomes overdue (rule 5).</item>
	/// <item>Past due but within the grace period the member is at risk; past the grace period, overdue.</item>
	/// </list>
	/// </summary>
	public static class LmsRequirementEvaluator
	{
		/// <summary>A counted requirement is behind pace when the share done trails the share of the period elapsed by this much.</summary>
		public const decimal PaceTolerance = 0.25m;

		public static LmsRequirementStatus Evaluate(LmsRequirement requirement, LmsRequirementMember member, IEnumerable<LmsRequirementFact> facts, LmsRequirementContext context)
		{
			if (requirement == null) throw new ArgumentNullException(nameof(requirement));
			if (member == null) throw new ArgumentNullException(nameof(member));
			if (context == null) throw new ArgumentNullException(nameof(context));

			var now = context.UtcNow;
			var department = context.Department;
			var status = new LmsRequirementStatus { Required = requirement.RequiredAmount };
			var mine = (facts ?? Enumerable.Empty<LmsRequirementFact>()).Where(f => f != null && f.CompletedOn <= now).OrderBy(f => f.CompletedOn).ToList();
			var anchor = member.EnteredOn;
			var anchorDue = TrainingDueDates.EndOfLocalDayUtc(TrainingDueDates.LocalDate(anchor, department).AddDays(Math.Max(0, requirement.DueDays)), department);
			var recurrence = (LmsRequirementRecurrences)requirement.Recurrence;
			var interval = requirement.IntervalMonths ?? 0;
			if (recurrence == LmsRequirementRecurrences.RollingMonths && interval <= 0)
				recurrence = LmsRequirementRecurrences.OneTime;

			DateTime? due;
			DateTime? nextDue = null;
			DateTime periodStart;
			DateTime? periodEnd = null;
			bool satisfied;
			var last = mine.LastOrDefault();
			status.LastCompletedUtc = last?.CompletedOn;
			status.LastLearningRecordId = last?.LearningRecordId;

			if (!requirement.IsCounted)
			{
				switch (recurrence)
				{
					case LmsRequirementRecurrences.RollingMonths:
						if (last != null)
						{
							var validUntil = ValidUntil(last.CompletedOn, interval, department);
							if (validUntil >= now)
							{
								satisfied = true;
								nextDue = validUntil;
								due = null;
								periodStart = last.CompletedOn;
								periodEnd = validUntil;
							}
							else
							{
								satisfied = false;
								due = Max(validUntil, anchorDue);
								periodStart = validUntil > anchor ? validUntil : anchor;
							}
						}
						else
						{
							satisfied = false;
							due = anchorDue;
							periodStart = anchor;
						}
						break;
					case LmsRequirementRecurrences.CalendarYear:
						{
							var (yearStart, yearEnd) = Year(now, department);
							satisfied = mine.Any(f => f.CompletedOn >= yearStart);
							due = satisfied ? null : Max(yearEnd, anchorDue);
							periodStart = anchor > yearStart ? anchor : yearStart;
							periodEnd = yearEnd;
							break;
						}
					default:
						satisfied = last != null;
						due = satisfied ? null : anchorDue;
						periodStart = anchor;
						break;
				}

				status.Progress = satisfied ? 1m : 0m;
			}
			else
			{
				var required = requirement.RequiredAmount;
				decimal progress;
				switch (recurrence)
				{
					case LmsRequirementRecurrences.CalendarYear:
						{
							var (yearStart, yearEnd) = Year(now, department);
							if (anchor > yearStart)
								required = Prorate(required, anchor, yearStart, yearEnd, requirement.Kind == (int)LmsRequirementKinds.CategoryHours);
							progress = mine.Where(f => f.CompletedOn >= yearStart).Sum(f => f.Amount);
							satisfied = progress >= required;
							due = satisfied ? null : Max(yearEnd, anchorDue);
							periodStart = anchor > yearStart ? anchor : yearStart;
							periodEnd = yearEnd;
							break;
						}
					case LmsRequirementRecurrences.RollingMonths:
						{
							var windowStart = now.AddMonths(-interval);
							var inWindow = mine.Where(f => f.CompletedOn >= windowStart).ToList();
							progress = inWindow.Sum(f => f.Amount);
							satisfied = progress >= required;
							periodStart = windowStart > anchor ? windowStart : anchor;
							if (satisfied)
							{
								// Due again when the oldest of the newest items that still make up the amount leaves the window.
								nextDue = DropsBelowOn(inWindow, required, interval, department);
								due = null;
								periodEnd = nextDue;
							}
							else
							{
								var lapsedOn = DropsBelowOn(mine, required, interval, department);
								due = lapsedOn.HasValue ? Max(lapsedOn.Value, anchorDue) : anchorDue;
							}
							break;
						}
					default:
						progress = mine.Sum(f => f.Amount);
						satisfied = progress >= required;
						due = satisfied ? null : anchorDue;
						periodStart = anchor;
						break;
				}

				status.Progress = progress;
				status.Required = required;
			}

			status.Satisfied = satisfied;
			status.DueUtc = due;
			status.NextDueUtc = nextDue;
			status.PeriodStartUtc = periodStart;
			status.PeriodEndUtc = periodEnd;

			// Extension, leave (R-C2) and the lapse re-base (rule 12), in that order.
			DateTime? effective = due;
			if (effective.HasValue)
			{
				if (member.ExtendedDueOn.HasValue && member.ExtendedDueOn.Value > effective.Value)
					effective = member.ExtendedDueOn.Value;
				effective = TrainingDueDates.ApplyLeave(effective.Value, periodStart, context.Leaves);
				var lapses = (context.Lapses ?? Enumerable.Empty<LmsAddonLapse>()).ToList();
				if (lapses.Count > 0)
				{
					var rebased = LmsLapseRebase.Rebase(new LmsDueItem { Key = string.Empty, DueUtc = effective.Value, AssignedOnUtc = periodStart }, lapses, context.ReactivationGrace);
					effective = rebased.DueUtc;
					status.HoldUntilUtc = rebased.HoldUntilUtc;
				}

				status.EffectiveDueUtc = effective;
				status.GraceEndsUtc = effective.Value.AddDays(Math.Max(0, requirement.GraceDays));
			}

			// Rule 5: while a lapse runs nothing becomes overdue after it began.
			var asOf = context.OpenLapseSinceUtc.HasValue && context.OpenLapseSinceUtc.Value < now ? context.OpenLapseSinceUtc.Value : now;
			status.OnLeave = TrainingDueDates.IsOnLeave(now, context.Leaves);
			var atRisk = TimeSpan.FromDays(Math.Max(0, requirement.AtRiskDays));

			if (!context.Licensed)
				status.State = LmsRequirementStates.NotLicensed;
			else if (member.IsExemptOn(now))
				status.State = LmsRequirementStates.Exempt;
			else if (satisfied)
				status.State = nextDue.HasValue && nextDue.Value - asOf <= atRisk ? LmsRequirementStates.AtRisk : LmsRequirementStates.Compliant;
			else if (status.OnLeave)
				status.State = LmsRequirementStates.OnLeave;
			else if (!effective.HasValue)
				status.State = LmsRequirementStates.Pending;
			else
			{
				var pastGrace = asOf > status.GraceEndsUtc.Value;
				var held = status.HoldUntilUtc.HasValue && asOf < status.HoldUntilUtc.Value;
				if (pastGrace && !held)
					status.State = LmsRequirementStates.Overdue;
				else if (pastGrace || asOf > effective.Value || effective.Value - asOf <= atRisk || (requirement.IsCounted && BehindPace(status, asOf)))
					status.State = LmsRequirementStates.AtRisk;
				else
					status.State = LmsRequirementStates.Pending;
			}

			status.Enforceable = status.State == LmsRequirementStates.Overdue && !context.OpenLapseSinceUtc.HasValue &&
				(!status.HoldUntilUtc.HasValue || now >= status.HoldUntilUtc.Value);
			return status;
		}

		/// <summary>The end of the local day <paramref name="months"/> after a completion: how long a rolling completion stays valid.</summary>
		public static DateTime ValidUntil(DateTime completedOnUtc, int months, Department department) =>
			TrainingDueDates.EndOfLocalDayUtc(TrainingDueDates.LocalDate(completedOnUtc, department).AddMonths(months), department);

		/// <summary>The department-local calendar year containing <paramref name="utc"/>, as UTC moments.</summary>
		public static (DateTime Start, DateTime End) Year(DateTime utc, Department department)
		{
			var year = TrainingDueDates.LocalDate(utc, department).Year;
			return (TrainingDueDates.StartOfLocalDayUtc(new DateTime(year, 1, 1), department), TrainingDueDates.EndOfLocalDayUtc(new DateTime(year, 12, 31), department));
		}

		/// <summary>
		/// The amount asked of a member who entered the audience during the period: the share of the period left from their entry,
		/// rounded up (to half an hour for hours, to a whole evaluation for counts), never below one unit.
		/// </summary>
		public static decimal Prorate(decimal required, DateTime enteredOnUtc, DateTime periodStartUtc, DateTime periodEndUtc, bool hours)
		{
			if (required <= 0m || enteredOnUtc <= periodStartUtc)
				return required;
			var span = (periodEndUtc - periodStartUtc).TotalDays;
			if (span <= 0)
				return required;
			var left = Math.Max(0d, (periodEndUtc - enteredOnUtc).TotalDays);
			var share = (decimal)(left / span);
			var raw = required * share;
			var rounded = hours ? Math.Ceiling(raw * 2m) / 2m : Math.Ceiling(raw);
			var unit = hours ? 0.5m : 1m;
			return Math.Min(required, Math.Max(unit, rounded));
		}

		/// <summary>When the newest items that make up <paramref name="required"/> stop counting (their oldest leaves the window); null when they never did.</summary>
		private static DateTime? DropsBelowOn(IEnumerable<LmsRequirementFact> facts, decimal required, int months, Department department)
		{
			var total = 0m;
			foreach (var fact in facts.OrderByDescending(f => f.CompletedOn))
			{
				total += fact.Amount;
				if (total >= required)
					return ValidUntil(fact.CompletedOn, months, department);
			}

			return null;
		}

		private static bool BehindPace(LmsRequirementStatus status, DateTime asOf)
		{
			if (!status.EffectiveDueUtc.HasValue || status.Required <= 0m)
				return false;
			var span = (status.EffectiveDueUtc.Value - status.PeriodStartUtc).TotalSeconds;
			if (span <= 0)
				return false;
			var expected = (decimal)Math.Min(1d, Math.Max(0d, (asOf - status.PeriodStartUtc).TotalSeconds / span));
			var done = Math.Min(1m, status.Progress / status.Required);
			return expected >= 0.5m && done + PaceTolerance < expected;
		}

		private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
	}

	/// <summary>A role a member would lose because an enforcement expired the certification it requires.</summary>
	public sealed class LmsRoleImpact
	{
		public int PersonnelRoleId { get; set; }

		public string RoleName { get; set; }

		public string UserId { get; set; }

		public int LmsRequirementId { get; set; }

		public int PersonnelCertificationId { get; set; }
	}

	/// <summary>A role whose holders one run would thin out by more than the allowed share (R-L11).</summary>
	public sealed class LmsRoleBreach
	{
		public int PersonnelRoleId { get; set; }

		public string RoleName { get; set; }

		public int Losing { get; set; }

		public int Holders { get; set; }

		public decimal SharePercent => Holders <= 0 ? 100m : Math.Round(Losing * 100m / Holders, 1);
	}

	/// <summary>
	/// The blast-radius guard (R-L11) and the role impact of an enforcement, as pure functions. Role removal reaches staffing and run
	/// cards, so one run never thins a role by more than the configured share of its active holders: the whole run is refused instead
	/// and the department admins are told.
	/// </summary>
	public static class LmsEnforcementGuard
	{
		public const int DefaultMaxSharePercent = 20;

		/// <summary>The roles one run would thin by more than <paramref name="maxSharePercent"/> of their holders (strictly more).</summary>
		public static List<LmsRoleBreach> Check(IEnumerable<LmsRoleImpact> impacts, IReadOnlyDictionary<int, int> activeHoldersByRole, int maxSharePercent)
		{
			var share = Math.Clamp(maxSharePercent, 1, 100);
			var breaches = new List<LmsRoleBreach>();
			foreach (var role in (impacts ?? Enumerable.Empty<LmsRoleImpact>()).Where(i => i != null).GroupBy(i => i.PersonnelRoleId))
			{
				var losing = role.Select(i => i.UserId).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
				var holders = activeHoldersByRole != null && activeHoldersByRole.TryGetValue(role.Key, out var count) ? count : 0;
				if (losing > 0 && (holders <= 0 || losing * 100 > holders * share))
					breaches.Add(new LmsRoleBreach { PersonnelRoleId = role.Key, RoleName = role.First().RoleName, Losing = losing, Holders = Math.Max(holders, losing) });
			}

			return breaches;
		}

		/// <summary>
		/// The roles (of <paramref name="roleIds"/>) the member is qualified for now and would not be with <paramref name="expiringRecordId"/>
		/// expired, under the department's role certification requirements (the single validity rule of the certification module).
		/// </summary>
		public static List<int> RolesLost(string userId, IEnumerable<int> roleIds, IReadOnlyList<PersonnelRoleCertificationRequirement> requirements,
			IReadOnlyList<PersonnelCertification> records, IReadOnlyDictionary<int, DepartmentCertificationType> types, DepartmentCertificationSettings settings,
			int expiringRecordId, DateTime onDate)
		{
			var lost = new List<int>();
			if (requirements == null || requirements.Count == 0 || records == null)
				return lost;

			var after = records.Select(r => r.PersonnelCertificationId == expiringRecordId ? ExpiredCopy(r, onDate) : r).ToList();
			foreach (var roleId in (roleIds ?? Enumerable.Empty<int>()).Distinct())
			{
				if (!requirements.Any(r => r.PersonnelRoleId == roleId && r.IsMandatory))
					continue;
				var before = CertificationRequirementEvaluator.Evaluate(roleId, userId, requirements, records, types, settings, onDate);
				if (!before.Qualified)
					continue;
				var then = CertificationRequirementEvaluator.Evaluate(roleId, userId, requirements, after, types, settings, onDate);
				if (!then.Qualified)
					lost.Add(roleId);
			}

			return lost;
		}

		private static PersonnelCertification ExpiredCopy(PersonnelCertification record, DateTime onDate)
		{
			return new PersonnelCertification
			{
				PersonnelCertificationId = record.PersonnelCertificationId,
				DepartmentId = record.DepartmentId,
				UserId = record.UserId,
				DepartmentCertificationTypeId = record.DepartmentCertificationTypeId,
				Status = (int)PersonnelCertificationStatuses.Expired,
				ExpiresOn = record.ExpiresOn.HasValue && record.ExpiresOn.Value.Date < onDate.Date ? record.ExpiresOn : onDate.Date,
				IsDeleted = record.IsDeleted
			};
		}
	}
}
