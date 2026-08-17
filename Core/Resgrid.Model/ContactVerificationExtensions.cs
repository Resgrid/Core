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

		/// <summary>
		/// The label shown to users for a verification state. <see cref="ContactVerificationStatus.Pending"/>
		/// reads as "Unverified" rather than "Pending": from an administrator's point of view the fact
		/// that matters is that the channel is not verified and will not be sent to, not that a code
		/// happens to be outstanding.
		/// </summary>
		public static string ToDisplayText(this ContactVerificationStatus status)
		{
			switch (status)
			{
				case ContactVerificationStatus.Verified:
					return "Verified";
				case ContactVerificationStatus.Pending:
					return "Unverified";
				case ContactVerificationStatus.Grandfathered:
					return "Grandfathered";
				default:
					return "Unknown";
			}
		}
	}
}

