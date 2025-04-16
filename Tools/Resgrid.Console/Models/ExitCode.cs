namespace Resgrid.Console.Models;

/// <summary>
///     Indicates that the application failed to complete successfully.
/// </summary>
public enum ExitCode
{
	/// <summary>
	///     Indicates that the application succeeded.
	/// </summary>
	Success = 0,

	/// <summary>
	///     Indicates that the application was cancelled.
	/// </summary>
	Cancelled,

	/// <summary>
	///     Indicates that the application was aborted.
	/// </summary>
	Aborted,

	/// <summary>
	///     Indicates that the application failed to complete successfully.
	/// </summary>
	Failed
}
