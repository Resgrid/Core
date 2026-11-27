using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;

namespace Resgrid.Services.Wellness
{
	/// <summary>
	/// The BBP / infectious-disease exposure case (§13b WL1): report → source follow-up request keyed to the call (never patient identity) →
	/// Ryan White notification request and answer → post-exposure follow-up tasks → closure. Only the designated officer moves a case.
	/// </summary>
	public partial class MemberHealthService
	{
		#region Report

		public async Task<RmsExposureIncident> ReportExposureAsync(int departmentId, string actorUserId, ExposureIncidentReport report, CancellationToken cancellationToken = default)
		{
			if (report == null)
				throw new ArgumentNullException(nameof(report));
			if (string.IsNullOrWhiteSpace(actorUserId))
				throw new UnauthorizedAccessException("A signed-in member is required.");
			await RequireReadableAsync(departmentId);

			var memberUserId = string.IsNullOrWhiteSpace(report.UserId) ? actorUserId : report.UserId.Trim();
			if (Same(actorUserId, memberUserId))
			{
				if (!await IsCurrentMemberAsync(departmentId, actorUserId))
					throw new UnauthorizedAccessException("Only a current member reports their own exposure.");
			}
			else
				await RequireDesignatedOfficerAsync(departmentId, actorUserId);
			if (!await IsCurrentMemberAsync(departmentId, memberUserId))
				throw new MemberWellnessException("member_not_found");
			await RequireWritableForAsync(departmentId, memberUserId);
			if (!MemberHealthRules.IsKnownRoute(report.Route))
				throw new MemberWellnessException("route_required");

			var now = DateTime.UtcNow;
			RmsResponderExposure entry;
			if (!string.IsNullOrWhiteSpace(report.ResponderExposureId))
			{
				entry = await _exposures.GetByIdForDepartmentAsync(departmentId, report.ResponderExposureId.Trim());
				if (entry == null || !Same(entry.UserId, memberUserId))
					throw new MemberWellnessException("entry_not_found");
				if (!ExposureIncidentWorkflow.CanOpenFrom(entry))
					throw new MemberWellnessException("entry_not_bloodborne");
				var cases = await _incidents.GetForExposureAsync(departmentId, entry.RmsResponderExposureId) ?? Enumerable.Empty<RmsExposureIncident>();
				if (cases.Any(c => c.IsOpen))
					throw new MemberWellnessException("case_exists");
			}
			else
			{
				ValidateDate(report.OccurredOn, "date_invalid");
				await ValidateCallAsync(departmentId, report.CallId);
				entry = await RecordExposureEntryAsync(departmentId, actorUserId, memberUserId, report, now, cancellationToken);
			}

			var incident = new RmsExposureIncident
			{
				RmsExposureIncidentId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				UserId = memberUserId,
				ResponderExposureId = entry.RmsResponderExposureId,
				CallId = entry.CallId,
				Route = report.Route,
				OccurredOn = DateTime.SpecifyKind(entry.OccurredOn, DateTimeKind.Utc),
				Stage = (int)ExposureIncidentStages.Reported,
				ReportedOn = now,
				ReportedByUserId = actorUserId,
				CreatedOn = now,
				CreatedByUserId = actorUserId,
				ModifiedOn = now,
				ModifiedByUserId = actorUserId,
				RowVersion = 1
			};
			await _incidents.InsertAsync(incident, cancellationToken, true);
			Audit(departmentId, actorUserId, AuditLogTypes.ExposureIncidentReported, null, Snapshot(incident));

			// The designated officers hear of it at once; the text names the member and the date, never what happened.
			var department = await DepartmentAsync(departmentId);
			var when = LocalDate(incident.OccurredOn, department);
			var name = await NameOfAsync(departmentId, memberUserId);
			await NotifyAsync(departmentId, (await DesignatedOfficersAsync(departmentId)).Where(o => !Same(o, actorUserId)), "Health.ExposureReported.Title",
				"Health.ExposureReported.Message", name, () => new object[] { when, Link("/User/MemberHealth/Case?id=" + incident.RmsExposureIncidentId) },
				EventTypes.ExposureIncidentNotice, incident.RmsExposureIncidentId, cancellationToken);
			return incident;
		}

		/// <summary>The confirmed WL0 entry a newly reported exposure is logged as (bloodborne, or Other for an airborne disease).</summary>
		private async Task<RmsResponderExposure> RecordExposureEntryAsync(int departmentId, string actorUserId, string memberUserId, ExposureIncidentReport report, DateTime now,
			CancellationToken cancellationToken)
		{
			var entry = new RmsResponderExposure
			{
				RmsResponderExposureId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				UserId = memberUserId,
				CallId = report.CallId,
				Category = ExposureIncidentWorkflow.CategoryFor(report.Route),
				OccurredOn = DateTime.SpecifyKind(report.OccurredOn, DateTimeKind.Utc),
				Status = (int)ResponderExposureStatuses.Confirmed,
				Source = (int)ResponderExposureSources.Manual,
				Narrative = Trim(report.Narrative, MemberWellnessRules.NarrativeMax),
				PpeWorn = Trim(report.PpeWorn, MemberWellnessRules.PpeWornMax),
				ConfirmedOn = now,
				ConfirmedByUserId = actorUserId,
				CreatedOn = now,
				CreatedByUserId = actorUserId,
				ModifiedOn = now,
				ModifiedByUserId = actorUserId,
				RowVersion = 1
			};
			await ProtectAsync(departmentId, entry, null, entry.RmsResponderExposureId, WellnessProtectedFields.Exposures,
				() => { entry.IsProtected = true; entry.ProtectedCatalogVersion = Math.Max(entry.ProtectedCatalogVersion, WellnessProtectedFields.CatalogVersion); }, cancellationToken);
			await _exposures.InsertAsync(entry, cancellationToken, true);
			Audit(departmentId, actorUserId, AuditLogTypes.ResponderExposureRecorded, null, MemberWellnessService.Snapshot(entry));
			return entry;
		}

		#endregion Report

		#region Read

		public async Task<RmsExposureIncident> GetCaseAsync(int departmentId, string viewerUserId, string incidentId)
		{
			await RequireReadableAsync(departmentId);
			if (string.IsNullOrWhiteSpace(incidentId))
				return null;
			var row = await _incidents.GetByIdForDepartmentAsync(departmentId, incidentId.Trim());
			if (row == null || !await CanReadAsync(departmentId, viewerUserId, row.UserId, true))
				return null;
			await RevealAsync(departmentId, new List<RmsExposureIncident> { row });
			return row;
		}

		public async Task<List<RmsExposureIncident>> GetCasesForMemberAsync(int departmentId, string viewerUserId, string memberUserId)
		{
			await RequireReadableAsync(departmentId);
			if (!await CanReadAsync(departmentId, viewerUserId, memberUserId, true))
				throw new UnauthorizedAccessException("Exposure cases are restricted to the member and the designated officers.");
			var rows = (await _incidents.GetForUserAsync(departmentId, memberUserId) ?? Enumerable.Empty<RmsExposureIncident>()).ToList();
			await RevealAsync(departmentId, rows);
			return rows;
		}

		public async Task<List<RmsExposureIncident>> GetDepartmentCasesAsync(int departmentId, string officerUserId, bool includeClosed)
		{
			await RequireReadableAsync(departmentId);
			await RequireDesignatedOfficerAsync(departmentId, officerUserId);
			var rows = (await _incidents.GetForDepartmentAsync(departmentId, includeClosed) ?? Enumerable.Empty<RmsExposureIncident>()).ToList();
			// The list carries operational columns only; the notes stay on the case page.
			Blank(rows, MemberHealthProtectedFields.ExposureIncidents);
			return rows;
		}

		public async Task<BbpTrainingStatus> GetBbpTrainingStatusAsync(int departmentId, string memberUserId)
		{
			await RequireReadableAsync(departmentId);
			try
			{
				var settings = await _wellness.GetSettingsAsync(departmentId) ?? new WellnessSettings();
				if (settings.BbpTrainingCertificationTypeId <= 0 || string.IsNullOrWhiteSpace(memberUserId))
					return new BbpTrainingStatus { State = BbpTrainingStates.NotTracked };
				var type = await _certificationService.Value.GetCertificationTypeByIdAsync(settings.BbpTrainingCertificationTypeId);
				if (type == null || type.DepartmentId != departmentId)
					return new BbpTrainingStatus { State = BbpTrainingStates.NotTracked };
				var records = (await _certificationService.Value.GetCertificationsByUserIdAsync(memberUserId) ?? new List<PersonnelCertification>())
					.Where(c => c.DepartmentId == departmentId).ToList();
				var department = await DepartmentAsync(departmentId);
				return MemberHealthRules.BbpStatus(records, type, LocalDate(DateTime.UtcNow, department));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"BBP training status failed for department {departmentId}.");
				return new BbpTrainingStatus { State = BbpTrainingStates.NotTracked };
			}
		}

		#endregion Read

		#region Workflow

		public async Task<RmsExposureIncident> AdvanceCaseAsync(int departmentId, string officerUserId, string incidentId, ExposureIncidentStep step, CancellationToken cancellationToken = default)
		{
			if (step == null)
				throw new ArgumentNullException(nameof(step));
			await RequireReadableAsync(departmentId);
			await RequireDesignatedOfficerAsync(departmentId, officerUserId);
			var row = string.IsNullOrWhiteSpace(incidentId) ? null : await _incidents.GetByIdForDepartmentAsync(departmentId, incidentId.Trim());
			if (row == null)
				throw new MemberWellnessException("case_not_found");
			await RequireWritableForAsync(departmentId, row.UserId);
			if (step.ExpectedRowVersion.HasValue && step.ExpectedRowVersion.Value != row.RowVersion)
				throw new MemberWellnessException("case_changed");
			if (!ExposureIncidentWorkflow.CanMove(row.Stage, step.ToStage))
				throw new MemberWellnessException("stage_not_allowed");

			var now = DateTime.UtcNow;
			var on = step.On.HasValue ? DateTime.SpecifyKind(step.On.Value, DateTimeKind.Utc) : now;
			if (on > now.AddDays(1) || on < row.OccurredOn.AddDays(-1))
				throw new MemberWellnessException("date_invalid");

			var existing = Clone(row);
			var before = Snapshot(row);
			var notes = Trim(step.Notes, MemberHealthRules.NotesMax);
			switch ((ExposureIncidentStages)step.ToStage)
			{
				case ExposureIncidentStages.SourceFollowUpRequested:
					var facility = Trim(step.SourceFacility, MemberHealthRules.FacilityMax);
					if (facility == null)
						throw new MemberWellnessException("facility_required");
					row.SourceFacility = facility;
					row.SourceRequestedOn = on;
					if (notes != null)
						row.SourceFollowUpNotes = notes;
					break;
				case ExposureIncidentStages.NotificationRequested:
					if (!string.IsNullOrWhiteSpace(step.SourceFacility))
						row.SourceFacility = Trim(step.SourceFacility, MemberHealthRules.FacilityMax);
					row.NotificationRequestedOn = on;
					row.NotificationDueOn = on.Add(ExposureIncidentWorkflow.NotificationResponseWindow);
					row.NotificationOverdueNoticeOn = null;
					break;
				case ExposureIncidentStages.NotificationReceived:
					row.NotificationReceivedOn = on;
					if (notes != null)
						row.NotificationResponse = notes;
					break;
				case ExposureIncidentStages.FollowUp:
					// Reopening a closed case keeps its first follow-up date.
					row.FollowUpStartedOn ??= on;
					row.ClosedOn = null;
					row.ClosedByUserId = null;
					if (notes != null)
						row.MedicalNotes = notes;
					break;
				case ExposureIncidentStages.Closed:
					var open = (await FollowUpsForCaseAsync(departmentId, row.RmsExposureIncidentId)).Count(a => a.IsOpen);
					if (open > 0)
						throw new MemberWellnessException("follow_ups_open");
					row.ClosedOn = on;
					row.ClosedByUserId = officerUserId;
					if (notes != null)
						row.ClosureNotes = notes;
					break;
				case ExposureIncidentStages.Withdrawn:
					row.WithdrawnOn = on;
					row.WithdrawnByUserId = officerUserId;
					if (notes != null)
						row.ClosureNotes = notes;
					break;
			}

			row.Stage = step.ToStage;
			row.ModifiedOn = now;
			row.ModifiedByUserId = officerUserId;
			row.RowVersion++;
			await ProtectAsync(departmentId, row, existing, cancellationToken);
			await _incidents.UpdateAsync(row, cancellationToken, true);
			Audit(departmentId, officerUserId, AuditLogTypes.ExposureIncidentStageChanged, before, Snapshot(row));

			if (row.Stage == (int)ExposureIncidentStages.Withdrawn)
				await CancelOpenFollowUpsAsync(departmentId, officerUserId, row.RmsExposureIncidentId, "case_withdrawn", cancellationToken);
			return await GetCaseAsync(departmentId, officerUserId, row.RmsExposureIncidentId) ?? row;
		}

		public async Task<RmsExposureIncident> UpdateCaseNotesAsync(int departmentId, string officerUserId, string incidentId, string sourceFacility, string sourceFollowUpNotes,
			string notificationResponse, string medicalNotes, long? expectedRowVersion, CancellationToken cancellationToken = default)
		{
			await RequireReadableAsync(departmentId);
			await RequireDesignatedOfficerAsync(departmentId, officerUserId);
			var row = string.IsNullOrWhiteSpace(incidentId) ? null : await _incidents.GetByIdForDepartmentAsync(departmentId, incidentId.Trim());
			if (row == null)
				throw new MemberWellnessException("case_not_found");
			await RequireWritableForAsync(departmentId, row.UserId);
			if (row.Stage == (int)ExposureIncidentStages.Withdrawn)
				throw new MemberWellnessException("case_closed");
			if (expectedRowVersion.HasValue && expectedRowVersion.Value != row.RowVersion)
				throw new MemberWellnessException("case_changed");

			var existing = Clone(row);
			var before = Snapshot(row);
			row.SourceFacility = Trim(sourceFacility, MemberHealthRules.FacilityMax);
			row.SourceFollowUpNotes = Keep(sourceFollowUpNotes, row.SourceFollowUpNotes, MemberHealthRules.NotesMax);
			row.NotificationResponse = Keep(notificationResponse, row.NotificationResponse, MemberHealthRules.NotesMax);
			row.MedicalNotes = Keep(medicalNotes, row.MedicalNotes, MemberHealthRules.NotesMax);
			row.ModifiedOn = DateTime.UtcNow;
			row.ModifiedByUserId = officerUserId;
			row.RowVersion++;
			await ProtectAsync(departmentId, row, existing, cancellationToken);
			await _incidents.UpdateAsync(row, cancellationToken, true);
			Audit(departmentId, officerUserId, AuditLogTypes.ExposureIncidentUpdated, before, Snapshot(row));
			return await GetCaseAsync(departmentId, officerUserId, row.RmsExposureIncidentId) ?? row;
		}

		/// <summary>Audit detail: ids, stage, route, dates and which notes hold a value. Never a protected value, never patient data.</summary>
		internal static string Snapshot(RmsExposureIncident row)
		{
			if (row == null)
				return null;
			return JsonConvert.SerializeObject(new
			{
				id = row.RmsExposureIncidentId,
				user = row.UserId,
				exposure = row.ResponderExposureId,
				call = row.CallId,
				route = Enum.IsDefined(typeof(ExposureIncidentRoutes), row.Route) ? ((ExposureIncidentRoutes)row.Route).ToString() : null,
				stage = ExposureIncidentWorkflow.IsKnownStage(row.Stage) ? ((ExposureIncidentStages)row.Stage).ToString() : null,
				facility = row.SourceFacility,
				source_requested = Code(row.SourceRequestedOn),
				notification_requested = Code(row.NotificationRequestedOn),
				notification_received = Code(row.NotificationReceivedOn),
				closed = Code(row.ClosedOn),
				notes = MemberHealthProtectedFields.ExposureIncidents.Where(a => !string.IsNullOrEmpty(a.Value.Get(row))).Select(a => a.Key.Substring(a.Key.IndexOf('.') + 1)).ToList()
			});
		}

		#endregion Workflow

		#region Follow-ups

		public async Task<List<RmsRecordWorkAssignment>> GetCaseFollowUpsAsync(int departmentId, string viewerUserId, string incidentId)
		{
			await RequireReadableAsync(departmentId);
			var row = string.IsNullOrWhiteSpace(incidentId) ? null : await _incidents.GetByIdForDepartmentAsync(departmentId, incidentId.Trim());
			if (row == null || !await CanReadAsync(departmentId, viewerUserId, row.UserId, true))
				throw new UnauthorizedAccessException("Exposure cases are restricted to the member and the designated officers.");
			return await FollowUpsForCaseAsync(departmentId, row.RmsExposureIncidentId);
		}

		public async Task<RmsRecordWorkAssignment> AddCaseFollowUpAsync(int departmentId, string officerUserId, string incidentId, string kind, string assigneeUserId, DateTime? dueOn,
			CancellationToken cancellationToken = default)
		{
			await RequireReadableAsync(departmentId);
			await RequireDesignatedOfficerAsync(departmentId, officerUserId);
			var row = string.IsNullOrWhiteSpace(incidentId) ? null : await _incidents.GetByIdForDepartmentAsync(departmentId, incidentId.Trim());
			if (row == null)
				throw new MemberWellnessException("case_not_found");
			await RequireWritableForAsync(departmentId, row.UserId);
			if (!row.IsOpen)
				throw new MemberWellnessException("case_closed");
			if (!ExposureCaseFollowUpKinds.IsKnown(kind))
				throw new MemberWellnessException("follow_up_kind_required");

			// Addressed to the exposed member or a designated officer only: the task says whose case it is.
			var assignee = string.IsNullOrWhiteSpace(assigneeUserId) ? row.UserId : assigneeUserId.Trim();
			if (!Same(assignee, row.UserId) && !await IsDesignatedOfficerAsync(departmentId, assignee))
				throw new MemberWellnessException("follow_up_assignee_invalid");
			if (!await IsCurrentMemberAsync(departmentId, assignee))
				throw new MemberWellnessException("follow_up_assignee_invalid");

			var now = DateTime.UtcNow;
			var task = new RmsRecordWorkAssignment
			{
				RmsRecordWorkAssignmentId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				RecordId = row.RmsExposureIncidentId,
				AssigneeKind = (int)RmsWorkAssigneeKind.Person,
				AssigneeUserId = assignee,
				Purpose = RmsWorkAssignmentPurposes.ExposureCaseFollowUp,
				Note = kind.Trim().ToLowerInvariant(),
				DueOn = dueOn.HasValue ? DateTime.SpecifyKind(dueOn.Value, DateTimeKind.Utc) : (DateTime?)null,
				State = (int)RmsWorkAssignmentState.Open,
				OriginClient = (int)RmsOriginClient.Web,
				CreatedOn = now,
				CreatedByUserId = officerUserId,
				ModifiedOn = now,
				ModifiedByUserId = officerUserId,
				RowVersion = 1
			};
			await _assignments.InsertAsync(task, cancellationToken, true);
			Audit(departmentId, officerUserId, AuditLogTypes.ExposureIncidentFollowUpChanged, null, FollowUpSnapshot(task, "added"));
			return task;
		}

		public async Task<RmsRecordWorkAssignment> CompleteCaseFollowUpAsync(int departmentId, string actorUserId, string assignmentId, CancellationToken cancellationToken = default)
		{
			var task = await LoadFollowUpAsync(departmentId, assignmentId);
			if (!await IsCurrentMemberAsync(departmentId, actorUserId))
				throw new UnauthorizedAccessException("Only a current member completes a follow-up.");
			if (!Same(task.AssigneeUserId, actorUserId) && !await IsDesignatedOfficerAsync(departmentId, actorUserId))
				throw new UnauthorizedAccessException("Only the assignee or a designated officer completes a follow-up.");
			await RequireWritableForCaseTaskAsync(departmentId, task);
			if (!task.IsOpen)
				throw new MemberWellnessException("follow_up_closed");

			var now = DateTime.UtcNow;
			task.State = (int)RmsWorkAssignmentState.Completed;
			task.CompletedOn = now;
			task.CompletedByUserId = actorUserId;
			task.ModifiedOn = now;
			task.ModifiedByUserId = actorUserId;
			task.RowVersion++;
			await _assignments.UpdateAsync(task, cancellationToken, true);
			Audit(departmentId, actorUserId, AuditLogTypes.ExposureIncidentFollowUpChanged, null, FollowUpSnapshot(task, "completed"));
			return task;
		}

		public async Task<RmsRecordWorkAssignment> CancelCaseFollowUpAsync(int departmentId, string officerUserId, string assignmentId, CancellationToken cancellationToken = default)
		{
			await RequireDesignatedOfficerAsync(departmentId, officerUserId);
			var task = await LoadFollowUpAsync(departmentId, assignmentId);
			await RequireWritableForCaseTaskAsync(departmentId, task);
			if (!task.IsOpen)
				throw new MemberWellnessException("follow_up_closed");
			await CancelAsync(departmentId, officerUserId, task, null, cancellationToken);
			return task;
		}

		private async Task CancelOpenFollowUpsAsync(int departmentId, string officerUserId, string incidentId, string reason, CancellationToken cancellationToken)
		{
			foreach (var task in (await FollowUpsForCaseAsync(departmentId, incidentId)).Where(a => a.IsOpen))
				await CancelAsync(departmentId, officerUserId, task, reason, cancellationToken);
		}

		private async Task CancelAsync(int departmentId, string officerUserId, RmsRecordWorkAssignment task, string reason, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			task.State = (int)RmsWorkAssignmentState.Cancelled;
			task.CancelledOn = now;
			task.CancelledByUserId = officerUserId;
			task.CancelReason = reason;
			task.ModifiedOn = now;
			task.ModifiedByUserId = officerUserId;
			task.RowVersion++;
			await _assignments.UpdateAsync(task, cancellationToken, true);
			Audit(departmentId, officerUserId, AuditLogTypes.ExposureIncidentFollowUpChanged, null, FollowUpSnapshot(task, "cancelled"));
		}

		/// <summary>A case follow-up is part of the member's case: editable only while that member holds a seat.</summary>
		private async Task RequireWritableForCaseTaskAsync(int departmentId, RmsRecordWorkAssignment task)
		{
			var incident = string.IsNullOrWhiteSpace(task?.RecordId) ? null : await _incidents.GetByIdForDepartmentAsync(departmentId, task.RecordId);
			if (incident != null)
				await RequireWritableForAsync(departmentId, incident.UserId);
			else
				(await WorkforceProAsync(departmentId)).EnsureWritable();
		}

		private async Task<RmsRecordWorkAssignment> LoadFollowUpAsync(int departmentId, string assignmentId)
		{
			await RequireReadableAsync(departmentId);
			var task = string.IsNullOrWhiteSpace(assignmentId) ? null : await _assignments.GetByIdForDepartmentAsync(departmentId, assignmentId.Trim());
			// Only the exposure-case purpose is reachable here; a Record's own work assignment (or a WL0 one) is never touched.
			if (task == null || task.DeletedOn.HasValue || !string.Equals(task.Purpose, RmsWorkAssignmentPurposes.ExposureCaseFollowUp, StringComparison.Ordinal))
				throw new MemberWellnessException("follow_up_not_found");
			return task;
		}

		private async Task<List<RmsRecordWorkAssignment>> FollowUpsForCaseAsync(int departmentId, string incidentId)
		{
			return (await _assignments.GetForRecordAsync(departmentId, incidentId) ?? Enumerable.Empty<RmsRecordWorkAssignment>())
				.Where(a => !a.DeletedOn.HasValue && string.Equals(a.Purpose, RmsWorkAssignmentPurposes.ExposureCaseFollowUp, StringComparison.Ordinal))
				.OrderBy(a => a.IsOpen ? 0 : 1)
				.ThenBy(a => a.DueOn ?? DateTime.MaxValue)
				.ThenBy(a => a.CreatedOn)
				.ToList();
		}

		private static string FollowUpSnapshot(RmsRecordWorkAssignment task, string action) => JsonConvert.SerializeObject(new
		{
			id = task.RmsRecordWorkAssignmentId,
			incident = task.RecordId,
			kind = task.Note,
			assignee = task.AssigneeUserId,
			due = task.DueOn,
			action
		});

		#endregion Follow-ups
	}
}
