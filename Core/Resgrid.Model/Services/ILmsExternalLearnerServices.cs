using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Services
{
	// Training T5 item 2 (round 6E): external learners, the branded learner portal and paid public classes (LMS add-on, dark behind
	// Training.Lms). Refusals are service codes ("lms_xl_*", "lms_class_*") the pages show as text. Nothing here is ever called from a sweep.

	/// <summary>
	/// External learners (extended enterprise, §13 T5 item 2): a learner-only identity per department, invited by e-mail or self-registered
	/// for a public class, with its own password and e-mail verification; never a member, never an AspNetUsers account. Fair use (§17.4,
	/// decision 10): included up to 25% of purchased seats (minimum 25), beyond that they count toward packs like members; refused with the
	/// reason when no seat is left, and suspended while the add-on is not working (lapse rule 7).
	/// </summary>
	public interface ILmsExternalLearnerService
	{
		Task<LmsLearnerPortal> GetPortalAsync(int departmentId);
		Task<LmsLearnerPortal> GetPortalByKeyAsync(string portalKey);
		Task<LmsLearnerPortal> SavePortalAsync(int departmentId, LmsLearnerPortalInput input, string actingUserId, CancellationToken cancellationToken = default);
		/// <summary>A new portal key: every old link (and the sessions on it) stops working.</summary>
		Task<LmsLearnerPortal> RegeneratePortalKeyAsync(int departmentId, string actingUserId, CancellationToken cancellationToken = default);
		/// <summary>The department logo and name and the accent colour, each falling back to Resgrid's.</summary>
		Task<LmsLearnerPortalBranding> GetBrandingAsync(LmsLearnerPortal portal);
		/// <summary>The portal answers learners now: switched on, the Trainings module and the operator flag on, and the add-on working (lapse rule 7).</summary>
		Task<bool> IsPortalOpenAsync(LmsLearnerPortal portal);
		/// <summary>The portal URL (absolute) for a department's key.</summary>
		string PortalUrl(LmsLearnerPortal portal, string path = null);

		Task<LmsFairUse> GetFairUseAsync(int departmentId);

		Task<List<LmsExternalLearner>> GetLearnersAsync(int departmentId);
		Task<LmsExternalLearner> GetLearnerAsync(int departmentId, int lmsExternalLearnerId);
		Task<LmsExternalLearnerResult> InviteAsync(int departmentId, LmsExternalLearnerInvite invite, string actingUserId, CancellationToken cancellationToken = default);
		Task<LmsExternalLearnerResult> ResendInviteAsync(int departmentId, int lmsExternalLearnerId, string actingUserId, CancellationToken cancellationToken = default);
		Task<LmsExternalLearnerResult> UpdateAsync(int departmentId, int lmsExternalLearnerId, string firstName, string lastName, string organization, string sponsorContactId,
			string billingContactId, string actingUserId, CancellationToken cancellationToken = default);
		/// <summary>Active (reactivate: fair use applies), Suspended or Removed (open enrolments withdrawn); every portal session ends.</summary>
		Task<LmsExternalLearnerResult> SetStatusAsync(int departmentId, int lmsExternalLearnerId, LmsExternalLearnerStatuses status, string actingUserId,
			CancellationToken cancellationToken = default);
		Task<LmsExternalLearnerResult> EnrollInCourseAsync(int departmentId, int lmsExternalLearnerId, int lmsCourseId, string actingUserId, CancellationToken cancellationToken = default);
		Task<LmsExternalLearnerResult> EnrollInClassAsync(int departmentId, int lmsExternalLearnerId, int lmsClassSessionId, string actingUserId, CancellationToken cancellationToken = default);
		Task<List<LmsExternalCourseAccess>> GetCourseAccessAsync(int departmentId);
		Task<LmsExternalCourseAccess> SetCourseAccessAsync(int departmentId, int lmsCourseId, LmsExternalCourseAccessKinds access, string actingUserId,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Self-registration (portal setting): a pending learner and a verification e-mail; an address already registered gets a sign-in reminder
		/// instead (no account enumeration). A registration still pending is never changed (security review M1): its address gets the
		/// verification link again and the answer is the same as for a new address.
		/// </summary>
		Task<LmsExternalLearnerResult> RegisterAsync(int departmentId, LmsLearnerRegistration registration, CancellationToken cancellationToken = default);
		/// <summary>
		/// The verification link's Confirm POST: whoever opened the e-mailed link chooses the password and confirms the name (security review
		/// follow-up: the password typed at registration is discarded, so an address someone else registered first can never be signed in to
		/// with their password); the stamp is rotated, the learner becomes active (fair use applies) and is registered for the class they chose.
		/// </summary>
		Task<LmsExternalLearnerResult> VerifyEmailAsync(int departmentId, string token, string password, string firstName, string lastName,
			CancellationToken cancellationToken = default);
		/// <summary>
		/// Security review M2 (2026-11-25): whether a verification link is still good, read only (the link's page shows a Confirm button; the
		/// activation is <see cref="VerifyEmailAsync"/> on the POST). The learner when it is.
		/// </summary>
		Task<LmsExternalLearnerResult> GetVerificationAsync(int departmentId, string token);
		/// <summary>Whether an invitation link is still good (not used, not expired); the learner when it is.</summary>
		Task<LmsExternalLearnerResult> GetInviteAsync(int departmentId, string token);
		/// <summary>Accepts an invitation: the learner chooses a password (the e-mailed link proves the address) and becomes active.</summary>
		Task<LmsExternalLearnerResult> AcceptInviteAsync(int departmentId, string token, string password, CancellationToken cancellationToken = default);
		/// <summary>Password sign-in with a per-learner lockout (5 failures, 15 minutes); the caller applies the per-address throttle.</summary>
		Task<LmsExternalLearnerResult> SignInAsync(int departmentId, string email, string password, CancellationToken cancellationToken = default);
		/// <summary>Sends a reset link when the address belongs to a learner who can sign in; silent otherwise.</summary>
		Task BeginPasswordResetAsync(int departmentId, string email, CancellationToken cancellationToken = default);
		Task<LmsExternalLearnerResult> ResetPasswordAsync(int departmentId, string token, string password, CancellationToken cancellationToken = default);
		/// <summary>The signed-in learner when the session is still good (active, same security stamp); null otherwise.</summary>
		Task<LmsExternalLearner> ValidateSessionAsync(int departmentId, int lmsExternalLearnerId, string securityStamp);
		/// <summary>Security review L2 (2026-11-25): sign-out gives the learner a new security stamp, so every copy of the portal cookie stops working.</summary>
		Task SignOutAsync(int departmentId, int lmsExternalLearnerId, CancellationToken cancellationToken = default);

		Task<LmsLearnerCatalog> GetCatalogAsync(int departmentId, LmsExternalLearner learner, DateTime utcNow);
		Task<LmsLearnerMyLearning> GetMyLearningAsync(int departmentId, LmsExternalLearner learner, DateTime utcNow);
		Task<LmsExternalLearnerResult> RegisterForClassAsync(int departmentId, LmsExternalLearner learner, int lmsClassSessionId, CancellationToken cancellationToken = default);
		Task<LmsExternalLearnerResult> WithdrawFromClassAsync(int departmentId, LmsExternalLearner learner, int lmsClassSessionId, CancellationToken cancellationToken = default);
		Task<LmsExternalLearnerResult> EnrollSelfInCourseAsync(int departmentId, LmsExternalLearner learner, int lmsCourseId, CancellationToken cancellationToken = default);
		/// <summary>The learner's own certificate of a verified learning record, or null.</summary>
		Task<CertificateDocument> GetCertificateAsync(int departmentId, LmsExternalLearner learner, int learningRecordId);

		/// <summary>GDPR export: the learner's account, enrolments, classes, fees and learning records as JSON.</summary>
		Task<string> ExportAsync(int departmentId, int lmsExternalLearnerId);
		/// <summary>GDPR erasure: personal fields cleared, sessions ended, open places withdrawn, course data and learning records deleted, roster snapshots cleared.</summary>
		Task<LmsExternalLearnerResult> EraseAsync(int departmentId, int lmsExternalLearnerId, string actingUserId, CancellationToken cancellationToken = default);

		// ---- Round 7B: e-mail preferences and access e-mail resend ----------------------------------------------------------------

		/// <summary>The learner's own e-mail language (a supported locale, or null for the department's) and class reminders on or off.</summary>
		Task<LmsExternalLearnerResult> SetPreferencesAsync(int departmentId, LmsExternalLearner learner, string language, bool remindersOptOut,
			CancellationToken cancellationToken = default);

		/// <summary>The learner a reminder unsubscribe link names (no sign-in; the key is the only proof), or null.</summary>
		Task<LmsExternalLearner> GetByUnsubscribeKeyAsync(int departmentId, string unsubscribeKey);

		/// <summary>Turns class reminders off for the learner the link names (idempotent); transactional e-mails still go out.</summary>
		Task<LmsExternalLearnerResult> UnsubscribeAsync(int departmentId, string unsubscribeKey, CancellationToken cancellationToken = default);

		/// <summary>
		/// The anonymous "resend my e-mail" form: a pending learner gets a new verification link, an invited one the invitation again, an active
		/// one a note to sign in or reset the password. The page says the same thing whatever the address (no account enumeration).
		/// </summary>
		Task ResendAccessEmailAsync(int departmentId, string email, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Paid public classes (R-C13, §25.4a item 12): a class session listed on the portal with an optional fee per external learner; the roster
	/// always exports (learner, organization, attendance, fee); where Business Operations invoicing is active, one invoice per external learner
	/// or per sponsoring organization is raised with that add-on's customers, rate items and invoices. The LMS never takes payment.
	/// </summary>
	/// <summary>
	/// Round 7B: e-mails to external learners about their class places (enrolment confirmed, waitlisted, promoted, reminder, changed,
	/// cancelled, signed off, completion recorded): department-branded, in the learner's language (else the department's, else English),
	/// claimed before sending so each goes once, never to a learner who is not active or has no address, and reminders only while the
	/// learner has not unsubscribed. Members are ignored (they get their own notices). Never throws; returns how many were sent.
	/// </summary>
	public interface ILmsLearnerClassMailer
	{
		Task<int> SendAsync(int departmentId, LmsClassSession session, IEnumerable<string> learnerUserIds, LmsLearnerNoticeKinds kind,
			CancellationToken cancellationToken = default);
	}

	public interface ILmsPublicClassService
	{
		Task<LmsPublicClass> GetPublicClassAsync(int departmentId, int lmsClassSessionId);
		Task<LmsPublicClass> SavePublicClassAsync(int departmentId, int lmsClassSessionId, bool isPublic, decimal? feeAmount, string currency, string rateCardItemId,
			string actingUserId, CancellationToken cancellationToken = default);
		/// <summary>The fee row of an external learner's roster place (created at the class's fee when missing); null for a free class or a member.</summary>
		Task<LmsClassFee> EnsureFeeAsync(LmsClassEnrollment enrollment, CancellationToken cancellationToken = default);
		Task<LmsClassFee> SetFeeAsync(int departmentId, int lmsClassEnrollmentId, bool waived, string sponsorContactId, string actingUserId, CancellationToken cancellationToken = default);
		Task<LmsClassBillingView> GetBillingAsync(int departmentId, int lmsClassSessionId);
		/// <summary>The roster export as CSV (always available).</summary>
		Task<string> ExportRosterCsvAsync(int departmentId, int lmsClassSessionId, Func<string, string> label);
		/// <summary>Business Operations customers (billing profiles) to pick a sponsor or a learner's customer from; empty without invoicing.</summary>
		Task<List<Resgrid.Model.Invoicing.CustomerBillingProfile>> GetCustomersAsync(int departmentId);
		/// <summary>Business Operations rate items (active, of active rate cards) a public class fee can carry; empty without invoicing.</summary>
		Task<List<Resgrid.Model.Invoicing.RateCardItem>> GetRateItemsAsync(int departmentId);
		/// <summary>Raises draft invoices (lms_xl_invoicing_unavailable without Business Operations invoicing); each fee is invoiced once.</summary>
		Task<LmsClassInvoiceRun> RaiseInvoicesAsync(int departmentId, int lmsClassSessionId, LmsClassInvoiceModes mode, string actingUserId, string ipAddress, string userAgent,
			CancellationToken cancellationToken = default);
	}
}
