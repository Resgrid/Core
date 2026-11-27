using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The department's one step-up policy (owner decision 2026-11-26): per area whether a step-up is required and which kinds of proof
	/// count (a second factor always; SSO re-sign-in and the password where the department allows them), and whether this session's
	/// server-side evidence satisfies it. Reads fail closed to today's behaviour. Nothing here trusts the client.
	/// </summary>
	public interface IStepUpPolicyService
	{
		/// <summary>Every area's effective rule for the department.</summary>
		Task<IReadOnlyList<StepUpRule>> GetRulesAsync(int departmentId, CancellationToken cancellationToken = default);

		/// <summary>One area's effective rule.</summary>
		Task<StepUpRule> GetRuleAsync(int departmentId, StepUpArea area, CancellationToken cancellationToken = default);

		/// <summary>
		/// Saves the department's choices. The read-and-sign "required" switch is the Protocols setting and is written there; protected
		/// data's "required" stays the ADP exemptions' and is ignored here, and changing how protected data may be verified needs the
		/// managing member and advances the ADP policy epoch (grants issued under the old rule stop working). Audited.
		/// </summary>
		Task<StepUpPolicySaveOutcome> SaveAsync(int departmentId, IReadOnlyList<StepUpRuleInput> rules, string userId, bool isManagingMember,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Whether this session's evidence satisfies the area now: a second factor the department accepts for the area's MFA scope, or a
		/// password / SSO reauthentication where the area allows it, verified within the area's window, for this account generation,
		/// and (when the evidence names a department) in this department.
		/// </summary>
		Task<StepUpEvaluation> EvaluateAsync(StepUpSubject subject, StepUpArea area, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// What a member can use to complete a step-up, and who could not complete one (the settings warning). Read-only and advisory: every
	/// verification rechecks.
	/// </summary>
	public interface IStepUpCapabilityService
	{
		/// <summary>The member's kinds of proof for this client and department, gathered server-side.</summary>
		Task<StepUpCapabilities> GetCapabilitiesAsync(int departmentId, string userId, UserSessionClientApplication client, StepUpArea area,
			CancellationToken cancellationToken = default);

		/// <summary>The area's rule and what this member can use for it: the body of every <c>step_up_required</c> refusal.</summary>
		Task<StepUpRequirement> DescribeAsync(int departmentId, string userId, UserSessionClientApplication client, StepUpArea area,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Per area, the current members (hidden included; deleted and disabled skipped) who could not complete its step-up as the policy
		/// stands: no second factor and no other allowed kind of proof they have. Capped at <paramref name="maxListed"/> names; the count
		/// is exact.
		/// </summary>
		Task<IReadOnlyList<StepUpReadiness>> GetReadinessAsync(int departmentId, int maxListed = 50, CancellationToken cancellationToken = default);

		/// <summary>As <see cref="GetReadinessAsync"/> for a proposed rule set, before it is saved.</summary>
		Task<IReadOnlyList<StepUpReadiness>> GetReadinessAsync(int departmentId, IReadOnlyList<StepUpRule> rules, int maxListed = 50,
			CancellationToken cancellationToken = default);
	}
}
