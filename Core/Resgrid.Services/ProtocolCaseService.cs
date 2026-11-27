using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// <see cref="IProtocolCaseService"/> (Protocols P4): the case flow of the department's Dispatch protocols, the override after a call is
	/// saved, the caller follow-up over the intake SMS thread and the instruction audio. Graphs are read from the version in force (or the
	/// version the call cites), never from a draft, and only when they validate.
	/// </summary>
	public class ProtocolCaseService : IProtocolCaseService
	{
		/// <summary>Longest instruction block texted to a caller (several SMS segments at most).</summary>
		public const int CallerTextMaxLength = 640;

		private readonly IProtocolsService _protocols;
		private readonly IProtocolVersionsRepository _versions;
		private readonly ICallProtocolsRepository _callProtocols;
		private readonly Lazy<IIntakeConversationService> _conversations;
		private readonly Lazy<ITtsAudioService> _tts;
		private readonly IEventAggregator _events;
		private readonly Lazy<IProtectedReadService> _protectedRead;
		private readonly Lazy<IProtectedWriteService> _protectedWrite;
		private readonly IProtectedGrantContext _grant;

		public ProtocolCaseService(IProtocolsService protocols, IProtocolVersionsRepository versions, ICallProtocolsRepository callProtocols,
			Lazy<IIntakeConversationService> conversations, Lazy<ITtsAudioService> tts, IEventAggregator events,
			Lazy<IProtectedReadService> protectedRead, Lazy<IProtectedWriteService> protectedWrite, IProtectedGrantContext grant)
		{
			_protectedRead = protectedRead;
			_protectedWrite = protectedWrite;
			_grant = grant;
			_protocols = protocols;
			_versions = versions;
			_callProtocols = callProtocols;
			_conversations = conversations;
			_tts = tts;
			_events = events;
		}

		public async Task<ProtocolCaseDefinition> GetDefinitionAsync(int departmentId, int protocolId)
		{
			if (departmentId <= 0 || protocolId <= 0)
				return null;

			var protocol = (await _protocols.GetMatchingSetAsync(departmentId)).FirstOrDefault(p => p.DispatchProtocolId == protocolId);
			return protocol == null ? null : (await DefinitionsAsync(departmentId, new[] { protocol })).FirstOrDefault();
		}

		public async Task<List<ProtocolCaseDefinition>> GetDefinitionsAsync(int departmentId)
		{
			if (departmentId <= 0)
				return new List<ProtocolCaseDefinition>();

			return await DefinitionsAsync(departmentId, await _protocols.GetMatchingSetAsync(departmentId));
		}

		private async Task<List<ProtocolCaseDefinition>> DefinitionsAsync(int departmentId, IEnumerable<DispatchProtocol> protocols)
		{
			// The matching set is the department's enabled, published Dispatch and Procedure protocols; only Dispatch ones run a case flow.
			var candidates = (protocols ?? Enumerable.Empty<DispatchProtocol>())
				.Where(p => p != null && p.DepartmentId == departmentId && p.Kind == (int)ProtocolKinds.Dispatch && p.CurrentVersionId.HasValue && !p.IsDisabled && !p.IsDeleted)
				.ToList();
			if (candidates.Count == 0)
				return new List<ProtocolCaseDefinition>();

			var versions = (await _versions.GetByIdsAsync(departmentId, candidates.Select(p => p.CurrentVersionId.Value).Distinct(), true) ?? new List<ProtocolVersion>())
				.Where(v => v != null && v.DepartmentId == departmentId)
				.ToDictionary(v => v.ProtocolVersionId);

			var definitions = new List<ProtocolCaseDefinition>();
			foreach (var protocol in candidates)
			{
				if (!versions.TryGetValue(protocol.CurrentVersionId.Value, out var version))
					continue;

				var graph = ValidGraph(version);
				if (graph == null)
					continue;

				definitions.Add(new ProtocolCaseDefinition
				{
					ProtocolId = protocol.DispatchProtocolId,
					ProtocolVersionId = version.ProtocolVersionId,
					Code = protocol.Code,
					Name = protocol.Name,
					VersionLabel = version.Label,
					Graph = graph
				});
			}

			return definitions;
		}

		public async Task<ProtocolDecisionGraph> GetGraphForVersionAsync(int departmentId, int protocolVersionId)
		{
			if (departmentId <= 0 || protocolVersionId <= 0)
				return null;

			var version = (await _versions.GetByIdsAsync(departmentId, new[] { protocolVersionId }, true) ?? new List<ProtocolVersion>())
				.FirstOrDefault(v => v != null && v.DepartmentId == departmentId);
			return ValidGraph(version);
		}

		/// <summary>The version's graph when it is authored content with a graph that validates; null otherwise.</summary>
		public static ProtocolDecisionGraph ValidGraph(ProtocolVersion version)
		{
			if (version == null || version.IsDocument || string.IsNullOrWhiteSpace(version.ContentJson))
				return null;

			var graph = ProtocolContentDocument.Parse(version.ContentJson, null).Graph;
			return graph != null && !graph.IsEmpty && ProtocolDecisionGraphs.Validate(graph).Count == 0 ? graph : null;
		}

		public async Task<ProtocolCaseResult> EvaluateAsync(int departmentId, int protocolId, ProtocolCaseInput input)
		{
			var definition = await GetDefinitionAsync(departmentId, protocolId);
			if (definition == null)
				return new ProtocolCaseResult { Status = ProtocolCaseStatuses.Invalid, Error = ProtocolCaseErrors.NoCaseFlow };

			return ProtocolCaseFlow.Run(definition.Graph, input?.Answers);
		}

		public async Task<ProtocolCaseUpdateResult> OverrideDeterminantAsync(int departmentId, int callId, int callProtocolId, string determinant, string reason,
			string userId, CancellationToken cancellationToken = default)
		{
			var row = await _callProtocols.GetForDepartmentCallAsync(departmentId, callId, callProtocolId);
			if (row == null || row.IsRemoved)
				return ProtocolCaseUpdateResult.Of(ProtocolCaseUpdateOutcomes.NotFound);
			if (!await RevealAsync(departmentId, row, userId, cancellationToken))
				return ProtocolCaseUpdateResult.Of(ProtocolCaseUpdateOutcomes.ProtectedDataRequired);

			var record = CallProtocolCase.Parse(row.CaseJson);
			if (record == null)
				return ProtocolCaseUpdateResult.Of(ProtocolCaseUpdateOutcomes.NoCase);

			// The audit keeps ids and determinants; the override reason is free text about the call (ADP catalog 39) and stays on the row.
			var before = new { row.CallId, row.CallProtocolId, row.DispatchProtocolId, record.Determinant, record.Overridden };
			var chosen = ProtocolDecisionGraphs.NormalizeDeterminant(determinant);

			// A blank determinant on this path means "back to what the case flow reached".
			var error = chosen == null
				? (record.ComputedDeterminant == null ? ProtocolCaseErrors.DeterminantInvalid : ProtocolCaseFlow.ApplyOverride(record, record.ComputedDeterminant, null, userId, DateTime.UtcNow))
				: ProtocolCaseFlow.ApplyOverride(record, chosen, reason, userId, DateTime.UtcNow);

			if (error == ProtocolCaseErrors.OverrideReasonRequired)
				return ProtocolCaseUpdateResult.Of(ProtocolCaseUpdateOutcomes.OverrideReasonRequired);
			if (error != ProtocolCaseErrors.None)
				return ProtocolCaseUpdateResult.Of(ProtocolCaseUpdateOutcomes.DeterminantInvalid);

			row.Determinant = record.Determinant;
			row.OverrideReason = record.Overridden ? record.OverrideReason : null;
			row.CaseJson = record.ToJson();

			if (!await WriteCaseAsync(departmentId, row, cancellationToken))
				return ProtocolCaseUpdateResult.Of(ProtocolCaseUpdateOutcomes.NotFound);

			Audit(departmentId, userId, AuditLogTypes.ProtocolDeterminantOverridden, JsonConvert.SerializeObject(before),
				JsonConvert.SerializeObject(new { row.CallId, row.CallProtocolId, row.DispatchProtocolId, record.Determinant, record.ComputedDeterminant, record.Overridden }));

			// Workflow trigger 254 after the stored change: the determinants and ids, never the case answers or the override reason.
			if (!string.Equals(before.Determinant, record.Determinant, StringComparison.Ordinal) || before.Overridden != record.Overridden)
				ContentWorkflowContext.Publish(_events, new DispatchCaseWorkflowEvent
				{
					DepartmentId = departmentId,
					Change = DispatchCaseWorkflowChanges.DeterminantChanged,
					CallId = row.CallId,
					CallProtocolId = row.CallProtocolId,
					ProtocolId = row.DispatchProtocolId,
					ProtocolVersionId = row.ProtocolVersionId,
					ProtocolCode = record.ProtocolCode,
					ProtocolName = record.ProtocolName,
					Determinant = record.Determinant,
					PreviousDeterminant = before.Determinant,
					ComputedDeterminant = record.ComputedDeterminant,
					Overridden = record.Overridden,
					UserId = userId,
					OccurredOn = DateTime.UtcNow
				});

			return new ProtocolCaseUpdateResult { Outcome = ProtocolCaseUpdateOutcomes.Done, CallProtocol = row, Case = record };
		}

		public async Task<ProtocolCaseUpdateResult> SendCallerInstructionAsync(int departmentId, int callId, int callProtocolId, string nodeKey, string userId,
			CancellationToken cancellationToken = default)
		{
			var row = await _callProtocols.GetForDepartmentCallAsync(departmentId, callId, callProtocolId);
			if (row == null || row.IsRemoved)
				return ProtocolCaseUpdateResult.Of(ProtocolCaseUpdateOutcomes.NotFound);
			if (!await RevealAsync(departmentId, row, userId, cancellationToken))
				return ProtocolCaseUpdateResult.Of(ProtocolCaseUpdateOutcomes.ProtectedDataRequired);

			var record = CallProtocolCase.Parse(row.CaseJson);
			if (record == null)
				return ProtocolCaseUpdateResult.Of(ProtocolCaseUpdateOutcomes.NoCase);

			// The text comes from the version the call cites, never from the client: an instruction marked for the caller, or the outcome's
			// post-dispatch instructions.
			var graph = record.ProtocolVersionId.HasValue ? await GetGraphForVersionAsync(departmentId, record.ProtocolVersionId.Value) : null;
			var text = CallerText(graph, nodeKey);
			if (text == null)
				return ProtocolCaseUpdateResult.Of(ProtocolCaseUpdateOutcomes.NotAnInstruction);

			var conversation = await _conversations.Value.GetForCallAsync(departmentId, callId);
			if (conversation == null || conversation.State != (int)IntakeConversationStates.Open || conversation.ChannelType != (int)IntakeChannelTypes.Sms)
				return ProtocolCaseUpdateResult.Of(ProtocolCaseUpdateOutcomes.NoThread);
			if (conversation.OptedOut)
				return ProtocolCaseUpdateResult.Of(ProtocolCaseUpdateOutcomes.OptedOut);

			// The intake service sends as the connected dispatcher (connecting first when nobody is) and stores the text as the thread's call
			// note; the case record keeps which instruction went and how it ended.
			var sent = await _conversations.Value.SendDispatcherMessageAsync(departmentId, conversation.IntakeConversationId, userId, text, cancellationToken);
			var outcome = sent switch
			{
				IntakeConversationActionResults.Done => ProtocolCaseUpdateOutcomes.Done,
				IntakeConversationActionResults.OptedOut => ProtocolCaseUpdateOutcomes.OptedOut,
				IntakeConversationActionResults.HeldByAnotherDispatcher => ProtocolCaseUpdateOutcomes.HeldByAnotherDispatcher,
				IntakeConversationActionResults.Closed => ProtocolCaseUpdateOutcomes.NoThread,
				IntakeConversationActionResults.NotFound => ProtocolCaseUpdateOutcomes.NoThread,
				_ => ProtocolCaseUpdateOutcomes.SendFailed
			};

			if (outcome == ProtocolCaseUpdateOutcomes.Done || outcome == ProtocolCaseUpdateOutcomes.SendFailed)
			{
				record.CallerMessages ??= new List<ProtocolCallerMessage>();
				if (record.CallerMessages.Count < ProtocolCaseFlow.MaxCallerMessages)
				{
					record.CallerMessages.Add(new ProtocolCallerMessage { SentOn = DateTime.UtcNow, ByUserId = userId, NodeKey = nodeKey, Text = text, Result = sent.ToString() });
					row.CaseJson = record.ToJson();
					await WriteCaseAsync(departmentId, row, cancellationToken);
				}

				Audit(departmentId, userId, AuditLogTypes.ProtocolCallerInstructionSent, null,
					JsonConvert.SerializeObject(new { row.CallId, row.CallProtocolId, row.DispatchProtocolId, NodeKey = nodeKey, Result = sent.ToString(), conversation.IntakeConversationId }));
			}

			return new ProtocolCaseUpdateResult { Outcome = outcome, CallProtocol = row, Case = record };
		}

		/// <summary>
		/// ADP catalog 39: a sealed case is read only for an attended dispatcher holding a grant (the read-modify-write needs the plaintext);
		/// false when it stays withheld, so the caller is asked to step up. Unsealed rows pass straight through.
		/// </summary>
		private async Task<bool> RevealAsync(int departmentId, CallProtocol row, string userId, CancellationToken cancellationToken)
		{
			if (!CallProtocolProtection.IsSealed(row))
				return !CallProtocolProtection.HasPlaceholder(row);

			await CallProtocolProtection.ResolveAsync(_protectedRead?.Value, departmentId, new[] { row }, _grant?.GrantToken, _grant?.UserId ?? userId, cancellationToken);
			return !CallProtocolProtection.IsWithheld(row);
		}

		/// <summary>
		/// Writes the case (determinant in force, override reason, case record). For a department that encrypts new writes the protected
		/// columns are enveloped first (workload encrypt-only lane), so no plaintext reaches the row; the returned row keeps the plaintext the
		/// attended caller already holds.
		/// </summary>
		private async Task<bool> WriteCaseAsync(int departmentId, CallProtocol row, CancellationToken cancellationToken)
		{
			var stored = new CallProtocol { CallProtocolId = row.CallProtocolId, CallId = row.CallId, CaseJson = row.CaseJson, OverrideReason = row.OverrideReason };
			if (_protectedWrite != null)
			{
				var write = await _protectedWrite.Value.PrepareRecordsEntityWriteAsync(departmentId, stored, null,
					stored.CallProtocolId.ToString(System.Globalization.CultureInfo.InvariantCulture), ContentProtectedFields.CallProtocols, null, null, null,
					workloadCaller: true, cancellationToken);
				if (write != null && !write.Success)
					throw new InvalidOperationException($"Protected write blocked ({write.Reason}); the case of call protocol {row.CallProtocolId} was not saved.");
			}

			return await _callProtocols.TryUpdateCaseAsync(departmentId, row.CallProtocolId, row.Determinant, stored.OverrideReason, stored.CaseJson, cancellationToken);
		}

		/// <summary>The text an instruction step marked for the caller (or an outcome's post-dispatch instructions) sends; null for anything else.</summary>
		public static string CallerText(ProtocolDecisionGraph graph, string nodeKey)
		{
			var node = graph?.Find(nodeKey);
			if (node == null || !node.SendToCaller)
				return null;

			var text = node.IsInstruction ? node.Text : node.IsOutcome ? node.PostDispatch : null;
			if (string.IsNullOrWhiteSpace(text))
				return null;

			text = text.Trim();
			return text.Length > CallerTextMaxLength ? text.Substring(0, CallerTextMaxLength) : text;
		}

		public async Task<List<Uri>> GetInstructionAudioAsync(int departmentId, int protocolId, string nodeKey, CancellationToken cancellationToken = default)
		{
			var urls = new List<Uri>();
			var definition = await GetDefinitionAsync(departmentId, protocolId);
			var node = definition?.Graph?.Find(nodeKey);
			if (node == null || !node.IsInstruction || !node.Speak)
				return urls;

			// The same prompt path as the dispatch voice prompt (Piper, chunked, the dispatch speaking rate), so a repeated play is cached.
			var spoken = DispatchVoicePromptBuilder.BuildInstructionPrompt(node.Text);
			if (spoken == null)
				return urls;

			try
			{
				foreach (var chunk in DispatchVoicePromptBuilder.ChunkText(spoken))
					urls.Add(await _tts.Value.GenerateSpeechUrlAsync(chunk, null, DispatchVoicePromptBuilder.DispatchSpeed, cancellationToken));
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, $"Instruction audio for protocol {protocolId} step {nodeKey} could not be generated.");
				return new List<Uri>();
			}

			return urls;
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, string before, string after)
		{
			_events?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				Before = before,
				After = after,
				Successful = true,
				ServerName = Environment.MachineName
			});
		}
	}
}
