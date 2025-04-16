using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Console.Models;

/// <summary>
///     Main application service interface.
/// </summary>
public interface ICommandService
{
	/// <summary>
	///     Executes the main functionality of the application.
	/// </summary>
	/// <param name="args">An array of command-line arguments passed to the application.</param>
	/// <param name="cancellationToken">A token that can be used to signal the operation should be canceled.</param>
	/// <returns>Returns an <see cref="ExitCode" /> indicating the result of the execution.</returns>
	// ReSharper disable once UnusedParameter.Global
	Task<ExitCode> ExecuteMainAsync(string[] args, CancellationToken cancellationToken);
}
