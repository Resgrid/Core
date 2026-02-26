namespace Resgrid.Model
{
	/// <summary>
	/// Identifies which contact method type is being verified.
	/// </summary>
	public enum ContactVerificationType
	{
		/// <summary>Email address verification.</summary>
		Email = 0,

		/// <summary>Mobile / SMS phone number verification.</summary>
		MobileNumber = 1,

		/// <summary>Home phone number verification.</summary>
		HomeNumber = 2
	}
}

