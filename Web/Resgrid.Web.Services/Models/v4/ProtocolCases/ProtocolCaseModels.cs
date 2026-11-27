using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Models.v4.ProtocolCases
{
	/// <summary>The department's published Dispatch protocols with a case flow (Protocols P4), for Omni's dispatch form.</summary>
	public class GetProtocolCaseFlowsResult : StandardApiResponseV4Base
	{
		public List<ProtocolCaseFlowData> Data { get; set; } = new List<ProtocolCaseFlowData>();

		/// <summary>True when Enhanced AI protocol suggestions are available to this department right now.</summary>
		public bool AiAvailable { get; set; }
	}

	public class GetProtocolCaseFlowResult : StandardApiResponseV4Base
	{
		public ProtocolCaseFlowData Data { get; set; }
	}

	public class ProtocolCaseFlowData
	{
		public string ProtocolId { get; set; }
		public string ProtocolVersionId { get; set; }
		public string Code { get; set; }
		public string Name { get; set; }
		public string VersionLabel { get; set; }

		/// <summary>The decision graph as stored in the version's ContentJson ("nodes": key, root, kind, stage, text, questionType, answers…).</summary>
		public ProtocolDecisionGraph Graph { get; set; }

		public static ProtocolCaseFlowData From(ProtocolCaseDefinition definition) => definition == null ? null : new ProtocolCaseFlowData
		{
			ProtocolId = definition.ProtocolId.ToString(System.Globalization.CultureInfo.InvariantCulture),
			ProtocolVersionId = definition.ProtocolVersionId.ToString(System.Globalization.CultureInfo.InvariantCulture),
			Code = definition.Code,
			Name = definition.Name,
			VersionLabel = definition.VersionLabel,
			Graph = definition.Graph
		};
	}

	/// <summary>A case flow walked with answers (nothing saved): where it stands, the path, the instructions passed and the outcome.</summary>
	public class EvaluateProtocolCaseInput
	{
		public int ProtocolId { get; set; }
		public ProtocolCaseInput Case { get; set; }
	}

	public class EvaluateProtocolCaseResult : StandardApiResponseV4Base
	{
		public ProtocolCaseEvaluationData Data { get; set; }
	}

	public class ProtocolCaseEvaluationData
	{
		/// <summary><see cref="ProtocolCaseStatuses"/> name: InProgress, Complete or Invalid.</summary>
		public string Status { get; set; }
		public string CurrentNodeKey { get; set; }
		public List<ProtocolCaseStep> Steps { get; set; } = new List<ProtocolCaseStep>();
		public List<string> InstructionKeys { get; set; } = new List<string>();
		public string OutcomeNodeKey { get; set; }
		public string Determinant { get; set; }
		public int? SuggestedPriority { get; set; }
		public string SuggestedPriorityName { get; set; }
		public string ResponseHint { get; set; }
		public string PostDispatch { get; set; }
		/// <summary><see cref="ProtocolCaseErrors"/> name when the walk could not continue.</summary>
		public string Error { get; set; }
		public string ErrorNodeKey { get; set; }
	}

	public class OverrideProtocolDeterminantInput
	{
		public int CallId { get; set; }
		public int CallProtocolId { get; set; }

		/// <summary>The determinant in force from now; blank restores the computed one.</summary>
		public string Determinant { get; set; }

		/// <summary>Required when the determinant differs from the computed one.</summary>
		public string Reason { get; set; }
	}

	public class SendProtocolCallerInstructionInput
	{
		public int CallId { get; set; }
		public int CallProtocolId { get; set; }

		/// <summary>The instruction step (marked for the caller) or the outcome whose post-dispatch instructions are texted.</summary>
		public string NodeKey { get; set; }
	}

	public class ProtocolCaseUpdateResultData
	{
		/// <summary><see cref="ProtocolCaseUpdateOutcomes"/> name: Done, NotFound, NoCase, OverrideReasonRequired, NoThread, OptedOut…</summary>
		public string Outcome { get; set; }
		public string Message { get; set; }
		public string Determinant { get; set; }
		public CallProtocolCase Case { get; set; }
	}

	public class ProtocolCaseUpdateApiResult : StandardApiResponseV4Base
	{
		public ProtocolCaseUpdateResultData Data { get; set; }
	}

	public class SuggestProtocolInput
	{
		public int CallId { get; set; }
	}

	public class SuggestProtocolResult : StandardApiResponseV4Base
	{
		public ProtocolSuggestionData Data { get; set; }
	}

	public class ProtocolSuggestionData
	{
		/// <summary><see cref="ProtocolAiSuggestionOutcomes"/> name; only Suggested carries a suggestion.</summary>
		public string Outcome { get; set; }
		public string Message { get; set; }
		public string SuggestionId { get; set; }
		public string ProtocolId { get; set; }
		public string Code { get; set; }
		public string Name { get; set; }
		public decimal Confidence { get; set; }
		public string Rationale { get; set; }

		/// <summary>Prefilled answers by step key; the dispatcher checks them, then sends them in ProtocolCases with AiSuggestionId set.</summary>
		public Dictionary<string, string> Answers { get; set; } = new Dictionary<string, string>();
	}

	public class GetInstructionAudioResult : StandardApiResponseV4Base
	{
		public List<string> Data { get; set; } = new List<string>();
	}
}
