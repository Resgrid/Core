namespace OpenPop.Pop3.Exceptions
{
	/// <summary>
	/// This exception indicates that the user has logged in recently and
	/// will not be allowed to login again until the login delay period has expired.
	/// Check the parameter to the LOGIN-DELAY capability, that the server responds with when
	/// <see cref="Pop3Client.Capabilities()"/> is called, to see what the delay is.
	/// </summary>
	public class LoginDelayException : PopClientException
	{
		///<summary>
		/// Creates a LoginDelayException with the given inner exception
		///</summary>
		///<param name="innerException">The exception that is the cause of this exception</param>
		public LoginDelayException(PopServerException innerException)
			: base("Login denied because of recent connection to this maildrop. Increase time between connections.", innerException)
		{ }
	}
}