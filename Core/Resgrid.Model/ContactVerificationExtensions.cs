namespace Resgrid.Model
{
	/// <summary>
	/// Extension methods for interpreting the nullable-bool tri-state used on
	/// <see cref="UserProfile"/> contact verification fields.
	/// </summary>
	public static class ContactVerificationExtensions
	{
		/// <summary>
		/// Converts the nullable-bool storage value to the human-readable
		/// <see cref="ContactVerificationStatus"/> enum.
		/// </summary>
		public static ContactVerificationStatus ToVerificationStatus(this bool? value)
		{
			if (value is null)
				return ContactVerificationStatus.Grandfathered;

			return value.Value ? ContactVerificationStatus.Verified : ContactVerificationStatus.Pending;
		}

		/// <summary>
		/// Returns <c>true</c> when the system is allowed to send to this contact method.
		/// Grandfathered (<c>null</c>) and Verified (<c>true</c>) are both allowed.
		/// Pending (<c>false</c>) is blocked.
		/// </summary>
		public static bool IsContactMethodAllowedForSending(this bool? verified)
			=> verified != false;
	}
}

