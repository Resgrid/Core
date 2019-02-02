using System;
using System.Runtime.Serialization;
using System.Text;

namespace Resgrid.Model
{
	public class ResgridException : Exception
	{
		#region Fields
		private const string GENERIC_USER_FRIENDLY_MESSAGE
			= "An error occurred. Submit a ticket if this continues. (tickets@resgrid.uservoice.com)";
		private string _userFriendlyMessage = GENERIC_USER_FRIENDLY_MESSAGE;
		#endregion

		#region Properties
		public string UserFriendlyMessage
		{
			get
			{
				if (HasUserFriendlyMessage())
					return _userFriendlyMessage;
				else
					return GENERIC_USER_FRIENDLY_MESSAGE;
			}
			set
			{
				_userFriendlyMessage = value;
			}
		}
		#endregion

		#region Constructors

		#region Standard Exception Constructors
		public ResgridException()
		{
		}

		public ResgridException(string message)
			: base(message)
		{
		}

		public ResgridException(string message, Exception innerException)
			: base(message, innerException)
		{
		}

		public ResgridException(SerializationInfo serializationInfo, StreamingContext streamingContext)
			: base(serializationInfo, streamingContext)
		{
		}
		#endregion

		public ResgridException(string message, string userFriendlyMessage)
			: base(message)
		{
			UserFriendlyMessage = userFriendlyMessage;
		}

		public ResgridException(string message, Exception innerException, string userFriendlyMessage)
			: base(message, innerException)
		{
			UserFriendlyMessage = userFriendlyMessage;
		}

		#endregion

		#region Methods

		/// <summary>
		/// Checks to see if UserFriendlyMessage property has been set.
		/// </summary>
		/// <returns></returns>
		public bool HasUserFriendlyMessage()
		{
			if (string.IsNullOrEmpty(_userFriendlyMessage) ||
				_userFriendlyMessage == GENERIC_USER_FRIENDLY_MESSAGE)
				return false;

			return true;
		}

		/// <summary>
		/// Overrides Exception.ToString to include the UserFriendlyMessage in the output.
		/// </summary>
		/// <returns></returns>
		public override string ToString()
		{
			StringBuilder sb = new StringBuilder();

			sb.Append("UserFriendlyMessage: ");
			if (HasUserFriendlyMessage())
				sb.Append(UserFriendlyMessage);
			else
				sb.Append("N/A");
			sb.Append(Environment.NewLine);
			sb.Append(base.ToString());

			return sb.ToString();
		}

		/// <summary>
		/// If the exception is an ResgridException and it has a friendly message it returns that.
		/// otherwise it returns the generic exception.
		/// </summary>
		public static string GetUserFriendlyMessage()
		{
			return GetUserFriendlyMessage(null);
		}

		/// <summary>
		/// If the exception is an ResgridException and it has a friendly message it returns that.
		/// otherwise it returns the generic exception.
		/// </summary>
		public static string GetUserFriendlyMessage(Exception exception)
		{
			var resgridException = exception as ResgridException;
			if (resgridException == null)
				return new ResgridException().UserFriendlyMessage;
			else
				return resgridException.UserFriendlyMessage;
		}
		#endregion
	}
}