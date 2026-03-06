namespace Resgrid.Model.TwoFactor
{
	/// <summary>
	/// Represents the outcome of evaluating whether a 2FA step-up should be enforced for a
	/// given user against a department's configured scope.
	/// </summary>
	public enum TwoFactorEnforcementOutcome
	{
		/// <summary>
		/// The user is not subject to 2FA enforcement — pass through.
		/// Applies when the user has no 2FA enrolled and the department scope does not cover them.
		/// </summary>
		NotRequired,

		/// <summary>
		/// The user is in scope (department mandate or voluntary enrollment) and has 2FA enabled.
		/// A valid step-up proof must be presented.
		/// </summary>
		StepUpRequired,

		/// <summary>
		/// The department scope covers this user but they have not yet enrolled in 2FA.
		/// Redirect them to the enrollment page.
		/// </summary>
		EnrollmentRequired,
	}

	/// <summary>
	/// The result of <see cref="TwoFactorEnforcementEvaluator.Evaluate"/>.
	/// </summary>
	public sealed record TwoFactorEnforcementDecision(TwoFactorEnforcementOutcome Outcome);
}

