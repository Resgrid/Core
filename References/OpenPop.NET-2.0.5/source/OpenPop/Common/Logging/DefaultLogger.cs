using System;

namespace OpenPop.Common.Logging
{
	/// <summary>
	/// This is the log that all logging will go trough.
	/// </summary>
	public static class DefaultLogger
	{
		/// <summary>
		/// This is the logger used by all logging methods in the assembly.<br/>
		/// You can override this if you want, to move logging to one of your own
		/// logging implementations.<br/>
		/// <br/>
		/// By default a <see cref="DiagnosticsLogger"/> is used.
		/// </summary>
		public static ILog Log { get; private set; }

		static DefaultLogger()
		{
			Log = new DiagnosticsLogger();
		}

		/// <summary>
		/// Changes the default logging to log to a new logger
		/// </summary>
		/// <param name="newLogger">The new logger to use to send log messages to</param>
		/// <exception cref="ArgumentNullException">
		/// Never set this to <see langword="null"/>.<br/>
		/// Instead you should implement a NullLogger which just does nothing.
		/// </exception>
		public static void SetLog(ILog newLogger)
		{
			if(newLogger == null)
				throw new ArgumentNullException("newLogger");
			Log = newLogger;
		}
	}
}