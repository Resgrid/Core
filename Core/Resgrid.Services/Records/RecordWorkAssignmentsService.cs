using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Work assignments on Records (RMS plan section 5.2, RMS-1D). Assigning needs the ReviewRecords permission,
	/// draft ownership, or department administration; acknowledging and completing need the caller to be an
	/// addressee in a verified context; the queue re-checks per-Record visibility on every read because an
	/// assignment narrows a queue and never grants access. Every change is audited with the client origin.
	/// </summary>
	public class RecordWorkAssignmentsService : IRecordWorkAssignmentsService
	{
		private readonly IRmsRecordWorkAssignmentsRepository _assignments;
		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly IUnitsService _units;
		private readonly IDepartmentGroupsService _groups;
		private readonly IIncidentCommandService _command;

		public RecordWorkAssignmentsService(IRmsRecordWorkAssignmentsRepository assignments, IRmsOperationalRecordsRepository records, IRecordsAuthorizationService authorization,
			IRmsAccessAuditsRepository audits, IUnitsService units, IDepartmentGroupsService groups, IIncidentCommandService command)
		{
			_assignments = assignments;
			_records = records;
			_authorization = authorization;
			_audits = audits;
			_units = units;
			_groups = groups;
			_command = command;
		}

		public async Task<RmsRecordWorkAssignment> AssignAsync(int departmentId, string userId, RecordWorkAssignmentInput input, CancellationToken cancellationToken = default)
		{
			if (input == null) throw new ArgumentNullException(nameof(input));
			if (string.IsNullOrWhiteSpace(input.RecordId)) throw new ArgumentException("A record is required.", nameof(input));
			var purpose = (input.Purpose ?? RmsWorkAssignmentPurposes.Complete).Trim().ToLowerInvariant();
			if (!RmsWorkAssignmentPurposes.IsKnown(purpose)) throw new ArgumentException("Unknown assignment purpose '" + input.Purpose + "'.", nameof(input));

			var record = await _records.GetByIdForDepartmentAsync(departmentId, input.RecordId);
			if (record == null || record.DeletedOn.HasValue || record.PurgedOn.HasValue) throw new ArgumentException("The record was not found.", nameof(input));
			if (!await _authorization.CanUserViewRecordAsync(userId, record.RmsOperationalRecordId, departmentId)) throw new UnauthorizedAccessException("Record access is not authorized.");
			if (!await CanManageAsync(departmentId, userId, record)) throw new UnauthorizedAccessException("Assigning work requires ReviewRecords, draft ownership or department administration.");
			var state = (RmsRecordState)record.State;
			if (RmsLifecycle.IsTerminal(state)) throw new RecordTransitionException(record.RmsOperationalRecordId, state, state, "work cannot be assigned on a voided or cancelled Record");

			var row = new RmsRecordWorkAssignment
			{
				RmsRecordWorkAssignmentId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RecordId = record.RmsOperationalRecordId,
				AssigneeKind = (int)input.AssigneeKind, Purpose = purpose, Note = Trim(input.Note, 1000), DueOn = input.DueOn, State = (int)RmsWorkAssignmentState.Open,
				SourceContextJson = input.SourceContext == null || input.SourceContext.IsEmpty ? null : JsonConvert.SerializeObject(new { input.SourceContext.CallId, input.SourceContext.UnitId, input.SourceContext.GroupId, input.SourceContext.CommandRole }),
				OriginClient = (int)input.OriginClient, CreatedOn = DateTime.UtcNow, CreatedByUserId = userId, ModifiedOn = DateTime.UtcNow, ModifiedByUserId = userId, RowVersion = 1
			};

			switch (input.AssigneeKind)
			{
				case RmsWorkAssigneeKind.Person:
					if (string.IsNullOrWhiteSpace(input.AssigneeUserId)) throw new ArgumentException("An assignee is required.", nameof(input));
					if (!await _authorization.IsActiveMemberAsync(input.AssigneeUserId, departmentId)) throw new ArgumentException("The assignee is not an active member of this department.", nameof(input));
					row.AssigneeUserId = input.AssigneeUserId;
					break;
				case RmsWorkAssigneeKind.Unit:
					var unit = input.AssigneeUnitId.HasValue ? await _units.GetUnitByIdAsync(input.AssigneeUnitId.Value) : null;
					if (unit == null || unit.DepartmentId != departmentId) throw new ArgumentException("The unit was not found in this department.", nameof(input));
					row.AssigneeUnitId = unit.UnitId;
					break;
				case RmsWorkAssigneeKind.Group:
					var group = input.AssigneeGroupId.HasValue ? await _groups.GetGroupByIdAsync(input.AssigneeGroupId.Value) : null;
					if (group == null || group.DepartmentId != departmentId) throw new ArgumentException("The group was not found in this department.", nameof(input));
					row.AssigneeGroupId = group.DepartmentGroupId;
					break;
				case RmsWorkAssigneeKind.CommandRole:
				case RmsWorkAssigneeKind.DispatchRole:
					if (string.IsNullOrWhiteSpace(input.AssigneeRole)) throw new ArgumentException("A role name is required.", nameof(input));
					row.AssigneeRole = Trim(input.AssigneeRole, 100);
					break;
				default:
					throw new ArgumentException("Unknown assignee kind.", nameof(input));
			}

			await _assignments.InsertAsync(row, cancellationToken, true);
			await AuditAsync(departmentId, userId, row, "Assign work", input.OriginClient, cancellationToken, new { row.AssigneeKind, row.AssigneeUserId, row.AssigneeUnitId, row.AssigneeGroupId, row.AssigneeRole, row.Purpose, row.DueOn });
			return row;
		}

		public async Task<RmsRecordWorkAssignment> AcknowledgeAsync(int departmentId, string userId, string assignmentId, long? expectedRowVersion, FieldRecordContext context, RmsOriginClient origin, CancellationToken cancellationToken = default)
		{
			var row = await LoadAsync(departmentId, userId, assignmentId);
			if (!await IsAssigneeAsync(departmentId, userId, row, context)) throw new UnauthorizedAccessException("Only an addressee may acknowledge this assignment.");
			if (row.State != (int)RmsWorkAssignmentState.Open) throw new InvalidOperationException("Only an open assignment can be acknowledged.");
			Guard(row, expectedRowVersion);
			row.State = (int)RmsWorkAssignmentState.Acknowledged;
			row.AcknowledgedOn = DateTime.UtcNow;
			row.AcknowledgedByUserId = userId;
			await SaveAsync(row, userId, cancellationToken);
			await AuditAsync(departmentId, userId, row, "Acknowledge work", origin, cancellationToken);
			return row;
		}

		public async Task<RmsRecordWorkAssignment> CompleteAsync(int departmentId, string userId, string assignmentId, long? expectedRowVersion, FieldRecordContext context, RmsOriginClient origin, CancellationToken cancellationToken = default)
		{
			var row = await LoadAsync(departmentId, userId, assignmentId);
			if (!await IsAssigneeAsync(departmentId, userId, row, context) && !await CanManageAsync(departmentId, userId, await _records.GetByIdForDepartmentAsync(departmentId, row.RecordId)))
				throw new UnauthorizedAccessException("Only an addressee or the assigner may complete this assignment.");
			if (!row.IsOpen) throw new InvalidOperationException("The assignment is already closed.");
			Guard(row, expectedRowVersion);
			row.State = (int)RmsWorkAssignmentState.Completed;
			row.CompletedOn = DateTime.UtcNow;
			row.CompletedByUserId = userId;
			await SaveAsync(row, userId, cancellationToken);
			await AuditAsync(departmentId, userId, row, "Complete work", origin, cancellationToken);
			return row;
		}

		public async Task<RmsRecordWorkAssignment> CancelAsync(int departmentId, string userId, string assignmentId, long? expectedRowVersion, string reason, RmsOriginClient origin, CancellationToken cancellationToken = default)
		{
			var row = await LoadAsync(departmentId, userId, assignmentId);
			if (!await CanManageAsync(departmentId, userId, await _records.GetByIdForDepartmentAsync(departmentId, row.RecordId)) && !string.Equals(row.CreatedByUserId, userId, StringComparison.OrdinalIgnoreCase))
				throw new UnauthorizedAccessException("Only the assigner, a reviewer or an administrator may cancel this assignment.");
			if (!row.IsOpen) throw new InvalidOperationException("The assignment is already closed.");
			Guard(row, expectedRowVersion);
			row.State = (int)RmsWorkAssignmentState.Cancelled;
			row.CancelledOn = DateTime.UtcNow;
			row.CancelledByUserId = userId;
			row.CancelReason = Trim(reason, 500);
			await SaveAsync(row, userId, cancellationToken);
			await AuditAsync(departmentId, userId, row, "Cancel work", origin, cancellationToken, new { reason = row.CancelReason });
			return row;
		}

		public async Task<RmsRecordWorkAssignment> GetAsync(int departmentId, string userId, string assignmentId)
		{
			if (string.IsNullOrWhiteSpace(assignmentId)) return null;
			var row = await _assignments.GetByIdForDepartmentAsync(departmentId, assignmentId);
			if (row == null || row.DeletedOn.HasValue) return null;
			return await _authorization.CanUserViewRecordAsync(userId, row.RecordId, departmentId) ? row : null;
		}

		public async Task<List<RmsRecordWorkAssignment>> GetForRecordAsync(int departmentId, string userId, string recordId)
		{
			if (string.IsNullOrWhiteSpace(recordId) || !await _authorization.CanUserViewRecordAsync(userId, recordId, departmentId)) return new List<RmsRecordWorkAssignment>();
			return (await _assignments.GetForRecordAsync(departmentId, recordId))?.Where(a => !a.DeletedOn.HasValue).OrderBy(a => a.State).ThenBy(a => a.DueOn ?? DateTime.MaxValue).ThenBy(a => a.CreatedOn).ToList() ?? new List<RmsRecordWorkAssignment>();
		}

		public async Task<List<RmsRecordWorkAssignment>> GetQueueAsync(int departmentId, string userId, FieldRecordContext context, int take)
		{
			take = Math.Max(1, Math.Min(RecordsFieldConfig.AssignmentsMax, take <= 0 ? RecordsFieldConfig.AssignmentsMax : take));
			var addressees = await AddresseesAsync(departmentId, userId, context ?? new FieldRecordContext());
			var rows = (await _assignments.GetOpenForAssigneesAsync(departmentId, userId, addressees.UnitIds, addressees.GroupIds, addressees.Roles, take * 2))?.Where(a => !a.DeletedOn.HasValue).ToList() ?? new List<RmsRecordWorkAssignment>();
			var visible = new List<RmsRecordWorkAssignment>();
			foreach (var row in rows)
			{
				// The queue narrows; live authorization decides. A row the caller cannot read is withheld, not tombstoned here.
				if (await _authorization.CanUserViewRecordAsync(userId, row.RecordId, departmentId))
					visible.Add(row);
				if (visible.Count >= take) break;
			}
			return visible;
		}

		public async Task<bool> IsAssigneeAsync(int departmentId, string userId, RmsRecordWorkAssignment assignment, FieldRecordContext context)
		{
			if (assignment == null || string.IsNullOrWhiteSpace(userId)) return false;
			switch ((RmsWorkAssigneeKind)assignment.AssigneeKind)
			{
				case RmsWorkAssigneeKind.Person:
					return string.Equals(assignment.AssigneeUserId, userId, StringComparison.OrdinalIgnoreCase);
				case RmsWorkAssigneeKind.Unit:
					return assignment.AssigneeUnitId.HasValue && await IsStaffedOnUnitAsync(departmentId, userId, assignment.AssigneeUnitId.Value);
				case RmsWorkAssigneeKind.Group:
					var group = await _groups.GetGroupForUserAsync(userId, departmentId);
					return group != null && assignment.AssigneeGroupId == group.DepartmentGroupId;
				case RmsWorkAssigneeKind.CommandRole:
					return await HoldsCommandRoleAsync(departmentId, userId, assignment, context);
				case RmsWorkAssigneeKind.DispatchRole:
					return await _authorization.CanCreateSourceCallAsync(userId, departmentId);
				default:
					return false;
			}
		}

		/// <summary>The caller is staffed on the unit when the unit's latest state lists them in a role.</summary>
		public async Task<bool> IsStaffedOnUnitAsync(int departmentId, string userId, int unitId)
		{
			var unit = await _units.GetUnitByIdAsync(unitId);
			if (unit == null || unit.DepartmentId != departmentId) return false;
			var state = await _units.GetLastUnitStateByUnitIdAsync(unitId);
			return state?.Roles != null && state.Roles.Any(r => string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase));
		}

		private async Task<bool> HoldsCommandRoleAsync(int departmentId, string userId, RmsRecordWorkAssignment assignment, FieldRecordContext context)
		{
			var record = await _records.GetByIdForDepartmentAsync(departmentId, assignment.RecordId);
			var callId = record?.CallId ?? context?.CallId;
			if (!callId.HasValue) return false;
			var command = await _command.GetActiveCommandForCallAsync(departmentId, callId.Value);
			if (command == null) return false;
			if (string.Equals(command.CurrentCommanderUserId, userId, StringComparison.OrdinalIgnoreCase)) return true;
			var board = await _command.GetCommandBoardAsync(departmentId, callId.Value);
			return board?.Nodes != null && board.Nodes.Any(n => string.Equals(n.SupervisorUserId, userId, StringComparison.OrdinalIgnoreCase)
				&& (string.IsNullOrWhiteSpace(assignment.AssigneeRole) || string.Equals(n.Name, assignment.AssigneeRole, StringComparison.OrdinalIgnoreCase)));
		}

		private async Task<(List<int> UnitIds, List<int> GroupIds, List<string> Roles)> AddresseesAsync(int departmentId, string userId, FieldRecordContext context)
		{
			var unitIds = new List<int>();
			var groupIds = new List<int>();
			var roles = new List<string>();
			if (context.UnitId.HasValue && await IsStaffedOnUnitAsync(departmentId, userId, context.UnitId.Value)) unitIds.Add(context.UnitId.Value);
			var group = await _groups.GetGroupForUserAsync(userId, departmentId);
			if (group != null) groupIds.Add(group.DepartmentGroupId);
			if (context.CallId.HasValue)
			{
				var command = await _command.GetActiveCommandForCallAsync(departmentId, context.CallId.Value);
				if (command != null)
				{
					var board = await _command.GetCommandBoardAsync(departmentId, context.CallId.Value);
					if (string.Equals(command.CurrentCommanderUserId, userId, StringComparison.OrdinalIgnoreCase)) roles.Add("Command");
					if (board?.Nodes != null) roles.AddRange(board.Nodes.Where(n => string.Equals(n.SupervisorUserId, userId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(n.Name)).Select(n => n.Name));
				}
			}
			if (await _authorization.CanCreateSourceCallAsync(userId, departmentId)) roles.Add("Dispatch");
			return (unitIds, groupIds.Distinct().ToList(), roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
		}

		private async Task<bool> CanManageAsync(int departmentId, string userId, RmsOperationalRecord record)
		{
			if (record != null && string.Equals(record.OwnerUserId, userId, StringComparison.OrdinalIgnoreCase)) return true;
			if (await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ReviewRecords)) return true;
			return await _authorization.IsDepartmentAdminAsync(userId, departmentId);
		}

		private async Task<RmsRecordWorkAssignment> LoadAsync(int departmentId, string userId, string assignmentId)
		{
			var row = await GetAsync(departmentId, userId, assignmentId);
			if (row == null) throw new ArgumentException("The assignment was not found.", nameof(assignmentId));
			return row;
		}

		private static void Guard(RmsRecordWorkAssignment row, long? expectedRowVersion)
		{
			if (expectedRowVersion.HasValue && expectedRowVersion.Value != row.RowVersion)
				throw new RecordConcurrencyException(row.RecordId, expectedRowVersion.Value, row.RowVersion);
		}

		private async Task SaveAsync(RmsRecordWorkAssignment row, string userId, CancellationToken cancellationToken)
		{
			row.RowVersion += 1;
			row.ModifiedOn = DateTime.UtcNow;
			row.ModifiedByUserId = userId;
			await _assignments.UpdateAsync(row, cancellationToken, true);
		}

		private Task AuditAsync(int departmentId, string userId, RmsRecordWorkAssignment row, string purpose, RmsOriginClient origin, CancellationToken cancellationToken, object detail = null)
		{
			return _audits.InsertAsync(new RmsAccessAudit
			{
				DepartmentId = departmentId, RecordId = row.RecordId, Action = (int)RmsAccessAuditAction.Admin, ActorUserId = userId, Purpose = purpose, OriginClient = (int)origin, Successful = true, OccurredOn = DateTime.UtcNow,
				DetailJson = JsonConvert.SerializeObject(new { assignment_id = row.RmsRecordWorkAssignmentId, state = ((RmsWorkAssignmentState)row.State).ToString(), origin_client = origin.ToString(), detail })
			}, cancellationToken, true);
		}

		private static string Trim(string value, int max) => string.IsNullOrWhiteSpace(value) ? null : (value.Trim().Length > max ? value.Trim().Substring(0, max) : value.Trim());
	}
}
