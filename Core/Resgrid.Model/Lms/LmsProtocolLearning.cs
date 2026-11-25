using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Resgrid.Model.Lms
{
	/// <summary>
	/// The LMS learning settings of one protocol (P2 LMS parts, §17.2, M0417): the question bank linked to it, whether its acknowledgment
	/// requires passing a quiz drawn from that bank (how many questions, the pass mark, an optional tag filter), and whether it takes
	/// part in the department's weekly micro-learning. LMS table: references the protocol by id only (§17.3); the baseline protocol
	/// tables never reference it.
	/// </summary>
	[Table("LmsProtocolLearnings")]
	public class LmsProtocolLearning : IEntity
	{
		public const int DefaultQuestionCount = 5;
		public const int MaxQuestionCount = 50;
		public const int DefaultPassMark = 80;
		public const int TagsMaxLength = 500;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsProtocolLearningId { get; set; }

		public int DepartmentId { get; set; }

		public int DispatchProtocolId { get; set; }

		/// <summary>The department question bank linked to the protocol (the quiz and the micro-learning pool draw from it).</summary>
		public int? LmsQuestionBankId { get; set; }

		/// <summary>The acknowledgment of the protocol's versions completes only when a quiz drawn from the bank is passed.</summary>
		public bool QuizRequired { get; set; }

		/// <summary>Questions drawn per attempt (all live questions when the bank has fewer).</summary>
		public int QuestionCount { get; set; } = DefaultQuestionCount;

		public int PassMark { get; set; } = DefaultPassMark;

		/// <summary>Optional draw filter: only questions carrying one of these tags (comma separated).</summary>
		public string Tags { get; set; }

		/// <summary>The protocol is one of the department's weekly micro-learning protocols.</summary>
		public bool MicroLearning { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsProtocolLearningId; }
			set { LmsProtocolLearningId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsProtocolLearnings";

		[NotMapped]
		public string IdName => "LmsProtocolLearningId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "GatesAcknowledgment" };

		/// <summary>A quiz gates the acknowledgment: required and a bank is linked.</summary>
		[NotMapped]
		[JsonIgnore]
		public bool GatesAcknowledgment => QuizRequired && LmsQuestionBankId.HasValue;
	}

	/// <summary>A protocol acknowledgment quiz attempt's state. Persisted (LmsProtocolQuizAttempts.Status): append only.</summary>
	public enum LmsProtocolQuizAttemptStatuses
	{
		InProgress = 0,
		Scored = 1
	}

	/// <summary>
	/// One attempt at a protocol version's acknowledgment quiz (M0417). Drawn and scored on the server only: <see cref="SnapshotJson"/>
	/// holds the drawn questions in the order shown with the key (R-T6/R-T11) and never leaves the server; the learner receives questions
	/// and options. Attempts are per version, so a major version asks again.
	/// </summary>
	[Table("LmsProtocolQuizAttempts")]
	public class LmsProtocolQuizAttempt : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsProtocolQuizAttemptId { get; set; }

		public int DepartmentId { get; set; }

		public int DispatchProtocolId { get; set; }

		public int ProtocolVersionId { get; set; }

		public string UserId { get; set; }

		public int LmsQuestionBankId { get; set; }

		public int AttemptNumber { get; set; }

		/// <summary><see cref="LmsProtocolQuizAttemptStatuses"/>.</summary>
		public int Status { get; set; }

		public DateTime StartedOn { get; set; }

		public DateTime? SubmittedOn { get; set; }

		public decimal Score { get; set; }

		public decimal MaxScore { get; set; }

		public decimal Percent { get; set; }

		public bool? Passed { get; set; }

		public int PassMark { get; set; }

		public string RevisionHash { get; set; }

		/// <summary>The drawn quiz with its key (<see cref="LmsAttemptSnapshot"/>). Server side only.</summary>
		public string SnapshotJson { get; set; }

		public string AnswersJson { get; set; }

		public string ResultsJson { get; set; }

		public long RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsProtocolQuizAttemptId; }
			set { LmsProtocolQuizAttemptId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsProtocolQuizAttempts";

		[NotMapped]
		public string IdName => "LmsProtocolQuizAttemptId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// One member's weekly micro-learning question (M0417): a single scenario question drawn from the banks tagged to one of the
	/// department's micro-learning protocols, unique per department, member and week. The snapshot (with its key) stays on the server
	/// until the member has answered.
	/// </summary>
	[Table("LmsMicroLearningItems")]
	public class LmsMicroLearningItem : IEntity
	{
		public const int WeekKeyMaxLength = 10;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsMicroLearningItemId { get; set; }

		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		public int DispatchProtocolId { get; set; }

		public int LmsQuestionId { get; set; }

		/// <summary>The ISO week of the department's local calendar ("2026-W48").</summary>
		public string WeekKey { get; set; }

		/// <summary>The one drawn question with its key (<see cref="LmsAttemptSnapshot"/>). Server side only until answered.</summary>
		public string SnapshotJson { get; set; }

		public DateTime SentOn { get; set; }

		public DateTime? AnsweredOn { get; set; }

		public string AnswersJson { get; set; }

		public bool? Correct { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsMicroLearningItemId; }
			set { LmsMicroLearningItemId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsMicroLearningItems";

		[NotMapped]
		public string IdName => "LmsMicroLearningItemId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// Worker 86's micro-learning clock per department (M0417): when the next weekly question is due (Monday 09:00 department-local).
	/// The due-work query reads only departments with a micro-learning protocol whose clock has run out (or has none yet); a run claims
	/// the department by moving the clock before anything is sent.
	/// </summary>
	[Table("LmsMicroLearningRuns")]
	public class LmsMicroLearningRun : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsMicroLearningRunId { get; set; }

		public int DepartmentId { get; set; }

		public DateTime NextRunOn { get; set; }

		public DateTime? LastRunOn { get; set; }

		public string LastWeekKey { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsMicroLearningRunId; }
			set { LmsMicroLearningRunId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsMicroLearningRuns";

		[NotMapped]
		public string IdName => "LmsMicroLearningRunId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>The pure rules of the protocol learning extensions (bank quizzes, micro-learning), testable without a database.</summary>
	public static class LmsProtocolLearningRules
	{
		/// <summary>The local hour on Monday the weekly question goes out.</summary>
		public const int MicroLearningHourLocal = 9;

		/// <summary>Questions a member answered in these recent weeks are not drawn again while others remain.</summary>
		public const int RecentWeeks = 12;

		/// <summary>Acknowledgment quizzes and micro-learning draw only questions the server scores on its own (no grader, no ordering UI).</summary>
		public static bool IsDrawable(LmsQuestion question) =>
			question != null && !question.IsRetired && (question.Kind == (int)LmsQuestionKinds.SingleChoice || question.Kind == (int)LmsQuestionKinds.MultiChoice ||
				question.Kind == (int)LmsQuestionKinds.TrueFalse) && (question.Options ?? new List<LmsQuestionOption>()).Any(o => o.IsCorrect);

		public static int ClampQuestions(int count) => count <= 0 ? LmsProtocolLearning.DefaultQuestionCount : Math.Min(count, LmsProtocolLearning.MaxQuestionCount);

		public static int ClampPassMark(int passMark) => passMark <= 0 ? LmsProtocolLearning.DefaultPassMark : Math.Min(passMark, 100);

		/// <summary>The ISO week of a local date ("2026-W48").</summary>
		public static string WeekKey(DateTime localDate) =>
			ISOWeek.GetYear(localDate).ToString("0000", CultureInfo.InvariantCulture) + "-W" + ISOWeek.GetWeekOfYear(localDate).ToString("00", CultureInfo.InvariantCulture);

		/// <summary>The next Monday 09:00 (local) strictly after <paramref name="localNow"/>.</summary>
		public static DateTime NextRunLocal(DateTime localNow)
		{
			var daysToMonday = ((int)DayOfWeek.Monday - (int)localNow.DayOfWeek + 7) % 7;
			var candidate = localNow.Date.AddDays(daysToMonday).AddHours(MicroLearningHourLocal);
			return candidate > localNow ? candidate : candidate.AddDays(7);
		}

		/// <summary>
		/// A bank is tagged to a protocol when its tags name the protocol's code or number (case-insensitive, whole tags), or the bank is
		/// the one linked to the protocol.
		/// </summary>
		public static bool IsBankTaggedTo(LmsQuestionBank bank, DispatchProtocol protocol, int? linkedBankId)
		{
			if (bank == null || protocol == null || bank.IsDeleted || bank.DepartmentId != protocol.DepartmentId)
				return false;
			if (linkedBankId.HasValue && bank.LmsQuestionBankId == linkedBankId.Value)
				return true;
			var names = new[] { protocol.Code, protocol.Number }.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
			if (names.Count == 0 || string.IsNullOrWhiteSpace(bank.Tags))
				return false;
			return bank.Tags.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim())
				.Any(t => names.Any(n => string.Equals(n, t, StringComparison.OrdinalIgnoreCase)));
		}

		/// <summary>A stable, process-independent number for a member and a week (string.GetHashCode is randomized per process).</summary>
		public static int StableIndex(string userId, string weekKey, int count)
		{
			if (count <= 1)
				return 0;
			using var sha = SHA256.Create();
			var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes((userId ?? string.Empty).ToUpperInvariant() + "|" + (weekKey ?? string.Empty)));
			return (int)(BitConverter.ToUInt32(bytes, 0) % (uint)count);
		}

		/// <summary>The protocol of a member's week: their micro-learning protocols take turns (a stable rotation per member and week).</summary>
		public static int? ChooseProtocol(string userId, string weekKey, IEnumerable<int> protocolIds)
		{
			var ids = (protocolIds ?? Enumerable.Empty<int>()).Distinct().OrderBy(i => i).ToList();
			return ids.Count == 0 ? (int?)null : ids[StableIndex(userId, weekKey, ids.Count)];
		}

		/// <summary>One drawable question, preferring ones the member was not asked recently.</summary>
		public static LmsQuestion ChooseQuestion(IEnumerable<LmsQuestion> pool, IEnumerable<int> recentQuestionIds, Random random)
		{
			random ??= new Random();
			var drawable = (pool ?? Enumerable.Empty<LmsQuestion>()).Where(IsDrawable).GroupBy(q => q.LmsQuestionId).Select(g => g.First())
				.OrderBy(q => q.LmsQuestionId).ToList();
			if (drawable.Count == 0)
				return null;
			var recent = new HashSet<int>(recentQuestionIds ?? Enumerable.Empty<int>());
			var fresh = drawable.Where(q => !recent.Contains(q.LmsQuestionId)).ToList();
			var from = fresh.Count > 0 ? fresh : drawable;
			return from[random.Next(from.Count)];
		}
	}
}
