using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Lms
{
	// Training T5 standards (LMS add-on): what Omni needs to play a SCORM / cmi5 lesson in a web view or open an LTI lesson (Omni itself is
	// out of scope for this build). Times are UTC.

	/// <summary>How to open a standards lesson on a device.</summary>
	public class LmsStandardsLaunchData
	{
		/// <summary>"Scorm", "Cmi5" or "LtiLink".</summary>
		public string Kind { get; set; }

		/// <summary>
		/// SCORM / cmi5: the absolute content URL of a fresh launch (token-bound; load it in a web view with the same sandbox the web player
		/// uses: scripts allowed, no shared origin). LTI: the web launch page, which needs the member's web session.
		/// </summary>
		public string Url { get; set; }

		public DateTime? ExpiresOn { get; set; }

		/// <summary>The SCO / AU launched and the package's items (a multi-item package launches one at a time).</summary>
		public string ItemIdentifier { get; set; }

		public List<LmsStandardsItemData> Items { get; set; } = new List<LmsStandardsItemData>();

		public bool Satisfied { get; set; }

		public decimal? ScorePercent { get; set; }
	}

	public class LmsStandardsItemData
	{
		public string Identifier { get; set; }
		public string Title { get; set; }
		public bool Satisfied { get; set; }
	}

	public class LmsStandardsLaunchResult : StandardApiResponseV4Base
	{
		public LmsStandardsLaunchData Data { get; set; }

		/// <summary>The refusal (an LmsErrors name, e.g. "LessonLocked", "AddonRequired", "MediaUnavailable", "NotLicensed" without an LMS seat) when the lesson cannot open.</summary>
		public string Error { get; set; }
	}

	// Round 10 A8: Lms/GetLessonLaunch, the in-app launch of SCORM, cmi5 and LTI lessons (a single-use launch link to the Core web launch page).

	/// <summary>How to open a SCORM, cmi5 or LTI lesson in the app's web view.</summary>
	public class LmsLessonLaunchData
	{
		/// <summary>"Scorm12", "Scorm2004", "Cmi5" or "Lti13".</summary>
		public string Kind { get; set; }

		/// <summary>
		/// The absolute launch link to the Core web launch page (https://…/lms-app/launch?token=…). Single use and short-lived (ExpiresOn, at most
		/// five minutes): open it in the web view at once and never store or share it. It carries an opaque token only (never the bearer token).
		/// </summary>
		public string LaunchUrl { get; set; }

		/// <summary>When the launch link stops working if it was not opened (UTC).</summary>
		public DateTime ExpiresOn { get; set; }

		/// <summary>The message the launch page posts when the runtime records a result, and where it comes from.</summary>
		public LmsLessonCompletionMessageData CompletionMessage { get; set; }
	}

	/// <summary>
	/// What the app listens for. The page posts { type, version, enrollmentId, lessonId, kind, status, satisfied, score?, error? } (camelCase) with
	/// window.ReactNativeWebView.postMessage (a JSON string; check the event's url starts with Origin) and window.parent.postMessage (an object;
	/// check event.origin equals Origin) when status becomes completed, passed, failed, terminated or unavailable. Treat it as a hint: GetCourse
	/// has the lesson's recorded progress.
	/// </summary>
	public class LmsLessonCompletionMessageData
	{
		/// <summary>"resgrid.lms.lesson".</summary>
		public string Type { get; set; }

		/// <summary>The message version (1).</summary>
		public int Version { get; set; }

		/// <summary>The Core web origin the launch page (and so every message) comes from, e.g. https://app.resgrid.com.</summary>
		public string Origin { get; set; }

		public int EnrollmentId { get; set; }

		public int LessonId { get; set; }

		/// <summary>Every status a message may carry: notAttempted, incomplete, completed, passed, failed, terminated, unavailable.</summary>
		public List<string> Statuses { get; set; } = new List<string>();

		/// <summary>The statuses the page posts (results): completed, passed, failed, terminated, unavailable (with error, an LmsErrors name).</summary>
		public List<string> ResultStatuses { get; set; } = new List<string>();
	}

	public class LmsLessonLaunchResult : StandardApiResponseV4Base
	{
		public LmsLessonLaunchData Data { get; set; }

		/// <summary>
		/// The refusal (an LmsErrors name) when the lesson cannot open now: "LessonLocked", "AddonRequired", "LapseWindowEnded", "NotLicensed" (no LMS
		/// seat), "MediaUnavailable" (the package is not ready, no store is configured or the tool is not active), "NotEntitled"; "RateLimited" when
		/// the member asked for too many launch links in a minute.
		/// </summary>
		public string Error { get; set; }
	}
}
