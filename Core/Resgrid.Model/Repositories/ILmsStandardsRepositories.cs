using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Repositories
{
	// Training T5 standards (round 6C, M0392-M0394): content packages, runtime registrations and sessions, the cmi5 minimal LRS, LTI 1.3
	// tools, platform keys and AGS tokens, and the xAPI outbox.

	public interface ILmsContentPackagesRepository : IRepository<LmsContentPackage>
	{
		Task<LmsContentPackage> GetByIdForDepartmentAsync(int departmentId, int lmsContentPackageId);

		Task<List<LmsContentPackage>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false);

		/// <summary>
		/// Claims the oldest queued package (or one whose Processing claim is older than <paramref name="staleClaimBeforeUtc"/>) for worker 98:
		/// Processing, ClaimedOn now, Attempts + 1. Null when there is no work.
		/// </summary>
		Task<LmsContentPackage> ClaimNextAsync(DateTime utcNow, DateTime staleClaimBeforeUtc, CancellationToken cancellationToken = default);

		/// <summary>Moves a package from <paramref name="from"/> only (a compare-and-set on Status), writing its processing fields.</summary>
		Task<bool> TryMoveAsync(LmsContentPackage package, LmsContentPackageStatuses from, CancellationToken cancellationToken = default);
	}

	public interface ILmsRuntimeRegistrationsRepository : IRepository<LmsRuntimeRegistration>
	{
		Task<LmsRuntimeRegistration> GetByIdForDepartmentAsync(int departmentId, int lmsRuntimeRegistrationId);

		Task<LmsRuntimeRegistration> GetAsync(int departmentId, int lmsEnrollmentId, int lmsLessonId);

		/// <summary>The registration of an LTI lesson for the subject the tool knows the learner by (the newest when several enrollments ran).</summary>
		Task<LmsRuntimeRegistration> GetForLessonSubjectAsync(int departmentId, int lmsLessonId, string registrationKey);

		/// <summary>Inserts the registration unless the enrollment already has one for the lesson (unique index); returns the stored row.</summary>
		Task<(LmsRuntimeRegistration Registration, bool Inserted)> InsertOrGetAsync(LmsRuntimeRegistration registration, CancellationToken cancellationToken = default);

		/// <summary>Writes the registration only when its RowVersion is still <paramref name="expectedRowVersion"/>.</summary>
		Task<bool> TryUpdateAsync(LmsRuntimeRegistration registration, long expectedRowVersion, CancellationToken cancellationToken = default);
	}

	public interface ILmsRuntimeSessionsRepository : IRepository<LmsRuntimeSession>
	{
		Task<LmsRuntimeSession> GetByTokenHashAsync(string tokenHash);

		Task<LmsRuntimeSession> GetByFetchTokenHashAsync(string fetchTokenHash);

		Task<LmsRuntimeSession> GetByAuthTokenHashAsync(string authTokenHash);

		/// <summary>cmi5: records the fetch (one time only) and the auth token it handed out. False when it was fetched already.</summary>
		Task<bool> TryMarkFetchedAsync(int lmsRuntimeSessionId, string authTokenHash, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>LTI: consumes the launch (single use). False when it was used already.</summary>
		Task<bool> TryMarkUsedAsync(int lmsRuntimeSessionId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Ends a session (SCORM finish, cmi5 terminated); later writes are refused.</summary>
		Task MarkEndedAsync(int lmsRuntimeSessionId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Worker 98: removes sessions that expired before <paramref name="beforeUtc"/>. Returns how many.</summary>
		Task<int> DeleteExpiredAsync(DateTime beforeUtc, CancellationToken cancellationToken = default);

		/// <summary>Round 10 A8: the newest content session or LTI hint an in-app launch session minted (null when none).</summary>
		Task<LmsRuntimeSession> GetLatestForAppSessionAsync(int departmentId, int appSessionId);
	}

	public interface ILmsCmi5StatementsRepository : IRepository<LmsCmi5Statement>
	{
		/// <summary>Stores a statement unless the registration already holds one with its id. False for the duplicate.</summary>
		Task<bool> TryInsertAsync(LmsCmi5Statement statement, CancellationToken cancellationToken = default);

		Task<LmsCmi5Statement> GetByStatementIdAsync(int departmentId, int lmsRuntimeRegistrationId, string statementId);

		Task<int> CountForRegistrationAsync(int departmentId, int lmsRuntimeRegistrationId);

		/// <summary>The registration's statements, newest first (no more than <paramref name="take"/>).</summary>
		Task<List<LmsCmi5Statement>> GetForRegistrationAsync(int departmentId, int lmsRuntimeRegistrationId, int take);
	}

	public interface ILmsCmi5StateDocumentsRepository : IRepository<LmsCmi5StateDocument>
	{
		Task<LmsCmi5StateDocument> GetAsync(int departmentId, int lmsRuntimeRegistrationId, string activityId, string stateId);

		/// <summary>Removes one document, or every document of the activity when <paramref name="stateId"/> is null.</summary>
		Task<int> DeleteAsync(int departmentId, int lmsRuntimeRegistrationId, string activityId, string stateId, CancellationToken cancellationToken = default);
	}

	public interface ILmsLtiToolsRepository : IRepository<LmsLtiTool>
	{
		Task<List<LmsLtiTool>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false);

		Task<LmsLtiTool> GetByIdForDepartmentAsync(int departmentId, int lmsLtiToolId);

		Task<LmsLtiTool> GetByClientIdAsync(int departmentId, string clientId);
	}

	public interface ILmsLtiPlatformKeysRepository : IRepository<LmsLtiPlatformKey>
	{
		/// <summary>Every key of the department, newest first.</summary>
		Task<List<LmsLtiPlatformKey>> GetForDepartmentAsync(int departmentId);

		/// <summary>Retires every active key of the department except <paramref name="keepKeyId"/>.</summary>
		Task<int> RetireOthersAsync(int departmentId, int keepKeyId, DateTime utcNow, CancellationToken cancellationToken = default);
	}

	public interface ILmsLtiAccessTokensRepository : IRepository<LmsLtiAccessToken>
	{
		/// <summary>Stores the token unless the tool already used the assertion's jti (replay). False for the replay.</summary>
		Task<bool> TryInsertAsync(LmsLtiAccessToken token, CancellationToken cancellationToken = default);

		Task<LmsLtiAccessToken> GetByHashAsync(int departmentId, string tokenHash);

		/// <summary>Worker 98: removes tokens that expired before <paramref name="beforeUtc"/>.</summary>
		Task<int> DeleteExpiredAsync(DateTime beforeUtc, CancellationToken cancellationToken = default);
	}

	public interface ILmsXapiOutboxRepository : IRepository<LmsXapiOutboxItem>
	{
		/// <summary>
		/// Claims up to <paramref name="take"/> statements that are due (Pending and NextAttemptOn reached, or Sending with a claim older than
		/// <paramref name="staleClaimBeforeUtc"/>): Sending, ClaimedOn now, Attempts + 1. Oldest first.
		/// </summary>
		Task<List<LmsXapiOutboxItem>> ClaimDueAsync(DateTime utcNow, DateTime staleClaimBeforeUtc, int take, CancellationToken cancellationToken = default);

		/// <summary>Records a claimed statement's result (delivered, back to Pending with its next attempt, or Failed) while it is still Sending.</summary>
		Task<bool> TryFinishAsync(LmsXapiOutboxItem item, CancellationToken cancellationToken = default);

		Task<(int Pending, int Failed, int Delivered)> GetCountsAsync(int departmentId);

		Task<List<LmsXapiOutboxItem>> GetRecentFailedAsync(int departmentId, int take);

		/// <summary>Puts the department's failed statements back in the queue (attempts reset). Returns how many.</summary>
		Task<int> RequeueFailedAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Worker 98: removes statements delivered before <paramref name="beforeUtc"/>.</summary>
		Task<int> PurgeDeliveredAsync(DateTime beforeUtc, CancellationToken cancellationToken = default);
	}
}
