using System;

namespace OpenPop.Pop3.Exceptions
{
	/// <summary>
	/// Thrown when the user mailbox is locked or in-use.<br/>
	/// </summary>
	/// <remarks>
	/// The mail boxes are locked when an existing session is open on the POP3 server.<br/>
	/// Only one POP3 client can use a POP3 account at a time.
	/// </remarks>
	public class PopServerLockedException : PopClientException
	{
		///<summary>
		/// Creates a PopServerLockedException with the given inner exception
		///</summary>
		///<param name="innerException">The exception that is the cause of this exception</param>
		public PopServerLockedException(PopServerException innerException)
			: base("The account is locked or in use", innerException)
		{ }
	}
}