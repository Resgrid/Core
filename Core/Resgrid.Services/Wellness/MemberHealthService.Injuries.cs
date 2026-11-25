using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;

namespace Resgrid.Services.Wellness
{
	/// <summary>
	/// Injury → return to duty (§13b WL1) and the OSHA 300/301 shapes. Duty status is operational: supervisors and staffing read it through
	/// <see cref="GetDutyBoardAsync"/> and <see cref="GetDutyStatusAsync"/>, which never carry a reason. The injury record itself is in the
	/// restricted tier. Day counts come from the duty periods (<see cref="OshaRules.CountDays"/>).
	/// </summary>
	public partial class MemberHealthService
	{
		#region Report and read

		public async Task<RmsMemberInjury> ReportInjuryAsync(int departmentId, string actorUserId, MemberInjuryInput input, CancellationToken cancellationToken = default)
		{
			if (input == null)
				throw new ArgumentNullException(nameof(input));
			if (string.IsNullOrWhiteSpace(actorUserId))
				throw new UnauthorizedAccessException("A signed-in member is required.");

			var memberUserId = string.IsNullOrWhiteSpace(input.UserId) ? actorUserId : input.UserId.Trim();
			var access = await GetAccessAsync(departmentId, actorUserId);
			if (!access.IsMember || (!Same(actorUserId, memberUserId) && !access.IsHealthOfficer))
				throw new UnauthorizedAccessException("Only the member or a health officer reports an injury.");
			if (!await IsCurrentMemberAsync(departmentId, memberUserId))
				throw new MemberWellnessException("member_not_found");
			ValidateDate(input.OccurredOn, "date_invalid");
			await ValidateCallAsync(departmentId, input.CallId);
			var incident = await LinkedIncidentAsync(departmentId, memberUserId, input.ExposureIncidentId);

			var now = DateTime.UtcNow;
			var department = await DepartmentAsync(departmentId);
			var occurred = DateTime.SpecifyKind(input.OccurredOn, DateTimeKind.Utc);
			var year = LocalDate(occurred, department).Year;
			var row = new RmsMemberInjury
			{
				RmsMemberInjuryId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				UserId = memberUserId,
				CallId = input.CallId ?? incident?.CallId,
				ExposureIncidentId = incident?.RmsExposureIncidentId,
				OccurredOn = occurred,
				ReportedOn = now,
				ReportedByUserId = actorUserId,
				OshaYear = year,
				Location = Trim(input.Location, MemberHealthRules.LocationMax),
				JobTitle = Trim(input.JobTitle, MemberHealthRules.JobTitleMax),
				State = (int)MemberInjuryStates.Open,
				DutyStatus = (int)MemberDutyStatuses.FullDuty,
				Description = Trim(input.Description, MemberHealthRules.NotesMax),
				InjuryDetails = Trim(input.InjuryDetails, MemberHealthRules.NotesMax),
				TreatmentDetails = Trim(input.TreatmentDetails, MemberHealthRules.NotesMax),
				OshaInjuryType = (int)OshaInjuryTypes.Injury,
				CreatedOn = now,
				CreatedByUserId = actorUserId,
				ModifiedOn = now,
				ModifiedByUserId = actorUserId,
				RowVersion = 1
			};
			// The OSHA classification is an officer's call; a member's own report waits for it.
			if (access.IsHealthOfficer)
				ApplyOsha(row, input);
			if (incident != null)
				row.IsPrivacyCase = true;

			await ProtectAsync(departmentId, row, null, cancellationToken);
			await InsertWithCaseNumberAsync(row, cancellationToken);
			Audit(departmentId, actorUserId, AuditLogTypes.MemberInjuryReported, null, Snapshot(row));

			var name = await NameOfAsync(departmentId, memberUserId);
			await NotifyAsync(departmentId, (await HealthOfficersAsync(departmentId)).Where(o => !Same(o, actorUserId)), "Injury reported",
				$"{name} has an injury report from {LocalDate(occurred, department).ToString("d", CultureInfo.InvariantCulture)} to review. {Link("/User/MemberHealth/Injury?id=" + row.RmsMemberInjuryId)}",
				EventTypes.DutyStatusNotice, row.RmsMemberInjuryId, cancellationToken);
			return row;
		}

		/// <summary>Takes the next case number of the year; a concurrent report that took it first makes this one try the next.</summary>
		private async Task InsertWithCaseNumberAsync(RmsMemberInjury row, CancellationToken cancellationToken)
		{
			for (var attempt = 0; ; attempt++)
			{
				row.OshaCaseNumber = await _injuries.GetMaxCaseNumberAsync(row.DepartmentId, row.OshaYear) + 1;
				try
				{
					await _injuries.InsertAsync(row, cancellationToken, true);
					return;
				}
				catch (Exception) when (attempt < 3 && !cancellationToken.IsCancellationRequested)
				{
					// UX_RmsMemberInjuries_Case refused a duplicate number: read the maximum again.
				}
			}
		}

		private async Task<RmsExposureIncident> LinkedIncidentAsync(int departmentId, string memberUserId, string incidentId)
		{
			if (string.IsNullOrWhiteSpace(incidentId))
				return null;
			var incident = await _incidents.GetByIdForDepartmentAsync(departmentId, incidentId.Trim());
			if (incident == null || !Same(incident.UserId, memberUserId))
				throw new MemberWellnessException("case_not_found");
			return incident;
		}

		private static void ApplyOsha(RmsMemberInjury row, MemberInjuryInput input)
		{
			row.IsRecordable = input.IsRecordable;
			row.OshaInjuryType = OshaRules.IsKnownInjuryType(input.OshaInjuryType) ? input.OshaInjuryType : (int)OshaInjuryTypes.Injury;
			row.IsPrivacyCase = input.IsPrivacyCase;
			row.TreatedInEmergencyRoom = input.TreatedInEmergencyRoom;
			row.HospitalizedOvernight = input.HospitalizedOvernight;
			row.DiedOn = input.DiedOn.HasValue ? DateTime.SpecifyKind(input.DiedOn.Value.Date, DateTimeKind.Unspecified) : (DateTime?)null;
		}

		public async Task<RmsMemberInjury> GetInjuryAsync(int departmentId, string viewerUserId, string injuryId)
		{
			if (string.IsNullOrWhiteSpace(injuryId))
				return null;
			var row = await _injuries.GetByIdForDepartmentAsync(departmentId, injuryId.Trim());
			if (row == null || !await CanReadAsync(departmentId, viewerUserId, row.UserId, false))
				return null;
			await RevealAsync(departmentId, new List<RmsMemberInjury> { row });
			return row;
		}

		public async Task<List<RmsMemberInjury>> GetInjuriesForMemberAsync(int departmentId, string viewerUserId, string memberUserId)
		{
			if (!await CanReadAsync(departmentId, viewerUserId, memberUserId, false))
				throw new UnauthorizedAccessException("Injury records are restricted to the member and the health officers.");
			var rows = (await _injuries.GetForUserAsync(departmentId, memberUserId) ?? Enumerable.Empty<RmsMemberInjury>()).ToList();
			await RevealAsync(departmentId, rows);
			return rows;
		}

		public async Task<List<RmsMemberInjury>> GetOpenInjuriesAsync(int departmentId, string officerUserId)
		{
			await RequireHealthOfficerAsync(departmentId, officerUserId);
			var rows = (await _injuries.GetOpenForDepartmentAsync(departmentId) ?? Enumerable.Empty<RmsMemberInjury>()).ToList();
			Blank(rows, MemberHealthProtectedFields.Injuries);
			return rows;
		}

		public async Task<List<RmsInjuryDutyPeriod>> GetDutyPeriodsAsync(int departmentId, string viewerUserId, string injuryId)
		{
			var row = string.IsNullOrWhiteSpace(injuryId) ? null : await _injuries.GetByIdForDepartmentAsync(departmentId, injuryId.Trim());
			if (row == null || !await CanReadAsync(departmentId, viewerUserId, row.UserId, false))
				throw new UnauthorizedAccessException("Injury records are restricted to the member and the health officers.");
			return (await _periods.GetForInjuryAsync(departmentId, row.RmsMemberInjuryId) ?? Enumerable.Empty<RmsInjuryDutyPeriod>()).ToList();
		}

		#endregion Report and read

		#region Officer changes

		private async Task<RmsMemberInjury> LoadInjuryForOfficerAsync(int departmentId, string officerUserId, string injuryId)
		{
			await RequireHealthOfficerAsync(departmentId, officerUserId);
			var row = string.IsNullOrWhiteSpace(injuryId) ? null : await _injuries.GetByIdForDepartmentAsync(departmentId, injuryId.Trim());
			if (row == null)
				throw new MemberWellnessException("injury_not_found");
			return row;
		}

		public async Task<RmsMemberInjury> UpdateInjuryAsync(int departmentId, string officerUserId, string injuryId, MemberInjuryInput input, long? expectedRowVersion,
			CancellationToken cancellationToken = default)
		{
			if (input == null)
				throw new ArgumentNullException(nameof(input));
			var row = await LoadInjuryForOfficerAsync(departmentId, officerUserId, injuryId);
			if (row.State == (int)MemberInjuryStates.Voided)
				throw new MemberWellnessException("injury_closed");
			if (expectedRowVersion.HasValue && expectedRowVersion.Value != row.RowVersion)
				throw new MemberWellnessException("injury_changed");
			await ValidateCallAsync(departmentId, input.CallId);
			if (input.DiedOn.HasValue && input.DiedOn.Value.Date < row.OccurredOn.Date.AddDays(-1))
				throw new MemberWellnessException("date_invalid");

			var existing = Clone(row);
			var before = Snapshot(row);
			// The member, the date and the case number stay as reported: the log's case number never moves.
			row.CallId = input.CallId;
			row.Location = Trim(input.Location, MemberHealthRules.LocationMax);
			row.JobTitle = Trim(input.JobTitle, MemberHealthRules.JobTitleMax);
			row.Description = Keep(input.Description, row.Description, MemberHealthRules.NotesMax);
			row.InjuryDetails = Keep(input.InjuryDetails, row.InjuryDetails, MemberHealthRules.NotesMax);
			row.TreatmentDetails = Keep(input.TreatmentDetails, row.TreatmentDetails, MemberHealthRules.NotesMax);
			ApplyOsha(row, input);
			// A contaminated sharps injury from an exposure case stays a privacy case (1904.29(b)(7)(v)).
			if (!string.IsNullOrWhiteSpace(row.ExposureIncidentId))
				row.IsPrivacyCase = true;
			row.ModifiedOn = DateTime.UtcNow;
			row.ModifiedByUserId = officerUserId;
			row.RowVersion++;
			await ProtectAsync(departmentId, row, existing, cancellationToken);
			await _injuries.UpdateAsync(row, cancellationToken, true);
			Audit(departmentId, officerUserId, AuditLogTypes.MemberInjuryUpdated, before, Snapshot(row));
			return await GetInjuryAsync(departmentId, officerUserId, row.RmsMemberInjuryId) ?? row;
		}

		public async Task<RmsMemberInjury> SetDutyStatusAsync(int departmentId, string officerUserId, string injuryId, int dutyStatus, DateTime startsOn, DateTime? expectedReturnOn,
			string restrictions, CancellationToken cancellationToken = default)
		{
			var row = await LoadInjuryForOfficerAsync(departmentId, officerUserId, injuryId);
			if (row.State == (int)MemberInjuryStates.Voided)
				throw new MemberWellnessException("injury_closed");
			if (dutyStatus != (int)MemberDutyStatuses.RestrictedDuty && dutyStatus != (int)MemberDutyStatuses.OffDuty)
				throw new MemberWellnessException("duty_status_required");

			var start = startsOn.Date;
			var department = await DepartmentAsync(departmentId);
			if (start < LocalDate(row.OccurredOn, department) || start > LocalDate(DateTime.UtcNow, department).AddDays(1))
				throw new MemberWellnessException("date_invalid");
			if (expectedReturnOn.HasValue && expectedReturnOn.Value.Date <= start)
				throw new MemberWellnessException("return_date_invalid");

			var periods = (await _periods.GetForInjuryAsync(departmentId, row.RmsMemberInjuryId) ?? Enumerable.Empty<RmsInjuryDutyPeriod>()).ToList();
			var open = periods.Where(p => !p.EndsOn.HasValue).OrderByDescending(p => p.StartsOn).FirstOrDefault();
			if (open != null && start < open.StartsOn.Date)
				throw new MemberWellnessException("date_before_current_status");

			var existing = Clone(row);
			var before = Snapshot(row);
			var now = DateTime.UtcNow;
			await CloseOpenPeriodsAsync(periods, start, cancellationToken);
			await _periods.InsertAsync(new RmsInjuryDutyPeriod
			{
				RmsInjuryDutyPeriodId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				InjuryId = row.RmsMemberInjuryId,
				UserId = row.UserId,
				DutyStatus = dutyStatus,
				StartsOn = DateTime.SpecifyKind(start, DateTimeKind.Unspecified),
				CreatedOn = now,
				CreatedByUserId = officerUserId
			}, cancellationToken, true);

			// A further restriction after a clearance is a recurrence: the injury opens again.
			row.State = (int)MemberInjuryStates.Open;
			row.DutyStatus = dutyStatus;
			row.DutyStatusSince = DateTime.SpecifyKind(start, DateTimeKind.Unspecified);
			row.ExpectedReturnOn = expectedReturnOn.HasValue ? DateTime.SpecifyKind(expectedReturnOn.Value.Date, DateTimeKind.Unspecified) : (DateTime?)null;
			row.ReturnedToDutyOn = null;
			if (restrictions != null)
				row.Restrictions = Keep(restrictions, row.Restrictions, MemberHealthRules.NotesMax);
			row.ModifiedOn = now;
			row.ModifiedByUserId = officerUserId;
			row.RowVersion++;
			await ProtectAsync(departmentId, row, existing, cancellationToken);
			await _injuries.UpdateAsync(row, cancellationToken, true);
			Audit(departmentId, officerUserId, AuditLogTypes.MemberDutyStatusChanged, before, Snapshot(row));
			return await GetInjuryAsync(departmentId, officerUserId, row.RmsMemberInjuryId) ?? row;
		}

		public async Task<RmsMemberInjury> ClearReturnToDutyAsync(int departmentId, string officerUserId, string injuryId, DateTime returnedOn, string clearanceNotes,
			CancellationToken cancellationToken = default)
		{
			var row = await LoadInjuryForOfficerAsync(departmentId, officerUserId, injuryId);
			if (row.State != (int)MemberInjuryStates.Open)
				throw new MemberWellnessException("injury_not_open");

			var returned = returnedOn.Date;
			var department = await DepartmentAsync(departmentId);
			if (returned < LocalDate(row.OccurredOn, department) || returned > LocalDate(DateTime.UtcNow, department).AddDays(1))
				throw new MemberWellnessException("date_invalid");
			var periods = (await _periods.GetForInjuryAsync(departmentId, row.RmsMemberInjuryId) ?? Enumerable.Empty<RmsInjuryDutyPeriod>()).ToList();
			if (periods.Any(p => !p.EndsOn.HasValue && p.StartsOn.Date > returned))
				throw new MemberWellnessException("date_before_current_status");

			var existing = Clone(row);
			var before = Snapshot(row);
			var now = DateTime.UtcNow;
			await CloseOpenPeriodsAsync(periods, returned, cancellationToken);
			row.State = (int)MemberInjuryStates.Cleared;
			row.DutyStatus = (int)MemberDutyStatuses.FullDuty;
			row.DutyStatusSince = DateTime.SpecifyKind(returned, DateTimeKind.Unspecified);
			row.ReturnedToDutyOn = DateTime.SpecifyKind(returned, DateTimeKind.Unspecified);
			row.ExpectedReturnOn = null;
			row.ClearedOn = now;
			row.ClearedByUserId = officerUserId;
			if (clearanceNotes != null)
				row.ClearanceNotes = Trim(clearanceNotes, MemberHealthRules.NotesMax);
			row.ModifiedOn = now;
			row.ModifiedByUserId = officerUserId;
			row.RowVersion++;
			await ProtectAsync(departmentId, row, existing, cancellationToken);
			await _injuries.UpdateAsync(row, cancellationToken, true);
			Audit(departmentId, officerUserId, AuditLogTypes.MemberReturnedToDuty, before, Snapshot(row));
			return await GetInjuryAsync(departmentId, officerUserId, row.RmsMemberInjuryId) ?? row;
		}

		public async Task<RmsMemberInjury> VoidInjuryAsync(int departmentId, string officerUserId, string injuryId, CancellationToken cancellationToken = default)
		{
			var row = await LoadInjuryForOfficerAsync(departmentId, officerUserId, injuryId);
			if (row.State == (int)MemberInjuryStates.Voided)
				return row;
			var before = Snapshot(row);
			var now = DateTime.UtcNow;
			var periods = (await _periods.GetForInjuryAsync(departmentId, row.RmsMemberInjuryId) ?? Enumerable.Empty<RmsInjuryDutyPeriod>()).ToList();
			await CloseOpenPeriodsAsync(periods, LocalDate(now, await DepartmentAsync(departmentId)), cancellationToken);
			row.State = (int)MemberInjuryStates.Voided;
			row.DutyStatus = (int)MemberDutyStatuses.FullDuty;
			row.ExpectedReturnOn = null;
			row.VoidedOn = now;
			row.VoidedByUserId = officerUserId;
			row.ModifiedOn = now;
			row.ModifiedByUserId = officerUserId;
			row.RowVersion++;
			// Only operational columns change: the row is written as it is stored.
			await _injuries.UpdateAsync(row, cancellationToken, true);
			Audit(departmentId, officerUserId, AuditLogTypes.MemberInjuryVoided, before, Snapshot(row));
			return row;
		}

		private async Task CloseOpenPeriodsAsync(IEnumerable<RmsInjuryDutyPeriod> periods, DateTime endsOn, CancellationToken cancellationToken)
		{
			foreach (var open in periods.Where(p => !p.EndsOn.HasValue))
			{
				// A stretch that would end before it began (same-day change) ends on its start: zero days.
				open.EndsOn = DateTime.SpecifyKind(endsOn.Date < open.StartsOn.Date ? open.StartsOn.Date : endsOn.Date, DateTimeKind.Unspecified);
				await _periods.UpdateAsync(open, cancellationToken, true);
			}
		}

		/// <summary>Audit detail: ids, state, duty status, OSHA codes and dates, and which details hold a value. Never a protected value.</summary>
		internal static string Snapshot(RmsMemberInjury row)
		{
			if (row == null)
				return null;
			return JsonConvert.SerializeObject(new
			{
				id = row.RmsMemberInjuryId,
				user = row.UserId,
				call = row.CallId,
				incident = row.ExposureIncidentId,
				case_number = OshaRules.CaseNumber(row.OshaYear, row.OshaCaseNumber),
				state = Enum.IsDefined(typeof(MemberInjuryStates), row.State) ? ((MemberInjuryStates)row.State).ToString() : null,
				duty = MemberHealthRules.IsKnownDutyStatus(row.DutyStatus) ? ((MemberDutyStatuses)row.DutyStatus).ToString() : null,
				since = Code(row.DutyStatusSince),
				expected_return = Code(row.ExpectedReturnOn),
				returned = Code(row.ReturnedToDutyOn),
				recordable = row.IsRecordable,
				privacy_case = row.IsPrivacyCase,
				details = MemberHealthProtectedFields.Injuries.Where(a => !string.IsNullOrEmpty(a.Value.Get(row))).Select(a => a.Key.Substring(a.Key.IndexOf('.') + 1)).ToList()
			});
		}

		#endregion Officer changes

		#region Duty status for supervisors and staffing

		public async Task<MemberDutyStatus> GetDutyStatusAsync(int departmentId, string viewerUserId, string memberUserId)
		{
			if (string.IsNullOrWhiteSpace(memberUserId))
				return null;
			if (!Same(viewerUserId, memberUserId))
			{
				var visible = await VisibleMembersAsync(departmentId, viewerUserId);
				if (visible != null && !visible.Contains(memberUserId))
					throw new UnauthorizedAccessException("Duty status is visible to the member, their supervisors and the health officers.");
			}
			else if (!await IsCurrentMemberAsync(departmentId, viewerUserId))
				throw new UnauthorizedAccessException("Only a current member reads their own duty status.");

			var injuries = (await _injuries.GetForUserAsync(departmentId, memberUserId) ?? Enumerable.Empty<RmsMemberInjury>()).ToList();
			var clearances = (await _clearances.GetForUserAsync(departmentId, memberUserId) ?? Enumerable.Empty<RmsMedicalClearance>()).ToList();
			return Project(memberUserId, injuries, clearances);
		}

		public async Task<List<MemberDutyStatus>> GetDutyBoardAsync(int departmentId, string viewerUserId)
		{
			var visible = await VisibleMembersAsync(departmentId, viewerUserId);
			if (visible != null && visible.Count == 0)
				return new List<MemberDutyStatus>();

			var injuries = (await _injuries.GetOpenForDepartmentAsync(departmentId) ?? Enumerable.Empty<RmsMemberInjury>()).ToList();
			var clearances = (await _clearances.GetActiveForDepartmentAsync(departmentId) ?? Enumerable.Empty<RmsMedicalClearance>()).ToList();
			var active = await _departmentsService.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var userIds = injuries.Select(i => i.UserId).Concat(clearances.Select(c => c.UserId))
				.Where(u => !string.IsNullOrWhiteSpace(u) && active.Contains(u) && (visible == null || visible.Contains(u)))
				.Distinct(StringComparer.OrdinalIgnoreCase);

			return userIds
				.Select(u => Project(u, injuries.Where(i => Same(i.UserId, u)), clearances.Where(c => Same(c.UserId, u))))
				.Where(s => s.DutyStatus != MemberDutyStatuses.FullDuty || (s.ClearanceStatus.HasValue && s.ClearanceStatus.Value != MedicalClearanceStatuses.Cleared))
				.OrderByDescending(s => s.DutyStatus)
				.ThenBy(s => s.ExpectedReturnOn ?? DateTime.MaxValue)
				.ToList();
		}

		/// <summary>The operational projection: the most restrictive open injury, and the current clearance's status. Never a reason.</summary>
		internal static MemberDutyStatus Project(string userId, IEnumerable<RmsMemberInjury> injuries, IEnumerable<RmsMedicalClearance> clearances)
		{
			var open = (injuries ?? Enumerable.Empty<RmsMemberInjury>()).Where(i => i != null && i.IsOpen).ToList();
			var status = MemberHealthRules.DutyStatusOf(open);
			var governing = open.Where(i => i.DutyStatus == (int)status).OrderBy(i => i.DutyStatusSince ?? i.OccurredOn).FirstOrDefault();
			var clearance = MemberHealthRules.Current(clearances);
			return new MemberDutyStatus
			{
				UserId = userId,
				DutyStatus = status,
				DutyStatusSince = status == MemberDutyStatuses.FullDuty ? null : governing?.DutyStatusSince,
				ExpectedReturnOn = status == MemberDutyStatuses.FullDuty ? null : open.Where(i => i.DutyStatus != (int)MemberDutyStatuses.FullDuty).Select(i => i.ExpectedReturnOn).Max(),
				ClearanceStatus = clearance == null || !MemberHealthRules.IsKnownClearanceStatus(clearance.Status) ? (MedicalClearanceStatuses?)null : (MedicalClearanceStatuses)clearance.Status,
				ClearanceValidUntil = clearance?.ValidUntil
			};
		}

		/// <summary>
		/// The members whose duty status the viewer sees: null = everyone (a department admin or a health officer), the viewer's own group for
		/// a group admin, the viewer alone otherwise.
		/// </summary>
		private async Task<HashSet<string>> VisibleMembersAsync(int departmentId, string viewerUserId)
		{
			var access = await GetAccessAsync(departmentId, viewerUserId);
			if (!access.IsMember)
				return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (access.IsAdmin || access.IsHealthOfficer)
				return null;

			var visible = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { viewerUserId };
			if (access.IsSupervisor)
			{
				var membership = await _groupsService.GetGroupMemberForUserAsync(viewerUserId, departmentId);
				if (membership?.IsAdmin == true)
				{
					var groups = await _groupsService.GetGroupIdsForAllUsersInDepartmentAsync(departmentId) ?? new Dictionary<string, int>();
					foreach (var pair in groups.Where(p => p.Value == membership.DepartmentGroupId))
						visible.Add(pair.Key);
				}
			}
			return visible;
		}

		#endregion Duty status for supervisors and staffing

		#region OSHA 300 / 301

		public async Task<OshaLog> BuildOshaLogAsync(int departmentId, string officerUserId, int year, Func<string, string> nameOf)
		{
			await RequireHealthOfficerAsync(departmentId, officerUserId);
			if (year < 1971 || year > DateTime.UtcNow.Year + 1)
				throw new MemberWellnessException("year_invalid");

			var department = await DepartmentAsync(departmentId);
			var rows = OshaRules.OnLog(await _injuries.GetForYearAsync(departmentId, year) ?? Enumerable.Empty<RmsMemberInjury>(), year).ToList();
			var log = new OshaLog { Year = year, EstablishmentName = department?.Name };
			log.ProtectedWithheld = await RevealAsync(departmentId, rows);
			var periods = (await _periods.GetForInjuriesAsync(departmentId, rows.Select(r => r.RmsMemberInjuryId)) ?? Enumerable.Empty<RmsInjuryDutyPeriod>()).ToList();

			// Day counts run to today for a stretch still open: a case that continues into the next year keeps counting on the log of the
			// year it happened, up to the 180-day cap (1904.7(b)(3)).
			var asOf = LocalDate(DateTime.UtcNow, department);
			foreach (var injury in rows)
			{
				var local = DateTime.SpecifyKind(injury.OccurredOn, DateTimeKind.Utc);
				var localOccurred = department == null ? local : local.TimeConverter(department);
				var (away, restricted) = OshaRules.CountDays(localOccurred, periods.Where(p => Same(p.InjuryId, injury.RmsMemberInjuryId)), asOf);
				var name = nameOf?.Invoke(injury.UserId) ?? injury.UserId;
				var number = OshaRules.CaseNumber(injury.OshaYear, injury.OshaCaseNumber);
				log.Rows.Add(new OshaLogRow
				{
					InjuryId = injury.RmsMemberInjuryId,
					CaseNumber = number,
					EmployeeName = OshaRules.LogName(injury, name),
					JobTitle = injury.JobTitle,
					DateOfInjury = localOccurred.Date,
					Location = injury.Location,
					Description = injury.InjuryDetails,
					Outcome = OshaRules.Outcome(injury, away, restricted),
					DaysAway = away,
					DaysRestricted = restricted,
					InjuryType = OshaRules.IsKnownInjuryType(injury.OshaInjuryType) ? (OshaInjuryTypes)injury.OshaInjuryType : OshaInjuryTypes.Injury,
					IsPrivacyCase = injury.IsPrivacyCase
				});
				log.Reports.Add(new Osha301Report
				{
					CaseNumber = number,
					// The 301 is the confidential incident report: it names the member even for a privacy case.
					EmployeeName = name,
					JobTitle = injury.JobTitle,
					DateOfInjury = localOccurred.Date,
					TimeOfEvent = localOccurred.ToString("HH:mm", CultureInfo.InvariantCulture),
					Location = injury.Location,
					WhatHappened = injury.Description,
					InjuryDetails = injury.InjuryDetails,
					TreatmentDetails = injury.TreatmentDetails,
					TreatedInEmergencyRoom = injury.TreatedInEmergencyRoom,
					HospitalizedOvernight = injury.HospitalizedOvernight,
					DiedOn = injury.DiedOn,
					IsPrivacyCase = injury.IsPrivacyCase
				});
			}

			Audit(departmentId, officerUserId, AuditLogTypes.OshaLogExported, null, JsonConvert.SerializeObject(new { year, cases = log.Rows.Count }));
			return log;
		}

		#endregion OSHA 300 / 301
	}
}
