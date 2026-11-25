using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Protocols
{
	/// <summary>One acknowledgment the member owes or gave (P2; Omni "My acknowledgments", offline-capable).</summary>
	public class ProtocolAcknowledgmentResultData
	{
		public int AcknowledgmentId { get; set; }
		public int ProtocolId { get; set; }
		public int ProtocolVersionId { get; set; }
		public string Code { get; set; }
		public string Name { get; set; }
		public int Kind { get; set; }
		public bool IsBulletin { get; set; }
		public string VersionLabel { get; set; }
		/// <summary>The hash Omni sends back with an offline acknowledgment (R-P11).</summary>
		public string ContentHash { get; set; }
		/// <summary>open, awaiting_quiz, acknowledged.</summary>
		public string Status { get; set; }
		public bool IsOverdue { get; set; }
		public DateTime AssignedOn { get; set; }
		public DateTime? DueOn { get; set; }
		public DateTime? SignedOn { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public int BundleOrder { get; set; }
		public int? QuizTrainingId { get; set; }
		public string QuizTrainingName { get; set; }
		public bool QuizPassed { get; set; }
		/// <summary>The version is a document (read through GetVersionFile).</summary>
		public bool IsDocument { get; set; }
		/// <summary>Signed and waiting for the LMS question-bank quiz (GetBankQuiz / SubmitBankQuiz; round 5F).</summary>
		public bool BankQuizOwed { get; set; }
	}

	public class GetMyProtocolAcknowledgmentsResult : StandardApiResponseV4Base
	{
		public List<ProtocolAcknowledgmentResultData> Data { get; set; } = new List<ProtocolAcknowledgmentResultData>();
	}

	/// <summary>An acknowledgment, online or replayed from the offline queue: the version and the content hash the member read.</summary>
	public class AcknowledgeProtocolInput
	{
		public int ProtocolVersionId { get; set; }
		public string ContentHash { get; set; }
		/// <summary>True when the member read it offline and the app queued the acknowledgment (R-P11).</summary>
		public bool Offline { get; set; }
		/// <summary>When the member signed on the device (for the record only; the server stamps its own time).</summary>
		public DateTime? SignedOnDevice { get; set; }
	}

	public class AcknowledgeProtocolResultData
	{
		/// <summary>Acknowledged, AwaitingQuiz, AlreadyAcknowledged, ReadNewerVersion, ContentChanged, NotAssigned, NotOpened, StepUpRequired, InvalidRequest.</summary>
		public string Outcome { get; set; }
		public bool Accepted { get; set; }
		/// <summary>The version in force when the member must read a newer one.</summary>
		public int? CurrentVersionId { get; set; }
		public string CurrentContentHash { get; set; }
		public int? AcknowledgmentId { get; set; }
	}

	public class AcknowledgeProtocolResult : StandardApiResponseV4Base
	{
		public AcknowledgeProtocolResultData Data { get; set; }
	}

	/// <summary>
	/// A version's question-bank acknowledgment quiz (LMS, round 5F): questions and options only, never a key. Error is None, NotAssigned,
	/// NoQuestions or AlreadySubmitted (already passed). Online only: an acknowledgment quiz gates completion, so it is never scored offline.
	/// </summary>
	public class GetBankQuizResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.ProtocolQuizView Data { get; set; }
		public string Error { get; set; }
	}

	public class SubmitBankQuizInput
	{
		public int AttemptId { get; set; }
		public List<Resgrid.Model.ProtocolQuizAnswer> Answers { get; set; } = new List<Resgrid.Model.ProtocolQuizAnswer>();
	}

	public class SubmitBankQuizResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.ProtocolQuizResult Data { get; set; }
		public string Error { get; set; }
	}

	/// <summary>The member's weekly micro-learning questions (LMS): the key and explanation only once answered.</summary>
	public class GetWeeklyQuestionsResult : StandardApiResponseV4Base
	{
		public List<Resgrid.Model.ProtocolMicroLearningItem> Data { get; set; } = new List<Resgrid.Model.ProtocolMicroLearningItem>();
	}

	public class AnswerWeeklyQuestionInput
	{
		public int Id { get; set; }
		public List<string> Keys { get; set; } = new List<string>();
	}

	public class AnswerWeeklyQuestionResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.ProtocolMicroLearningItem Data { get; set; }
		public string Error { get; set; }
	}
}
