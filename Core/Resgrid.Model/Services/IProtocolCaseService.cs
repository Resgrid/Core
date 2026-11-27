using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The protocol case flow on calls (Protocols P4): the decision graph of a protocol's version in force, the walk preview, the
	/// determinant override after the call is saved, the caller follow-up text over the intake SMS thread, and the instruction audio.
	/// Attaching a protocol with its case is <see cref="IProtocolsService.PrepareCallProtocolsAsync"/>.
	/// </summary>
	public interface IProtocolCaseService
	{
		/// <summary>
		/// The case flow of a published, enabled Dispatch protocol of the department: its version in force and the graph, when the graph is
		/// valid. Null when the protocol is not the department's, has no version in force, or has no valid graph.
		/// </summary>
		Task<ProtocolCaseDefinition> GetDefinitionAsync(int departmentId, int protocolId);

		/// <summary>The definitions of every published Dispatch protocol of the department that has a valid case flow.</summary>
		Task<List<ProtocolCaseDefinition>> GetDefinitionsAsync(int departmentId);

		/// <summary>The graph of a cited version, when it is the department's and valid; null otherwise.</summary>
		Task<ProtocolDecisionGraph> GetGraphForVersionAsync(int departmentId, int protocolVersionId);

		/// <summary>Walks the protocol's case flow with the answers (a preview for clients; nothing is saved).</summary>
		Task<ProtocolCaseResult> EvaluateAsync(int departmentId, int protocolId, ProtocolCaseInput input);

		/// <summary>
		/// Overrides (or restores) the determinant of a call protocol's case after the call was saved. A determinant other than the computed one
		/// needs a reason. Audited; the call protocol row's Determinant, OverrideReason and CaseJson are written alone.
		/// </summary>
		Task<ProtocolCaseUpdateResult> OverrideDeterminantAsync(int departmentId, int callId, int callProtocolId, string determinant, string reason,
			string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Texts an instruction block of the case (an instruction step marked for the caller, or the outcome's post-dispatch instructions) to
		/// the caller over the call's open intake SMS thread, as the connected dispatcher. Text only; the message is logged on the call (the
		/// thread's call note and the case record) and audited. Refused when the call has no open SMS thread, the texter opted out, or
		/// another dispatcher holds the thread.
		/// </summary>
		Task<ProtocolCaseUpdateResult> SendCallerInstructionAsync(int departmentId, int callId, int callProtocolId, string nodeKey, string userId,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// The audio of an instruction step marked "speak" (Piper TTS through the dispatch voice prompt path), as one or more URLs to play in
		/// order. Empty when the step is not an instruction to speak or TTS is not available.
		/// </summary>
		Task<List<Uri>> GetInstructionAudioAsync(int departmentId, int protocolId, string nodeKey, CancellationToken cancellationToken = default);
	}

	/// <summary>A protocol's case flow as clients run it.</summary>
	public sealed class ProtocolCaseDefinition
	{
		public int ProtocolId { get; set; }
		public int ProtocolVersionId { get; set; }
		public string Code { get; set; }
		public string Name { get; set; }
		public string VersionLabel { get; set; }
		public ProtocolDecisionGraph Graph { get; set; }
	}

	public enum ProtocolCaseUpdateOutcomes
	{
		Done = 0,
		NotFound = 1,
		NoCase = 2,
		OverrideReasonRequired = 3,
		DeterminantInvalid = 4,
		NotAnInstruction = 5,
		/// <summary>The call has no intake SMS thread, or it is closed.</summary>
		NoThread = 6,
		/// <summary>The texter opted out (STOP); nothing can be sent.</summary>
		OptedOut = 7,
		/// <summary>Another dispatcher is connected to the thread.</summary>
		HeldByAnotherDispatcher = 8,
		SendFailed = 9,
		/// <summary>ADP catalog 39: the case is sealed and the dispatcher holds no protected-data grant; step up and retry.</summary>
		ProtectedDataRequired = 10
	}

	public sealed class ProtocolCaseUpdateResult
	{
		public ProtocolCaseUpdateOutcomes Outcome { get; set; }
		public CallProtocol CallProtocol { get; set; }
		public CallProtocolCase Case { get; set; }

		public bool Succeeded => Outcome == ProtocolCaseUpdateOutcomes.Done;

		public static ProtocolCaseUpdateResult Of(ProtocolCaseUpdateOutcomes outcome) => new ProtocolCaseUpdateResult { Outcome = outcome };
	}

	/// <summary>
	/// The Enhanced AI protocol suggestion (Protocols P4) on the intake desk seam: reads the call's text or its intake SMS thread, suggests
	/// one published Dispatch protocol with a case flow and prefills its answers, and keeps the suggestion until a dispatcher confirms it
	/// on the case flow (then the call records Source = AI). Fails closed without the add-on, under Advanced Data Protection, or when no
	/// model is configured; licensed and "no AI" protocols are never shown to the model.
	/// </summary>
	public interface IProtocolAiSuggestionService
	{
		Task<ProtocolAiSuggestionResult> SuggestAsync(int departmentId, int callId, string userId, CancellationToken cancellationToken = default);

		/// <summary>True when the department may ask for suggestions right now (the add-on, the capability flag, no ADP, a model).</summary>
		Task<bool> IsAvailableAsync(int departmentId);

		/// <summary>
		/// The suggestion a client posted with a case, when it is real: the department's, for this call and protocol, and not confirmed yet.
		/// Null otherwise (the protocol is then recorded as a person's pick).
		/// </summary>
		Task<ProtocolAiSuggestion> GetConfirmableAsync(int departmentId, int callId, int protocolId, int protocolAiSuggestionId);

		/// <summary>Marks the suggestion confirmed by the dispatcher who saved the call (once) and audits it.</summary>
		Task<bool> ConfirmAsync(int departmentId, int protocolAiSuggestionId, string userId, CancellationToken cancellationToken = default);
	}

	/// <summary>The model call behind <see cref="IProtocolAiSuggestionService"/> (one implementation over the operator's LLM; tests mock it).</summary>
	public interface IProtocolCaseSuggester
	{
		/// <summary>True when the operator's generative model is configured.</summary>
		bool IsConfigured { get; }

		/// <summary>The model's pick among the candidates for the text, or null when it gave none. Throws TimeoutException when inference is down.</summary>
		Task<ProtocolAiPick> SuggestAsync(int departmentId, string callText, IReadOnlyList<ProtocolAiCandidate> candidates, CancellationToken cancellationToken);
	}
}
