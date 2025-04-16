using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly.Timeout;
using Resgrid.Console.Helpers;
using Resgrid.Console.Models;

namespace Resgrid.Console.Services;

/// <summary>
///     Represents a hosted service in an ASP.NET Core application or a .NET Core worker.
///     A hosted service is a service that runs long-running background tasks, which typically run over the lifetime of
///     your application.
///     Original Implementation: https://github.com/egarcia74/GenericHostConsoleApp/blob/main/GenericHostConsoleApp/Services/ApplicationHostedService.cs
/// </summary>
public sealed class ApplicationHostedService : IHostedService, IDisposable
{
    // Service dependencies.
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly ILogger<ApplicationHostedService> _logger;

    // Commands
    private ICommandService _resetPasswordCommand;
    private ICommandService _addHostsCommand;
    private ICommandService _clearCacheCommand;
    private ICommandService _dbUpdateCommand;
    private ICommandService _genOidcCertsCommand;
    private ICommandService _migrateDocsDbCommand;
    private ICommandService _oidcUpdateCommand;
    private ICommandService _securityRefreshCommand;
    private ICommandService _helpCommand;

    // Cancellation token source used to submit a cancellation request.
    private CancellationTokenSource? _cancellationTokenSource;

    // Task that executes the main service.
    private Task<ExitCode>? _mainTask;

    /// <summary>
    ///     Initialises the <see cref="ApplicationHostedService" /> using the specified dependencies.
    /// </summary>
    /// <param name="hostApplicationLifetime">Notifies of application lifetime events.</param>
    /// <param name="logger">The logger to use within this service.</param>
    /// <param name="mainService">The main service.</param>
    public ApplicationHostedService(
        IHostApplicationLifetime hostApplicationLifetime,
        ILogger<ApplicationHostedService> logger,
        [FromKeyedServices("ResetPasswordCommand")] ICommandService resetPasswordCommand,
        [FromKeyedServices("AddHostsCommand")] ICommandService addHostsCommand,
        [FromKeyedServices("ClearCacheCommand")] ICommandService clearCacheCommand,
        [FromKeyedServices("DbUpdateCommand")] ICommandService dbUpdateCommand,
        [FromKeyedServices("GenOidcCertsCommand")] ICommandService genOidcCertsCommand,
        [FromKeyedServices("MigrateDocsDbCommand")] ICommandService migrateDocsDbCommand,
        [FromKeyedServices("OidcUpdateCommand")] ICommandService oidcUpdateCommand,
        [FromKeyedServices("SecurityRefreshCommand")] ICommandService securityRefreshCommand,
        [FromKeyedServices("HelpCommand")] ICommandService helpCommand)
    {
        _hostApplicationLifetime = hostApplicationLifetime;
        _logger = logger;
        _resetPasswordCommand = resetPasswordCommand;
        _addHostsCommand = addHostsCommand;
        _clearCacheCommand = clearCacheCommand;
        _dbUpdateCommand = dbUpdateCommand;
        _genOidcCertsCommand = genOidcCertsCommand;
        _migrateDocsDbCommand = migrateDocsDbCommand;
        _oidcUpdateCommand = oidcUpdateCommand;
        _securityRefreshCommand = securityRefreshCommand;
        _helpCommand = helpCommand;
    }

    /// <summary>
    ///     Disposes the resources.
    /// </summary>
    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }

    /// <summary>
    ///     Triggered when the application host is ready to start the service.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the start process has been aborted.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogApplicationStarting();

        // Initialise the cancellation token source so that it is in the cancelled state if the
        // supplied token is in the cancelled state.
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Bail if the start process has been aborted.
        if (_cancellationTokenSource.IsCancellationRequested)
        {
            _logger.LogWarning("StartAsync was aborted due to cancellation");
            return Task.CompletedTask;
        }

        //
        // Register callbacks to handle the application lifetime events.
        //

        RegisterEventHandler(_hostApplicationLifetime, () =>
        {
            var args = Environment.GetCommandLineArgs();

            _logger.LogApplicationStarted(args);

            // Execute the main service but do not await it here. The task will be awaited in StopAsync().
            _mainTask = ExecuteMainAsync(args, _cancellationTokenSource.Token);
        }, ApplicationLifetimeEvent.ApplicationStarted);

        RegisterEventHandler(
            _hostApplicationLifetime,
            _logger.LogApplicationStopping,
            ApplicationLifetimeEvent.ApplicationStopping);

        RegisterEventHandler(
            _hostApplicationLifetime,
            _logger.LogApplicationStopped,
            ApplicationLifetimeEvent.ApplicationStopped);

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Triggered when the application host is performing a graceful shutdown.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the shutdown process should no longer be graceful.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ExitCode exitCode;

        if (_mainTask is not null)
        {
            // If the main service is still running or the passed in cancellation token is in the cancelled state
            // then request a cancellation.
            if (!_mainTask.IsCompleted || cancellationToken.IsCancellationRequested)
                await _cancellationTokenSource!
                    .CancelAsync()
                    .ConfigureAwait(false);

            // Wait for the main service to fully complete any cleanup tasks.
            // Note that this relies on the cancellation token to be properly used in the application.
            exitCode = await _mainTask.ConfigureAwait(false);
        }
        else
        {
            // The main service task never started.
            exitCode = ExitCode.Aborted;
        }

        Environment.ExitCode = (int)exitCode;

        _logger.LogApplicationExiting(exitCode, (int)exitCode);
    }

    /// <summary>
    ///     Executes the main service of the application asynchronously.
    /// </summary>
    /// <param name="args">Command-line arguments passed to the main service.</param>
    /// <param name="cancellationToken">A token to signal the operation to cancel.</param>
    /// <returns>
    ///     A <see cref="Task" /> representing the result of the asynchronous operation.
    ///     The result contains an <see cref="ExitCode" /> indicating the exit status of the application.
    /// </returns>
    /// <remarks>
    ///     This method calls the <c>Main</c> method of the main service defined by <c>_mainService</c>,
    ///     passing in command-line arguments and cancellation token.
    ///     In case of an exception, the exception is passed to <c>HandleException</c> method
    ///     to handle it and return an appropriate exit code.
    ///     Regardless of success or failure, the application is then stopped by calling <c>StopApplication</c>
    ///     method of <c>_hostApplicationLifetime</c>.
    /// </remarks>
    private async Task<ExitCode> ExecuteMainAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
	        if (args.Contains("--ResetPassword"))
				return await _resetPasswordCommand.ExecuteMainAsync(args, cancellationToken).ConfigureAwait(false);
	        else if (args.Contains("--AddHosts"))
		        return await _addHostsCommand.ExecuteMainAsync(args, cancellationToken).ConfigureAwait(false);
	        else if (args.Contains("--ClearCache"))
		        return await _clearCacheCommand.ExecuteMainAsync(args, cancellationToken).ConfigureAwait(false);
	        else if (args.Contains("--DbUpdate") || args.Contains("--UpdateDb"))
		        return await _dbUpdateCommand.ExecuteMainAsync(args, cancellationToken).ConfigureAwait(false);
	        else if (args.Contains("--GenOidcCerts"))
		        return await _genOidcCertsCommand.ExecuteMainAsync(args, cancellationToken).ConfigureAwait(false);
	        else if (args.Contains("--MigrateDocsDb"))
		        return await _migrateDocsDbCommand.ExecuteMainAsync(args, cancellationToken).ConfigureAwait(false);
	        else if (args.Contains("--OidcUpdate"))
		        return await _oidcUpdateCommand.ExecuteMainAsync(args, cancellationToken).ConfigureAwait(false);
	        else if (args.Contains("--SecurityRefresh"))
		        return await _securityRefreshCommand.ExecuteMainAsync(args, cancellationToken).ConfigureAwait(false);
	        else
	        {
		        return await _helpCommand.ExecuteMainAsync(args, cancellationToken).ConfigureAwait(false);
	        }
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
        finally
        {
            // Stop the application once the work is done.
            _hostApplicationLifetime.StopApplication();
        }
    }

    /// <summary>
    ///     Handles the given exception, logs it accordingly, and determines the appropriate exit code.
    /// </summary>
    /// <param name="ex">The exception that was thrown</param>
    /// <returns>The corresponding exit code</returns>
    private ExitCode HandleException(Exception ex)
    {
        switch (ex)
        {
            case TaskCanceledException:
            case OperationCanceledException:
            case TimeoutRejectedException:
                _logger.LogApplicationCanceled();
                return ExitCode.Cancelled;

            case AggregateException aggregateException:
                aggregateException.Handle(exception =>
                {
                    _logger.LogAggregateException(exception);
                    return true;
                });
                return ExitCode.Failed;

            default:
                _logger.LogUnhandledException(ex);
                return ExitCode.Failed;
        }
    }

    /// <summary>
    ///     Registers an event handler for a specified application lifetime event.
    /// </summary>
    /// <param name="hostApplicationLifetime">The <see cref="IHostApplicationLifetime" /> instance.</param>
    /// <param name="action">The action to be executed when the event is triggered.</param>
    /// <param name="lifetimeEvent">
    ///     The name of the application lifetime event ("ApplicationStarted", "ApplicationStopping", or
    ///     "ApplicationStopped").
    /// </param>
    private static void RegisterEventHandler(
        IHostApplicationLifetime hostApplicationLifetime,
        Action action,
        ApplicationLifetimeEvent lifetimeEvent)
    {
        ArgumentNullException.ThrowIfNull(hostApplicationLifetime);

        ArgumentNullException.ThrowIfNull(action);

        switch (lifetimeEvent)
        {
            case ApplicationLifetimeEvent.ApplicationStarted:
                hostApplicationLifetime.ApplicationStarted.Register(action);
                break;
            case ApplicationLifetimeEvent.ApplicationStopping:
                hostApplicationLifetime.ApplicationStopping.Register(action);
                break;
            case ApplicationLifetimeEvent.ApplicationStopped:
                hostApplicationLifetime.ApplicationStopped.Register(action);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(lifetimeEvent), lifetimeEvent, "Unknown event name.");
        }
    }
}
