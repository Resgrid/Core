using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;

namespace Resgrid.Services.Wellness
{
	public partial class MemberWellnessService
	{
		#region Officer report

		public async Task<List<MemberExposureSummary>> GetDepartmentSummaryAsync(int departmentId, string officerUserId, DateTime? fromUtc, DateTime? toUtc)
		{
			await RequireReadableAsync(departmentId);
			await RequireOfficerAsync(departmentId, officerUserId);

			// Operational columns only: the report counts, it never reveals a protected value.
			var rows = (await _exposures.GetForDepartmentAsync(departmentId, fromUtc, toUtc) ?? Enumerable.Empty<RmsResponderExposure>()).ToList();
			var flags = (await _screeningFlags.GetActiveForDepartmentAsync(departmentId) ?? Enumerable.Empty<RmsResponderScreeningFlag>()).ToList();
			var summaries = new Dictionary<string, MemberExposureSummary>(StringComparer.OrdinalIgnoreCase);

			MemberExposureSummary For(string userId)
			{
				if (!summaries.TryGetValue(userId, out var summary))
				{
					summary = new MemberExposureSummary { UserId = userId };
					summaries[userId] = summary;
				}
				return summary;
			}

			foreach (var row in rows.Where(r => !string.IsNullOrWhiteSpace(r.UserId)))
			{
				var summary = For(row.UserId);
				if (row.Status == (int)ResponderExposureStatuses.Suggested)
				{
					summary.PendingSuggestions++;
					continue;
				}
				if (!row.Counts)
					continue;
				summary.Total++;
				summary.ByCategory[row.Category] = summary.ByCategory.TryGetValue(row.Category, out var n) ? n + 1 : 1;
				if (row.Category == (int)ResponderExposureCategories.CriticalIncident)
					summary.CriticalIncidents++;
				if (!summary.LastOccurredOn.HasValue || row.OccurredOn > summary.LastOccurredOn.Value)
					summary.LastOccurredOn = row.OccurredOn;
			}

			foreach (var flag in flags.Where(f => !string.IsNullOrWhiteSpace(f.UserId)))
			{
				var summary = For(flag.UserId);
				summary.ScreeningFlagged = true;
				summary.ScreeningReviewOn = flag.ReviewOn;
				summary.OpenFollowUps = (await FollowUpsForFlagAsync(departmentId, flag.RmsResponderScreeningFlagId)).Count(a => a.IsOpen);
			}

			return summaries.Values
				.OrderByDescending(s => s.ScreeningFlagged)
				.ThenByDescending(s => s.CriticalIncidents)
				.ThenByDescending(s => s.Total)
				.ToList();
		}

		public async Task<List<RmsResponderExposure>> GetDepartmentEntriesAsync(int departmentId, string officerUserId, DateTime? fromUtc, DateTime? toUtc)
		{
			await RequireReadableAsync(departmentId);
			await RequireOfficerAsync(departmentId, officerUserId);
			var rows = (await _exposures.GetForDepartmentAsync(departmentId, fromUtc, toUtc) ?? Enumerable.Empty<RmsResponderExposure>())
				.Where(r => r.Counts)
				.ToList();
			// The department report carries the operational columns only; details stay on the member's own page.
			foreach (var row in rows)
				foreach (var accessor in WellnessProtectedFields.Exposures.Values)
					accessor.Set(row, null);
			return rows;
		}

		#endregion Officer report

		#region Post-call flags

		public async Task<RmsCallExposureFlag> FlagCallAsync(int departmentId, string actorUserId, int callId, int category, CancellationToken cancellationToken = default)
		{
			await RequireReadableAsync(departmentId);
			if (!MemberWellnessRules.IsKnownCategory(category))
				throw new MemberWellnessException("category_required");
			var call = await _callsService.GetCallByIdAsync(callId);
			if (call == null || call.DepartmentId != departmentId || call.IsDeleted)
				throw new MemberWellnessException("call_not_found");
			if (!await CanFlagCallAsync(departmentId, actorUserId, callId))
				throw new UnauthorizedAccessException("Flagging a call needs the wellness officer role, department administration or the right to edit the call.");
			// A flag builds suggestions (records) for the members on the call: only while Workforce Pro is licensed, and only for seat holders.
			await RequireWritableAsync(departmentId);

			var now = DateTime.UtcNow;
			var existing = (await _callFlags.GetForCallAsync(departmentId, callId) ?? Enumerable.Empty<RmsCallExposureFlag>()).FirstOrDefault(f => f.Category == category);
			if (existing != null)
			{
				if (existing.IsActive)
					return existing;
				// Flagged again after it was cleared: build the suggestions afresh (dismissed ones are never suggested twice).
				existing.ClearedOn = null;
				existing.ClearedByUserId = null;
				existing.FlaggedOn = now;
				existing.FlaggedByUserId = actorUserId;
				existing.SuggestionsGeneratedOn = null;
				existing.SuggestionCount = 0;
				existing.RowVersion++;
				await _callFlags.UpdateAsync(existing, cancellationToken, true);
				Audit(departmentId, actorUserId, AuditLogTypes.CallExposureFlagChanged, null, FlagSnapshot(existing, "flagged"));
				return existing;
			}

			var flag = new RmsCallExposureFlag
			{
				RmsCallExposureFlagId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				CallId = callId,
				Category = category,
				FlaggedOn = now,
				FlaggedByUserId = actorUserId,
				RowVersion = 1
			};
			await _callFlags.InsertAsync(flag, cancellationToken, true);
			Audit(departmentId, actorUserId, AuditLogTypes.CallExposureFlagChanged, null, FlagSnapshot(flag, "flagged"));
			return flag;
		}

		public async Task ClearCallFlagAsync(int departmentId, string actorUserId, string flagId, CancellationToken cancellationToken = default)
		{
			await RequireReadableAsync(departmentId);
			var flag = string.IsNullOrWhiteSpace(flagId) ? null : await _callFlags.GetByIdForDepartmentAsync(departmentId, flagId);
			if (flag == null)
				throw new MemberWellnessException("flag_not_found");
			if (!await CanFlagCallAsync(departmentId, actorUserId, flag.CallId))
				throw new UnauthorizedAccessException("Clearing a call flag needs the same right as setting it.");
			await RequireWritableAsync(departmentId);
			if (!flag.IsActive)
				return;

			flag.ClearedOn = DateTime.UtcNow;
			flag.ClearedByUserId = actorUserId;
			flag.RowVersion++;
			await _callFlags.UpdateAsync(flag, cancellationToken, true);
			// Suggestions nobody confirmed go with the flag; anything a person confirmed stays the member's record.
			await _exposures.DeleteSuggestionsFromFlagAsync(departmentId, flag.RmsCallExposureFlagId, cancellationToken);
			Audit(departmentId, actorUserId, AuditLogTypes.CallExposureFlagChanged, null, FlagSnapshot(flag, "cleared"));
		}

		public async Task<List<RmsCallExposureFlag>> GetCallFlagsAsync(int departmentId, string actorUserId, int callId)
		{
			await RequireReadableAsync(departmentId);
			if (!await CanFlagCallAsync(departmentId, actorUserId, callId))
				throw new UnauthorizedAccessException("Reading a call's wellness flags needs the right to set them.");
			return (await _callFlags.GetForCallAsync(departmentId, callId) ?? Enumerable.Empty<RmsCallExposureFlag>()).ToList();
		}

		public async Task<List<RmsCallExposureFlag>> GetRecentFlagsAsync(int departmentId, string officerUserId, int take)
		{
			await RequireReadableAsync(departmentId);
			await RequireOfficerAsync(departmentId, officerUserId);
			return (await _callFlags.GetRecentForDepartmentAsync(departmentId, Math.Max(1, Math.Min(200, take))) ?? Enumerable.Empty<RmsCallExposureFlag>()).ToList();
		}

		private static string FlagSnapshot(RmsCallExposureFlag flag, string action) => JsonConvert.SerializeObject(new
		{
			id = flag.RmsCallExposureFlagId,
			call = flag.CallId,
			category = ((ResponderExposureCategories)flag.Category).ToString(),
			action
		});

		#endregion Post-call flags

		#region Screening

		public async Task<RmsResponderScreeningFlag> SetScreeningFlagAsync(int departmentId, string officerUserId, string memberUserId, int? category, DateTime? reviewOn,
			string planNotes, CancellationToken cancellationToken = default)
		{
			await RequireReadableAsync(departmentId);
			await RequireOfficerAsync(departmentId, officerUserId);
			if (!await IsCurrentMemberAsync(departmentId, memberUserId))
				throw new MemberWellnessException("member_not_found");
			await RequireWritableForAsync(departmentId, memberUserId);
			if (category.HasValue && !MemberWellnessRules.IsKnownCategory(category.Value))
				throw new MemberWellnessException("category_required");

			var now = DateTime.UtcNow;
			var flag = await _screeningFlags.GetActiveForUserAsync(departmentId, memberUserId);
			var isNew = flag == null;
			RmsResponderScreeningFlag existing = null;
			if (isNew)
			{
				flag = new RmsResponderScreeningFlag
				{
					RmsResponderScreeningFlagId = Guid.NewGuid().ToString(),
					DepartmentId = departmentId,
					UserId = memberUserId,
					FlaggedOn = now,
					FlaggedByUserId = officerUserId,
					RowVersion = 1
				};
			}
			else
			{
				existing = new RmsResponderScreeningFlag { PlanNotes = flag.PlanNotes, IsProtected = flag.IsProtected, ProtectedCatalogVersion = flag.ProtectedCatalogVersion };
				flag.RowVersion++;
			}

			flag.Category = category;
			flag.ReviewOn = reviewOn.HasValue ? DateTime.SpecifyKind(reviewOn.Value, DateTimeKind.Utc) : (DateTime?)null;
			flag.PlanNotes = Trim(planNotes, MemberWellnessRules.PlanNotesMax);
			flag.ModifiedOn = now;
			flag.ModifiedByUserId = officerUserId;
			await ProtectAsync(departmentId, flag, existing, cancellationToken);
			if (isNew)
				await _screeningFlags.InsertAsync(flag, cancellationToken, true);
			else
				await _screeningFlags.UpdateAsync(flag, cancellationToken, true);

			Audit(departmentId, officerUserId, AuditLogTypes.ResponderScreeningFlagChanged, null, JsonConvert.SerializeObject(new
			{
				id = flag.RmsResponderScreeningFlagId,
				user = flag.UserId,
				category = flag.Category,
				review = flag.ReviewOn,
				action = isNew ? "flagged" : "updated"
			}));
			return flag;
		}

		public async Task ClearScreeningFlagAsync(int departmentId, string officerUserId, string flagId, CancellationToken cancellationToken = default)
		{
			await RequireReadableAsync(departmentId);
			await RequireOfficerAsync(departmentId, officerUserId);
			var flag = string.IsNullOrWhiteSpace(flagId) ? null : await _screeningFlags.GetByIdForDepartmentAsync(departmentId, flagId);
			if (flag == null)
				throw new MemberWellnessException("flag_not_found");
			await RequireWritableForAsync(departmentId, flag.UserId);
			if (!flag.IsActive)
				return;

			var now = DateTime.UtcNow;
			flag.ClearedOn = now;
			flag.ClearedByUserId = officerUserId;
			flag.ModifiedOn = now;
			flag.ModifiedByUserId = officerUserId;
			flag.RowVersion++;
			await _screeningFlags.UpdateAsync(flag, cancellationToken, true);

			// Open follow-ups end with the flag.
			foreach (var task in (await FollowUpsForFlagAsync(departmentId, flag.RmsResponderScreeningFlagId)).Where(a => a.IsOpen))
			{
				task.State = (int)RmsWorkAssignmentState.Cancelled;
				task.CancelledOn = now;
				task.CancelledByUserId = officerUserId;
				task.CancelReason = "screening_flag_cleared";
				task.ModifiedOn = now;
				task.ModifiedByUserId = officerUserId;
				task.RowVersion++;
				await _assignments.UpdateAsync(task, cancellationToken, true);
			}

			Audit(departmentId, officerUserId, AuditLogTypes.ResponderScreeningFlagChanged, null, JsonConvert.SerializeObject(new
			{
				id = flag.RmsResponderScreeningFlagId,
				user = flag.UserId,
				action = "cleared"
			}));
		}

		public async Task<RmsRecordWorkAssignment> AddFollowUpAsync(int departmentId, string officerUserId, string flagId, string kind, string assigneeUserId, DateTime? dueOn,
			CancellationToken cancellationToken = default)
		{
			await RequireReadableAsync(departmentId);
			await RequireOfficerAsync(departmentId, officerUserId);
			var flag = string.IsNullOrWhiteSpace(flagId) ? null : await _screeningFlags.GetByIdForDepartmentAsync(departmentId, flagId);
			if (flag == null || !flag.IsActive)
				throw new MemberWellnessException("flag_not_found");
			await RequireWritableForAsync(departmentId, flag.UserId);
			if (!ResponderFollowUpTaskKinds.IsKnown(kind))
				throw new MemberWellnessException("follow_up_kind_required");

			// Addressed to the member or to a wellness officer, never to anyone else (the task names whose screening it is).
			var assignee = string.IsNullOrWhiteSpace(assigneeUserId) ? flag.UserId : assigneeUserId.Trim();
			if (!Same(assignee, flag.UserId) && !await IsWellnessOfficerAsync(departmentId, assignee))
				throw new MemberWellnessException("follow_up_assignee_invalid");
			if (!await IsCurrentMemberAsync(departmentId, assignee))
				throw new MemberWellnessException("follow_up_assignee_invalid");

			var now = DateTime.UtcNow;
			var task = new RmsRecordWorkAssignment
			{
				RmsRecordWorkAssignmentId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				RecordId = flag.RmsResponderScreeningFlagId,
				AssigneeKind = (int)RmsWorkAssigneeKind.Person,
				AssigneeUserId = assignee,
				Purpose = RmsWorkAssignmentPurposes.WellnessFollowUp,
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
			Audit(departmentId, officerUserId, AuditLogTypes.ResponderFollowUpChanged, null, FollowUpSnapshot(task, "added"));
			return task;
		}

		public async Task<RmsRecordWorkAssignment> CompleteFollowUpAsync(int departmentId, string actorUserId, string assignmentId, CancellationToken cancellationToken = default)
		{
			var task = await LoadFollowUpAsync(departmentId, assignmentId);
			if (!Same(task.AssigneeUserId, actorUserId) && !await IsWellnessOfficerAsync(departmentId, actorUserId))
				throw new UnauthorizedAccessException("Only the assignee or a wellness officer completes a follow-up.");
			if (!await IsCurrentMemberAsync(departmentId, actorUserId))
				throw new UnauthorizedAccessException("Only a current member completes a follow-up.");
			await RequireWritableForFollowUpAsync(departmentId, task);
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
			Audit(departmentId, actorUserId, AuditLogTypes.ResponderFollowUpChanged, null, FollowUpSnapshot(task, "completed"));
			return task;
		}

		public async Task<RmsRecordWorkAssignment> CancelFollowUpAsync(int departmentId, string officerUserId, string assignmentId, CancellationToken cancellationToken = default)
		{
			await RequireOfficerAsync(departmentId, officerUserId);
			var task = await LoadFollowUpAsync(departmentId, assignmentId);
			await RequireWritableForFollowUpAsync(departmentId, task);
			if (!task.IsOpen)
				throw new MemberWellnessException("follow_up_closed");

			var now = DateTime.UtcNow;
			task.State = (int)RmsWorkAssignmentState.Cancelled;
			task.CancelledOn = now;
			task.CancelledByUserId = officerUserId;
			task.ModifiedOn = now;
			task.ModifiedByUserId = officerUserId;
			task.RowVersion++;
			await _assignments.UpdateAsync(task, cancellationToken, true);
			Audit(departmentId, officerUserId, AuditLogTypes.ResponderFollowUpChanged, null, FollowUpSnapshot(task, "cancelled"));
			return task;
		}

		/// <summary>A follow-up is part of the member's screening record: editable only while that member holds a seat.</summary>
		private async Task RequireWritableForFollowUpAsync(int departmentId, RmsRecordWorkAssignment task)
		{
			var flag = string.IsNullOrWhiteSpace(task?.RecordId) ? null : await _screeningFlags.GetByIdForDepartmentAsync(departmentId, task.RecordId);
			if (flag != null)
				await RequireWritableForAsync(departmentId, flag.UserId);
			else
				await RequireWritableAsync(departmentId);
		}

		private async Task<RmsRecordWorkAssignment> LoadFollowUpAsync(int departmentId, string assignmentId)
		{
			await RequireReadableAsync(departmentId);
			var task = string.IsNullOrWhiteSpace(assignmentId) ? null : await _assignments.GetByIdForDepartmentAsync(departmentId, assignmentId);
			// Only the wellness purpose is reachable here; a Record's own work assignment is never touched by this service.
			if (task == null || task.DeletedOn.HasValue || !string.Equals(task.Purpose, RmsWorkAssignmentPurposes.WellnessFollowUp, StringComparison.Ordinal))
				throw new MemberWellnessException("follow_up_not_found");
			return task;
		}

		private async Task<List<RmsRecordWorkAssignment>> FollowUpsForFlagAsync(int departmentId, string flagId)
		{
			return (await _assignments.GetForRecordAsync(departmentId, flagId) ?? Enumerable.Empty<RmsRecordWorkAssignment>())
				.Where(a => !a.DeletedOn.HasValue && string.Equals(a.Purpose, RmsWorkAssignmentPurposes.WellnessFollowUp, StringComparison.Ordinal))
				.OrderBy(a => a.IsOpen ? 0 : 1)
				.ThenBy(a => a.DueOn ?? DateTime.MaxValue)
				.ThenBy(a => a.CreatedOn)
				.ToList();
		}

		private static string FollowUpSnapshot(RmsRecordWorkAssignment task, string action) => JsonConvert.SerializeObject(new
		{
			id = task.RmsRecordWorkAssignmentId,
			flag = task.RecordId,
			kind = task.Note,
			assignee = task.AssigneeUserId,
			due = task.DueOn,
			action
		});

		#endregion Screening
	}
}
