using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Service responsible for generating, sending, and confirming contact-method
	/// verification codes (email, mobile SMS, home SMS). Enforces configurable
	/// expiry, daily attempt caps, and hourly send rate limits.
	/// </summary>
	public interface IContactVerificationService
	{
		/// <summary>
		/// Generates a verification code and sends it to the user's email address.
		/// Returns <c>false</c> if the user has no email, if rate limits are exceeded,
		/// or if the send fails.
		/// </summary>
		Task<bool> SendEmailVerificationCodeAsync(string userId, int departmentId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Generates a verification code and sends it via SMS to the user's mobile number.
		/// Returns <c>false</c> if the user has no mobile number, if rate limits are exceeded,
		/// or if the send fails.
		/// </summary>
		Task<bool> SendMobileVerificationCodeAsync(string userId, int departmentId, string departmentNumber, CancellationToken cancellationToken = default);

		/// <summary>
		/// Generates a verification code and sends it via SMS to the user's home number.
		/// Returns <c>false</c> if the user has no home number, if rate limits are exceeded,
		/// or if the send fails.
		/// </summary>
		Task<bool> SendHomeVerificationCodeAsync(string userId, int departmentId, string departmentNumber, CancellationToken cancellationToken = default);

		/// <summary>
		/// Validates the supplied <paramref name="code"/> against the stored code for the given
		/// contact type. On success, marks the contact method as verified. On failure, increments
		/// the attempt counter. Enforces the daily attempt cap.
		/// </summary>
		/// <returns><c>true</c> if the code matched and the contact method was marked verified.</returns>
		Task<bool> ConfirmVerificationCodeAsync(string userId, int departmentId, ContactVerificationType type, string code, string ipAddress = null, CancellationToken cancellationToken = default);

		/// <summary>
		/// Compares the <paramref name="existingProfile"/> against the <paramref name="updatedProfile"/>
		/// and, for any contact field that has changed value, resets the verification state to
		/// <c>false</c> (pending) and clears the stored code and expiry.
		/// </summary>
		Task ResetVerificationForChangedContactAsync(UserProfile existingProfile, UserProfile updatedProfile, CancellationToken cancellationToken = default);
	}
}

