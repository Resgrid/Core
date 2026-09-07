using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records.Evidence
{
	/// <summary>
	/// The projection kinds an operational pack composes from owning modules (RMS plan RMS-1C, "compose ... from their
	/// owning modules with immutable references"). Each is a bounded snapshot with source identifiers; none hydrates a live
	/// source or becomes a second copy of it.
	/// </summary>
	public static class RecordPackProjectionKinds
	{
		/// <summary>Who was on the record and their current status/staffing at capture (CERT/EOC/deployment check-in).</summary>
		public const string PersonnelCheckIn = "personnel-check-in";
		/// <summary>Units on the record plus the inventory ledger usage the record already references (equipment/resource summary).</summary>
		public const string ResourceSummary = "resource-summary";
		/// <summary>Certification codes/status/validity for the record's participants; never numbers or documents.</summary>
		public const string Qualifications = "qualifications";
		/// <summary>The active command structure for the record's Call: roles, assignments, objectives, needs (IAP/ICS inputs).</summary>
		public const string CommandSummary = "command-summary";
		public static readonly IReadOnlyList<string> All = new[] { PersonnelCheckIn, ResourceSummary, Qualifications, CommandSummary };
		public static bool IsKnown(string kind) => kind != null && All.Contains(kind.Trim().ToLowerInvariant());
	}

	/// <summary>
	/// Composes an operational pack's module projections into one evidence artifact per kind (RMS-1C). The projection kind
	/// rides in <see cref="RecordEvidenceCaptureRequest.SourceIds"/>[0]; the manifest carries only identifiers, codes,
	/// counts and statuses from the owning module so the artifact is minimum-necessary by construction.
	/// </summary>
	public class PackProjectionEvidenceAdapter : IRecordEvidenceAdapter
	{
		public const string SourceSubsystem = "Modules";

		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRmsRecordParticipantsRepository _participants;
		private readonly IRmsRecordUnitResponsesRepository _unitResponses;
		private readonly IUserStateService _userStates;
		private readonly IDepartmentsService _departments;
		private readonly ICertificationService _certifications;
		private readonly IRmsInventoryUsageAdapter _inventory;
		private readonly IUnitsService _units;
		private readonly IIncidentCommandService _command;
		private readonly Lazy<IAuthorizationService> _authorization;

		public PackProjectionEvidenceAdapter(IRmsOperationalRecordsRepository records, IRmsRecordParticipantsRepository participants, IRmsRecordUnitResponsesRepository unitResponses,
			IUserStateService userStates, IDepartmentsService departments, ICertificationService certifications, IRmsInventoryUsageAdapter inventory, IUnitsService units,
			IIncidentCommandService command, Lazy<IAuthorizationService> authorization)
		{
			_records = records;
			_participants = participants;
			_unitResponses = unitResponses;
			_userStates = userStates;
			_departments = departments;
			_certifications = certifications;
			_inventory = inventory;
			_units = units;
			_command = command;
			_authorization = authorization;
		}

		public RmsEvidenceKind Kind => RmsEvidenceKind.ModuleProjection;

		public Task<bool> IsAvailableAsync(int departmentId) => Task.FromResult(true);

		public async Task<RecordEvidenceCapture> CaptureAsync(RecordEvidenceCaptureRequest request, CancellationToken cancellationToken = default)
		{
			var kind = request.SourceIds?.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim().ToLowerInvariant();
			if (!RecordPackProjectionKinds.IsKnown(kind))
				throw new ArgumentException("Choose a module projection: " + string.Join(", ", RecordPackProjectionKinds.All) + ".");
			if (request.RecordKind != RmsRecordKind.Operational)
				return RecordEvidenceCapture.Unavailable("Module projections compose onto operational Records only.");
			var record = await _records.GetByIdForDepartmentAsync(request.DepartmentId, request.RecordId);
			if (record == null)
				return RecordEvidenceCapture.Unavailable("The Record was not found.");

			switch (kind)
			{
				case RecordPackProjectionKinds.PersonnelCheckIn: return await PersonnelCheckInAsync(request, record, cancellationToken);
				case RecordPackProjectionKinds.ResourceSummary: return await ResourceSummaryAsync(request, record, cancellationToken);
				case RecordPackProjectionKinds.Qualifications: return await QualificationsAsync(request, record, cancellationToken);
				default: return await CommandSummaryAsync(request, record, cancellationToken);
			}
		}

		private async Task<List<RmsRecordParticipant>> ParticipantsAsync(RecordEvidenceCaptureRequest request)
		{
			var rows = (await _participants.GetForRecordAsync(request.DepartmentId, request.RecordId, null))?.Where(p => !string.IsNullOrWhiteSpace(p.UserId)).ToList() ?? new List<RmsRecordParticipant>();
			var chosen = (request.UserIds ?? new List<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).ToHashSet(StringComparer.OrdinalIgnoreCase);
			if (chosen.Count > 0) rows = rows.Where(p => chosen.Contains(p.UserId)).ToList();
			if (rows.Count > EvidenceLimits.MaxItems) throw new ArgumentException("The Record has too many participants for one projection; select a subset.");
			return rows;
		}

		private async Task RequirePeopleAsync(RecordEvidenceCaptureRequest request, IEnumerable<string> userIds)
		{
			foreach (var userId in userIds.Distinct(StringComparer.OrdinalIgnoreCase))
				if (!await _authorization.Value.CanUserViewPersonAsync(request.CapturedByUserId, userId, request.DepartmentId))
					throw new UnauthorizedAccessException("Personnel source access is not authorized.");
		}

		private async Task<RecordEvidenceCapture> PersonnelCheckInAsync(RecordEvidenceCaptureRequest request, RmsOperationalRecord record, CancellationToken cancellationToken)
		{
			var participants = await ParticipantsAsync(request);
			if (participants.Count == 0) return RecordEvidenceCapture.Unavailable("Personnel check-in needs at least one participant on the Record.");
			await RequirePeopleAsync(request, participants.Select(p => p.UserId));
			var names = (await _departments.GetAllPersonnelNamesForDepartmentAsync(request.DepartmentId) ?? new List<PersonName>()).ToDictionary(n => n.UserId, n => n.Name, StringComparer.OrdinalIgnoreCase);
			var now = DateTime.UtcNow;
			var people = new List<object>();
			foreach (var participant in participants)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var state = await _userStates.GetLastUserStateByUserIdAsync(participant.UserId);
				people.Add(new
				{
					user_id = participant.UserId, name = participant.DisplayNameSnapshot ?? (names.TryGetValue(participant.UserId, out var n) ? n : null), role = participant.Role,
					group_id = participant.GroupIdSnapshot, group = participant.GroupNameSnapshot, unit_id = participant.UnitId,
					participation_start = participant.ParticipationStart, participation_end = participant.ParticipationEnd,
					status = state == null ? null : new { state_id = state.State, changed_on = state.Timestamp, source_id = state.UserId + ":" + state.Timestamp.ToString("O") }
				});
			}
			return new RecordEvidenceCapture
			{
				Title = "Personnel check-in", SourceSubsystem = SourceSubsystem, SourceEntityType = "personnel-check-in", SourceEntityId = RecordPackProjectionKinds.PersonnelCheckIn,
				IdentifierScheme = "resgrid:user", CoverageStart = record.StartedOn, CoverageEnd = now, SourceItemCount = people.Count, Classification = RmsEvidenceClassification.Unrestricted,
				Manifest = new { projection = RecordPackProjectionKinds.PersonnelCheckIn, record_id = record.RmsOperationalRecordId, captured_on = now, people }
			};
		}

		private async Task<RecordEvidenceCapture> ResourceSummaryAsync(RecordEvidenceCaptureRequest request, RmsOperationalRecord record, CancellationToken cancellationToken)
		{
			var responses = (await _unitResponses.GetForRecordAsync(request.DepartmentId, request.RecordId, null))?.ToList() ?? new List<RmsRecordUnitResponse>();
			var usage = (await _inventory.GetUsageForRecordAsync(request.DepartmentId, request.RecordId))?.ToList() ?? new List<RmsInventoryUsage>();
			if (responses.Count == 0 && usage.Count == 0) return RecordEvidenceCapture.Unavailable("The Record names no units and references no inventory usage yet.");
			if (responses.Count + usage.Count > EvidenceLimits.MaxItems) throw new ArgumentException("The Record references too many resources for one projection.");
			var units = new List<object>();
			foreach (var response in responses)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var unit = response.UnitId > 0 ? await _units.GetUnitByIdAsync(response.UnitId) : null;
				if (unit != null && unit.DepartmentId != request.DepartmentId) unit = null;
				units.Add(new { unit_id = response.UnitId, name = response.UnitNameSnapshot ?? unit?.Name, type = unit?.Type, station_group_id = unit?.StationGroupId, dispatched = response.Dispatched, enroute = response.Enroute, on_scene = response.OnScene, released = response.Released, in_quarters = response.InQuarters });
			}
			var now = DateTime.UtcNow;
			return new RecordEvidenceCapture
			{
				Title = "Equipment and resource summary", SourceSubsystem = SourceSubsystem, SourceEntityType = "resource-summary", SourceEntityId = RecordPackProjectionKinds.ResourceSummary,
				IdentifierScheme = "resgrid:unit", CoverageStart = record.StartedOn, CoverageEnd = record.EndedOn ?? now, SourceItemCount = responses.Count + usage.Count, Classification = RmsEvidenceClassification.Unrestricted,
				Manifest = new
				{
					projection = RecordPackProjectionKinds.ResourceSummary, record_id = record.RmsOperationalRecordId, captured_on = now, units,
					inventory_usage = usage.Select(u => new { source_id = u.ReferenceId, inventory_id = u.InventoryId, item = u.ItemName, quantity = u.Quantity, unit_of_measure = u.UnitOfMeasure, recorded_on = u.CapturedOn, source_checksum = u.SourceChecksum }).ToList()
				}
			};
		}

		private async Task<RecordEvidenceCapture> QualificationsAsync(RecordEvidenceCaptureRequest request, RmsOperationalRecord record, CancellationToken cancellationToken)
		{
			var participants = await ParticipantsAsync(request);
			if (participants.Count == 0) return RecordEvidenceCapture.Unavailable("Qualifications need at least one participant on the Record.");
			await RequirePeopleAsync(request, participants.Select(p => p.UserId));
			var asOf = request.CoverageEnd ?? record.StartedOn ?? DateTime.UtcNow;
			var people = new List<object>();
			var total = 0;
			foreach (var userId in participants.Select(p => p.UserId).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				cancellationToken.ThrowIfCancellationRequested();
				var certifications = (await _certifications.GetCertificationsByUserIdAsync(userId))?.Where(c => c != null && c.DepartmentId == request.DepartmentId).ToList() ?? new List<PersonnelCertification>();
				total += certifications.Count;
				if (total > EvidenceLimits.MaxItems) throw new ArgumentException("The participants hold too many certifications for one projection.");
				people.Add(new
				{
					user_id = userId,
					qualifications = certifications.Select(c => new { source_id = c.PersonnelCertificationId, type = c.Type, name = c.Name, area = c.Area, issued_by = c.IssuedBy, expires_on = c.ExpiresOn,
						valid_as_of = (!c.RecievedOn.HasValue || c.RecievedOn.Value <= asOf) && (!c.ExpiresOn.HasValue || c.ExpiresOn.Value >= asOf) }).ToList()
				});
			}
			return new RecordEvidenceCapture
			{
				Title = "Qualifications", SourceSubsystem = SourceSubsystem, SourceEntityType = "qualifications", SourceEntityId = RecordPackProjectionKinds.Qualifications,
				IdentifierScheme = "resgrid:personnelcertification", CoverageEnd = asOf, SourceItemCount = total,
				// Certification standing is Restricted in every pack's protected-data policy; numbers and documents never leave Certifications.
				Classification = RmsEvidenceClassification.Restricted,
				Manifest = new { projection = RecordPackProjectionKinds.Qualifications, record_id = record.RmsOperationalRecordId, as_of = asOf, people }
			};
		}

		private async Task<RecordEvidenceCapture> CommandSummaryAsync(RecordEvidenceCaptureRequest request, RmsOperationalRecord record, CancellationToken cancellationToken)
		{
			var callId = request.CallId ?? record.CallId;
			if (!callId.HasValue) return RecordEvidenceCapture.Unavailable("A command summary needs the Record's Call.");
			var board = await _command.GetCommandBoardAsync(request.DepartmentId, callId.Value);
			if (board?.Command == null) return RecordEvidenceCapture.Unavailable("No incident command was established for this Call.");
			var command = board.Command;
			var now = DateTime.UtcNow;
			return new RecordEvidenceCapture
			{
				Title = "Incident command summary", SourceSubsystem = "IncidentCommand", SourceEntityType = "incident-command", SourceEntityId = command.IncidentCommandId,
				IdentifierScheme = "resgrid:incidentcommand", CoverageStart = command.EstablishedOn, CoverageEnd = command.EstimatedEndOn ?? now,
				SourceItemCount = (board.Nodes?.Count ?? 0) + (board.Assignments?.Count ?? 0) + (board.Objectives?.Count ?? 0) + (board.Needs?.Count ?? 0),
				Classification = RmsEvidenceClassification.Unrestricted,
				Manifest = new
				{
					projection = RecordPackProjectionKinds.CommandSummary, record_id = record.RmsOperationalRecordId, captured_on = now,
					command = new { command.IncidentCommandId, command.CallId, command.Name, command.EstablishedOn, command.EstablishedByUserId, command.CurrentCommanderUserId, command.IcsLevel, command.EstimatedEndOn, command_post = command.CommandPostLocationText, staging = command.StagingLocationText },
					structure_nodes = board.Nodes?.Count ?? 0, assignments = board.Assignments?.Count ?? 0, objectives = board.Objectives?.Count ?? 0, needs = board.Needs?.Count ?? 0, timers = board.Timers?.Count ?? 0,
					node_ids = (board.Nodes ?? new List<CommandStructureNode>()).Select(n => n.CommandStructureNodeId).ToList(),
					objective_ids = (board.Objectives ?? new List<TacticalObjective>()).Select(o => o.TacticalObjectiveId).ToList()
				}
			};
		}
	}
}
