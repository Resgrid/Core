using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model.Lms
{
	/// <summary>
	/// A question bank (§12.3): pools of questions with tags, drawn at random per attempt. Shared banks are usable by every
	/// course author of the department; a private bank only by its owner and holders of Manage Courses (Vector parity).
	/// </summary>
	[Table("LmsQuestionBanks")]
	public class LmsQuestionBank : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsQuestionBankId { get; set; }

		/// <summary>0 for the system publisher's banks (Academy checks).</summary>
		public int DepartmentId { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		/// <summary>Department-shared (true) or private to <see cref="OwnerUserId"/> (false).</summary>
		public bool IsShared { get; set; }

		public string OwnerUserId { get; set; }

		public string Tags { get; set; }

		/// <summary>The system publisher's key ("academy:05-calls"); null for department banks.</summary>
		public string SystemKey { get; set; }

		public bool IsDeleted { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsQuestionBankId; }
			set { LmsQuestionBankId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsQuestionBanks";

		[NotMapped]
		public string IdName => "LmsQuestionBankId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A bank question. <see cref="OptionsJson"/> holds the options with the answer key (<see cref="LmsQuestionOption.IsCorrect"/>;
	/// for Ordering the stored order is the key). The key never leaves the server (R-T11): learners only ever receive an
	/// <see cref="LmsLearnerQuestion"/>. Editing a question does not touch past attempts, which carry their own snapshot (R-T6).
	/// </summary>
	[Table("LmsQuestions")]
	public class LmsQuestion : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsQuestionId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsQuestionBankId { get; set; }

		/// <summary><see cref="LmsQuestionKinds"/>.</summary>
		public int Kind { get; set; }

		public string Text { get; set; }

		/// <summary>An image shown with the question (an https URL; Vector parity "images in questions").</summary>
		public string ImageUrl { get; set; }

		public string OptionsJson { get; set; }

		/// <summary>Shown after the attempt when the answer-review policy allows it; for ShortAnswer, the model answer for graders.</summary>
		public string Explanation { get; set; }

		public string Tags { get; set; }

		public decimal Points { get; set; } = 1;

		/// <summary>Per-locale text (<see cref="LmsQuestionTranslation"/> by locale, JSON). The Academy checks ship in 10 locales.</summary>
		public string TranslationsJson { get; set; }

		public bool IsRetired { get; set; }

		public int SortOrder { get; set; }

		/// <summary>Stable key of a system question ("05-calls.q1").</summary>
		public string SystemKey { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public List<LmsQuestionOption> Options
		{
			get => LmsQuestionOption.Parse(OptionsJson);
			set => OptionsJson = value == null ? null : JsonConvert.SerializeObject(value);
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsQuestionId; }
			set { LmsQuestionId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsQuestions";

		[NotMapped]
		public string IdName => "LmsQuestionId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Options" };
	}

	/// <summary>One option of a question, with the key. Server side only.</summary>
	public sealed class LmsQuestionOption
	{
		/// <summary>A short stable key ("a", "b"…), the value a learner's answer carries.</summary>
		public string Key { get; set; }

		public string Text { get; set; }

		public bool IsCorrect { get; set; }

		public static List<LmsQuestionOption> Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new List<LmsQuestionOption>();
			try
			{
				return (JsonConvert.DeserializeObject<List<LmsQuestionOption>>(json) ?? new List<LmsQuestionOption>()).Where(o => o != null).ToList();
			}
			catch (JsonException)
			{
				return new List<LmsQuestionOption>();
			}
		}
	}

	/// <summary>A question's text in one locale: the text, option texts by key and the explanation.</summary>
	public sealed class LmsQuestionTranslation
	{
		public string Text { get; set; }

		public Dictionary<string, string> Options { get; set; } = new Dictionary<string, string>();

		public string Explanation { get; set; }

		public static Dictionary<string, LmsQuestionTranslation> Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new Dictionary<string, LmsQuestionTranslation>(StringComparer.OrdinalIgnoreCase);
			try
			{
				var parsed = JsonConvert.DeserializeObject<Dictionary<string, LmsQuestionTranslation>>(json);
				return parsed == null
					? new Dictionary<string, LmsQuestionTranslation>(StringComparer.OrdinalIgnoreCase)
					: new Dictionary<string, LmsQuestionTranslation>(parsed, StringComparer.OrdinalIgnoreCase);
			}
			catch (JsonException)
			{
				return new Dictionary<string, LmsQuestionTranslation>(StringComparer.OrdinalIgnoreCase);
			}
		}
	}

	/// <summary>
	/// An assessment attempt (R-T7: the LMS table, separate from the baseline TrainingQuizAttempts; FK to the course version and
	/// lesson). Stores the quiz it was taken against (R-T6): <see cref="RevisionHash"/> and <see cref="SnapshotJson"/> (questions,
	/// options in the order shown, the key), and the answers chosen. Failed attempts and answers are visible only to the member and
	/// graders (R-T12).
	/// </summary>
	[Table("LmsAssessmentAttempts")]
	public class LmsAssessmentAttempt : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsAssessmentAttemptId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsCourseVersionId { get; set; }

		public int LmsLessonId { get; set; }

		public int LmsEnrollmentId { get; set; }

		public string UserId { get; set; }

		public int AttemptNumber { get; set; }

		/// <summary><see cref="LmsAttemptStatuses"/>.</summary>
		public int Status { get; set; }

		public DateTime StartedOn { get; set; }

		/// <summary>When a timed attempt runs out (server clock; the device clock is never trusted, R-T11).</summary>
		public DateTime? DeadlineOn { get; set; }

		public DateTime? SubmittedOn { get; set; }

		public decimal Score { get; set; }

		public decimal MaxScore { get; set; }

		public decimal Percent { get; set; }

		public bool? Passed { get; set; }

		public bool IsPreTest { get; set; }

		public int PassMark { get; set; }

		[MaxLength(64)]
		public string RevisionHash { get; set; }

		/// <summary>The drawn questions with the key (<see cref="LmsAttemptSnapshot"/>). Server side only.</summary>
		public string SnapshotJson { get; set; }

		/// <summary>The learner's answers by question id (<see cref="LmsAttemptAnswer"/> list).</summary>
		public string AnswersJson { get; set; }

		/// <summary>Per-question outcome (<see cref="LmsQuestionResult"/> list).</summary>
		public string ResultsJson { get; set; }

		public string GradedByUserId { get; set; }

		public DateTime? GradedOn { get; set; }

		public string GradeNotes { get; set; }

		/// <summary>The learner accepted the "I completed this myself" attestation (R-C15).</summary>
		public bool Attested { get; set; }

		public string IpAddress { get; set; }

		public string UserAgent { get; set; }

		public long RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsAssessmentAttemptId; }
			set { LmsAssessmentAttemptId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsAssessmentAttempts";

		[NotMapped]
		public string IdName => "LmsAssessmentAttemptId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>The quiz an attempt was taken against (R-T6): drawn questions, options in display order, key, pass mark.</summary>
	public sealed class LmsAttemptSnapshot
	{
		public int PassMark { get; set; }

		public List<LmsSnapshotQuestion> Questions { get; set; } = new List<LmsSnapshotQuestion>();

		public static LmsAttemptSnapshot Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new LmsAttemptSnapshot();
			try
			{
				return JsonConvert.DeserializeObject<LmsAttemptSnapshot>(json) ?? new LmsAttemptSnapshot();
			}
			catch (JsonException)
			{
				return new LmsAttemptSnapshot();
			}
		}
	}

	/// <summary>One drawn question as it was shown, with its key. Server side only.</summary>
	public sealed class LmsSnapshotQuestion
	{
		public int QuestionId { get; set; }

		public int Kind { get; set; }

		public string Text { get; set; }

		public string ImageUrl { get; set; }

		public decimal Points { get; set; }

		/// <summary>The options in the order the learner saw them (keys and texts).</summary>
		public List<LmsLearnerOption> Options { get; set; } = new List<LmsLearnerOption>();

		/// <summary>Choice questions: the correct keys.</summary>
		public List<string> CorrectKeys { get; set; } = new List<string>();

		/// <summary>Ordering: the keys in the correct order.</summary>
		public List<string> CorrectOrder { get; set; } = new List<string>();

		public string Explanation { get; set; }
	}

	/// <summary>A question as a learner receives it: text and options only, never the key (R-T11).</summary>
	public sealed class LmsLearnerQuestion
	{
		public int QuestionId { get; set; }

		public int Kind { get; set; }

		public string Text { get; set; }

		public string ImageUrl { get; set; }

		public decimal Points { get; set; }

		public List<LmsLearnerOption> Options { get; set; } = new List<LmsLearnerOption>();
	}

	public sealed class LmsLearnerOption
	{
		public string Key { get; set; }

		public string Text { get; set; }
	}

	/// <summary>A learner's answer to one question: chosen keys (choice), keys in order (ordering) or text (short answer).</summary>
	public sealed class LmsAttemptAnswer
	{
		public int QuestionId { get; set; }

		public List<string> Keys { get; set; } = new List<string>();

		public string Text { get; set; }

		public static List<LmsAttemptAnswer> Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new List<LmsAttemptAnswer>();
			try
			{
				return (JsonConvert.DeserializeObject<List<LmsAttemptAnswer>>(json) ?? new List<LmsAttemptAnswer>()).Where(a => a != null).ToList();
			}
			catch (JsonException)
			{
				return new List<LmsAttemptAnswer>();
			}
		}
	}

	/// <summary>One question's outcome in an attempt.</summary>
	public sealed class LmsQuestionResult
	{
		public int QuestionId { get; set; }

		public bool Correct { get; set; }

		public decimal Awarded { get; set; }

		public decimal Points { get; set; }

		/// <summary>A short answer not graded yet.</summary>
		public bool AwaitingGrade { get; set; }

		public static List<LmsQuestionResult> Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new List<LmsQuestionResult>();
			try
			{
				return (JsonConvert.DeserializeObject<List<LmsQuestionResult>>(json) ?? new List<LmsQuestionResult>()).Where(r => r != null).ToList();
			}
			catch (JsonException)
			{
				return new List<LmsQuestionResult>();
			}
		}
	}

	/// <summary>A grader's mark for one short answer.</summary>
	public sealed class LmsManualGrade
	{
		public int QuestionId { get; set; }

		public decimal Awarded { get; set; }
	}

	/// <summary>The outcome of scoring: per question, the totals, and whether the attempt waits for manual grading.</summary>
	public sealed class LmsScoreOutcome
	{
		public List<LmsQuestionResult> Results { get; set; } = new List<LmsQuestionResult>();
		public decimal Score { get; set; }
		public decimal MaxScore { get; set; }
		public decimal Percent { get; set; }
		public bool PendingGrading { get; set; }

		/// <summary>Null while short answers wait for a grader.</summary>
		public bool? Passed { get; set; }
	}

	/// <summary>Item analysis of one question across scored attempts (§13a "question analysis").</summary>
	public sealed class LmsItemStatistic
	{
		public int QuestionId { get; set; }
		public string Text { get; set; }
		public int Responses { get; set; }

		/// <summary>Difficulty index p: the share of responses that were correct (0–1; higher is easier).</summary>
		public decimal Difficulty { get; set; }

		/// <summary>
		/// Discrimination index D: p in the top 27% of attempts by total score minus p in the bottom 27% (-1 to 1). Null when
		/// fewer than <see cref="LmsAssessmentEngine.MinimumAttemptsForDiscrimination"/> attempts answered it.
		/// </summary>
		public decimal? Discrimination { get; set; }

		/// <summary>How often each option was chosen (choice questions), by key.</summary>
		public Dictionary<string, int> OptionCounts { get; set; } = new Dictionary<string, int>();
	}
}
