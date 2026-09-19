using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Certifications
{
	/// <summary>
	/// The single validity rule (Workforce &amp; Business Operations plan, Phase D1.7), used identically by role add
	/// validation, the nightly worker, the Phase C wizard and the compliance report so none of them drift.
	///
	/// A requirement is satisfied when the member holds a non-deleted typed record of the required type whose status is
	/// Active (or Trainee when the requirement allows trainees, or PendingVerification when the department treats it as
	/// valid) and which either never expires or expires on or after the evaluation date. Rows sharing a non-null
	/// AnyOfGroup are one OR-set satisfied by any member; null rows are AND requirements. Pure: no clock, no I/O.
	/// </summary>
	public static class CertificationRequirementEvaluator
	{
		/// <summary>A record's validity against one requirement on a given date.</summary>
		public static bool IsRecordValid(PersonnelCertification record, DepartmentCertificationType type, DateTime onDate,
			bool allowTrainee, bool treatPendingVerificationAsValid)
		{
			if (record == null || type == null || record.IsDeleted || !record.IsTyped || record.DepartmentCertificationTypeId != type.DepartmentCertificationTypeId)
				return false;

			var status = (PersonnelCertificationStatuses)record.Status;
			var statusOk = status == PersonnelCertificationStatuses.Active
				|| (allowTrainee && status == PersonnelCertificationStatuses.Trainee)
				|| (treatPendingVerificationAsValid && status == PersonnelCertificationStatuses.PendingVerification);
			if (!statusOk)
				return false;

			if (type.NeverExpires)
				return true;

			// A typed record with no expiry on a type that does expire is treated as open-ended: the department chose not
			// to record one. Expiry is compared by date, not instant, so a card valid "through" its printed date stays valid.
			return !record.ExpiresOn.HasValue || record.ExpiresOn.Value.Date >= onDate.Date;
		}

		/// <summary>
		/// Evaluates one member against a role's requirements. <paramref name="records"/> are that member's records (any
		/// scope; untyped and deleted rows are ignored). <paramref name="memberSince"/> anchors the grace window for a
		/// requirement the member never satisfied; null means "no anchor" and the outcome carries no violation start.
		/// </summary>
		public static RoleCertificationEvaluation Evaluate(int personnelRoleId, string userId,
			IReadOnlyList<PersonnelRoleCertificationRequirement> requirements,
			IReadOnlyList<PersonnelCertification> records,
			IReadOnlyDictionary<int, DepartmentCertificationType> types,
			DepartmentCertificationSettings settings, DateTime onDate, DateTime? memberSince = null)
		{
			var evaluation = new RoleCertificationEvaluation { PersonnelRoleId = personnelRoleId, UserId = userId, Qualified = true };
			var reqs = (requirements ?? Array.Empty<PersonnelRoleCertificationRequirement>()).Where(r => r != null && r.PersonnelRoleId == personnelRoleId).ToList();
			if (reqs.Count == 0)
				return evaluation;

			var pendingOk = settings?.TreatPendingVerificationAsValid == true;
			var defaultGrace = settings?.RoleRemovalGraceDays ?? DepartmentCertificationSettings.DefaultRoleRemovalGraceDays;
			var mine = (records ?? Array.Empty<PersonnelCertification>()).Where(r => r != null && !r.IsDeleted && r.IsTyped && (userId == null || r.UserId == userId)).ToList();

			// Outcome per requirement row, then OR-sets collapse: a group passes when any of its rows passes.
			var outcomes = new List<CertificationRequirementOutcome>();
			foreach (var req in reqs)
			{
				types.TryGetValue(req.DepartmentCertificationTypeId, out var type);
				var candidates = mine.Where(r => r.DepartmentCertificationTypeId == req.DepartmentCertificationTypeId).ToList();
				var valid = candidates.Where(r => IsRecordValid(r, type, onDate, req.AllowTrainee, pendingOk))
					.OrderByDescending(r => r.ExpiresOn ?? DateTime.MaxValue).FirstOrDefault();
				var outcome = new CertificationRequirementOutcome
				{
					PersonnelRoleCertificationRequirementId = req.PersonnelRoleCertificationRequirementId,
					DepartmentCertificationTypeId = req.DepartmentCertificationTypeId,
					TypeCode = type?.Code,
					TypeName = type?.Type,
					IsMandatory = req.IsMandatory,
					AnyOfGroup = req.AnyOfGroup,
					Satisfied = valid != null,
					PersonnelCertificationId = valid?.PersonnelCertificationId,
					ExpiresOn = valid?.ExpiresOn,
					GraceDays = Math.Max(0, req.GraceDaysOverride ?? defaultGrace)
				};
				if (valid == null)
				{
					// Violation anchor: the latest expiry among the member's records of this type (the card they let lapse);
					// else the membership date when the caller knows it; else the day the requirement was added (role
					// memberships carry no date, so a member who never held the type starts their grace when the rule did).
					var lapsed = candidates.Where(r => r.ExpiresOn.HasValue && (type == null || !type.NeverExpires)).OrderByDescending(r => r.ExpiresOn).FirstOrDefault();
					outcome.ViolationStartedOn = lapsed?.ExpiresOn?.Date ?? memberSince?.Date ?? (req.AddedOn == default ? (DateTime?)null : req.AddedOn.Date);
				}
				outcomes.Add(outcome);
			}

			var failing = new List<CertificationRequirementOutcome>();
			foreach (var group in outcomes.GroupBy(o => o.AnyOfGroup))
			{
				if (group.Key.HasValue)
				{
					if (group.Any(o => o.Satisfied))
						continue;
					failing.AddRange(group);
				}
				else
					failing.AddRange(group.Where(o => !o.Satisfied));
			}

			evaluation.Outcomes = outcomes;
			evaluation.Violations = failing;
			var mandatory = failing.Where(f => f.IsMandatory).ToList();
			evaluation.Qualified = mandatory.Count == 0;
			evaluation.WarningsOnly = mandatory.Count == 0 && failing.Count > 0;
			evaluation.RemovalDueOn = mandatory.Where(f => f.ViolationStartedOn.HasValue)
				.Select(f => (DateTime?)f.ViolationStartedOn.Value.Date.AddDays(f.GraceDays)).Min();
			return evaluation;
		}

		/// <summary>True once the evaluation date is past the grace deadline (removal happens on day grace + 1, plan D5.3).</summary>
		public static bool IsPastGrace(DateTime? removalDueOn, DateTime onDate)
			=> removalDueOn.HasValue && onDate.Date > removalDueOn.Value.Date;

		/// <summary>Days from <paramref name="onDate"/> to the record's expiry date; null when it never expires or has no expiry.</summary>
		public static int? DaysUntilExpiry(DateTime? expiresOn, bool neverExpires, DateTime onDate)
			=> neverExpires || !expiresOn.HasValue ? (int?)null : (int)(expiresOn.Value.Date - onDate.Date).TotalDays;
	}
}
