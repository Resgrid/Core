using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	// The baseline-facing shapes of the LMS protocol learning extensions (IProtocolLearningExtensions, §17.2). Protocol pages and the v4
	// API speak these; only the LMS layer knows the question banks behind them (§17.3: baseline code never references LMS course types).

	/// <summary>Why a protocol learning action was refused. Not persisted.</summary>
	public enum ProtocolLearningErrors
	{
		None = 0,
		NotAuthorized,
		/// <summary>The LMS add-on is not active (authoring and new LMS assignments stop, rule 3).</summary>
		AddonRequired,
		NotFound,
		InvalidInput,
		/// <summary>The member owes no acknowledgment of the version, or the version does not ask for a bank quiz.</summary>
		NotAssigned,
		/// <summary>The bank has no question the server can score on its own.</summary>
		NoQuestions,
		AlreadySubmitted,
		AlreadyAnswered
	}

	/// <summary>A protocol's LMS learning settings as the acknowledgment settings page edits them.</summary>
	public sealed class ProtocolLearningSettings
	{
		public int ProtocolId { get; set; }

		/// <summary>The department question bank linked to the protocol.</summary>
		public int? QuestionBankId { get; set; }

		/// <summary>Acknowledgment completes only when a quiz drawn from the bank is passed.</summary>
		public bool QuizRequired { get; set; }

		public int QuestionCount { get; set; } = 5;

		public int PassMark { get; set; } = 80;

		public string Tags { get; set; }

		/// <summary>The protocol takes part in the weekly micro-learning.</summary>
		public bool MicroLearning { get; set; }
	}

	/// <summary>A question bank the settings page offers (department banks only).</summary>
	public sealed class ProtocolQuestionBankChoice
	{
		public int Id { get; set; }
		public string Name { get; set; }

		/// <summary>Live questions the server can score on its own (single choice, multiple choice, true/false).</summary>
		public int DrawableQuestions { get; set; }
	}

	/// <summary>Where a member stands on a version's bank quiz.</summary>
	public sealed class ProtocolBankQuizState
	{
		/// <summary>The protocol's settings ask for a bank quiz (whether the add-on is active is the caller's question).</summary>
		public bool Configured { get; set; }

		/// <summary>A scored attempt on this version passed.</summary>
		public bool Passed { get; set; }

		public int QuestionCount { get; set; }
		public int PassMark { get; set; }
		public int Attempts { get; set; }
		public decimal? BestPercent { get; set; }

		/// <summary>An attempt started and not yet submitted (it resumes).</summary>
		public int? OpenAttemptId { get; set; }

		public static ProtocolBankQuizState None => new ProtocolBankQuizState();
	}

	/// <summary>A quiz question as the member sees it: text and options in display order, never the key (R-T11).</summary>
	public sealed class ProtocolQuizQuestion
	{
		public int QuestionId { get; set; }

		/// <summary>0 single choice, 1 multiple choice, 2 true/false.</summary>
		public int Kind { get; set; }

		public string Text { get; set; }
		public string ImageUrl { get; set; }
		public decimal Points { get; set; }
		public List<ProtocolQuizOption> Options { get; set; } = new List<ProtocolQuizOption>();

		public bool IsMultiple => Kind == 1;
	}

	public sealed class ProtocolQuizOption
	{
		public string Key { get; set; }
		public string Text { get; set; }
	}

	/// <summary>The keys a member chose for one question.</summary>
	public sealed class ProtocolQuizAnswer
	{
		public int QuestionId { get; set; }
		public List<string> Keys { get; set; } = new List<string>();
	}

	/// <summary>An acknowledgment quiz attempt as the member takes it (the learner payload: no key, no explanation).</summary>
	public sealed class ProtocolQuizView
	{
		public ProtocolLearningErrors Error { get; set; }
		public int AttemptId { get; set; }
		public int ProtocolId { get; set; }
		public int ProtocolVersionId { get; set; }
		public string ProtocolCode { get; set; }
		public string ProtocolName { get; set; }
		public string VersionLabel { get; set; }
		public int PassMark { get; set; }
		public List<ProtocolQuizQuestion> Questions { get; set; } = new List<ProtocolQuizQuestion>();

		public static ProtocolQuizView Fail(ProtocolLearningErrors error) => new ProtocolQuizView { Error = error };
	}

	/// <summary>A scored attempt: the score and whether it passed, with which questions were right (never the right answers).</summary>
	public sealed class ProtocolQuizResult
	{
		public ProtocolLearningErrors Error { get; set; }
		public int AttemptId { get; set; }
		public int ProtocolVersionId { get; set; }
		public decimal Percent { get; set; }
		public int PassMark { get; set; }
		public bool Passed { get; set; }
		public int Correct { get; set; }
		public int Total { get; set; }

		/// <summary>The member had signed and was waiting for the quiz: the acknowledgment is now complete.</summary>
		public bool AcknowledgmentCompleted { get; set; }

		public static ProtocolQuizResult Fail(ProtocolLearningErrors error) => new ProtocolQuizResult { Error = error };
	}

	/// <summary>
	/// A member's weekly micro-learning question. Before it is answered it carries the question and options only; once answered (one
	/// answer, locked) it also shows the correct keys and the explanation, which is the learning.
	/// </summary>
	public sealed class ProtocolMicroLearningItem
	{
		public ProtocolLearningErrors Error { get; set; }
		public int Id { get; set; }
		public int ProtocolId { get; set; }
		public string ProtocolCode { get; set; }
		public string ProtocolName { get; set; }
		public string WeekKey { get; set; }
		public DateTime SentOn { get; set; }
		public DateTime? AnsweredOn { get; set; }
		public bool? Correct { get; set; }
		public ProtocolQuizQuestion Question { get; set; }
		public List<string> ChosenKeys { get; set; } = new List<string>();

		/// <summary>Only after the member answered.</summary>
		public List<string> CorrectKeys { get; set; }

		/// <summary>Only after the member answered.</summary>
		public string Explanation { get; set; }

		public static ProtocolMicroLearningItem Fail(ProtocolLearningErrors error) => new ProtocolMicroLearningItem { Error = error };
	}
}
