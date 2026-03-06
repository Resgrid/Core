using System;

namespace Resgrid.Model.TwoFactor
{
	/// <summary>
	/// Pure, stateless evaluator that decides what 2FA enforcement action is required for a user.
	/// All inputs are plain values so the method is trivially unit-testable with no mocks.
	/// </summary>
	/// <remarks>
	/// Decision table:
	/// <list type="table">
	///   <listheader>
	///     <term>Scope</term><term>User has 2FA</term><term>In scope</term><term>Step-up valid</term><term>Outcome</term>
	///   </listheader>
	///   <item><term>any</term><term>no</term><term>no</term><term>—</term><term>NotRequired</term></item>
	///   <item><term>1 or 2</term><term>no</term><term>yes</term><term>—</term><term>EnrollmentRequired</term></item>
	///   <item><term>any</term><term>yes</term><term>—</term><term>yes</term><term>NotRequired (pass through)</term></item>
	///   <item><term>any</term><term>yes</term><term>—</term><term>no / expired</term><term>StepUpRequired</term></item>
	/// </list>
	/// </remarks>
	public static class TwoFactorEnforcementEvaluator
	{
		/// <summary>
		/// Evaluates the enforcement decision for the supplied context.
		/// </summary>
		/// <param name="context">All inputs required to make the decision.</param>
		/// <param name="nowUtc">
		/// The current UTC time used to test step-up window expiry.
		/// Pass <see cref="DateTime.UtcNow"/> in production; inject a fixed value in tests.
		/// </param>
		public static TwoFactorEnforcementDecision Evaluate(TwoFactorEnforcementContext context, DateTime nowUtc)
		{
			bool departmentCoversUser = IsCoveredByDepartmentScope(context);

			// User has no 2FA and is not covered by the department mandate — nothing to enforce
			if (!context.UserHas2FaEnabled && !departmentCoversUser)
				return new TwoFactorEnforcementDecision(TwoFactorEnforcementOutcome.NotRequired);

			// User is covered by the department mandate but has not yet enrolled
			if (!context.UserHas2FaEnabled && departmentCoversUser)
				return new TwoFactorEnforcementDecision(TwoFactorEnforcementOutcome.EnrollmentRequired);

			// User has 2FA enrolled — check whether the step-up proof is still valid
			if (context.LastStepUpVerifiedAtUtc.HasValue)
			{
				var windowExpiry = context.LastStepUpVerifiedAtUtc.Value.AddMinutes(context.StepUpWindowMinutes);
				if (nowUtc <= windowExpiry)
					return new TwoFactorEnforcementDecision(TwoFactorEnforcementOutcome.NotRequired);
			}

			return new TwoFactorEnforcementDecision(TwoFactorEnforcementOutcome.StepUpRequired);
		}

		// ── private helpers ──────────────────────────────────────────────────────────

		/// <summary>
		/// Returns <see langword="true"/> when the department scope mandates 2FA for this user.
		/// </summary>
		private static bool IsCoveredByDepartmentScope(TwoFactorEnforcementContext context) =>
			context.DepartmentScope switch
			{
				// Scope 1: department admins + managing user
				1 => context.IsAdminOrManagingUser,

				// Scope 2: department admins + managing user + group admins
				2 => context.IsAdminOrManagingUser || context.IsGroupAdmin,

				// Scope 0 or any unrecognised value: no department-driven enforcement
				_ => false,
			};
	}
}

