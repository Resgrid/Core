using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Resgrid.Model.Lms
{
	/// <summary>
	/// The canonical form of a course version that approvals sign (the content core's hash, §12.1): the version's settings and
	/// overview, and every module and lesson in order. Any change to what a learner would take changes the hash, so a signature
	/// given to one draft does not carry over to an edited one.
	/// </summary>
	public static class LmsCourseContent
	{
		public static string Canonical(LmsCourseVersion version, IEnumerable<LmsCourseModule> modules, IEnumerable<LmsLesson> lessons)
		{
			var moduleList = (modules ?? Enumerable.Empty<LmsCourseModule>()).Where(m => m != null).OrderBy(m => m.SortOrder).ThenBy(m => m.LmsCourseModuleId).ToList();
			var lessonList = (lessons ?? Enumerable.Empty<LmsLesson>()).Where(l => l != null).ToList();
			string D(decimal? d) => d?.ToString("0.####", CultureInfo.InvariantCulture);

			return JsonConvert.SerializeObject(new
			{
				kind = "lms-course-version/1",
				title = version?.Title ?? string.Empty,
				overview = version?.ContentHtml ?? string.Empty,
				hours = D(version?.Hours),
				category = version?.CategoryId,
				ceProvider = version?.CeProvider ?? string.Empty,
				ceNumber = version?.CeCourseNumber ?? string.Empty,
				certificateTemplate = version?.CertificateTemplateId,
				issuesCertification = version?.IssuesCertificationTypeId,
				modules = moduleList.Select(m => new
				{
					title = m.Title ?? string.Empty,
					lessons = lessonList.Where(l => l.LmsCourseModuleId == m.LmsCourseModuleId).OrderBy(l => l.SortOrder).ThenBy(l => l.LmsLessonId).Select(l => new
					{
						kind = l.Kind,
						title = l.Title ?? string.Empty,
						required = l.IsRequired,
						html = l.ContentHtml ?? string.Empty,
						video = new { source = l.VideoSource, url = l.VideoUrl ?? string.Empty, asset = l.LmsMediaAssetId, captions = l.CaptionTracksJson ?? string.Empty, transcript = l.TranscriptText ?? string.Empty },
						document = new { url = l.DocumentUrl ?? string.Empty, name = l.DocumentName ?? string.Empty },
						protocolVersion = l.ProtocolVersionId,
						seat = l.MinimumSeatSeconds,
						watch = l.RequiredWatchPercent,
						offline = new { l.AvailableOffline, l.OfflineMaxBytes, l.OfflineExpiryDays },
						tasks = new { response = l.FreeResponsePrompt ?? string.Empty, file = l.FileRequestPrompt ?? string.Empty },
						assessment = new
						{
							bank = l.LmsQuestionBankId, draw = l.DrawCount, tags = l.DrawTags ?? string.Empty, l.PassMark, l.AttemptLimit, l.RetakeCooldownHours,
							l.TimeLimitMinutes, l.ShowAnswersPolicy, l.IsPreTest, l.RandomizeAnswers, l.RequireAttestation
						},
						survey = l.SurveyJson ?? string.Empty
					}).ToList()
				}).ToList()
			});
		}

		/// <summary>SHA-256 (lower-case hex) of <see cref="Canonical"/>: what an approval's L2 signature binds to.</summary>
		public static string ComputeHash(LmsCourseVersion version, IEnumerable<LmsCourseModule> modules, IEnumerable<LmsLesson> lessons) =>
			SignaturePolicy.HashArtifact(Canonical(version, modules, lessons));
	}

	/// <summary>The department's LMS gate as one request sees it (§17.3): the flag, the module, the add-on and the lapse clock.</summary>
	public sealed class LmsDepartmentGate
	{
		public int DepartmentId { get; set; }

		/// <summary>The Trainings module is on (system-published content needs only this, R-S2).</summary>
		public bool ModuleOn { get; set; }

		/// <summary>The Training.Lms operator flag is on and the module is on. Off: department LMS content is dark.</summary>
		public bool FlagOn { get; set; }

		/// <summary>An active Lms PaymentAddons window: true, false, or null when billing could not answer.</summary>
		public bool? AddonActive { get; set; }

		/// <summary>When the gate last saw the add-on active (the lapse windows run from here).</summary>
		public DateTime? LastEntitledOn { get; set; }

		/// <summary>"Can author and manage": authoring, new LMS assignments and LMS reports (rule 3 stops them on lapse).</summary>
		public bool CanAuthor => FlagOn && AddonActive == true;

		/// <summary>The add-on was active once and is not now (or billing cannot say).</summary>
		public bool IsLapsed => FlagOn && AddonActive != true && LastEntitledOn.HasValue;
	}

	/// <summary>
	/// The lapse rules this phase implements (§17.3): 3 (authoring and new assignments stop), 4 (assigned lessons stay takeable until
	/// their due date), 6 (hosted video read-only for 90 days, then archived) and 11 (an undated course stays takeable 30 days after the
	/// lapse). Nothing is ever deleted (rule 1). System-published content runs whatever the add-on (R-S2).
	/// </summary>
	public static class LmsLapseRules
	{
		public static readonly TimeSpan UndatedTakeableWindow = TimeSpan.FromDays(30);

		public static readonly TimeSpan HostedVideoReadOnlyWindow = TimeSpan.FromDays(90);

		/// <summary>Rule 3: authoring and new LMS assignments (self-enrolment included) need the add-on.</summary>
		public static bool CanAssignNew(LmsDepartmentGate gate, bool systemCourse) =>
			gate != null && (systemCourse ? gate.ModuleOn : gate.CanAuthor);

		/// <summary>Whether a member may take the course on <paramref name="enrollment"/> now.</summary>
		public static bool CanRun(LmsDepartmentGate gate, bool systemCourse, LmsEnrollment enrollment, DateTime utcNow)
		{
			if (gate == null)
				return false;

			if (systemCourse)
				return gate.ModuleOn;

			// Everything department-made stays dark while the operator flag (or the module) is off.
			if (!gate.FlagOn)
				return false;

			if (gate.AddonActive == true)
				return true;

			if (!gate.LastEntitledOn.HasValue || enrollment == null)
				return false;

			// Only assignments made while the add-on was active survive it (rule 3: nothing new after).
			var lapsedOn = gate.LastEntitledOn.Value;
			if (enrollment.AssignedOn > lapsedOn.AddMinutes(5))
				return false;

			if (enrollment.DueOn.HasValue)
				return utcNow <= enrollment.DueOn.Value;                // rule 4

			return utcNow <= lapsedOn.Add(UndatedTakeableWindow);      // rule 11
		}

		/// <summary>Rule 6: hosted video streams while the add-on is active and for 90 days after it lapsed.</summary>
		public static bool CanStreamHostedVideo(LmsDepartmentGate gate, DateTime utcNow) =>
			gate != null && gate.FlagOn && (gate.AddonActive == true || (gate.LastEntitledOn.HasValue && utcNow <= gate.LastEntitledOn.Value.Add(HostedVideoReadOnlyWindow)));

		/// <summary>Rule 6: no uploads (read-only) without the add-on.</summary>
		public static bool CanUploadMedia(LmsDepartmentGate gate) => gate != null && gate.CanAuthor;

		/// <summary>Rule 6: past the read-only window the hosted media is archived (kept and exportable, no longer streamed).</summary>
		public static bool ShouldArchiveHostedMedia(LmsDepartmentGate gate, DateTime utcNow) =>
			gate != null && gate.AddonActive == false && gate.LastEntitledOn.HasValue && utcNow > gate.LastEntitledOn.Value.Add(HostedVideoReadOnlyWindow);
	}

	/// <summary>Seat time (§13a minimum seat time, R-C15): accrued from heartbeats, never more than wall-clock time allows.</summary>
	public static class LmsSeatTime
	{
		/// <summary>The most one heartbeat may add, whatever it claims.</summary>
		public const int MaxHeartbeatSeconds = 120;

		/// <summary>The most a first heartbeat (no earlier one to measure from) may add.</summary>
		public const int FirstHeartbeatSeconds = 30;

		/// <summary>
		/// The seconds a heartbeat claiming <paramref name="claimedSeconds"/> may add: no more than elapsed since the previous one
		/// (plus two seconds of clock slack) and never more than <see cref="MaxHeartbeatSeconds"/>.
		/// </summary>
		public static int Accrue(DateTime? lastSeenOnUtc, DateTime utcNow, int claimedSeconds)
		{
			if (claimedSeconds <= 0)
				return 0;

			var cap = MaxHeartbeatSeconds;
			if (lastSeenOnUtc.HasValue)
			{
				var elapsed = (utcNow - lastSeenOnUtc.Value).TotalSeconds;
				if (elapsed <= 0)
					return 0;
				cap = Math.Min(cap, (int)Math.Ceiling(elapsed) + 2);
			}
			else
			{
				cap = FirstHeartbeatSeconds;
			}

			return Math.Min(claimedSeconds, cap);
		}
	}

	/// <summary>When a learner may start an assessment attempt (T2 item 4): attempt limit, cooldown, a pass already, grading pending.</summary>
	public static class LmsAttemptRules
	{
		/// <summary>The attempt that is still open (in progress and before its deadline), if any.</summary>
		public static LmsAssessmentAttempt OpenAttempt(IEnumerable<LmsAssessmentAttempt> attempts, DateTime utcNow) =>
			(attempts ?? Enumerable.Empty<LmsAssessmentAttempt>()).Where(a => a != null && a.Status == (int)LmsAttemptStatuses.InProgress && (!a.DeadlineOn.HasValue || a.DeadlineOn.Value > utcNow))
				.OrderByDescending(a => a.LmsAssessmentAttemptId).FirstOrDefault();

		/// <summary>None when a new attempt may start now; otherwise why not, and when the cooldown ends.</summary>
		public static (LmsErrors Error, DateTime? NextOn) CanStart(LmsLesson lesson, IEnumerable<LmsAssessmentAttempt> attempts, DateTime utcNow)
		{
			if (lesson == null || lesson.Kind != (int)LmsLessonKinds.Assessment)
				return (LmsErrors.InvalidInput, null);

			var list = (attempts ?? Enumerable.Empty<LmsAssessmentAttempt>()).Where(a => a != null).ToList();
			if (list.Any(a => a.Passed == true))
				return (LmsErrors.AlreadySubmitted, null);
			if (list.Any(a => a.Status == (int)LmsAttemptStatuses.PendingGrading))
				return (LmsErrors.RequirementNotMet, null);

			// Started attempts count, including one that ran out of time: a timed attempt cannot be dodged by walking away.
			var used = list.Count(a => a.Status != (int)LmsAttemptStatuses.InProgress || (a.DeadlineOn.HasValue && a.DeadlineOn.Value <= utcNow));
			if (lesson.AttemptLimit > 0 && used >= lesson.AttemptLimit)
				return (LmsErrors.AttemptLimitReached, null);

			if (lesson.RetakeCooldownHours > 0)
			{
				var last = list.Where(a => a.SubmittedOn.HasValue).OrderByDescending(a => a.SubmittedOn).FirstOrDefault();
				if (last != null)
				{
					var next = last.SubmittedOn.Value.AddHours(lesson.RetakeCooldownHours);
					if (next > utcNow)
						return (LmsErrors.CooldownActive, next);
				}
			}

			return (LmsErrors.None, null);
		}

		/// <summary>
		/// R-T11 offline rule: only an untimed, unlimited-attempt quiz that is not a pre-test (and does not issue a certification) may be
		/// answered offline in Omni and scored when it syncs. Everything else is online only.
		/// </summary>
		public static bool MayAnswerOffline(LmsLesson lesson, LmsCourseVersion version) =>
			lesson != null && lesson.Kind == (int)LmsLessonKinds.Assessment && lesson.TimeLimitMinutes <= 0 && lesson.AttemptLimit <= 0 && !lesson.IsPreTest &&
			(version == null || !version.IssuesCertificationTypeId.HasValue);
	}

	/// <summary>The hosted media state machine (worker 87, decision 9).</summary>
	public static class LmsMediaStateMachine
	{
		/// <summary>Attempts before a source is marked Failed.</summary>
		public const int MaxAttempts = 3;

		/// <summary>A Transcoding claim older than this is abandoned (a stopped worker) and goes back to the queue.</summary>
		public static readonly TimeSpan StaleClaim = TimeSpan.FromHours(2);

		public static bool CanMove(LmsMediaStatuses from, LmsMediaStatuses to)
		{
			switch (from)
			{
				case LmsMediaStatuses.Uploading:
					return to == LmsMediaStatuses.Queued || to == LmsMediaStatuses.Failed;
				case LmsMediaStatuses.Queued:
					return to == LmsMediaStatuses.Transcoding;
				case LmsMediaStatuses.Transcoding:
					return to == LmsMediaStatuses.Ready || to == LmsMediaStatuses.Queued || to == LmsMediaStatuses.Failed;
				case LmsMediaStatuses.Ready:
					return to == LmsMediaStatuses.Archived;
				case LmsMediaStatuses.Failed:
					return to == LmsMediaStatuses.Queued;
				case LmsMediaStatuses.Archived:
					return to == LmsMediaStatuses.Ready;
				default:
					return false;
			}
		}

		/// <summary>After a failed transcode: back to the queue, or Failed after <see cref="MaxAttempts"/>.</summary>
		public static LmsMediaStatuses AfterFailure(int attemptsSoFar) =>
			attemptsSoFar >= MaxAttempts ? LmsMediaStatuses.Failed : LmsMediaStatuses.Queued;
	}

	/// <summary>
	/// The rotating crew-viewing code (R-C4): an HMAC of the viewing's secret and the 30-second time slice, shown as a QR on the
	/// shared screen. A code is accepted in its own slice and the one after, so a scan as the code rotates still counts; a photo
	/// sent to someone at home stops working within a minute.
	/// </summary>
	public static class LmsCrewCodes
	{
		public const int SliceSeconds = 30;

		public static long Slice(DateTime utcNow) => (long)Math.Floor((utcNow - DateTime.UnixEpoch).TotalSeconds / SliceSeconds);

		public static string CodeFor(string secret, long slice)
		{
			if (string.IsNullOrEmpty(secret))
				return null;
			using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
			var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(slice.ToString(CultureInfo.InvariantCulture)));
			return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
		}

		public static string CurrentCode(string secret, DateTime utcNow) => CodeFor(secret, Slice(utcNow));

		public static bool IsValid(string secret, string code, DateTime utcNow)
		{
			if (string.IsNullOrWhiteSpace(code) || string.IsNullOrEmpty(secret))
				return false;
			var slice = Slice(utcNow);
			var given = code.Trim().ToLowerInvariant();
			return FixedEquals(given, CodeFor(secret, slice)) || FixedEquals(given, CodeFor(secret, slice - 1));
		}

		public static string NewSecret()
		{
			var bytes = RandomNumberGenerator.GetBytes(32);
			return Convert.ToHexString(bytes).ToLowerInvariant();
		}

		private static bool FixedEquals(string a, string b) =>
			a != null && b != null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
	}

	/// <summary>The statement a course approval signature carries (stored with the signature).</summary>
	public static class LmsCourseStatements
	{
		public const string ApprovalVersion = "course-approval/1";

		public static string Approval(LmsCourse course, LmsCourseVersion version) =>
			string.Format(CultureInfo.InvariantCulture,
				"I approve the course \"{0}\" (draft {1}) for publication. Content fingerprint {2}.",
				course?.Title ?? version?.Title ?? string.Empty,
				version?.LmsCourseVersionId.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
				SignaturePolicy.ShortCode(version?.ContentHash));
	}
}
