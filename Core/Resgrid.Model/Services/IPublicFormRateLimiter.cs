using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>The signed-out account forms whose use is capped per network address (sign-in has its own throttle).</summary>
	public enum PublicFormLimit
	{
		/// <summary>Posts of the registration form, valid or not.</summary>
		Register = 1,
		/// <summary>Accounts and departments the registration form actually created.</summary>
		RegisterCreated = 2,
		/// <summary>Posts that use an emailed password reset link: starting the reset, and choosing the new password.</summary>
		PasswordReset = 3,
		/// <summary>Posts of the lost-authenticator recovery: the recovery code, and the new authenticator's code.</summary>
		FactorRecovery = 4,
		/// <summary>Posts of the invitation form, and lookups of an invitation code that does not exist.</summary>
		Invite = 5
	}

	/// <summary>
	/// Per-address caps on the signed-out account forms, over the shared cache so every instance counts together. A form's counter is
	/// a fixed window from its first use; once an address reaches the cap the form refuses it until the window passes. The forgot-password
	/// request keeps its own per-address and per-account limits (<see cref="IPasswordRecoveryService"/>). Never blocks when the cache
	/// cannot answer.
	/// </summary>
	public interface IPublicFormRateLimiter
	{
		/// <summary>How long an address stays refused once it reaches the form's cap, for the message the user is shown.</summary>
		TimeSpan WindowOf(PublicFormLimit form);

		/// <summary>Counts this use of the form; false (refuse it) when the address is already at the form's cap.</summary>
		Task<bool> TryAcquireAsync(PublicFormLimit form, string ipAddress, CancellationToken cancellationToken = default);

		/// <summary>Whether the address is at the form's cap, without counting anything.</summary>
		Task<bool> IsLimitedAsync(PublicFormLimit form, string ipAddress, CancellationToken cancellationToken = default);

		/// <summary>Counts one use of the form (an outcome such as an account created, or a lookup that found nothing).</summary>
		Task RecordAsync(PublicFormLimit form, string ipAddress, CancellationToken cancellationToken = default);
	}
}
