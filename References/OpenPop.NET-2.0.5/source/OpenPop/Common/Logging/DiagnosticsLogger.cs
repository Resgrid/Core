using System;

namespace OpenPop.Common.Logging
{
	/// <summary>
	/// This logging object writes application error and debug output using the
	/// <see cref="System.Diagnostics.Trace"/> facilities.
	/// </summary>
	public class DiagnosticsLogger : ILog
	{
		/// <summary>
		/// Logs an error message to the System Trace facility
		/// </summary>
		/// <param name="message">This is the error message to log</param>
		public void LogError(string message)
		{
			if(message == null)
				throw new ArgumentNullException("message");

			System.Diagnostics.Trace.WriteLine("OpenPOP: " + message);
		}

		/// <summary>
		/// Logs a debug message to the system Trace Facility
		/// </summary>
		/// <param name="message">This is the debug message to log</param>
		public void LogDebug(string message)
		{
			if (message == null)
				throw new ArgumentNullException("message");

			System.Diagnostics.Trace.WriteLine("OpenPOP: (DEBUG) " + message);
		}
	}
}