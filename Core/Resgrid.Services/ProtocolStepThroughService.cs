using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Step-through (P3): a protocol run on a call like a checklist (the Checklists execution pattern: a stable list of items, each answered
	/// with who and when, saved as it goes). The steps are the cited version's (immutable), so a step id keeps meaning the same step; the log
	/// is append-only JSON on the call's row, written with an optimistic revision so two members stepping through at once never lose a step.
	/// </summary>
	public class ProtocolStepThroughService : IProtocolStepThroughService
	{
		public const int WriteAttempts = 3;

		private readonly IProtocolsService _protocolsService;
		private readonly ICallsService _callsService;
		private readonly ICallProtocolsRepository _callProtocols;
		private readonly IProtocolAccessService _access;

		public ProtocolStepThroughService(IProtocolsService protocolsService, ICallsService callsService, ICallProtocolsRepository callProtocols, IProtocolAccessService access)
		{
			_protocolsService = protocolsService;
			_callsService = callsService;
			_callProtocols = callProtocols;
			_access = access;
		}

		public async Task<List<ProtocolCallChecklist>> GetCallChecklistsAsync(int departmentId, int callId)
		{
			var call = await DepartmentCallAsync(departmentId, callId);
			if (call == null)
				return new List<ProtocolCallChecklist>();

			var rows = (await _callProtocols.GetCallProtocolsByCallIdAsync(callId) ?? Enumerable.Empty<CallProtocol>())
				.Where(r => r != null && r.CallId == callId && !r.IsRemoved).OrderBy(r => r.CallProtocolId).ToList();
			if (rows.Count == 0)
				return new List<ProtocolCallChecklist>();

			var versions = (await _protocolsService.GetCitedVersionsAsync(departmentId, rows.Where(r => r.ProtocolVersionId.HasValue).Select(r => r.ProtocolVersionId.Value)))
				.ToDictionary(v => v.ProtocolVersionId);

			var lists = new List<ProtocolCallChecklist>();
			foreach (var row in rows)
			{
				var protocol = row.Protocol ?? await _protocolsService.GetProtocolHeaderByIdAsync(row.DispatchProtocolId);
				if (protocol == null || protocol.DepartmentId != departmentId || !ProtocolStepRules.Applies(protocol.Kind))
					continue;
				versions.TryGetValue(row.ProtocolVersionId ?? 0, out var version);
				var checklist = Build(row, protocol, version);
				if (checklist.Steps.Count > 0)
					lists.Add(checklist);
			}

			return lists;
		}

		public async Task<ProtocolCallChecklist> GetCallChecklistAsync(int departmentId, int callId, int callProtocolId)
		{
			if (await DepartmentCallAsync(departmentId, callId) == null)
				return null;

			var row = await _callProtocols.GetForDepartmentCallAsync(departmentId, callId, callProtocolId);
			if (row == null || row.CallId != callId)
				return null;

			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(row.DispatchProtocolId);
			if (protocol == null || protocol.DepartmentId != departmentId)
				return null;

			var version = row.ProtocolVersionId.HasValue
				? (await _protocolsService.GetCitedVersionsAsync(departmentId, new[] { row.ProtocolVersionId.Value })).FirstOrDefault()
				: null;
			return Build(row, protocol, version);
		}

		public async Task<List<DispatchProtocol>> GetSopLibraryAsync(int departmentId, string userId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return new List<DispatchProtocol>();

			var candidates = (await _protocolsService.GetProtocolSummariesForDepartmentAsync(departmentId, true) ?? new List<DispatchProtocol>())
				.Where(p => p.DepartmentId == departmentId && !p.IsDeleted && !p.IsDisabled && p.Kind == (int)ProtocolKinds.Procedure
					&& p.Status == (int)ProtocolStatuses.Published && p.CurrentVersionId.HasValue)
				.ToList();
			if (candidates.Count == 0)
				return candidates;

			var reader = await _access.GetReaderAsync(departmentId, userId);
			return (await _access.FilterReadableAsync(reader, candidates) ?? new List<DispatchProtocol>())
				.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
		}

		public async Task<ProtocolStepResult> StartAsync(int departmentId, int callId, int protocolId, string userId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return ProtocolStepResult.Fail(ProtocolStepErrors.NotAuthorized);

			var call = await DepartmentCallAsync(departmentId, callId);
			if (call == null)
				return ProtocolStepResult.Fail(ProtocolStepErrors.NotFound);

			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(protocolId);
			if (protocol == null || protocol.DepartmentId != departmentId)
				return ProtocolStepResult.Fail(ProtocolStepErrors.NotFound);
			if (!ProtocolStepRules.Applies(protocol.Kind))
				return ProtocolStepResult.Fail(ProtocolStepErrors.NoSteps);

			var onCall = (await _callProtocols.GetCallProtocolsByCallIdAsync(callId) ?? Enumerable.Empty<CallProtocol>()).Where(r => r != null && r.CallId == callId).ToList();
			var row = onCall.FirstOrDefault(r => r.DispatchProtocolId == protocolId && !r.IsRemoved);
			if (row == null)
			{
				if (onCall.Any(r => r.DispatchProtocolId == protocolId))
					return ProtocolStepResult.Fail(ProtocolStepErrors.NotAttachable);

				// A reader who cannot read the protocol cannot start it (R-P4); the dispatcher rule for Dispatch protocols applies.
				if (await _access.GetReadableAsync(departmentId, userId, protocolId) == null)
					return ProtocolStepResult.Fail(ProtocolStepErrors.NotAuthorized);

				// Attached the way any manual pick is: the server decides, the version in force is cited, the text is snapshotted.
				var prepared = await _protocolsService.PrepareCallProtocolsAsync(departmentId, call,
					new[] { new ProtocolAttachRequest { ProtocolId = protocolId, AnswerIds = null, AnswersRequired = false } }, userId, onCall, true, cancellationToken);
				var attach = prepared?.Attached?.FirstOrDefault();
				if (attach == null)
					return ProtocolStepResult.Fail(ProtocolStepErrors.NotAttachable);
				row = await _callProtocols.InsertAsync(attach, cancellationToken, true);
			}

			var checklist = await GetCallChecklistAsync(departmentId, callId, row.CallProtocolId);
			if (checklist == null)
				return ProtocolStepResult.Fail(ProtocolStepErrors.NotFound);
			if (checklist.Steps.Count == 0)
				return new ProtocolStepResult { Error = ProtocolStepErrors.NoSteps, Checklist = checklist };
			if (checklist.Log != null)
				return new ProtocolStepResult { Checklist = checklist };

			return await WriteAsync(departmentId, callId, row.CallProtocolId, (log, now) =>
			{
				if (log != null)
					return (log, ProtocolStepErrors.None);
				return (new ProtocolStepLog { ProtocolVersionId = checklist.Version?.ProtocolVersionId, StartedOn = now, StartedByUserId = userId }, ProtocolStepErrors.None);
			}, cancellationToken);
		}

		public async Task<ProtocolStepResult> RecordStepAsync(int departmentId, int callId, int callProtocolId, string stepId, ProtocolStepStates state, string note,
			DateTime? deviceAtUtc, string userId, CancellationToken cancellationToken = default, string clientStepId = null)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return ProtocolStepResult.Fail(ProtocolStepErrors.NotAuthorized);
			if (string.IsNullOrWhiteSpace(stepId) || !Enum.IsDefined(typeof(ProtocolStepStates), state))
				return ProtocolStepResult.Fail(ProtocolStepErrors.UnknownStep);

			var checklist = await GetCallChecklistAsync(departmentId, callId, callProtocolId);
			if (checklist == null || checklist.Row.IsRemoved)
				return ProtocolStepResult.Fail(ProtocolStepErrors.NotFound);
			if (!ProtocolStepRules.Applies(checklist.Protocol.Kind) || checklist.Steps.Count == 0)
				return ProtocolStepResult.Fail(ProtocolStepErrors.NoSteps);
			if (checklist.Steps.All(s => !string.Equals(s.Step.Id, stepId, StringComparison.Ordinal)))
				return ProtocolStepResult.Fail(ProtocolStepErrors.UnknownStep);

			var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
			if (cleanNote != null && cleanNote.Length > ProtocolStepLogEntry.NoteMaxLength)
				cleanNote = cleanNote.Substring(0, ProtocolStepLogEntry.NoteMaxLength);

			// An offline replay of an event already recorded under the device's id changes nothing (idempotent), even after the log was finished.
			var cid = ProtocolStepLog.CleanClientStepId(clientStepId);
			if (checklist.Log != null && checklist.Log.HasClientStep(cid))
				return new ProtocolStepResult { Checklist = checklist };

			return await WriteAsync(departmentId, callId, callProtocolId, (log, now) =>
			{
				log ??= new ProtocolStepLog { ProtocolVersionId = checklist.Version?.ProtocolVersionId, StartedOn = now, StartedByUserId = userId };
				// Re-checked on the row as read for the write: two replays of the same event at once record it once.
				if (log.HasClientStep(cid))
					return (log, ProtocolStepErrors.None);
				if (log.IsComplete)
					return (log, ProtocolStepErrors.Completed);
				if (log.Entries.Count >= ProtocolStepLog.MaxEntries)
					return (log, ProtocolStepErrors.Conflict);
				log.Entries.Add(new ProtocolStepLogEntry
				{
					StepId = stepId,
					State = (int)state,
					At = now,
					// A device time is kept only as what the device said; it never orders the log (the server time does).
					DeviceAt = deviceAtUtc.HasValue && deviceAtUtc.Value <= now.AddMinutes(5) ? DateTime.SpecifyKind(deviceAtUtc.Value, DateTimeKind.Utc) : (DateTime?)null,
					UserId = userId,
					Note = cleanNote,
					ClientStepId = cid
				});
				return (log, ProtocolStepErrors.None);
			}, cancellationToken);
		}

		public async Task<ProtocolStepResult> CompleteAsync(int departmentId, int callId, int callProtocolId, string userId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return ProtocolStepResult.Fail(ProtocolStepErrors.NotAuthorized);

			var checklist = await GetCallChecklistAsync(departmentId, callId, callProtocolId);
			if (checklist == null || checklist.Row.IsRemoved)
				return ProtocolStepResult.Fail(ProtocolStepErrors.NotFound);
			if (checklist.Steps.Count == 0)
				return ProtocolStepResult.Fail(ProtocolStepErrors.NoSteps);

			return await WriteAsync(departmentId, callId, callProtocolId, (log, now) =>
			{
				log ??= new ProtocolStepLog { ProtocolVersionId = checklist.Version?.ProtocolVersionId, StartedOn = now, StartedByUserId = userId };
				if (log.IsComplete)
					return (log, ProtocolStepErrors.None);
				log.CompletedOn = now;
				log.CompletedByUserId = userId;
				return (log, ProtocolStepErrors.None);
			}, cancellationToken);
		}

		/// <summary>Read the row, apply the change to its log, write it against the revision read; re-read and re-apply on a conflict.</summary>
		private async Task<ProtocolStepResult> WriteAsync(int departmentId, int callId, int callProtocolId,
			Func<ProtocolStepLog, DateTime, (ProtocolStepLog Log, ProtocolStepErrors Error)> apply, CancellationToken cancellationToken)
		{
			for (var attempt = 0; attempt < WriteAttempts; attempt++)
			{
				var row = await _callProtocols.GetForDepartmentCallAsync(departmentId, callId, callProtocolId);
				if (row == null)
					return ProtocolStepResult.Fail(ProtocolStepErrors.NotFound);

				var current = ProtocolStepLog.Parse(row.StepLogJson);
				var (log, error) = apply(current, DateTime.UtcNow);
				if (error != ProtocolStepErrors.None)
					return new ProtocolStepResult { Error = error, Checklist = await GetCallChecklistAsync(departmentId, callId, callProtocolId) };

				if (await _callProtocols.TryUpdateStepLogAsync(callProtocolId, log.ToJson(), row.StepLogRevision, cancellationToken))
					return new ProtocolStepResult { Checklist = await GetCallChecklistAsync(departmentId, callId, callProtocolId) };
			}

			Logging.LogError($"Protocol step log of call protocol {callProtocolId} could not be written after {WriteAttempts} attempts.");
			return ProtocolStepResult.Fail(ProtocolStepErrors.Conflict);
		}

		private async Task<Call> DepartmentCallAsync(int departmentId, int callId)
		{
			if (departmentId <= 0 || callId <= 0)
				return null;
			var call = await _callsService.GetCallByIdAsync(callId);
			return call != null && call.DepartmentId == departmentId ? call : null;
		}

		/// <summary>The checklist of one row: the cited version's steps with each one's state from the log.</summary>
		public static ProtocolCallChecklist Build(CallProtocol row, DispatchProtocol protocol, ProtocolVersion version)
		{
			var log = ProtocolStepLog.Parse(row?.StepLogJson);
			var definitions = version == null || version.IsDocument
				? new List<ProtocolStepDefinition>()
				: ProtocolSteps.Extract(version.ContentJson, version.ContentHtml);
			return new ProtocolCallChecklist
			{
				Row = row,
				Protocol = protocol,
				Version = version,
				Log = log,
				Steps = definitions.Select(d => new ProtocolStepView
				{
					Step = d,
					State = log?.StateOf(d.Id) ?? ProtocolStepStates.Pending,
					Last = log?.LastFor(d.Id)
				}).ToList()
			};
		}
	}
}
