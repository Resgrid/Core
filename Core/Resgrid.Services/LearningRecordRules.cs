using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Services
{
	/// <summary>The pure rules of the one transcript (§12.2), kept apart from the writer so they are tested directly.</summary>
	public static class LearningRecordRules
	{
		public const int MaxTitleLength = 250;

		/// <summary>
		/// Which linked source of one session writes the hours (R-T2): instructor sign-off &gt; Training Record participant &gt;
		/// check-in/out &gt; course completion. Sources outside the chain (external submission, manual credit, import) return 0
		/// and never compete.
		/// </summary>
		public static int Precedence(int sourceKind)
		{
			switch ((LearningRecordSourceKinds)sourceKind)
			{
				case LearningRecordSourceKinds.ClassSession:
				case LearningRecordSourceKinds.SkillEvaluation:
				case LearningRecordSourceKinds.TaskBookSignoff:
					return 4;
				case LearningRecordSourceKinds.TrainingRecord:
					return 3;
				case LearningRecordSourceKinds.CalendarAttendance:
					return 2;
				case LearningRecordSourceKinds.CourseCompletion:
					return 1;
				default:
					return 0;
			}
		}

		/// <summary>Source ref ids, one per source instance (never the course or training id alone, R-T1).</summary>
		public static string CompletionRef(int trainingId, int cycle) => $"training:{trainingId}:{cycle}";

		public static string LogRef(int logId) => $"log:{logId}";

		public static string RecordsRef(string rmsRecordId) => $"rms:{rmsRecordId}";

		public static string CheckInRef(string checkInId) => $"checkin:{checkInId}";

		public static string CreditRef(int personnelCertificationCreditId) => $"credit:{personnelCertificationCreditId}";

		public static string SubmissionRef(Guid id) => $"submission:{id:N}";

		public static string CalendarSession(int calendarItemId) => $"calendar:{calendarItemId}";

		/// <summary>Hours from a start and an end (0 when either is missing or the end is not after the start), 2 decimals.</summary>
		public static decimal Hours(DateTime? start, DateTime? end)
		{
			if (!start.HasValue || !end.HasValue || end.Value <= start.Value)
				return 0m;
			return Math.Round((decimal)(end.Value - start.Value).TotalHours, 2, MidpointRounding.AwayFromZero);
		}

		public static string Trim(string value, int max)
		{
			var trimmed = value?.Trim();
			return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > max ? trimmed.Substring(0, max) : trimmed;
		}

		/// <summary>
		/// True when two records of one member are possible duplicates (R-T2): different, unlinked sources whose session times
		/// overlap on the same day. Records without both times, without hours, voided ones and records of one session are
		/// never flagged.
		/// </summary>
		public static bool IsPossibleDuplicate(LearningRecord a, LearningRecord b)
		{
			if (a == null || b == null || a.IsVoided || b.IsVoided)
				return false;
			if (a.LearningRecordId != 0 && a.LearningRecordId == b.LearningRecordId)
				return false;
			if (!string.Equals(a.UserId, b.UserId, StringComparison.OrdinalIgnoreCase))
				return false;
			if (a.SourceKind == b.SourceKind && string.Equals(a.SourceRefId, b.SourceRefId, StringComparison.Ordinal))
				return false;
			if (!string.IsNullOrEmpty(a.SessionKey) && string.Equals(a.SessionKey, b.SessionKey, StringComparison.Ordinal))
				return false;
			if (a.Hours <= 0 || b.Hours <= 0)
				return false;
			if (!a.StartedOn.HasValue || !a.EndedOn.HasValue || !b.StartedOn.HasValue || !b.EndedOn.HasValue)
				return false;
			if (a.StartedOn.Value.Date != b.StartedOn.Value.Date)
				return false;

			return a.StartedOn.Value < b.EndedOn.Value && b.StartedOn.Value < a.EndedOn.Value;
		}

		/// <summary>
		/// The certification record whose cycle contains <paramref name="completedOn"/> (R-T5): received on or before it and
		/// expiring on or after it (open ends allowed). When several do, the most recently received wins. Deleted records and
		/// records of other types never match; null means the date is outside every cycle (no credit).
		/// </summary>
		public static PersonnelCertification FindCycle(IEnumerable<PersonnelCertification> records, int departmentCertificationTypeId, DateTime completedOn)
		{
			var day = completedOn.Date;
			return (records ?? Enumerable.Empty<PersonnelCertification>())
				.Where(r => r != null && !r.IsDeleted && r.DepartmentCertificationTypeId == departmentCertificationTypeId &&
					r.Status != (int)PersonnelCertificationStatuses.Revoked &&
					(!r.RecievedOn.HasValue || r.RecievedOn.Value.Date <= day) &&
					(!r.ExpiresOn.HasValue || r.ExpiresOn.Value.Date >= day))
				.OrderByDescending(r => r.RecievedOn ?? DateTime.MinValue)
				.ThenByDescending(r => r.PersonnelCertificationId)
				.FirstOrDefault();
		}

		/// <summary>True when the mapping derives a credit from the record (category, component, enabled, and since when).</summary>
		public static bool MappingApplies(CategoryCreditMapping mapping, LearningRecord record, bool ignoreEnabledOn)
		{
			if (mapping == null || record == null || !mapping.IsEnabled || !record.CategoryId.HasValue || mapping.CategoryId != record.CategoryId.Value)
				return false;
			if (!string.IsNullOrWhiteSpace(mapping.ComponentKey) && !string.Equals(mapping.ComponentKey, record.ComponentKey ?? string.Empty, StringComparison.OrdinalIgnoreCase))
				return false;
			if (ignoreEnabledOn)
				return true;
			return mapping.EnabledOn.HasValue && record.CompletedOn >= mapping.EnabledOn.Value.Date;
		}

		/// <summary>A record that may become a certification credit at all: verified, with hours and a category, not a manual credit.</summary>
		public static bool CanCredit(LearningRecord record) =>
			record != null && record.Counts && record.Hours > 0 && record.CategoryId.HasValue &&
			record.SourceKind != (int)LearningRecordSourceKinds.ManualCredit;

		/// <summary>True when a stored record says the same as a draft (nothing to supersede, R-T4).</summary>
		public static bool SameAs(LearningRecord stored, LearningRecordDraft draft)
		{
			if (stored == null || draft == null)
				return false;

			return string.Equals(stored.Title ?? string.Empty, Trim(draft.Title, MaxTitleLength) ?? string.Empty, StringComparison.Ordinal) &&
				string.Equals(stored.CourseCode ?? string.Empty, Trim(draft.CourseCode, 100) ?? string.Empty, StringComparison.Ordinal) &&
				SameMoment(stored.CompletedOn, draft.CompletedOn) &&
				SameMoment(stored.StartedOn, draft.StartedOn) &&
				SameMoment(stored.EndedOn, draft.EndedOn) &&
				stored.Hours == Math.Round(Math.Max(0m, draft.Hours), 2, MidpointRounding.AwayFromZero) &&
				stored.CeHours == draft.CeHours &&
				(!draft.CategoryId.HasValue || stored.CategoryId == draft.CategoryId) &&
				stored.Status == (int)draft.Status &&
				string.Equals(stored.SessionKey ?? string.Empty, draft.SessionKey ?? string.Empty, StringComparison.Ordinal);
		}

		private static bool SameMoment(DateTime? a, DateTime? b)
		{
			if (!a.HasValue || !b.HasValue)
				return a.HasValue == b.HasValue;
			return Math.Abs((a.Value - b.Value).TotalSeconds) < 1;
		}
	}
}
