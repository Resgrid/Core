using System;
using Microsoft.Extensions.Logging;
using Resgrid.Console.Models;

namespace Resgrid.Console.Helpers;

// <summary>
///     Contains logger messages used throughout the application.
/// </summary>
public static partial class LoggerExtensions
{
    private const string UnhandledExceptionMessage = "An unhandled exception has occurred";
    private const string AppCanceledMessage = "Application was canceled";
    private const string AppStartingMessage = "Application is starting";
    private const string AppStartedMessage = "Application has started with args: \"{Args}\"";
    private const string AppStoppingMessage = "Application is stopping";
    private const string AppStoppedMessage = "Application has stopped";
    private const string AppExitingMessage = "Application is exiting with exit code: {ExitCode} ({ExitCodeAsInt})";
    private const string AggregateExceptionMessage = "An aggregate exception has occurred";

    /// <summary>
    ///     Logs the details of an unhandled exception.
    /// </summary>
    /// <param name="logger">The <see cref="ILogger{TCategoryName}" /> to use.</param>
    /// <param name="exception">The unhandled exception.</param>
    [LoggerMessage(1, LogLevel.Critical, UnhandledExceptionMessage)]
    public static partial void LogUnhandledException(this ILogger logger, Exception exception);

    /// <summary>
    ///     Logs a message indicating that the application was cancelled.
    /// </summary>
    /// <param name="logger">The <see cref="ILogger{TCategoryName}" /> to use.</param>
    [LoggerMessage(2, LogLevel.Information, AppCanceledMessage)]
    public static partial void LogApplicationCanceled(this ILogger logger);

    /// <summary>
    ///     Logs a message indicating that the application is starting.
    /// </summary>
    /// <param name="logger">The <see cref="ILogger{TCategoryName}" /> to use.</param>
    [LoggerMessage(3, LogLevel.Debug, AppStartingMessage)]
    public static partial void LogApplicationStarting(this ILogger logger);

    /// <summary>
    ///     Logs a message indicating that the application has started.
    /// </summary>
    /// <param name="logger">The <see cref="ILogger{TCategoryName}" /> to use.</param>
    /// <param name="args">The command line arguments.</param>
    [LoggerMessage(4, LogLevel.Debug, AppStartedMessage)]
    public static partial void LogApplicationStarted(this ILogger logger, string[] args);

    /// <summary>
    ///     Logs a message indicating that the application is stopping.
    /// </summary>
    /// <param name="logger">The <see cref="ILogger{TCategoryName}" /> to use.</param>
    [LoggerMessage(5, LogLevel.Debug, AppStoppingMessage)]
    public static partial void LogApplicationStopping(this ILogger logger);

    /// <summary>
    ///     Logs a message indicating that the application has stopped.
    /// </summary>
    /// <param name="logger">The <see cref="ILogger{TCategoryName}" /> to use.</param>
    [LoggerMessage(6, LogLevel.Debug, AppStoppedMessage)]
    public static partial void LogApplicationStopped(this ILogger logger);

    /// <summary>
    ///     Logs a message indicating that the application is exiting.
    /// </summary>
    /// <param name="logger">The <see cref="ILogger{TCategoryName}" /> to use.</param>
    /// <param name="exitCode">The exit code as an enum.</param>
    /// <param name="exitCodeAsInt">The exit code as an integer.</param>
    [LoggerMessage(7, LogLevel.Debug, AppExitingMessage)]
    public static partial void LogApplicationExiting(this ILogger logger, ExitCode exitCode, int exitCodeAsInt);

    /// <summary>
    ///     Logs a message indicating that an aggregate exception has occurred.
    /// </summary>
    /// <param name="logger">The <see cref="ILogger{TCategoryName}" /> to use.</param>
    /// <param name="exception">The aggregate exception.</param>
    [LoggerMessage(8, LogLevel.Critical, AggregateExceptionMessage)]
    public static partial void LogAggregateException(this ILogger logger, Exception exception);
}
