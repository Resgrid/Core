using System;

namespace OpenPop.Pop3.Exceptions
{
	/// <summary>
	/// Thrown when the supplied username or password is not accepted by the POP3 server.
	/// </summary>
	public class InvalidLoginException : PopClientException
	{
		///<summary>
		/// Creates a InvalidLoginException with the given message and InnerException
		///</summary>
		///<param name="innerException">The exception that is the cause of this exception</param>
		public InvalidLoginException(Exception innerException)
			: base("Server did not accept user credentials", innerException)
		{ }
	}
}