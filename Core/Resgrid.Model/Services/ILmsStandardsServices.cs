using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Services
{
	// Training T5 item 3 (round 6C): SCORM 1.2 / 2004 and cmi5 packages with a sandboxed runtime player, xAPI statements out to a department
	// LRS, and an LTI 1.3 tool consumer. LMS add-on: authoring needs the add-on (lapse rule 3), launching follows the course's lapse rules.

	/// <summary>SCORM and cmi5 package import (validate, store, unpack on worker 98).</summary>
	public interface ILmsContentPackageService
	{
		/// <summary>The LMS media store is configured (packages need it; without it uploads are refused).</summary>
		bool IsStoreConfigured { get; }

		/// <summary>
		/// Validates an uploaded zip (size and file caps, zip-slip, zip bombs, the manifest) and stores it for worker 98 to unpack. On a refusal
		/// <see cref="LmsResult.Value"/> is the <see cref="LmsPackageInspection"/> saying why; on success it is the queued package.
		/// </summary>
		Task<LmsResult> UploadAsync(LmsActor actor, string fileName, Stream content, long length, CancellationToken cancellationToken = default);

		Task<List<LmsContentPackage>> GetPackagesAsync(int departmentId, bool includeDeleted = false);

		Task<LmsContentPackage> GetPackageAsync(int departmentId, int lmsContentPackageId);

		/// <summary>Hides a package from new lessons (lessons using it keep playing).</summary>
		Task<LmsResult> DeleteAsync(LmsActor actor, int lmsContentPackageId, CancellationToken cancellationToken = default);

		/// <summary>Worker 98: unpacks up to <paramref name="maxItems"/> queued packages. Returns how many finished (ready or failed).</summary>
		Task<int> ProcessQueueAsync(int maxItems, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// The runtime of a SCORM or cmi5 lesson: launches (a token-bound session the sandboxed frame uses), the package files the frame reads, the
	/// SCORM adapter's commits, and the cmi5 fetch URL and minimal LRS. Every write is bound to the launch token; completion goes through the
	/// learner service (the course's gate and lapse rules).
	/// </summary>
	public interface ILmsStandardsRuntimeService
	{
		/// <summary>
		/// Starts (or resumes) the learner's runtime on a Scorm or Cmi5 lesson they may open now and returns the content URL for the sandboxed
		/// frame. <paramref name="itemIdentifier"/> picks a SCO or AU (the first not yet satisfied by default). <paramref name="siteBase"/> is the
		/// request's scheme and host, so the cmi5 endpoints are same-origin with the content.
		/// </summary>
		Task<LmsContentLaunch> LaunchAsync(int departmentId, string userId, int lmsEnrollmentId, int lmsLessonId, string itemIdentifier, string siteBase,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Round 10 A8: <see cref="LaunchAsync"/> for an in-app launch session (<see cref="LmsRuntimeKinds.AppSession"/>): the same rules, as the
		/// session's member on the session's enrollment and lesson only; the content session carries the app session's id and ends no later.
		/// </summary>
		Task<LmsContentLaunch> LaunchForAppAsync(LmsRuntimeSession appSession, string itemIdentifier, string siteBase, CancellationToken cancellationToken = default);

		/// <summary>The package a content token may read, or null (unknown, expired or ended token).</summary>
		Task<LmsContentSession> ResolveContentAsync(string token);

		/// <summary>One file of the session's package (its path checked again), or null when it is not in the package.</summary>
		Task<Stream> OpenContentAsync(LmsContentSession session, string path, CancellationToken cancellationToken = default);

		/// <summary>The SCORM adapter's start values for a launch token, or null.</summary>
		Task<LmsScormLaunchConfig> GetScormConfigAsync(string token, string commitUrl);

		/// <summary>One SCORM commit (validated, stored, outcome derived; satisfaction completes the lesson).</summary>
		Task<LmsRuntimeOutcome> CommitScormAsync(string token, LmsScormCommit commit, CancellationToken cancellationToken = default);

		/// <summary>cmi5 fetch URL: hands out the auth token once (<see cref="LmsRuntimeOutcome.Value"/>); a second call is refused.</summary>
		Task<LmsRuntimeOutcome> FetchCmi5TokenAsync(string fetchToken, CancellationToken cancellationToken = default);

		/// <summary>Minimal LRS: stores one statement or an array (only with the launch's auth token); Value is the list of statement ids.</summary>
		Task<LmsRuntimeOutcome> SaveCmi5StatementsAsync(string authToken, string body, string statementId, CancellationToken cancellationToken = default);

		/// <summary>Minimal LRS: reads one statement of the launch's registration (Value is its JSON).</summary>
		Task<LmsRuntimeOutcome> GetCmi5StatementAsync(string authToken, string statementId);

		/// <summary>Minimal LRS State API: LMS.LaunchData (generated) or a document the AU stored. Value is the document text.</summary>
		Task<LmsRuntimeOutcome> GetCmi5StateAsync(string authToken, string activityId, string stateId, string registration, string agent);

		/// <summary>Minimal LRS State API: stores (PUT) or merges (POST, JSON objects) a document. LMS.LaunchData is read-only.</summary>
		Task<LmsRuntimeOutcome> SaveCmi5StateAsync(string authToken, string activityId, string stateId, string registration, string agent, string contentType, string body, bool merge,
			CancellationToken cancellationToken = default);

		Task<LmsRuntimeOutcome> DeleteCmi5StateAsync(string authToken, string activityId, string stateId, string registration, string agent, CancellationToken cancellationToken = default);

		/// <summary>The learner's recorded results of a standards lesson (the officer's view and the learner's own).</summary>
		Task<LmsRuntimeRegistration> GetRegistrationAsync(int departmentId, int lmsEnrollmentId, int lmsLessonId);

		/// <summary>Worker 98: removes expired launch sessions. Returns how many.</summary>
		Task<int> PurgeExpiredSessionsAsync(DateTime utcNow, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// LTI 1.3 tool consumer (Resgrid is the platform): tool registrations, the department's rotate-able RS256 platform key and key set, the
	/// OIDC third-party-initiated launch with a signed id_token, and Assignment and Grade Services score passback that completes the lesson.
	/// </summary>
	public interface ILmsLtiService
	{
		Task<List<LmsLtiTool>> GetToolsAsync(int departmentId, bool includeDeleted = false);

		Task<LmsLtiTool> GetToolAsync(int departmentId, int lmsLtiToolId);

		Task<LmsResult> SaveToolAsync(LmsActor actor, LmsLtiToolInput input, CancellationToken cancellationToken = default);

		Task<LmsResult> DeleteToolAsync(LmsActor actor, int lmsLtiToolId, CancellationToken cancellationToken = default);

		/// <summary>What the administrator gives a tool (issuer, key set, login and token URLs, the active key id; null kid before the first key).</summary>
		Task<LmsLtiPlatformDetails> GetPlatformDetailsAsync(int departmentId);

		/// <summary>Creates a new active platform key and retires the old one (still published for a week).</summary>
		Task<LmsResult> RotateKeyAsync(LmsActor actor, CancellationToken cancellationToken = default);

		/// <summary>The department's public key set (JWKS JSON): the active key and keys retired within the publication window.</summary>
		Task<string> GetKeySetJsonAsync(int departmentId, DateTime utcNow);

		/// <summary>Step 1: the learner opens an LTI lesson they may run now: a single-use login hint and the tool's login initiation URL.</summary>
		Task<LmsLtiLaunchStep> BeginLaunchAsync(int departmentId, string userId, int lmsEnrollmentId, int lmsLessonId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Round 10 A8: step 1 for an in-app launch session (<see cref="LmsRuntimeKinds.AppSession"/>): the same rules, as the session's member on
		/// its enrollment and lesson only. The hint carries the app session's id, so the id_token's launch_presentation gets a signed return URL.
		/// </summary>
		Task<LmsLtiLaunchStep> BeginAppLaunchAsync(LmsRuntimeSession appSession, CancellationToken cancellationToken = default);

		/// <summary>
		/// Step 2: the tool's OIDC authentication request (on the learner's browser). Checks the client, redirect URI, login hint
		/// and message hint, consumes the launch and returns the form post of the signed id_token to the tool's redirect URI. The single-use
		/// message hint authenticates the request (the SameSite=Strict member cookie does not come with a cross-site redirect);
		/// <paramref name="signedInUserId"/> is null then, and when a member session does come with it, it must be the launch's member.
		/// </summary>
		Task<LmsLtiLaunchStep> AuthorizeAsync(int departmentId, string signedInUserId, IDictionary<string, string> request, string locale, CancellationToken cancellationToken = default);

		/// <summary>The AGS token endpoint: client credentials with the tool's signed assertion (verified with its key set).</summary>
		Task<LmsLtiTokenResult> IssueTokenAsync(int departmentId, IDictionary<string, string> form, CancellationToken cancellationToken = default);

		/// <summary>AGS score passback for a lesson's line item (bearer token with the score scope of the lesson's tool).</summary>
		Task<LmsRuntimeOutcome> PostScoreAsync(int departmentId, int lmsLessonId, string bearerToken, string body, CancellationToken cancellationToken = default);

		/// <summary>AGS line item of a lesson (Value is the JSON).</summary>
		Task<LmsRuntimeOutcome> GetLineItemAsync(int departmentId, int lmsLessonId, string bearerToken);

		/// <summary>Worker 98: removes expired AGS tokens.</summary>
		Task<int> PurgeExpiredTokensAsync(DateTime utcNow, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Where LMS learning events become xAPI statements (completed, passed, failed, attempted). Never throws and never blocks the learner: a
	/// department without a deliverable LRS configuration queues nothing.
	/// </summary>
	public interface ILmsXapiStatementQueue
	{
		Task EnqueueAsync(LmsXapiEvent learningEvent, CancellationToken cancellationToken = default);
	}

	/// <summary>The department's LRS settings (setting 147), the outbox view and worker 98's delivery with retry and backoff.</summary>
	public interface ILmsXapiService : ILmsXapiStatementQueue
	{
		Task<LmsStandardsSettings> GetSettingsAsync(int departmentId);

		/// <summary>Saves the settings; <paramref name="newSecret"/> replaces the stored secret (encrypted), <paramref name="clearSecret"/> removes it.</summary>
		Task<LmsResult> SaveSettingsAsync(LmsActor actor, LmsStandardsSettings settings, string newSecret, bool clearSecret, CancellationToken cancellationToken = default);

		Task<LmsXapiOverview> GetOverviewAsync(int departmentId);

		Task<LmsResult> RetryFailedAsync(LmsActor actor, CancellationToken cancellationToken = default);

		/// <summary>Worker 98: delivers due statements in batches per department. Returns how many were delivered.</summary>
		Task<int> DeliverDueAsync(DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Worker 98: removes statements delivered more than the retention ago.</summary>
		Task<int> PurgeDeliveredAsync(DateTime utcNow, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Round 10 A8: SCORM, cmi5 and LTI lessons in a bearer client's web view (Omni). The API hands the member a short-lived, single-use launch
	/// link (<see cref="IssueAsync"/>); the Core web page behind it exchanges the link for a page session bound to that member, enrollment and
	/// lesson (no sign-in, no cookie) and runs the lesson's runtime; the page reads that lesson's state to tell the app about results. Every step
	/// runs the player's rules again as the member (module, gate, lapse window, LMS seat, lesson order, playability) and fails closed.
	/// </summary>
	public interface ILmsAppLaunchService
	{
		/// <summary>
		/// A launch link for a Scorm, Cmi5 or LtiLink lesson of the member's own enrollment that may run now (<paramref name="itemIdentifier"/>
		/// optionally picks a SCO or AU). NotFound for another member's enrollment, an unknown lesson or another lesson kind.
		/// </summary>
		Task<LmsAppLaunchIssue> IssueAsync(int departmentId, string userId, int lmsEnrollmentId, int lmsLessonId, string itemIdentifier, CancellationToken cancellationToken = default);

		/// <summary>
		/// Exchanges (consumes) a launch link: a new page session and the lesson's runtime (the SCORM / cmi5 content launch, or the LTI tool to
		/// open). CodeExpired for an unknown, used or expired link; the player's refusal otherwise. Audited.
		/// </summary>
		Task<LmsAppPage> ExchangeAsync(string launchToken, string siteBase, string ipAddress, string userAgent, CancellationToken cancellationToken = default);

		/// <summary>Relaunches a SCO or AU of the page session's own lesson (the session's token is the only authority).</summary>
		Task<LmsAppPage> OpenItemAsync(string sessionToken, string itemIdentifier, string siteBase, CancellationToken cancellationToken = default);

		/// <summary>A fresh LTI login initiation for the page session's own lesson (the URL to send the web view to).</summary>
		Task<LmsLtiLaunchStep> BeginLtiAsync(string sessionToken, CancellationToken cancellationToken = default);

		/// <summary>The page session's lesson state (the completion message); null for an unknown or expired session.</summary>
		Task<LmsAppLessonMessage> GetStateAsync(string sessionToken);

		/// <summary>The lesson state behind a signed LTI return link (its app session); null when the session is unknown, another department's or expired.</summary>
		Task<LmsAppLessonMessage> GetReturnStateAsync(int departmentId, int appSessionId);
	}

	/// <summary>The course builder's check of a Scorm, Cmi5 or LtiLink lesson: the package or tool must be the department's and usable.</summary>
	public interface ILmsStandardsLessonValidator
	{
		/// <summary>Validates the standards fields of <paramref name="input"/> and copies them onto <paramref name="lesson"/>. None when acceptable.</summary>
		Task<LmsErrors> ApplyAsync(LmsActor actor, LmsLesson lesson, LmsLessonInput input);
	}
}
