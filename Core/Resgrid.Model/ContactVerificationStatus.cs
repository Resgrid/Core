namespace Resgrid.Model
{
	/// <summary>
	/// Represents the verification state of a contact method on a <see cref="UserProfile"/>.
	/// The underlying storage uses a nullable bool: null → Grandfathered, false → Pending, true → Verified.
	/// </summary>
	public enum ContactVerificationStatus
	{
		/// <summary>
		/// The contact method existed before verification was introduced.
		/// The system treats it as allowed for sending (same as Verified) but the user
		/// is encouraged to verify via a soft UI prompt.
		/// Maps to <c>null</c>.
		/// </summary>
		Grandfathered = 0,

		/// <summary>
		/// A verification code has been issued (or the contact method was set by an admin)
		/// but the user has not yet confirmed it. The system will NOT send to this channel.
		/// Maps to <c>false</c>.
		/// </summary>
		Pending = 1,

		/// <summary>
		/// The user has confirmed the verification code. The system may send to this channel.
		/// Maps to <c>true</c>.
		/// </summary>
		Verified = 2
	}
}

