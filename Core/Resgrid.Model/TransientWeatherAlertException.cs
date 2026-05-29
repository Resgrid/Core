using System;

namespace Resgrid.Model
{
	/// <summary>
	/// Thrown when a weather alert API request fails due to a transient condition
	/// (rate-limit 429, server error 5xx). Callers can distinguish this from permanent
	/// <see cref="HttpRequestException"/> to avoid flagging the source as permanently failed.
	/// </summary>
	public class TransientWeatherAlertException : Exception
	{
		public TransientWeatherAlertException(string message) : base(message) { }

		public TransientWeatherAlertException(string message, Exception innerException)
			: base(message, innerException) { }
	}
}
