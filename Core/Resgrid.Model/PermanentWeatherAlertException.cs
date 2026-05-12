using System;

namespace Resgrid.Model
{
	/// <summary>
	/// Thrown when a weather alert API request fails due to a permanent, non-retriable condition
	/// (e.g. invalid NWS zone code). Unlike <see cref="TransientWeatherAlertException"/>, callers
	/// should mark the source as permanently failed and stop retrying until the user corrects the configuration.
	/// </summary>
	public class PermanentWeatherAlertException : Exception
	{
		public PermanentWeatherAlertException(string message) : base(message) { }

		public PermanentWeatherAlertException(string message, Exception innerException)
			: base(message, innerException) { }
	}
}
