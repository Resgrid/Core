using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Resgrid.Model.Helpers
{
	/// <summary>The result of scoring one quiz submission.</summary>
	public class TrainingQuizResult
	{
		public int QuestionCount { get; set; }

		public int CorrectCount { get; set; }

		/// <summary>0 to 100, two decimals.</summary>
		public double Score { get; set; }

		public bool Passed { get; set; }

		public string QuizRevisionHash { get; set; }

		public TrainingQuizSnapshot Snapshot { get; set; }
	}

	/// <summary>
	/// Server-side quiz scoring (R-T11: the browser and the apps never see the answer key). Pure, so the rules are tested
	/// without a database: posted ids are validated against the training's own questions and options (T-D4), each
	/// question counts once whatever the posted key spelling (T-D11), the score is capped at 100, and a multiple-choice
	/// question is right only when exactly its correct options were chosen (R-S9).
	/// </summary>
	public static class TrainingQuizScorer
	{
		/// <summary>The most questions one submission may answer; a quiz is never longer.</summary>
		public const int MaxQuestions = 100;

		/// <summary>The most options one question may offer.</summary>
		public const int MaxOptionsPerQuestion = 26;

		// Canonical keys only: "question_5". "question_05", "question_+5" and "question_ 5" are not the same question twice.
		private static readonly Regex QuestionKey = new Regex(@"^question_([1-9][0-9]{0,9})$", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		/// <summary>
		/// The answers in a posted form: question id to chosen option ids. Keys that are not canonical question keys and
		/// values that are not canonical positive ids are ignored; at most <see cref="MaxQuestions"/> questions are read.
		/// </summary>
		public static Dictionary<int, List<int>> ParsePostedAnswers(IEnumerable<KeyValuePair<string, IEnumerable<string>>> form)
		{
			var answers = new Dictionary<int, List<int>>();
			if (form == null)
				return answers;

			foreach (var pair in form)
			{
				if (pair.Key == null)
					continue;

				var match = QuestionKey.Match(pair.Key);
				if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var questionId))
					continue;

				if (!answers.ContainsKey(questionId))
				{
					if (answers.Count >= MaxQuestions)
						break;
					answers[questionId] = new List<int>();
				}

				foreach (var raw in pair.Value ?? Enumerable.Empty<string>())
					foreach (var part in (raw ?? string.Empty).Split(','))
						if (TryCanonicalId(part, out var optionId) && !answers[questionId].Contains(optionId) && answers[questionId].Count < MaxOptionsPerQuestion)
							answers[questionId].Add(optionId);
			}

			return answers;
		}

		private static bool TryCanonicalId(string value, out int id)
		{
			id = 0;
			var trimmed = value?.Trim();
			if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 10 || trimmed[0] == '0')
				return false;
			foreach (var c in trimmed)
				if (c < '0' || c > '9')
					return false;
			return int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
		}

		/// <summary>Scores a submission against the training's current questions (each with its answers loaded).</summary>
		public static TrainingQuizResult Score(Training training, IReadOnlyDictionary<int, List<int>> chosen)
		{
			var questions = OrderedQuestions(training);
			var snapshot = new TrainingQuizSnapshot { MinimumScore = training?.MinimumScore ?? 0 };
			var correct = 0;

			foreach (var question in questions)
			{
				var options = OrderedAnswers(question);
				var optionIds = new HashSet<int>(options.Select(o => o.TrainingQuestionAnswerId));
				List<int> picked = null;
				if (chosen != null)
					chosen.TryGetValue(question.TrainingQuestionId, out picked);

				// Only this question's own options count (T-D4): an id from another question or training is dropped.
				var valid = (picked ?? new List<int>()).Where(optionIds.Contains).Distinct().ToList();
				var right = new HashSet<int>(options.Where(o => o.Correct).Select(o => o.TrainingQuestionAnswerId));

				bool isCorrect;
				if (question.Type == (int)TrainingQuestionTypes.MultiChoice)
					isCorrect = right.Count > 0 && valid.Count == right.Count && valid.All(right.Contains);
				else
					isCorrect = valid.Count == 1 && right.Contains(valid[0]);

				if (isCorrect)
					correct++;

				snapshot.Questions.Add(new TrainingQuizSnapshotQuestion
				{
					QuestionId = question.TrainingQuestionId,
					Text = question.Question,
					Type = question.Type,
					Options = options.Select(o => new TrainingQuizSnapshotOption { OptionId = o.TrainingQuestionAnswerId, Text = o.Answer, Correct = o.Correct }).ToList(),
					ChosenOptionIds = valid,
					AnsweredCorrectly = isCorrect
				});
			}

			var count = questions.Count;
			var score = count == 0 ? 0 : Math.Round((double)correct / count * 100d, 2);
			score = Math.Max(0, Math.Min(100, score));

			return new TrainingQuizResult
			{
				QuestionCount = count,
				CorrectCount = correct,
				Score = score,
				Passed = count > 0 && score >= (training?.MinimumScore ?? 0),
				QuizRevisionHash = RevisionHash(training),
				Snapshot = snapshot
			};
		}

		/// <summary>
		/// SHA-256 (hex, lower case) of the quiz as it would be scored: each question's type, text and options (text and
		/// whether it is correct), in order, and the pass mark. Ids are left out, so re-saving an unchanged quiz keeps its
		/// hash. Two attempts with the same hash were taken against the same quiz (R-T6).
		/// </summary>
		public static string RevisionHash(Training training)
		{
			var builder = new StringBuilder();
			builder.Append("min=").Append((training?.MinimumScore ?? 0).ToString("R", CultureInfo.InvariantCulture)).Append('\n');

			foreach (var question in OrderedQuestions(training))
			{
				builder.Append("q|").Append(question.Type.ToString(CultureInfo.InvariantCulture)).Append('|').Append(Normalize(question.Question)).Append('\n');
				foreach (var answer in OrderedAnswers(question))
					builder.Append("a|").Append(answer.Correct ? '1' : '0').Append('|').Append(Normalize(answer.Answer)).Append('\n');
			}

			using var sha = SHA256.Create();
			var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
			return string.Concat(hash.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
		}

		/// <summary>
		/// True when an edit changes a question, an option, the answer key or the pass mark (R-T6): the editor is asked
		/// whether the change is material before completed members are re-assigned.
		/// </summary>
		public static bool ChangesQuiz(Training stored, Training edited)
		{
			return !string.Equals(RevisionHash(stored), RevisionHash(edited), StringComparison.Ordinal);
		}

		/// <summary>Problems with a quiz an author is saving; empty when it can be saved.</summary>
		public static List<string> Validate(Training training)
		{
			var problems = new List<string>();
			var questions = training?.Questions?.Where(q => q != null).ToList() ?? new List<TrainingQuestion>();

			if (questions.Count > MaxQuestions)
				problems.Add("TooManyQuestions");

			if (training != null && (double.IsNaN(training.MinimumScore) || training.MinimumScore < 0 || training.MinimumScore > 100))
				problems.Add("MinimumScoreRange");

			foreach (var question in questions)
			{
				var answers = question.Answers?.Where(a => a != null).ToList() ?? new List<TrainingQuestionAnswer>();
				var correct = answers.Count(a => a.Correct);

				if (string.IsNullOrWhiteSpace(question.Question))
					problems.Add("QuestionText");
				if (!Enum.IsDefined(typeof(TrainingQuestionTypes), question.Type))
					problems.Add("QuestionType");
				if (answers.Count < 2 || answers.Count > MaxOptionsPerQuestion)
					problems.Add("QuestionOptions");
				if (question.Type == (int)TrainingQuestionTypes.MultiChoice ? correct < 1 : correct != 1)
					problems.Add("QuestionAnswerKey");
				if (question.Type == (int)TrainingQuestionTypes.TrueFalse && answers.Count != 2)
					problems.Add("TrueFalseOptions");
			}

			return problems.Distinct().ToList();
		}

		private static List<TrainingQuestion> OrderedQuestions(Training training) =>
			(training?.Questions ?? Enumerable.Empty<TrainingQuestion>()).Where(q => q != null).OrderBy(q => q.TrainingQuestionId <= 0 ? int.MaxValue : q.TrainingQuestionId).ToList();

		private static List<TrainingQuestionAnswer> OrderedAnswers(TrainingQuestion question) =>
			(question?.Answers ?? Enumerable.Empty<TrainingQuestionAnswer>()).Where(a => a != null).OrderBy(a => a.TrainingQuestionAnswerId <= 0 ? int.MaxValue : a.TrainingQuestionAnswerId).ToList();

		private static string Normalize(string value) => (value ?? string.Empty).Trim().Replace("\r\n", "\n");
	}
}
