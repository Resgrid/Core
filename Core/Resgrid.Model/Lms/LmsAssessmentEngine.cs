using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model.Lms
{
	/// <summary>
	/// The assessment rules (T2 item 4), pure so they can be tested: random draw from a pool, the learner's view (questions and
	/// options only, R-T11), the revision hash an attempt records (R-T6), server-side scoring, manual grading of short answers and
	/// item analysis (difficulty and discrimination).
	/// </summary>
	public static class LmsAssessmentEngine
	{
		/// <summary>Discrimination needs enough attempts for a top and bottom group to mean anything.</summary>
		public const int MinimumAttemptsForDiscrimination = 10;

		/// <summary>The share of attempts in each of the top and bottom groups (Kelley's 27%).</summary>
		public const decimal GroupShare = 0.27m;

		/// <summary>
		/// Draws the questions of one attempt: the live (not retired) questions of the pool carrying one of <paramref name="tags"/>
		/// (any when empty), <paramref name="drawCount"/> of them at random (all when 0 or more than the pool), each with its options
		/// shuffled when <paramref name="randomizeAnswers"/> (ordering questions are always shuffled; true/false never are).
		/// Translated text for <paramref name="locale"/> is used when the question carries it.
		/// </summary>
		public static LmsAttemptSnapshot Draw(IEnumerable<LmsQuestion> pool, int drawCount, string tags, bool randomizeAnswers, int passMark, Random random,
			string locale = null)
		{
			random ??= new Random();
			var wanted = SplitTags(tags);
			var candidates = (pool ?? Enumerable.Empty<LmsQuestion>())
				.Where(q => q != null && !q.IsRetired)
				.Where(q => wanted.Count == 0 || SplitTags(q.Tags).Overlaps(wanted))
				.OrderBy(q => q.SortOrder).ThenBy(q => q.LmsQuestionId)
				.ToList();

			var chosen = drawCount <= 0 || drawCount >= candidates.Count
				? Shuffle(candidates, random)
				: Shuffle(candidates, random).Take(drawCount).ToList();

			var snapshot = new LmsAttemptSnapshot { PassMark = Math.Max(0, Math.Min(100, passMark)) };
			foreach (var question in chosen)
				snapshot.Questions.Add(ToSnapshot(question, randomizeAnswers, random, locale));
			return snapshot;
		}

		private static LmsSnapshotQuestion ToSnapshot(LmsQuestion question, bool randomizeAnswers, Random random, string locale)
		{
			var options = question.Options ?? new List<LmsQuestionOption>();
			var translation = Translation(question, locale);
			string OptionText(LmsQuestionOption o) =>
				translation != null && translation.Options != null && o.Key != null && translation.Options.TryGetValue(o.Key, out var t) && !string.IsNullOrWhiteSpace(t) ? t : o.Text;

			var kind = (LmsQuestionKinds)question.Kind;
			var shown = options.ToList();
			if (kind == LmsQuestionKinds.Ordering || (randomizeAnswers && kind != LmsQuestionKinds.TrueFalse && kind != LmsQuestionKinds.ShortAnswer))
				shown = Shuffle(shown, random);
			if (kind == LmsQuestionKinds.Ordering && shown.Count > 1 && shown.Select(o => o.Key).SequenceEqual(options.Select(o => o.Key)))
			{
				// Never show an ordering question already in order.
				var first = shown[0];
				shown.RemoveAt(0);
				shown.Add(first);
			}

			return new LmsSnapshotQuestion
			{
				QuestionId = question.LmsQuestionId,
				Kind = question.Kind,
				Text = !string.IsNullOrWhiteSpace(translation?.Text) ? translation.Text : question.Text,
				ImageUrl = question.ImageUrl,
				Points = question.Points <= 0 ? 1 : question.Points,
				Options = kind == LmsQuestionKinds.ShortAnswer ? new List<LmsLearnerOption>() : shown.Select(o => new LmsLearnerOption { Key = o.Key, Text = OptionText(o) }).ToList(),
				CorrectKeys = kind == LmsQuestionKinds.Ordering || kind == LmsQuestionKinds.ShortAnswer ? new List<string>() : options.Where(o => o.IsCorrect).Select(o => o.Key).ToList(),
				CorrectOrder = kind == LmsQuestionKinds.Ordering ? options.Select(o => o.Key).ToList() : new List<string>(),
				Explanation = !string.IsNullOrWhiteSpace(translation?.Explanation) ? translation.Explanation : question.Explanation
			};
		}

		private static LmsQuestionTranslation Translation(LmsQuestion question, string locale)
		{
			if (string.IsNullOrWhiteSpace(locale) || string.IsNullOrWhiteSpace(question.TranslationsJson))
				return null;

			var translations = LmsQuestionTranslation.Parse(question.TranslationsJson);
			if (translations.TryGetValue(locale, out var exact))
				return exact;
			var dash = locale.IndexOf('-');
			return dash > 0 && translations.TryGetValue(locale.Substring(0, dash), out var language) ? language : null;
		}

		/// <summary>What the browser and the apps receive: questions and options in display order, never a key (R-T11).</summary>
		public static List<LmsLearnerQuestion> ToLearnerView(LmsAttemptSnapshot snapshot) =>
			(snapshot?.Questions ?? new List<LmsSnapshotQuestion>()).Select(q => new LmsLearnerQuestion
			{
				QuestionId = q.QuestionId,
				Kind = q.Kind,
				Text = q.Text,
				ImageUrl = q.ImageUrl,
				Points = q.Points,
				Options = q.Options.Select(o => new LmsLearnerOption { Key = o.Key, Text = o.Text }).ToList()
			}).ToList();

		/// <summary>
		/// The revision an attempt was taken against (R-T6): SHA-256 over the questions, their options, keys and points, and the
		/// pass mark, in a canonical form. Two attempts on the same quiz text and key have the same hash whatever the draw order.
		/// </summary>
		public static string RevisionHash(LmsAttemptSnapshot snapshot)
		{
			var canonical = JsonConvert.SerializeObject(new
			{
				passMark = snapshot?.PassMark ?? 0,
				questions = (snapshot?.Questions ?? new List<LmsSnapshotQuestion>()).OrderBy(q => q.QuestionId).Select(q => new
				{
					id = q.QuestionId,
					kind = q.Kind,
					text = q.Text ?? string.Empty,
					image = q.ImageUrl ?? string.Empty,
					points = q.Points.ToString("0.####", CultureInfo.InvariantCulture),
					options = q.Options.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => (o.Key ?? string.Empty) + "=" + (o.Text ?? string.Empty)).ToList(),
					correct = q.CorrectKeys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
					order = q.CorrectOrder
				}).ToList()
			});
			return SignaturePolicy.HashArtifact(canonical);
		}

		/// <summary>
		/// Scores an attempt on the server. Choice and true/false: all and only the correct keys. Ordering: the exact order.
		/// Short answer: awaits a grader (counts toward the maximum, not the score) unless <paramref name="manualGrades"/> has it.
		/// Unanswered questions score zero. Passed is null while anything awaits a grader.
		/// </summary>
		public static LmsScoreOutcome Score(LmsAttemptSnapshot snapshot, IEnumerable<LmsAttemptAnswer> answers, IEnumerable<LmsManualGrade> manualGrades = null)
		{
			var byQuestion = (answers ?? Enumerable.Empty<LmsAttemptAnswer>()).Where(a => a != null)
				.GroupBy(a => a.QuestionId).ToDictionary(g => g.Key, g => g.Last());
			var grades = (manualGrades ?? Enumerable.Empty<LmsManualGrade>()).Where(g => g != null)
				.GroupBy(g => g.QuestionId).ToDictionary(g => g.Key, g => g.Last().Awarded);

			var outcome = new LmsScoreOutcome();
			foreach (var question in snapshot?.Questions ?? new List<LmsSnapshotQuestion>())
			{
				byQuestion.TryGetValue(question.QuestionId, out var answer);
				var points = question.Points <= 0 ? 1 : question.Points;
				var result = new LmsQuestionResult { QuestionId = question.QuestionId, Points = points };

				switch ((LmsQuestionKinds)question.Kind)
				{
					case LmsQuestionKinds.ShortAnswer:
						if (grades.TryGetValue(question.QuestionId, out var awarded))
						{
							result.Awarded = Math.Max(0, Math.Min(points, awarded));
							result.Correct = result.Awarded >= points;
						}
						else if (string.IsNullOrWhiteSpace(answer?.Text))
						{
							// Nothing to grade: an empty answer scores zero without waiting for anyone.
							result.Awarded = 0;
						}
						else
						{
							result.AwaitingGrade = true;
						}
						break;
					case LmsQuestionKinds.Ordering:
						result.Correct = answer?.Keys != null && answer.Keys.Count == question.CorrectOrder.Count &&
							answer.Keys.SequenceEqual(question.CorrectOrder, StringComparer.Ordinal);
						result.Awarded = result.Correct ? points : 0;
						break;
					default:
						var chosen = new HashSet<string>((answer?.Keys ?? new List<string>()).Where(k => k != null), StringComparer.Ordinal);
						var correct = new HashSet<string>(question.CorrectKeys.Where(k => k != null), StringComparer.Ordinal);
						result.Correct = correct.Count > 0 && chosen.SetEquals(correct);
						result.Awarded = result.Correct ? points : 0;
						break;
				}

				outcome.Results.Add(result);
			}

			outcome.MaxScore = outcome.Results.Sum(r => r.Points);
			outcome.Score = outcome.Results.Sum(r => r.Awarded);
			outcome.Percent = outcome.MaxScore <= 0 ? 0 : Math.Round(outcome.Score * 100m / outcome.MaxScore, 2);
			outcome.PendingGrading = outcome.Results.Any(r => r.AwaitingGrade);
			outcome.Passed = outcome.PendingGrading ? (bool?)null : outcome.Percent >= (snapshot?.PassMark ?? 0);
			return outcome;
		}

		/// <summary>Whether the learner may see the key and explanations of a finished attempt under the lesson's policy.</summary>
		public static bool MayShowAnswers(LmsShowAnswersPolicies policy, bool? passed) =>
			policy == LmsShowAnswersPolicies.Always || (policy == LmsShowAnswersPolicies.AfterPassing && passed == true);

		/// <summary>Whether the learner may see which questions were right or wrong.</summary>
		public static bool MayShowCorrectness(LmsShowAnswersPolicies policy, bool? passed) =>
			policy == LmsShowAnswersPolicies.CorrectnessOnly || MayShowAnswers(policy, passed);

		/// <summary>
		/// Item analysis over finished attempts (scored or graded; in progress and awaiting-grade attempts are left out). Difficulty
		/// p = correct / responses. Discrimination D = p(top 27% by percent) − p(bottom 27%), each group at least one attempt,
		/// reported only from <see cref="MinimumAttemptsForDiscrimination"/> attempts answering the question.
		/// </summary>
		public static List<LmsItemStatistic> ItemAnalysis(IEnumerable<LmsAssessmentAttempt> attempts)
		{
			var finished = (attempts ?? Enumerable.Empty<LmsAssessmentAttempt>())
				.Where(a => a != null && (a.Status == (int)LmsAttemptStatuses.Scored || a.Status == (int)LmsAttemptStatuses.Graded))
				.Select(a => new
				{
					Attempt = a,
					Snapshot = LmsAttemptSnapshot.Parse(a.SnapshotJson),
					Results = LmsQuestionResult.Parse(a.ResultsJson).GroupBy(r => r.QuestionId).ToDictionary(g => g.Key, g => g.Last()),
					Answers = LmsAttemptAnswer.Parse(a.AnswersJson).GroupBy(r => r.QuestionId).ToDictionary(g => g.Key, g => g.Last())
				})
				.ToList();

			var statistics = new List<LmsItemStatistic>();
			foreach (var questionId in finished.SelectMany(f => f.Results.Keys).Distinct().OrderBy(id => id))
			{
				var answered = finished.Where(f => f.Results.ContainsKey(questionId)).OrderByDescending(f => f.Attempt.Percent).ThenBy(f => f.Attempt.LmsAssessmentAttemptId).ToList();
				var stat = new LmsItemStatistic
				{
					QuestionId = questionId,
					Text = answered.Select(f => f.Snapshot.Questions.FirstOrDefault(q => q.QuestionId == questionId)?.Text).FirstOrDefault(t => t != null),
					Responses = answered.Count
				};

				if (answered.Count > 0)
					stat.Difficulty = Math.Round((decimal)answered.Count(f => f.Results[questionId].Correct) / answered.Count, 4);

				if (answered.Count >= MinimumAttemptsForDiscrimination)
				{
					var size = Math.Max(1, (int)Math.Round(answered.Count * GroupShare, MidpointRounding.AwayFromZero));
					var top = answered.Take(size).ToList();
					var bottom = answered.Skip(answered.Count - size).ToList();
					var pTop = (decimal)top.Count(f => f.Results[questionId].Correct) / top.Count;
					var pBottom = (decimal)bottom.Count(f => f.Results[questionId].Correct) / bottom.Count;
					stat.Discrimination = Math.Round(pTop - pBottom, 4);
				}

				foreach (var f in answered)
				{
					if (!f.Answers.TryGetValue(questionId, out var answer) || answer.Keys == null)
						continue;
					var question = f.Snapshot.Questions.FirstOrDefault(q => q.QuestionId == questionId);
					if (question == null || question.Kind == (int)LmsQuestionKinds.Ordering || question.Kind == (int)LmsQuestionKinds.ShortAnswer)
						continue;
					foreach (var key in answer.Keys.Where(k => k != null).Distinct(StringComparer.Ordinal))
						stat.OptionCounts[key] = stat.OptionCounts.TryGetValue(key, out var n) ? n + 1 : 1;
				}

				statistics.Add(stat);
			}

			return statistics;
		}

		/// <summary>The tags of a comma separated list, trimmed, case-insensitive.</summary>
		public static HashSet<string> SplitTags(string tags) =>
			new HashSet<string>((tags ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0),
				StringComparer.OrdinalIgnoreCase);

		private static List<T> Shuffle<T>(IEnumerable<T> items, Random random)
		{
			var list = items.ToList();
			for (var i = list.Count - 1; i > 0; i--)
			{
				var j = random.Next(i + 1);
				(list[i], list[j]) = (list[j], list[i]);
			}
			return list;
		}
	}
}
