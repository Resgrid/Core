using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The shared MFA policy decisions (passkey plan section 7.6): whether a department requires MFA on a path, and which
	/// second factors a user may use there. Lookup failures are thrown, never read as "not required"; callers refuse.
	/// </summary>
	public interface IMfaPolicyService
	{
		/// <summary>
		/// <c>DepartmentSecurityPolicy.RequireMfa</c> on the paths newly covered by this delivery (Web password login, the
		/// API password grant, Web department entry): the department's flag, and only once the rollout gate is on.
		/// </summary>
		Task<bool> IsRequireMfaEnforcedAsync(int? departmentId, CancellationToken cancellationToken = default);

		/// <summary>The department's <c>RequireMfa</c> flag regardless of the rollout gate (the API SSO exchange has always enforced it).</summary>
		Task<bool> DepartmentRequiresMfaAsync(int? departmentId, CancellationToken cancellationToken = default);

		/// <summary>The user's second-factor choice in a department for a scope (plan section 7.5 rule 5).</summary>
		Task<MfaMethodChoice> GetMethodChoiceAsync(string userId, bool totpEnrolled, int? departmentId, MfaMethodScope scope, bool passkeyEnrolled = false,
			bool federatedEnrolled = false, bool approvalEnrolled = false, CancellationToken cancellationToken = default);

		/// <summary>
		/// Whether evidence verified with <paramref name="method"/> is accepted for <paramref name="scope"/> in the department
		/// now (plan sections 7.6 and 10.1). A method the department has since switched off no longer counts, so the user
		/// verifies again with an accepted one. TOTP is always accepted, without a policy read.
		/// </summary>
		Task<bool> IsMethodAcceptedAsync(int? departmentId, MfaMethodScope scope, MfaEvidenceMethod method,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Whether <paramref name="evidence"/> counts for <paramref name="scope"/> in the department now: its method is
		/// accepted, and evidence from unlocking a shared session serves protected data only where the department's
		/// <c>AcceptRecentUnlockMfaForAdp</c> is on (plan section 12.5.3).
		/// </summary>
		Task<bool> IsEvidenceAcceptedAsync(int? departmentId, MfaMethodScope scope, MfaEvidence evidence,
			CancellationToken cancellationToken = default);
	}
}
