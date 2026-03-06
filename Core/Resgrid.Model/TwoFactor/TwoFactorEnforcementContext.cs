using System;

namespace Resgrid.Model.TwoFactor
{
	/// <summary>
	/// Input context for <see cref="TwoFactorEnforcementEvaluator.Evaluate"/>.
	/// All values are plain data — no ASP.NET or Identity dependencies.
	/// </summary>
	public sealed record TwoFactorEnforcementContext(
		/// <summary>Whether the user currently has 2FA enrolled and enabled.</summary>
		bool UserHas2FaEnabled,

		/// <summary>
		/// The department's configured enforcement scope.
		/// <list type="bullet">
		///   <item>0 — disabled, no department-driven enforcement.</item>
		///   <item>1 — department admins and the managing user.</item>
		///   <item>2 — department admins, the managing user, and group admins.</item>
		/// </list>
		/// </summary>
		int DepartmentScope,

		/// <summary>Whether the user is a department admin or the managing user.</summary>
		bool IsAdminOrManagingUser,

		/// <summary>Whether the user is a group admin (only relevant when <see cref="DepartmentScope"/> == 2).</summary>
		bool IsGroupAdmin,

		/// <summary>
		/// The UTC timestamp at which the user last completed a successful step-up 2FA verification,
		/// or <see langword="null"/> if no proof exists in the current session.
		/// </summary>
		DateTime? LastStepUpVerifiedAtUtc,

		/// <summary>
		/// The number of minutes a step-up proof remains valid.
		/// Sourced from <c>TwoFactorConfig.StepUpVerificationWindowMinutes</c>.
		/// </summary>
		int StepUpWindowMinutes);
}

