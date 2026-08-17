using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface ICommunicationTestService
	{
		Task<IEnumerable<CommunicationTest>> GetTestsByDepartmentIdAsync(int departmentId);
		Task<CommunicationTest> GetTestByIdAsync(Guid communicationTestId);
		Task<bool> CanCreateScheduledTestAsync(int departmentId, int scheduleType, Guid? excludeTestId = null);
		Task<CommunicationTest> SaveTestAsync(CommunicationTest test, CancellationToken cancellationToken = default);
		Task<bool> DeleteTestAsync(Guid communicationTestId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets the group/role/user targets scoping a test. An empty result means the test covers
		/// the whole department.
		/// </summary>
		Task<IEnumerable<CommunicationTestTarget>> GetTargetsByTestIdAsync(Guid communicationTestId);

		/// <summary>
		/// Replaces the whole target set for a test. An empty collection clears targeting.
		/// </summary>
		Task SaveTargetsAsync(Guid communicationTestId, int departmentId, IEnumerable<CommunicationTestTarget> targets, CancellationToken cancellationToken = default);

		/// <summary>
		/// Saves a test and replaces its target set in one transaction. Callers editing a test should
		/// use this rather than the two calls separately: replacing targets clears them before writing
		/// the new ones, so a failure between the halves would leave a targeted test covering the whole
		/// department. The targets' own CommunicationTestId is ignored — the saved test's id is stamped
		/// on, so a new test can have its targets built before it has an id.
		/// </summary>
		Task<CommunicationTest> SaveTestWithTargetsAsync(CommunicationTest test, int departmentId, IEnumerable<CommunicationTestTarget> targets, CancellationToken cancellationToken = default);

		/// <summary>
		/// Resolves the user ids a test covers, or <c>null</c> when the test is untargeted and
		/// therefore covers every member of the department.
		/// </summary>
		Task<HashSet<string>> ResolveTargetedUserIdsAsync(Guid communicationTestId, int departmentId);

		Task<bool> CanStartOnDemandRunAsync(Guid communicationTestId);

		/// <summary>
		/// Creates the run row in Pending, snapshots the audience it covers onto the run, and hands it
		/// to the worker over the bus. Returns as soon as the run is claimed — no result rows are
		/// written and nothing is sent on this thread.
		/// </summary>
		Task<CommunicationTestRun> StartTestRunAsync(Guid communicationTestId, int departmentId, string initiatedByUserId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Worker-side entry point for a queued run: builds the result rows, then delivers them.
		/// Idempotent, so a redelivered queue message is harmless.
		/// </summary>
		Task ProcessRunAsync(Guid communicationTestRunId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Writes one result row per recipient per enabled channel for the audience snapshotted onto
		/// the run when it was started, moving the run to Running. No-ops when the run already has
		/// results.
		/// </summary>
		Task<CommunicationTestRun> BuildRunResultsAsync(Guid communicationTestRunId, CancellationToken cancellationToken = default);

		Task<IEnumerable<CommunicationTestRun>> GetRunsByTestIdAsync(Guid communicationTestId);
		Task<CommunicationTestRun> GetRunByIdAsync(Guid communicationTestRunId);
		Task<IEnumerable<CommunicationTestRun>> GetRunsByDepartmentIdAsync(int departmentId);

		Task<IEnumerable<CommunicationTestResult>> GetResultsByRunIdAsync(Guid communicationTestRunId);

		/// <summary>
		/// Sends the messages for every not-yet-sent result on a run and moves the run to
		/// AwaitingResponses. Idempotent per result, so an interrupted run can be finished later.
		/// Honours the DoNotBroadcast kill switch: a blocked department's results are recorded as
		/// failed sends and no provider is called.
		/// </summary>
		Task<int> DeliverRunAsync(Guid communicationTestRunId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Finishes delivery for runs left mid-send by an interrupted process.
		/// </summary>
		Task DeliverPendingRunsAsync(CancellationToken cancellationToken = default);

		Task<bool> RecordSmsResponseAsync(string runCode, string fromPhoneNumber);
		Task<bool> RecordEmailResponseAsync(string responseToken);
		Task<bool> RecordVoiceResponseAsync(string responseToken);
		Task<bool> RecordPushResponseAsync(string responseToken);
		Task<int?> GetDepartmentIdByResponseTokenAsync(string responseToken);

		/// <summary>
		/// The preferred language (UserProfile.Language) of the person a response token belongs to.
		/// The voice webhook has only the token, so this is how a call knows what language to speak.
		/// Returns null when the token is unknown or the recipient has no language set.
		/// </summary>
		Task<string> GetRecipientLanguageByResponseTokenAsync(string responseToken);

		Task ProcessScheduledTestsAsync(CancellationToken cancellationToken = default);
		Task CompleteExpiredRunsAsync(CancellationToken cancellationToken = default);
	}
}
