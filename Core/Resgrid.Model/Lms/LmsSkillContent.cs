using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Resgrid.Model.Lms
{
	/// <summary>An option of a choose-one or choose-any step.</summary>
	public sealed class LmsSkillOption
	{
		public string Key { get; set; }
		public string Label { get; set; }
		/// <summary>Points the option scores (in proportion to the step's best).</summary>
		public decimal Points { get; set; }
		/// <summary>Choosing a non-passing option fails the step.</summary>
		public bool Passing { get; set; } = true;
	}

	/// <summary>
	/// One step of a skill sheet. The pass/fail, critical, required, N/A, note-on-fail and evidence-on-fail semantics are the Checklists
	/// execution model's (<see cref="Resgrid.Model.Checklists.ChecklistValidation"/>), widened to the full question-type set.
	/// </summary>
	public sealed class LmsSkillStep
	{
		public const int LabelMaxLength = 300;
		public const int HelpMaxLength = 4000;

		public string Key { get; set; }
		public int Type { get; set; }
		public string Label { get; set; }
		public string Help { get; set; }
		/// <summary>A failed critical step fails the evaluation whatever the score (§13a "critical step auto-fail").</summary>
		public bool Critical { get; set; }
		/// <summary>An answer is needed to submit (scored steps always need one unless N/A is allowed).</summary>
		public bool Required { get; set; }
		public decimal Weight { get; set; } = 1m;
		public List<LmsSkillOption> Options { get; set; } = new List<LmsSkillOption>();
		public int? RatingMin { get; set; }
		public int? RatingMax { get; set; }
		/// <summary>The lowest rating that passes the step (null: every rating passes).</summary>
		public int? PassingRating { get; set; }
		/// <summary>The passing bounds of a number, duration (seconds) or computed step.</summary>
		public decimal? Min { get; set; }
		public decimal? Max { get; set; }
		public int ComputeOp { get; set; }
		public List<string> ComputeKeys { get; set; } = new List<string>();
		public bool AllowNotApplicable { get; set; }
		public bool RequireNoteOnFail { get; set; }
		public bool RequireEvidenceOnFail { get; set; }

		[JsonIgnore]
		public LmsSkillStepTypes StepType => (LmsSkillStepTypes)Type;
	}

	/// <summary>The steps of a skill sheet version (ContentJson).</summary>
	public sealed class LmsSkillSheetContent
	{
		public int Schema { get; set; } = 1;
		public List<LmsSkillStep> Steps { get; set; } = new List<LmsSkillStep>();

		public static LmsSkillSheetContent Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new LmsSkillSheetContent();
			try
			{
				return JsonConvert.DeserializeObject<LmsSkillSheetContent>(json) ?? new LmsSkillSheetContent();
			}
			catch (JsonException)
			{
				return new LmsSkillSheetContent();
			}
		}

		public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None);

		/// <summary>
		/// A task-book task signed without a sheet: one critical pass/fail step "performed to standard" and a comment (the same engine runs
		/// over it, so the result, hash and sealed document follow one code path).
		/// </summary>
		public static LmsSkillSheetContent TaskSignoff(string title) => new LmsSkillSheetContent
		{
			Steps = new List<LmsSkillStep>
			{
				new LmsSkillStep { Key = "task", Type = (int)LmsSkillStepTypes.PassFail, Label = string.IsNullOrWhiteSpace(title) ? "Task performed to standard" : title.Trim(), Critical = true, Required = true, RequireNoteOnFail = true },
				new LmsSkillStep { Key = "comments", Type = (int)LmsSkillStepTypes.Text, Label = "Evaluator comments" }
			}
		};
	}

	/// <summary>A note the evaluator added at a moment of the evaluation.</summary>
	public sealed class LmsSkillTimedNote
	{
		public DateTime At { get; set; }
		public string Text { get; set; }
	}

	/// <summary>The answer to one step.</summary>
	public sealed class LmsSkillAnswer
	{
		public int Status { get; set; }
		/// <summary>pass/fail, the rating, number, seconds, chosen option key, text or ISO date/time.</summary>
		public string Value { get; set; }
		/// <summary>The chosen option keys of a choose-any step.</summary>
		public List<string> Values { get; set; }
		/// <summary>The evaluator's annotation on the step.</summary>
		public string Note { get; set; }
		public List<LmsSkillTimedNote> Notes { get; set; }
		public string NotApplicableReason { get; set; }
		public DateTime? AnsweredOn { get; set; }
	}

	/// <summary>Every answer of an evaluation, keyed by step (AnswersJson).</summary>
	public sealed class LmsSkillAnswers
	{
		public Dictionary<string, LmsSkillAnswer> Steps { get; set; } = new Dictionary<string, LmsSkillAnswer>(StringComparer.OrdinalIgnoreCase);

		public static LmsSkillAnswers Parse(string json)
		{
			var answers = new LmsSkillAnswers();
			if (string.IsNullOrWhiteSpace(json))
				return answers;
			try
			{
				var parsed = JsonConvert.DeserializeObject<LmsSkillAnswers>(json);
				if (parsed?.Steps != null)
					foreach (var pair in parsed.Steps.Where(p => !string.IsNullOrWhiteSpace(p.Key) && p.Value != null))
						answers.Steps[pair.Key] = pair.Value;
			}
			catch (JsonException)
			{
			}
			return answers;
		}

		public LmsSkillAnswer Get(string key) => key != null && Steps.TryGetValue(key, out var answer) ? answer : null;

		/// <summary>Stored form, keys in ordinal order (stable for the hash).</summary>
		public string ToJson() => JsonConvert.SerializeObject(new
		{
			Steps = Steps.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value)
		}, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, DateFormatString = "yyyy-MM-ddTHH:mm:ssZ", DateTimeZoneHandling = DateTimeZoneHandling.Utc });

		public LmsSkillAnswers Clone() => Parse(ToJson());
	}

	/// <summary>How a version grades: the pass threshold and the time limit.</summary>
	public sealed class LmsSkillGrading
	{
		public decimal PassThreshold { get; set; }
		public int TimeLimitSeconds { get; set; }
	}

	/// <summary>What the engine made of an evaluation's answers.</summary>
	public sealed class LmsSkillOutcome
	{
		/// <summary>Why it cannot be submitted (error codes "lms_skill_step_*:{step key}").</summary>
		public List<string> Errors { get; } = new List<string>();
		public decimal? Score { get; set; }
		public bool Passed { get; set; }
		public bool CriticalFailed { get; set; }
		public bool TimeExceeded { get; set; }
		public List<string> FailedStepKeys { get; } = new List<string>();
		public Dictionary<string, decimal?> Computed { get; } = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
		public bool Complete => Errors.Count == 0;
	}

	/// <summary>
	/// The skill sheet engine (pure, tested directly): validates a sheet's steps, grades an evaluation's answers (critical steps fail the
	/// evaluation whatever the score, the time limit fails it, scored steps count by weight against the pass threshold), fills "Pass All",
	/// and computes the canonical hashes the signatures bind to.
	/// </summary>
	public static class LmsSkillSheetEngine
	{
		public const int MaxSteps = 200;
		public const int MaxOptions = 30;
		public const int MaxTextLength = 4000;
		public const int MaxTimedNotes = 100;

		public static bool IsDisplayOnly(LmsSkillStepTypes type) => type == LmsSkillStepTypes.Heading || type == LmsSkillStepTypes.Instructions;

		/// <summary>Steps that score toward the result (and pass or fail).</summary>
		public static bool IsScored(LmsSkillStep step)
		{
			switch (step.StepType)
			{
				case LmsSkillStepTypes.PassFail:
				case LmsSkillStepTypes.RatingScale:
				case LmsSkillStepTypes.ChooseOne:
				case LmsSkillStepTypes.ChooseAny:
					return true;
				case LmsSkillStepTypes.Number:
				case LmsSkillStepTypes.Duration:
				case LmsSkillStepTypes.Computed:
					return step.Min.HasValue || step.Max.HasValue;
				default:
					return false;
			}
		}

		private static bool IsNumericSource(LmsSkillStepTypes type) =>
			type == LmsSkillStepTypes.Number || type == LmsSkillStepTypes.Duration || type == LmsSkillStepTypes.RatingScale;

		/// <summary>Problems with a sheet's steps (error codes); empty when it can be published.</summary>
		public static List<string> Validate(LmsSkillSheetContent content)
		{
			var errors = new List<string>();
			var steps = content?.Steps ?? new List<LmsSkillStep>();
			if (steps.Count == 0 || steps.Count > MaxSteps)
			{
				errors.Add("lms_skill_sheet_step_count");
				return errors;
			}
			if (!steps.Any(s => s != null && !IsDisplayOnly(s.StepType)))
				errors.Add("lms_skill_sheet_no_gradable_step");

			var seen = new Dictionary<string, LmsSkillStep>(StringComparer.OrdinalIgnoreCase);
			foreach (var step in steps)
			{
				if (step == null || string.IsNullOrWhiteSpace(step.Key) || step.Key.Length > 40 || !step.Key.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_') || seen.ContainsKey(step.Key))
				{
					errors.Add("lms_skill_sheet_step_key");
					continue;
				}
				var key = step.Key;
				if (!Enum.IsDefined(typeof(LmsSkillStepTypes), step.Type))
					errors.Add("lms_skill_sheet_step_type:" + key);
				if (string.IsNullOrWhiteSpace(step.Label) || step.Label.Length > LmsSkillStep.LabelMaxLength || (step.Help?.Length ?? 0) > LmsSkillStep.HelpMaxLength)
					errors.Add("lms_skill_sheet_step_label:" + key);
				if (step.Weight < 0 || step.Weight > 1000 || (IsScored(step) && step.Critical && step.Weight == 0))
					errors.Add("lms_skill_sheet_step_weight:" + key);
				if (step.Critical && !IsScored(step))
					errors.Add("lms_skill_sheet_step_critical:" + key);

				switch (step.StepType)
				{
					case LmsSkillStepTypes.RatingScale:
						if (!step.RatingMin.HasValue || !step.RatingMax.HasValue || step.RatingMin.Value >= step.RatingMax.Value || step.RatingMax.Value - step.RatingMin.Value > 100 ||
							(step.PassingRating.HasValue && (step.PassingRating.Value < step.RatingMin.Value || step.PassingRating.Value > step.RatingMax.Value)))
							errors.Add("lms_skill_sheet_step_rating:" + key);
						break;
					case LmsSkillStepTypes.ChooseOne:
					case LmsSkillStepTypes.ChooseAny:
						var options = step.Options ?? new List<LmsSkillOption>();
						if (options.Count < 2 || options.Count > MaxOptions || options.Any(o => o == null || string.IsNullOrWhiteSpace(o.Key) || string.IsNullOrWhiteSpace(o.Label) || o.Label.Length > 200 || o.Points < 0) ||
							options.Select(o => o?.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Count)
							errors.Add("lms_skill_sheet_step_options:" + key);
						break;
					case LmsSkillStepTypes.Number:
					case LmsSkillStepTypes.Duration:
						if (step.Min.HasValue && step.Max.HasValue && step.Min.Value > step.Max.Value)
							errors.Add("lms_skill_sheet_step_bounds:" + key);
						break;
					case LmsSkillStepTypes.Computed:
						if (!Enum.IsDefined(typeof(LmsSkillComputeOps), step.ComputeOp) || step.ComputeKeys == null || step.ComputeKeys.Count == 0 ||
							step.ComputeKeys.Any(k => k == null || !seen.TryGetValue(k, out var source) || !IsNumericSource(source.StepType)))
							errors.Add("lms_skill_sheet_step_compute:" + key);
						if (step.Min.HasValue && step.Max.HasValue && step.Min.Value > step.Max.Value)
							errors.Add("lms_skill_sheet_step_bounds:" + key);
						break;
				}

				seen[key] = step;
			}

			return errors.Distinct().ToList();
		}

		/// <summary>
		/// Grades the answers. <paramref name="final"/> adds the submission checks (every required or scored step answered or N/A with a
		/// reason, a note on a failed step that asks for one, evidence where required). <paramref name="evidenceStepKeys"/> are the steps
		/// that have evidence files.
		/// </summary>
		public static LmsSkillOutcome Evaluate(LmsSkillSheetContent content, LmsSkillAnswers answers, LmsSkillGrading grading, int elapsedSeconds,
			ISet<string> evidenceStepKeys, bool final)
		{
			var outcome = new LmsSkillOutcome();
			answers ??= new LmsSkillAnswers();
			grading ??= new LmsSkillGrading();
			evidenceStepKeys ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var steps = (content?.Steps ?? new List<LmsSkillStep>()).Where(s => s != null && !string.IsNullOrWhiteSpace(s.Key)).ToList();
			var known = new HashSet<string>(steps.Select(s => s.Key), StringComparer.OrdinalIgnoreCase);
			if (answers.Steps.Keys.Any(k => !known.Contains(k)))
				outcome.Errors.Add("lms_skill_answer_unknown_step");

			var numbers = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
			decimal denominator = 0m, numerator = 0m;

			foreach (var step in steps)
			{
				var type = step.StepType;
				if (IsDisplayOnly(type))
					continue;

				var answer = answers.Get(step.Key);
				var status = answer == null ? LmsSkillAnswerStatuses.Unanswered : Enum.IsDefined(typeof(LmsSkillAnswerStatuses), answer.Status) ? (LmsSkillAnswerStatuses)answer.Status : LmsSkillAnswerStatuses.Unanswered;
				var scored = IsScored(step);

				if (answer != null && ((answer.Value?.Length ?? 0) > MaxTextLength || (answer.Note?.Length ?? 0) > MaxTextLength || (answer.Notes?.Count ?? 0) > MaxTimedNotes ||
					(answer.Notes ?? new List<LmsSkillTimedNote>()).Any(n => n == null || (n.Text?.Length ?? 0) > MaxTextLength)))
				{
					outcome.Errors.Add("lms_skill_step_too_long:" + step.Key);
					continue;
				}

				if (status == LmsSkillAnswerStatuses.NotApplicable)
				{
					if (!step.AllowNotApplicable || step.Critical || string.IsNullOrWhiteSpace(answer.NotApplicableReason))
						outcome.Errors.Add("lms_skill_step_not_applicable:" + step.Key);
					continue;
				}

				if (type == LmsSkillStepTypes.Computed)
				{
					var inputs = (step.ComputeKeys ?? new List<string>()).Where(numbers.ContainsKey).Select(k => numbers[k]).ToList();
					decimal? value = inputs.Count == 0 ? (decimal?)null : (LmsSkillComputeOps)step.ComputeOp switch
					{
						LmsSkillComputeOps.Average => Math.Round(inputs.Average(), 2, MidpointRounding.AwayFromZero),
						LmsSkillComputeOps.Min => inputs.Min(),
						LmsSkillComputeOps.Max => inputs.Max(),
						_ => inputs.Sum()
					};
					outcome.Computed[step.Key] = value;
					if (value.HasValue)
						numbers[step.Key] = value.Value;
					if (scored)
					{
						denominator += step.Weight;
						var pass = value.HasValue && InBounds(step, value.Value);
						if (pass)
							numerator += step.Weight;
						else if (value.HasValue || final)
							Fail(outcome, step);
					}
					continue;
				}

				var hasEvidence = evidenceStepKeys.Contains(step.Key);
				var answered = status == LmsSkillAnswerStatuses.Answered && (type == LmsSkillStepTypes.Evidence ? hasEvidence :
					type == LmsSkillStepTypes.ChooseAny ? answer.Values != null && answer.Values.Count > 0 :
					type == LmsSkillStepTypes.TimestampedNotes ? answer.Notes != null && answer.Notes.Count > 0 :
					!string.IsNullOrWhiteSpace(answer.Value));
				if (type == LmsSkillStepTypes.Evidence && hasEvidence)
					answered = true;

				if (scored)
					denominator += step.Weight;

				if (!answered)
				{
					if (final && (step.Required || scored))
						outcome.Errors.Add("lms_skill_step_required:" + step.Key);
					continue;
				}

				bool valid = true, passed = true;
				decimal earned = 0m;
				switch (type)
				{
					case LmsSkillStepTypes.PassFail:
						valid = answer.Value == "pass" || answer.Value == "fail";
						passed = answer.Value == "pass";
						earned = passed ? step.Weight : 0m;
						break;
					case LmsSkillStepTypes.RatingScale:
						valid = int.TryParse(answer.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rating) && step.RatingMin.HasValue && step.RatingMax.HasValue &&
							rating >= step.RatingMin.Value && rating <= step.RatingMax.Value;
						if (valid)
						{
							numbers[step.Key] = rating;
							passed = !step.PassingRating.HasValue || rating >= step.PassingRating.Value;
							var span = step.RatingMax.Value - step.RatingMin.Value;
							earned = span <= 0 ? step.Weight : step.Weight * (rating - step.RatingMin.Value) / span;
						}
						break;
					case LmsSkillStepTypes.ChooseOne:
					{
						var option = (step.Options ?? new List<LmsSkillOption>()).FirstOrDefault(o => string.Equals(o.Key, answer.Value, StringComparison.OrdinalIgnoreCase));
						valid = option != null;
						if (valid)
						{
							passed = option.Passing;
							var best = step.Options.Max(o => o.Points);
							earned = best <= 0 ? (passed ? step.Weight : 0m) : step.Weight * option.Points / best;
						}
						break;
					}
					case LmsSkillStepTypes.ChooseAny:
					{
						var options = step.Options ?? new List<LmsSkillOption>();
						var chosen = options.Where(o => answer.Values.Contains(o.Key, StringComparer.OrdinalIgnoreCase)).ToList();
						valid = chosen.Count == answer.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count();
						if (valid)
						{
							passed = chosen.All(o => o.Passing);
							var best = options.Where(o => o.Passing).Sum(o => o.Points);
							earned = best <= 0 ? (passed ? step.Weight : 0m) : step.Weight * Math.Min(1m, chosen.Where(o => o.Passing).Sum(o => o.Points) / best);
							if (!passed)
								earned = 0m;
						}
						break;
					}
					case LmsSkillStepTypes.Number:
					case LmsSkillStepTypes.Duration:
						valid = decimal.TryParse(answer.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) && (type != LmsSkillStepTypes.Duration || number >= 0);
						if (valid)
						{
							numbers[step.Key] = number;
							passed = InBounds(step, number);
							earned = passed ? step.Weight : 0m;
						}
						break;
					case LmsSkillStepTypes.Date:
						valid = DateTime.TryParseExact(answer.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
						break;
					case LmsSkillStepTypes.Time:
						valid = TimeSpan.TryParseExact(answer.Value, new[] { @"hh\:mm", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture, out _);
						break;
					case LmsSkillStepTypes.DateTime:
						valid = DateTime.TryParse(answer.Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out _);
						break;
				}

				if (!valid)
				{
					outcome.Errors.Add("lms_skill_step_invalid:" + step.Key);
					continue;
				}

				if (!scored)
					continue;
				numerator += earned;
				if (!passed)
				{
					Fail(outcome, step);
					if (final && step.RequireNoteOnFail && string.IsNullOrWhiteSpace(answer.Note))
						outcome.Errors.Add("lms_skill_step_note_required:" + step.Key);
					if (final && step.RequireEvidenceOnFail && !hasEvidence)
						outcome.Errors.Add("lms_skill_step_evidence_required:" + step.Key);
				}
			}

			outcome.Score = denominator > 0 ? Math.Round(100m * numerator / denominator, 2, MidpointRounding.AwayFromZero) : (decimal?)null;
			outcome.TimeExceeded = grading.TimeLimitSeconds > 0 && elapsedSeconds > grading.TimeLimitSeconds;
			outcome.Passed = outcome.Errors.Count == 0 && !outcome.CriticalFailed && !outcome.TimeExceeded &&
				(!outcome.Score.HasValue || outcome.Score.Value >= Math.Max(0m, Math.Min(100m, grading.PassThreshold)));
			return outcome;
		}

		private static void Fail(LmsSkillOutcome outcome, LmsSkillStep step)
		{
			if (!outcome.FailedStepKeys.Contains(step.Key, StringComparer.OrdinalIgnoreCase))
				outcome.FailedStepKeys.Add(step.Key);
			if (step.Critical)
				outcome.CriticalFailed = true;
		}

		private static bool InBounds(LmsSkillStep step, decimal value) =>
			(!step.Min.HasValue || value >= step.Min.Value) && (!step.Max.HasValue || value <= step.Max.Value);

		/// <summary>"Pass All": every pass/fail step not yet answered becomes a pass (answered steps keep the evaluator's grade).</summary>
		public static LmsSkillAnswers PassAll(LmsSkillSheetContent content, LmsSkillAnswers answers, DateTime utcNow)
		{
			var result = (answers ?? new LmsSkillAnswers()).Clone();
			foreach (var step in (content?.Steps ?? new List<LmsSkillStep>()).Where(s => s != null && s.StepType == LmsSkillStepTypes.PassFail && !string.IsNullOrWhiteSpace(s.Key)))
			{
				var existing = result.Get(step.Key);
				if (existing != null && existing.Status != (int)LmsSkillAnswerStatuses.Unanswered)
					continue;
				result.Steps[step.Key] = new LmsSkillAnswer { Status = (int)LmsSkillAnswerStatuses.Answered, Value = "pass", Note = existing?.Note, AnsweredOn = utcNow };
			}
			return result;
		}

		/// <summary>SHA-256 (lower-case hex) of a sheet version: its steps and grading settings.</summary>
		public static string VersionHash(LmsSkillSheetContent content, decimal passThreshold, int maxAttempts, int attemptWindowDays, int timeLimitSeconds, int defaultMinutes) =>
			Sha256(JsonConvert.SerializeObject(new
			{
				kind = "skill-sheet-version/1",
				steps = content?.Steps ?? new List<LmsSkillStep>(),
				pass = passThreshold.ToString("0.##", CultureInfo.InvariantCulture),
				maxAttempts,
				attemptWindowDays,
				timeLimitSeconds,
				defaultMinutes
			}, Formatting.None));

		public static string Sha256(string text)
		{
			var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
			var builder = new StringBuilder(64);
			foreach (var b in bytes)
				builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
			return builder.ToString();
		}

		/// <summary>The canonical text of a submitted evaluation: what the evaluator's and next signer's signatures bind to.</summary>
		public static string CanonicalEvaluation(LmsSkillEvaluation evaluation, string versionHash, IEnumerable<LmsSkillEvaluationMember> members,
			IEnumerable<LmsSkillEvaluationFile> files)
		{
			var included = (members ?? Enumerable.Empty<LmsSkillEvaluationMember>()).Where(m => m.IsIncluded && !string.IsNullOrWhiteSpace(m.UserId))
				.Select(m => new { user = m.UserId.Trim().ToLowerInvariant(), attempt = m.AttemptNumber }).OrderBy(m => m.user, StringComparer.Ordinal).ToList();
			var evidence = (files ?? Enumerable.Empty<LmsSkillEvaluationFile>()).OrderBy(f => f.LmsSkillEvaluationFileId)
				.Select(f => new { step = f.StepKey, sha = (f.Sha256 ?? string.Empty).ToLowerInvariant(), size = f.Size, type = f.ContentType }).ToList();
			var answers = JObject.Parse(LmsSkillAnswers.Parse(evaluation.AnswersJson).ToJson());
			return JsonConvert.SerializeObject(new
			{
				kind = "skill-evaluation/1",
				id = evaluation.LmsSkillEvaluationId,
				sheet = evaluation.LmsSkillSheetId,
				version = evaluation.LmsSkillSheetVersionId,
				versionHash = (versionHash ?? string.Empty).ToLowerInvariant(),
				code = evaluation.Code,
				title = evaluation.Title,
				mode = evaluation.Mode,
				evaluator = (evaluation.EvaluatorUserId ?? string.Empty).Trim().ToLowerInvariant(),
				external = evaluation.ExternalEvaluatorName,
				next = (evaluation.NextSignerUserId ?? string.Empty).Trim().ToLowerInvariant(),
				members = included,
				result = evaluation.Result,
				score = evaluation.Score?.ToString("0.##", CultureInfo.InvariantCulture),
				critical = evaluation.CriticalFailed,
				time = evaluation.TimeExceeded,
				started = evaluation.StartedOn.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
				submitted = evaluation.SubmittedOn?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
				duration = evaluation.DurationSeconds,
				call = evaluation.CallId,
				deployment = evaluation.DeploymentId,
				taskBook = evaluation.LmsTaskBookId,
				task = evaluation.TaskKey,
				classSession = evaluation.ClassSessionRef,
				location = evaluation.Location,
				notes = evaluation.Notes,
				answers,
				evidence
			}, Formatting.None);
		}
	}
}
