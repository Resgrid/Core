namespace OpenPop.Pop3.Exceptions
{
	/// <summary>
	/// Thrown when the <see cref="Pop3Client"/> is being used in an invalid way.<br/>
	/// This could for example happen if a someone tries to fetch a message without authenticating.
	/// </summary>
	public class InvalidUseException : PopClientException
	{
		///<summary>
		/// Creates a InvalidUseException with the given message
		///</summary>
		///<param name="message">The message to include in the exception</param>
		public InvalidUseException(string message)
			: base(message)
		{ }
	}
}