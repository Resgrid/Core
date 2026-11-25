using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;

namespace Resgrid.Services.Wellness
{
	/// <summary>
	/// NFPA 1582 medical clearance (§13b WL1): a certification of the department's clearance type carries the evaluation date and the next
	/// evaluation (so certification expiry reminders and role requirements see it), and this record carries the restriction status. "Not
	/// cleared" suspends that certification so a role requiring the clearance sees the member as unqualified; the reason never leaves Wellness.
	/// </summary>
	public partial class MemberHealthService
	{
		public async Task<RmsMedicalClearance> RecordClearanceAsync(int departmentId, string officerUserId, MedicalClearanceInput input, CancellationToken cancellationToken = default)
		{
			if (input == null)
				throw new ArgumentNullException(nameof(input));
			await RequireHealthOfficerAsync(departmentId, officerUserId);
			var memberUserId = input.UserId?.Trim();
			if (!await IsCurrentMemberAsync(departmentId, memberUserId))
				throw new MemberWellnessException("member_not_found");
			if (!MemberHealthRules.IsKnownClearanceStatus(input.Status))
				throw new MemberWellnessException("clearance_status_required");
			ValidateDate(input.EvaluatedOn, "date_invalid");
			var evaluated = input.EvaluatedOn.Date;
			if (input.ValidUntil.HasValue && input.ValidUntil.Value.Date <= evaluated)
				throw new MemberWellnessException("valid_until_invalid");

			var now = DateTime.UtcNow;
			var row = new RmsMedicalClearance
			{
				RmsMedicalClearanceId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				UserId = memberUserId,
				Status = input.Status,
				EvaluatedOn = DateTime.SpecifyKind(evaluated, DateTimeKind.Unspecified),
				ValidUntil = input.ValidUntil.HasValue ? DateTime.SpecifyKind(input.ValidUntil.Value.Date, DateTimeKind.Unspecified) : (DateTime?)null,
				RestrictionDetail = Trim(input.RestrictionDetail, MemberHealthRules.NotesMax),
				CreatedOn = now,
				CreatedByUserId = officerUserId,
				ModifiedOn = now,
				ModifiedByUserId = officerUserId,
				RowVersion = 1
			};

			row.PersonnelCertificationId = await RecordClearanceCertificationAsync(departmentId, officerUserId, row, cancellationToken);
			await ProtectAsync(departmentId, row, null, cancellationToken);
			await _clearances.InsertAsync(row, cancellationToken, true);
			Audit(departmentId, officerUserId, AuditLogTypes.MedicalClearanceRecorded, null, Snapshot(row));
			return row;
		}

		/// <summary>
		/// The certification of the evaluation when the department names a clearance type: received on the evaluation date, expiring at the
		/// next evaluation (or after the type's validity), suspended when not cleared. Its status reason is a code, never the restriction.
		/// </summary>
		private async Task<int?> RecordClearanceCertificationAsync(int departmentId, string officerUserId, RmsMedicalClearance row, CancellationToken cancellationToken)
		{
			var settings = await _wellness.GetSettingsAsync(departmentId) ?? new WellnessSettings();
			if (settings.MedicalClearanceCertificationTypeId <= 0)
				return null;
			var type = await _certificationService.Value.GetCertificationTypeByIdAsync(settings.MedicalClearanceCertificationTypeId);
			if (type == null || type.DepartmentId != departmentId)
				return null;

			var expires = row.ValidUntil ?? (type.NeverExpires || !type.DefaultValidityMonths.HasValue ? (DateTime?)null : row.EvaluatedOn.AddMonths(type.DefaultValidityMonths.Value));
			var notCleared = row.Status == (int)MedicalClearanceStatuses.NotCleared;
			var certification = new PersonnelCertification
			{
				DepartmentId = departmentId,
				UserId = row.UserId,
				Name = type.Type,
				Type = type.Type,
				DepartmentCertificationTypeId = type.DepartmentCertificationTypeId,
				IssuedBy = type.IssuingAuthority,
				RecievedOn = row.EvaluatedOn,
				ExpiresOn = expires,
				Status = (int)(notCleared ? PersonnelCertificationStatuses.Suspended : PersonnelCertificationStatuses.Active),
				StatusChangedOn = DateTime.UtcNow,
				StatusChangedByUserId = officerUserId,
				StatusReason = notCleared ? "not_cleared" : null
			};
			var saved = await _certificationService.Value.SaveCertificationAsync(certification, cancellationToken);
			row.ValidUntil ??= expires.HasValue ? DateTime.SpecifyKind(expires.Value.Date, DateTimeKind.Unspecified) : (DateTime?)null;
			return saved?.PersonnelCertificationId;
		}

		public async Task<List<RmsMedicalClearance>> GetClearancesForMemberAsync(int departmentId, string viewerUserId, string memberUserId)
		{
			if (!await CanReadAsync(departmentId, viewerUserId, memberUserId, false))
				throw new UnauthorizedAccessException("Medical clearances are restricted to the member and the health officers.");
			var rows = (await _clearances.GetForUserAsync(departmentId, memberUserId) ?? Enumerable.Empty<RmsMedicalClearance>()).ToList();
			await RevealAsync(departmentId, rows);
			return rows;
		}

		public async Task<List<RmsMedicalClearance>> GetCurrentClearancesAsync(int departmentId, string officerUserId)
		{
			await RequireHealthOfficerAsync(departmentId, officerUserId);
			var active = await _departmentsService.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var rows = (await _clearances.GetActiveForDepartmentAsync(departmentId) ?? Enumerable.Empty<RmsMedicalClearance>())
				.Where(c => !string.IsNullOrWhiteSpace(c.UserId) && active.Contains(c.UserId))
				.GroupBy(c => c.UserId, StringComparer.OrdinalIgnoreCase)
				.Select(g => MemberHealthRules.Current(g))
				.Where(c => c != null)
				.ToList();
			Blank(rows, MemberHealthProtectedFields.Clearances);
			return rows;
		}

		public async Task<RmsMedicalClearance> VoidClearanceAsync(int departmentId, string officerUserId, string clearanceId, CancellationToken cancellationToken = default)
		{
			await RequireHealthOfficerAsync(departmentId, officerUserId);
			var row = string.IsNullOrWhiteSpace(clearanceId) ? null : await _clearances.GetByIdForDepartmentAsync(departmentId, clearanceId.Trim());
			if (row == null)
				throw new MemberWellnessException("clearance_not_found");
			if (row.IsVoided)
				return row;
			var before = Snapshot(row);
			var now = DateTime.UtcNow;
			row.VoidedOn = now;
			row.VoidedByUserId = officerUserId;
			row.ModifiedOn = now;
			row.ModifiedByUserId = officerUserId;
			row.RowVersion++;
			await _clearances.UpdateAsync(row, cancellationToken, true);
			// The evaluation's certification goes with it (soft delete): a voided clearance qualifies nobody.
			if (row.PersonnelCertificationId.HasValue)
			{
				try { await _certificationService.Value.SoftDeleteCertificationAsync(row.PersonnelCertificationId.Value, departmentId, officerUserId, cancellationToken); }
				catch (Exception ex) { Logging.LogException(ex, $"The certification of voided clearance {row.RmsMedicalClearanceId} could not be removed."); }
			}
			Audit(departmentId, officerUserId, AuditLogTypes.MedicalClearanceVoided, before, Snapshot(row));
			return row;
		}

		internal static string Snapshot(RmsMedicalClearance row)
		{
			if (row == null)
				return null;
			return JsonConvert.SerializeObject(new
			{
				id = row.RmsMedicalClearanceId,
				user = row.UserId,
				certification = row.PersonnelCertificationId,
				status = MemberHealthRules.IsKnownClearanceStatus(row.Status) ? ((MedicalClearanceStatuses)row.Status).ToString() : null,
				evaluated = Code(row.EvaluatedOn),
				valid_until = Code(row.ValidUntil),
				voided = Code(row.VoidedOn),
				restriction_detail = !string.IsNullOrEmpty(row.RestrictionDetail)
			});
		}

		#region GDPR

		public async Task<object> BuildGdprExportAsync(string userId, int departmentId)
		{
			// Unattended: no grant, so a sealed value stays an envelope and the archive's sanitizer redacts it. Only the member's own rows.
			var cases = (await _incidents.GetForUserAsync(departmentId, userId) ?? Enumerable.Empty<RmsExposureIncident>()).Where(c => Same(c.UserId, userId)).ToList();
			var injuries = (await _injuries.GetForUserAsync(departmentId, userId) ?? Enumerable.Empty<RmsMemberInjury>()).Where(i => Same(i.UserId, userId)).ToList();
			var periods = (await _periods.GetForInjuriesAsync(departmentId, injuries.Select(i => i.RmsMemberInjuryId)) ?? Enumerable.Empty<RmsInjuryDutyPeriod>()).ToList();
			var clearances = (await _clearances.GetForUserAsync(departmentId, userId) ?? Enumerable.Empty<RmsMedicalClearance>()).Where(c => Same(c.UserId, userId)).ToList();

			return new
			{
				ExposureCases = cases.Select(c => new
				{
					c.RmsExposureIncidentId,
					c.ResponderExposureId,
					c.CallId,
					Route = Enum.IsDefined(typeof(ExposureIncidentRoutes), c.Route) ? ((ExposureIncidentRoutes)c.Route).ToString() : null,
					Stage = ExposureIncidentWorkflow.IsKnownStage(c.Stage) ? ((ExposureIncidentStages)c.Stage).ToString() : null,
					c.OccurredOn,
					c.ReportedOn,
					c.SourceFacility,
					c.SourceRequestedOn,
					c.NotificationRequestedOn,
					c.NotificationReceivedOn,
					c.FollowUpStartedOn,
					c.ClosedOn,
					c.SourceFollowUpNotes,
					c.NotificationResponse,
					c.MedicalNotes,
					c.ClosureNotes
				}).ToList(),
				Injuries = injuries.Select(i => new
				{
					i.RmsMemberInjuryId,
					CaseNumber = OshaRules.CaseNumber(i.OshaYear, i.OshaCaseNumber),
					i.CallId,
					i.OccurredOn,
					State = Enum.IsDefined(typeof(MemberInjuryStates), i.State) ? ((MemberInjuryStates)i.State).ToString() : null,
					DutyStatus = MemberHealthRules.IsKnownDutyStatus(i.DutyStatus) ? ((MemberDutyStatuses)i.DutyStatus).ToString() : null,
					i.ExpectedReturnOn,
					i.ReturnedToDutyOn,
					i.IsRecordable,
					i.Location,
					i.JobTitle,
					i.Description,
					i.InjuryDetails,
					i.Restrictions,
					i.TreatmentDetails,
					i.ClearanceNotes,
					DutyPeriods = periods.Where(p => Same(p.InjuryId, i.RmsMemberInjuryId)).Select(p => new
					{
						DutyStatus = MemberHealthRules.IsKnownDutyStatus(p.DutyStatus) ? ((MemberDutyStatuses)p.DutyStatus).ToString() : null,
						p.StartsOn,
						p.EndsOn
					}).ToList()
				}).ToList(),
				MedicalClearances = clearances.Select(c => new
				{
					c.RmsMedicalClearanceId,
					Status = MemberHealthRules.IsKnownClearanceStatus(c.Status) ? ((MedicalClearanceStatuses)c.Status).ToString() : null,
					c.EvaluatedOn,
					c.ValidUntil,
					c.RestrictionDetail,
					c.VoidedOn
				}).ToList()
			};
		}

		#endregion GDPR
	}
}
