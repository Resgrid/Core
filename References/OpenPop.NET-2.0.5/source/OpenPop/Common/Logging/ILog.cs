namespace OpenPop.Common.Logging
{
	/// <summary>
	/// Defines a logger for managing system logging output  
	/// </summary>
	public interface ILog
	{
		/// <summary>
		/// Logs an error message to the logs
		/// </summary>
		/// <param name="message">This is the error message to log</param>
		void LogError(string message);

		/// <summary>
		/// Logs a debug message to the logs
		/// </summary>
		/// <param name="message">This is the debug message to log</param>
		void LogDebug(string message);
	}
}