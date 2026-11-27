using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Repositories
{
	// Training T5 item 2 (round 6E, M0396–M0397): external learners, the learner portal, public classes and their fees.

	public interface ILmsLearnerPortalsRepository : IRepository<LmsLearnerPortal>
	{
		Task<LmsLearnerPortal> GetByDepartmentAsync(int departmentId);
		Task<LmsLearnerPortal> GetByKeyAsync(string portalKey);
	}

	public interface ILmsExternalLearnersRepository : IRepository<LmsExternalLearner>
	{
		Task<LmsExternalLearner> GetByIdForDepartmentAsync(int departmentId, int lmsExternalLearnerId);
		Task<LmsExternalLearner> GetByLearnerUserIdAsync(int departmentId, string learnerUserId);
		Task<LmsExternalLearner> GetByEmailAsync(int departmentId, string email);
		Task<List<LmsExternalLearner>> GetForDepartmentAsync(int departmentId);
		Task<List<LmsExternalLearner>> GetByLearnerUserIdsAsync(int departmentId, IEnumerable<string> learnerUserIds);
		/// <summary>Round 7B: the learner a reminder unsubscribe link names (the key is compared exactly).</summary>
		Task<LmsExternalLearner> GetByUnsubscribeKeyAsync(int departmentId, string unsubscribeKey);
		/// <summary>Invited and active learners: the fair-use count (§17.4).</summary>
		Task<int> CountCountedAsync(int departmentId);
		/// <summary>
		/// Erasure (GDPR): deletes the learner's course progress, attempts, uploaded lesson files, course enrolments, path assignments, crew
		/// viewing attendance and learning records (with their files), and clears the name, job title and note snapshots on class rosters,
		/// in one transaction. Returns the rows touched.
		/// </summary>
		Task<int> PurgeLearnerDataAsync(int departmentId, string learnerUserId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Security review follow-up (M0412): writes the row only while its stored RowVersion is still <paramref name="expectedRowVersion"/>
		/// (then bumps it). False when another save got there first: the caller re-reads and re-applies its change. UpdateAsync and
		/// SaveOrUpdateAsync of an existing learner go through this too and throw <see cref="System.Data.DBConcurrencyException"/> when stale.
		/// </summary>
		Task<bool> TryUpdateAsync(LmsExternalLearner learner, long expectedRowVersion, CancellationToken cancellationToken = default);

		/// <summary>Sets the reminder unsubscribe key when the row has none (bumping RowVersion); returns the key stored now.</summary>
		Task<string> EnsureUnsubscribeKeyAsync(int departmentId, int lmsExternalLearnerId, string newKey, CancellationToken cancellationToken = default);

		/// <summary>Links the learner's own Business Operations customer (bumping RowVersion); touches no other column.</summary>
		Task SetBillingContactAsync(int departmentId, int lmsExternalLearnerId, string billingContactId, DateTime utcNow, CancellationToken cancellationToken = default);
	}

	public interface ILmsExternalLearnerTokensRepository : IRepository<LmsExternalLearnerToken>
	{
		Task<LmsExternalLearnerToken> GetByHashAsync(string tokenHash);
		/// <summary>Marks the token used when it is unused and unexpired; false when another request used it first or it expired.</summary>
		Task<bool> ClaimAsync(int lmsExternalLearnerTokenId, DateTime utcNow, CancellationToken cancellationToken = default);
		Task<int> DeleteForLearnerAsync(int departmentId, int lmsExternalLearnerId, CancellationToken cancellationToken = default);
	}

	public interface ILmsPublicClassesRepository : IRepository<LmsPublicClass>
	{
		Task<LmsPublicClass> GetBySessionAsync(int departmentId, int lmsClassSessionId);
		Task<List<LmsPublicClass>> GetForDepartmentAsync(int departmentId);
	}

	public interface ILmsClassFeesRepository : IRepository<LmsClassFee>
	{
		Task<List<LmsClassFee>> GetForSessionAsync(int departmentId, int lmsClassSessionId);
		Task<LmsClassFee> GetByEnrollmentAsync(int departmentId, int lmsClassEnrollmentId);
		Task<List<LmsClassFee>> GetForLearnerAsync(int departmentId, string learnerUserId);
		/// <summary>Claims the uninvoiced rows for an invoice run (InvoiceId = the claim marker); returns how many were claimed.</summary>
		Task<int> ClaimAsync(int departmentId, IEnumerable<int> lmsClassFeeIds, string claim, CancellationToken cancellationToken = default);
		/// <summary>Turns the claimed rows into invoiced ones.</summary>
		Task<int> CompleteClaimAsync(int departmentId, IEnumerable<int> lmsClassFeeIds, string claim, string invoiceId, DateTime utcNow, CancellationToken cancellationToken = default);
		/// <summary>Gives claimed rows back (the invoice could not be made).</summary>
		Task<int> ReleaseClaimAsync(int departmentId, string claim, CancellationToken cancellationToken = default);
	}

	public interface ILmsExternalCourseAccessRepository : IRepository<LmsExternalCourseAccess>
	{
		Task<List<LmsExternalCourseAccess>> GetForDepartmentAsync(int departmentId);
		Task<LmsExternalCourseAccess> GetByCourseAsync(int departmentId, int lmsCourseId);
	}

	/// <summary>Round 7B (M0402): the once-only claims of learner class e-mails.</summary>
	public interface ILmsLearnerNoticesRepository : IRepository<LmsLearnerNotice>
	{
		/// <summary>Inserts the claim; false when the same learner, session, kind and key was claimed before (the unique index decides).</summary>
		Task<bool> TryClaimAsync(LmsLearnerNotice notice, CancellationToken cancellationToken = default);
		/// <summary>Records the outcome of a claimed notice: sent, or why it was skipped.</summary>
		Task<bool> MarkAsync(int departmentId, int lmsLearnerNoticeId, DateTime? sentOn, string skipped, CancellationToken cancellationToken = default);
		Task<List<LmsLearnerNotice>> GetForLearnerAsync(int departmentId, int lmsExternalLearnerId);
		Task<int> DeleteForLearnerAsync(int departmentId, int lmsExternalLearnerId, CancellationToken cancellationToken = default);
	}
}
