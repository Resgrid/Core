using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Commands;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Quidjibo.Models;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Console.Tasks;
using Resgrid.Workers.Framework;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Console.SystemOperations
{
	public sealed class SystemOperationOutcome
	{
		private SystemOperationOutcome(bool succeeded, string message)
		{
			Succeeded = succeeded;
			Message = message;
		}

		public bool Succeeded { get; }

		public string Message { get; }

		public static SystemOperationOutcome Ok(string message) => new SystemOperationOutcome(true, message);

		public static SystemOperationOutcome Fail(string message) => new SystemOperationOutcome(false, message);
	}

	/// <summary>
	/// The latest progress line of a running operation. Doubles as the IQuidjiboProgress handed to a job's handler; the
	/// heartbeat loop copies the line onto the request row, so the handler never writes to the database itself.
	/// </summary>
	public sealed class SystemOperationProgress : IQuidjiboProgress
	{
		private string _latest;

		public string Latest => Volatile.Read(ref _latest);

		public void Report(string message)
		{
			if (!string.IsNullOrWhiteSpace(message))
				Volatile.Write(ref _latest, message);
		}

		public void Report(int value, string text) => Report(text);

		void IProgress<Tracker>.Report(Tracker value) => Report(value?.Text);
	}

	/// <summary>
	/// Runs one <see cref="SystemOperationTypes"/> operation for worker 76. A scheduled job runs through the very handler
	/// its schedule runs (same command id), so an early run is the nightly run. The older handlers catch their own
	/// exceptions and only log them; the runner hands them a logger that remembers the first error so a run that failed
	/// is not reported as completed.
	/// </summary>
	public class SystemOperationRunner
	{
		private readonly ILogger _logger;
		private readonly Func<ILifetimeScope> _beginScope;

		public SystemOperationRunner(ILogger logger, Func<ILifetimeScope> beginScope = null)
		{
			_logger = logger;
			_beginScope = beginScope ?? (() => Bootstrapper.GetKernel().BeginLifetimeScope());
		}

		/// <summary>Every operation this worker build can run. A catalog entry missing here fails its requests instead of running them.</summary>
		public static IReadOnlyCollection<SystemOperationTypes> SupportedTypes { get; } = new HashSet<SystemOperationTypes>
		{
			SystemOperationTypes.RebuildSecurityMatrices,
			SystemOperationTypes.ClearDepartmentCaches,
			SystemOperationTypes.RefreshTtsStaticPrompts,
			SystemOperationTypes.PendingDepartmentDeletions,
			SystemOperationTypes.ReportingRollup,
			SystemOperationTypes.UnitTrackingLocationRetention,
			SystemOperationTypes.ChatRetention,
			SystemOperationTypes.BidExpiration,
			SystemOperationTypes.DeploymentFinanceReminder,
			SystemOperationTypes.ComplianceExpiry,
			SystemOperationTypes.RmsDueStateEvaluation,
			SystemOperationTypes.RmsRetentionAndPurge,
			SystemOperationTypes.PayDataReportingReadiness,
			SystemOperationTypes.ProtectedWorkflowSweep,
			SystemOperationTypes.Utf8Cleanup
		};

		public async Task<SystemOperationOutcome> RunAsync(SystemOperationRequest request, SystemOperationProgress progress, CancellationToken cancellationToken)
		{
			var type = (SystemOperationTypes)request.OperationType;

			if (!SupportedTypes.Contains(type) || SystemOperationCatalog.Get(type) == null)
				return SystemOperationOutcome.Fail($"This worker build cannot run operation {request.OperationType}; deploy the worker that added it.");

			// The catalog decides which operations may target one department; a row written around the service is refused here too.
			if (request.TargetDepartmentId.HasValue && !SystemOperationCatalog.Get(type).SupportsDepartmentScope)
				return SystemOperationOutcome.Fail($"{SystemOperationCatalog.Get(type).Name} cannot target one department.");

			switch (type)
			{
				case SystemOperationTypes.RebuildSecurityMatrices:
					return await RebuildSecurityMatricesAsync(request.TargetDepartmentId, progress, cancellationToken);

				case SystemOperationTypes.ClearDepartmentCaches:
					return await ClearDepartmentCachesAsync(request.TargetDepartmentId, progress, cancellationToken);

				case SystemOperationTypes.RefreshTtsStaticPrompts:
					if (string.IsNullOrWhiteSpace(TtsConfig.ServiceBaseUrl) || string.IsNullOrWhiteSpace(TtsConfig.StaticPromptAdminKey))
						return NotEnabled("the TTS service URL or static prompt admin key is not configured");
					return await RunJobAsync(logger => new TtsStaticPromptRefreshTask(logger), new TtsStaticPromptRefreshCommand(18), progress, cancellationToken);

				case SystemOperationTypes.PendingDepartmentDeletions:
					return await RunJobAsync(logger => new SystemSqlQueueTask(logger), new SystemSqlQueueCommand(14), progress, cancellationToken);

				case SystemOperationTypes.ReportingRollup:
					return await RunJobAsync(logger => new ReportingRollupTask(logger), new ReportingRollupCommand(21), progress, cancellationToken);

				case SystemOperationTypes.UnitTrackingLocationRetention:
					if (!UnitTrackingConfig.LocationRetentionWorkerEnabled)
						return NotEnabled("the location retention worker is turned off (UnitTrackingConfig.LocationRetentionWorkerEnabled)");
					return await RunJobAsync(logger => new UnitTrackingRetentionTask(logger), new UnitTrackingRetentionCommand(24), progress, cancellationToken);

				case SystemOperationTypes.ChatRetention:
					return await RunJobAsync(logger => new ChatRetentionTask(logger), new ChatRetentionCommand(25), progress, cancellationToken);

				case SystemOperationTypes.BidExpiration:
					return await RunJobAsync(_ => new BidExpirationTask(), new BidExpirationCommand(31), progress, cancellationToken);

				case SystemOperationTypes.DeploymentFinanceReminder:
					return await RunJobAsync(_ => new DeploymentFinanceReminderTask(), new DeploymentFinanceReminderCommand(32), progress, cancellationToken);

				case SystemOperationTypes.ComplianceExpiry:
					return await RunJobAsync(_ => new ComplianceExpiryTask(), new ComplianceExpiryCommand(33), progress, cancellationToken);

				case SystemOperationTypes.RmsDueStateEvaluation:
					return await RunJobAsync(logger => new RmsDueStateEvaluationTask(logger), new RmsDueStateEvaluationCommand(42), progress, cancellationToken);

				case SystemOperationTypes.RmsRetentionAndPurge:
					return await RunJobAsync(logger => new RmsRetentionAndPurgeTask(logger), new RmsRetentionAndPurgeCommand(43), progress, cancellationToken);

				case SystemOperationTypes.PayDataReportingReadiness:
					return await RunJobAsync(_ => new PayDataReportingReadinessTask(), new PayDataReportingReadinessCommand(49), progress, cancellationToken);

				case SystemOperationTypes.ProtectedWorkflowSweep:
					return await RunJobAsync(_ => new ProtectedWorkflowSweepTask(), new ProtectedWorkflowSweepCommand(71), progress, cancellationToken);

				case SystemOperationTypes.Utf8Cleanup:
					if (!SystemBehaviorConfig.Utf8CleanupEnabled)
						return NotEnabled("the UTF-8 cleanup is turned off (SystemBehaviorConfig.Utf8CleanupEnabled)");
					return await RunJobAsync(logger => new Utf8CleanupTask(logger), new Utf8CleanupCommand(22), progress, cancellationToken);

				default:
					return SystemOperationOutcome.Fail($"This worker build cannot run operation {request.OperationType}; deploy the worker that added it.");
			}
		}

		private async Task<SystemOperationOutcome> RebuildSecurityMatricesAsync(int? departmentId, SystemOperationProgress progress, CancellationToken cancellationToken)
		{
			var unavailable = CacheUnavailableReason();
			if (unavailable != null)
				return NotEnabled(unavailable);

			var logic = new SecurityLogic();

			if (departmentId.HasValue)
			{
				progress.Report($"Rebuilding the security matrices for department {departmentId.Value}.");
				var single = await logic.UpdateCachedSecurityForDepartment(departmentId.Value);

				return single.Item1
					? SystemOperationOutcome.Ok($"Rebuilt the four security matrices for department {departmentId.Value}.")
					: SystemOperationOutcome.Fail(single.Item2);
			}

			var total = 0;
			var result = await logic.UpdatedCachedSecurityForAllDepartments((done, count) =>
			{
				total = count;
				progress.Report($"Rebuilt {done} of {count} departments.");
				return Task.CompletedTask;
			}, cancellationToken);

			return result.Item1
				? SystemOperationOutcome.Ok($"Rebuilt the security matrices for all {total} departments.")
				: SystemOperationOutcome.Fail(result.Item2);
		}

		private async Task<SystemOperationOutcome> ClearDepartmentCachesAsync(int? departmentId, SystemOperationProgress progress, CancellationToken cancellationToken)
		{
			var unavailable = CacheUnavailableReason();
			if (unavailable != null)
				return NotEnabled(unavailable);

			using var scope = _beginScope();
			var systemOperations = scope.Resolve<ISystemOperationsService>();

			if (departmentId.HasValue)
			{
				var failedGroups = await systemOperations.ClearDepartmentCachesAsync(departmentId.Value);

				return failedGroups.Count == 0
					? SystemOperationOutcome.Ok($"Cleared the caches of department {departmentId.Value}; they reload from the database on next read.")
					: SystemOperationOutcome.Fail($"Department {departmentId.Value}: these cache groups failed: {string.Join(", ", failedGroups)}.");
			}

			var departments = await scope.Resolve<IDepartmentsService>().GetAllAsync() ?? new List<Department>();
			var failures = new List<string>();
			var done = 0;

			foreach (var department in departments)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var failedGroups = await systemOperations.ClearDepartmentCachesAsync(department.DepartmentId);
				if (failedGroups.Count > 0)
					failures.Add($"{department.DepartmentId} ({string.Join(", ", failedGroups)})");

				done++;
				progress.Report($"Cleared {done} of {departments.Count} departments.");
			}

			if (failures.Count == 0)
				return SystemOperationOutcome.Ok($"Cleared the caches of all {departments.Count} departments; they reload from the database on next read.");

			// Capped like the matrix rebuild: an outage would otherwise build a line per department.
			return SystemOperationOutcome.Fail($"{failures.Count} of {departments.Count} departments had cache groups fail: {string.Join("; ", failures.GetRange(0, Math.Min(10, failures.Count)))}");
		}

		/// <summary>Runs a scheduled job's own handler once, outside its schedule.</summary>
		private async Task<SystemOperationOutcome> RunJobAsync<TCommand>(Func<ILogger, IQuidjiboHandler<TCommand>> createHandler, TCommand command,
			SystemOperationProgress progress, CancellationToken cancellationToken) where TCommand : IQuidjiboCommand
		{
			var logger = new ErrorCapturingLogger(_logger);

			try
			{
				await createHandler(logger).ProcessAsync(command, progress, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, $"System operation run of {typeof(TCommand).Name} failed.");
				return SystemOperationOutcome.Fail(ex.Message);
			}

			if (logger.FirstError != null)
				return SystemOperationOutcome.Fail($"{logger.FirstError} (full error in the worker log)");

			return SystemOperationOutcome.Ok(progress.Latest ?? "Finished.");
		}

		/// <summary>Null when the cache can be written; otherwise why a cache operation would silently do nothing.</summary>
		private string CacheUnavailableReason()
		{
			if (!SystemBehaviorConfig.CacheEnabled)
				return "caching is turned off (SystemBehaviorConfig.CacheEnabled)";

			using var scope = _beginScope();
			if (!scope.Resolve<ICacheProvider>().IsConnected())
				return "Redis is not reachable from the worker, so nothing would be written";

			return null;
		}

		private static SystemOperationOutcome NotEnabled(string reason) =>
			SystemOperationOutcome.Fail($"Not run: {reason} in this worker's configuration.");
	}

	/// <summary>Passes everything through and remembers the first error-level line, trimmed to its first line.</summary>
	public sealed class ErrorCapturingLogger : ILogger
	{
		private const int MaxErrorLength = 500;

		private readonly ILogger _inner;
		private string _firstError;

		public ErrorCapturingLogger(ILogger inner)
		{
			_inner = inner;
		}

		public string FirstError => Volatile.Read(ref _firstError);

		public IDisposable BeginScope<TState>(TState state) where TState : notnull => _inner?.BeginScope(state);

		public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error || (_inner?.IsEnabled(logLevel) ?? false);

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
		{
			if (logLevel >= LogLevel.Error)
			{
				var message = formatter?.Invoke(state, exception);
				if (string.IsNullOrWhiteSpace(message))
					message = exception?.Message ?? "The job logged an error.";

				var firstLine = message.Split('\n')[0].Trim();
				if (firstLine.Length > MaxErrorLength)
					firstLine = firstLine.Substring(0, MaxErrorLength);

				Interlocked.CompareExchange(ref _firstError, firstLine, null);
			}

			_inner?.Log(logLevel, eventId, state, exception, formatter);
		}
	}
}
