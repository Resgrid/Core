using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model.Lms
{
	/// <summary>A new skill sheet or its catalog fields.</summary>
	public sealed class LmsSkillSheetInput
	{
		public string Code { get; set; }
		public string Title { get; set; }
		public string Description { get; set; }
		public LmsSkillSheetKinds Kind { get; set; }
		public int? CategoryId { get; set; }
		public SignerEligibility EvaluatorEligibility { get; set; }
		public SignerEligibility NextSignerEligibility { get; set; }
		public bool AllowSelfEvaluation { get; set; }
		public bool AllowCrewEvaluation { get; set; }
		public long ExpectedRowVersion { get; set; }
	}

	/// <summary>A draft version's steps and grading settings.</summary>
	public sealed class LmsSkillSheetDraftInput
	{
		public LmsSkillSheetContent Content { get; set; }
		public decimal PassThreshold { get; set; } = 100m;
		public int MaxAttempts { get; set; }
		public int AttemptWindowDays { get; set; }
		public int TimeLimitSeconds { get; set; }
		public int DefaultMinutes { get; set; } = 30;
	}

	/// <summary>Starting an evaluation (web, Omni or a class session's post-class evaluation).</summary>
	public sealed class LmsSkillEvaluationStart
	{
		public int? LmsSkillSheetId { get; set; }
		public List<string> MemberUserIds { get; set; } = new List<string>();
		/// <summary>The evaluator (defaults to the member starting it).</summary>
		public string EvaluatorUserId { get; set; }
		public int? LmsTaskBookId { get; set; }
		public string TaskKey { get; set; }
		public int? CallId { get; set; }
		public int? DeploymentId { get; set; }
		public string Location { get; set; }
		public string ClassSessionRef { get; set; }
		public string IdempotencyKey { get; set; }
		public DateTime? StartedOnUtc { get; set; }
	}

	/// <summary>Submitting an evaluation: the next signer the evaluator picks, or an outside evaluator who signs instead.</summary>
	public sealed class LmsSkillEvaluationSubmit
	{
		public long ExpectedRowVersion { get; set; }
		public string NextSignerUserId { get; set; }
		public string ExternalEvaluatorName { get; set; }
		public string ExternalEvaluatorEmail { get; set; }
		/// <summary>Remote (link + OTP, LMS add-on) or in person for an outside evaluator.</summary>
		public bool ExternalRemote { get; set; }
		public DateTime? EndedOnUtc { get; set; }
		public string Culture { get; set; }
		public string IpAddress { get; set; }
		public string UserAgent { get; set; }
	}

	/// <summary>
	/// A whole evaluation captured offline (Omni evaluator mode outbox): started, graded and submitted in one call. The idempotency key makes
	/// a replay return the evaluation the first delivery created.
	/// </summary>
	public sealed class LmsSkillOfflineSubmission
	{
		public string IdempotencyKey { get; set; }
		public int LmsSkillSheetId { get; set; }
		/// <summary>The version the device ran (refused when it is not a version of the sheet).</summary>
		public int LmsSkillSheetVersionId { get; set; }
		public List<string> MemberUserIds { get; set; } = new List<string>();
		public LmsSkillAnswers Answers { get; set; }
		public string Notes { get; set; }
		public DateTime StartedOnUtc { get; set; }
		public DateTime EndedOnUtc { get; set; }
		public int? LmsTaskBookId { get; set; }
		public string TaskKey { get; set; }
		public int? CallId { get; set; }
		public int? DeploymentId { get; set; }
		public string Location { get; set; }
		public string NextSignerUserId { get; set; }
		public string Culture { get; set; }
	}

	/// <summary>The "post-class evaluation" hook 5B's class sessions call: one crew evaluation of the roster on a sheet.</summary>
	public sealed class LmsPostClassEvaluationRequest
	{
		public int DepartmentId { get; set; }
		/// <summary>The session's stable reference, e.g. "class-session:42".</summary>
		public string ClassSessionRef { get; set; }
		public int LmsSkillSheetId { get; set; }
		public string EvaluatorUserId { get; set; }
		public List<string> MemberUserIds { get; set; } = new List<string>();
		public string RequestedByUserId { get; set; }
		public string Location { get; set; }
	}

	/// <summary>An evaluation with everything its pages show.</summary>
	public sealed class LmsSkillEvaluationView
	{
		public LmsSkillEvaluation Evaluation { get; set; }
		public LmsSkillSheet Sheet { get; set; }
		public LmsSkillSheetVersion Version { get; set; }
		public LmsSkillSheetContent Content { get; set; }
		public LmsSkillAnswers Answers { get; set; }
		public List<LmsSkillEvaluationMember> Members { get; set; } = new List<LmsSkillEvaluationMember>();
		public List<LmsSkillEvaluationFile> Files { get; set; } = new List<LmsSkillEvaluationFile>();
		public LmsSkillOutcome Outcome { get; set; }
		public LmsTaskBook TaskBook { get; set; }
		/// <summary>The sealed evidence document's state: "sealed", "pending", "failed" or null before completion.</summary>
		public string SealState { get; set; }
		public int? SealedDocumentId { get; set; }
	}

	/// <summary>One task of a member's task book with its progress.</summary>
	public sealed class LmsTaskBookTaskProgress
	{
		public LmsTaskBookTask Task { get; set; }
		public List<LmsSkillEvaluation> Evaluations { get; set; } = new List<LmsSkillEvaluation>();
		public int Counted { get; set; }
		public bool Complete { get; set; }
	}

	/// <summary>A task book with its tasks' progress.</summary>
	public sealed class LmsTaskBookView
	{
		public LmsTaskBook Book { get; set; }
		public List<LmsTaskBookTaskProgress> Tasks { get; set; } = new List<LmsTaskBookTaskProgress>();
		public bool AllTasksComplete => Tasks.Count > 0 && Tasks.All(t => t.Complete);
		public int CompleteCount => Tasks.Count(t => t.Complete);
	}

	/// <summary>The pure rules of skills and task books (tested directly).</summary>
	public static class LmsSkillRules
	{
		public const string EvaluationSubjectPrefix = "evaluation:";
		public const string TaskBookSubjectPrefix = "task-book:";

		public static string EvaluationSubjectId(int evaluationId) => EvaluationSubjectPrefix + evaluationId.ToString(CultureInfo.InvariantCulture);

		/// <summary>A task-book task as a competency subject (4C frameworks): "{definitionId}:{taskKey}".</summary>
		public static string TaskSubjectId(int definitionId, string taskKey) => definitionId.ToString(CultureInfo.InvariantCulture) + ":" + (taskKey ?? string.Empty).Trim();

		public static string TaskBookSubjectId(int taskBookId) => TaskBookSubjectPrefix + taskBookId.ToString(CultureInfo.InvariantCulture);

		/// <summary>Parses a SkillEvaluation signature subject id: ("evaluation", id) or ("task-book", id); (null, 0) otherwise.</summary>
		public static (string Kind, int Id) ParseSubject(string subjectId)
		{
			var value = (subjectId ?? string.Empty).Trim();
			foreach (var prefix in new[] { EvaluationSubjectPrefix, TaskBookSubjectPrefix })
				if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && int.TryParse(value.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
					return (prefix.TrimEnd(':'), id);
			return (null, 0);
		}

		/// <summary>The learning-record source key of an evaluation (one record per included member, R-T1).</summary>
		public static string EvaluationSourceRef(int evaluationId) => "skill-evaluation:" + evaluationId.ToString(CultureInfo.InvariantCulture);

		public static string TaskBookSourceRef(int taskBookId) => "task-book:" + taskBookId.ToString(CultureInfo.InvariantCulture);

		public static bool SameUser(string a, string b) =>
			!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

		/// <summary>The hours an evaluation credits: its measured duration, else the version's default minutes (2 decimals).</summary>
		public static decimal Hours(int durationSeconds, int defaultMinutes)
		{
			var seconds = durationSeconds > 0 ? durationSeconds : Math.Max(0, defaultMinutes) * 60;
			return Math.Round(seconds / 3600m, 2, MidpointRounding.AwayFromZero);
		}

		/// <summary>The seconds between start and end, bounded to a day (an offline clock cannot claim more).</summary>
		public static int Elapsed(DateTime startedUtc, DateTime endedUtc)
		{
			if (endedUtc <= startedUtc)
				return 0;
			return (int)Math.Min(TimeSpan.FromDays(1).TotalSeconds, Math.Round((endedUtc - startedUtc).TotalSeconds));
		}

		/// <summary>
		/// Whether another attempt may start: failed attempts in the window (completed or awaiting signature, never voided) below the
		/// version's maximum. Passing resets the count (only failures after the last pass count).
		/// </summary>
		public static bool CanAttempt(IEnumerable<(DateTime On, bool Passed)> priorAttempts, int maxAttempts, int windowDays, DateTime utcNow)
		{
			if (maxAttempts <= 0)
				return true;
			var from = windowDays > 0 ? utcNow.AddDays(-windowDays) : DateTime.MinValue;
			var attempts = (priorAttempts ?? Enumerable.Empty<(DateTime On, bool Passed)>()).Where(a => a.On >= from).OrderBy(a => a.On).ToList();
			var lastPass = attempts.LastOrDefault(a => a.Passed);
			var failures = attempts.Count(a => !a.Passed && (lastPass == default || a.On > lastPass.On));
			return failures < maxAttempts;
		}

		/// <summary>
		/// A task is complete when it has the required passed, completed evaluations; more than one must fall on different dates (UTC), and
		/// an incident task counts only evaluations that reference a call or a deployment.
		/// </summary>
		public static LmsTaskBookTaskProgress TaskProgress(LmsTaskBookTask task, IEnumerable<LmsSkillEvaluation> evaluations)
		{
			var counted = (evaluations ?? Enumerable.Empty<LmsSkillEvaluation>())
				.Where(e => e != null && e.Status == (int)LmsSkillEvaluationStatuses.Completed && e.Result == (int)LmsSkillEvaluationResults.Passed &&
					string.Equals(e.TaskKey, task.Key, StringComparison.OrdinalIgnoreCase) && (!task.RequiresIncident || e.CallId.HasValue || e.DeploymentId.HasValue))
				.OrderBy(e => e.CompletedOn ?? e.SubmittedOn ?? e.StartedOn).ToList();
			var dates = counted.Select(e => (e.SubmittedOn ?? e.StartedOn).Date).Distinct().Count();
			var required = Math.Max(1, task.RequiredEvaluations);
			return new LmsTaskBookTaskProgress { Task = task, Evaluations = counted, Counted = Math.Min(counted.Count, dates), Complete = Math.Min(counted.Count, dates) >= required };
		}

		public static List<LmsTaskBookTask> ParseTasks(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new List<LmsTaskBookTask>();
			try
			{
				return (JsonConvert.DeserializeObject<List<LmsTaskBookTask>>(json) ?? new List<LmsTaskBookTask>()).Where(t => t != null && !string.IsNullOrWhiteSpace(t.Key)).ToList();
			}
			catch (JsonException)
			{
				return new List<LmsTaskBookTask>();
			}
		}

		/// <summary>Problems with a definition's tasks (error codes); empty when valid.</summary>
		public static List<string> ValidateTasks(IReadOnlyCollection<LmsTaskBookTask> tasks)
		{
			var errors = new List<string>();
			if (tasks == null || tasks.Count == 0 || tasks.Count > 200)
			{
				errors.Add("lms_task_book_task_count");
				return errors;
			}
			var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var task in tasks)
			{
				if (task == null || string.IsNullOrWhiteSpace(task.Key) || task.Key.Length > 40 || !keys.Add(task.Key.Trim()))
					errors.Add("lms_task_book_task_key");
				else if (string.IsNullOrWhiteSpace(task.Title) || task.Title.Length > 300 || (task.Number?.Length ?? 0) > 40 || task.RequiredEvaluations < 1 || task.RequiredEvaluations > 50)
					errors.Add("lms_task_book_task_invalid");
			}
			return errors.Distinct().ToList();
		}

		/// <summary>
		/// When a notice queued now is due: immediately, or at the department-local digest hour (today if still ahead, else tomorrow), or on
		/// the digest weekday. <paramref name="toLocal"/>/<paramref name="toUtc"/> convert with the department's time zone.
		/// </summary>
		public static DateTime DueOn(LmsSkillSettings settings, DateTime utcNow, Func<DateTime, DateTime> toLocal, Func<DateTime, DateTime> toUtc)
		{
			settings = (settings ?? new LmsSkillSettings()).Normalized();
			var mode = (LmsSkillDigestModes)settings.DigestMode;
			if (mode == LmsSkillDigestModes.Immediate || mode == LmsSkillDigestModes.Off)
				return utcNow;
			var local = toLocal(utcNow);
			var slot = local.Date.AddHours(settings.DigestHour);
			if (mode == LmsSkillDigestModes.Daily)
			{
				if (slot <= local)
					slot = slot.AddDays(1);
			}
			else
			{
				var days = ((settings.DigestWeekday - (int)slot.DayOfWeek) + 7) % 7;
				slot = slot.AddDays(days);
				if (slot <= local)
					slot = slot.AddDays(7);
			}
			return toUtc(slot);
		}
	}
}
