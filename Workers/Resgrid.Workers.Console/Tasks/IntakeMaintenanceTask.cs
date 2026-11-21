using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;

namespace Resgrid.Workers.Console.Tasks
{
	/// <summary>
	/// Worker 80, every 15 minutes: deletes IntakeMessages older than IntakeConfig.MessageRetentionDays and voice intake sessions
	/// (transcripts) older than IntakeConfig.VoiceSessionRetentionHours, then runs the dedicated number lifecycle (lapse numbers whose
	/// add-on ended, release lapsed numbers past their grace period), then closes idle intake SMS conversations and those whose call
	/// ended. Each step has its own scope, so one failing never stops the others.
	/// </summary>
	public class IntakeMaintenanceTask : IQuidjiboHandler<IntakeMaintenanceCommand>
	{
		private static readonly SemaphoreSlim SingleFlight = new SemaphoreSlim(1, 1);

		public string Name => "Intake Maintenance";
		public int Priority => 1;

		private readonly ILogger _logger;

		public IntakeMaintenanceTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(IntakeMaintenanceCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			if (!await SingleFlight.WaitAsync(0, cancellationToken))
				return;

			try
			{
				await RunOnceAsync(() => Bootstrapper.GetKernel().BeginLifetimeScope(), _logger, DateTime.UtcNow, cancellationToken);
			}
			finally
			{
				SingleFlight.Release();
			}
		}

		/// <summary>One maintenance pass; <paramref name="beginScope"/> opens a fresh lifetime scope (unit of work) per step.</summary>
		public static async Task RunOnceAsync(Func<ILifetimeScope> beginScope, ILogger logger, DateTime nowUtc, CancellationToken cancellationToken)
		{
			try
			{
				using var scope = beginScope();
				var purged = await scope.Resolve<IIntakeService>().PurgeExpiredAsync(nowUtc, cancellationToken);
				if (purged.Messages > 0 || purged.VoiceSessions > 0)
					logger?.LogInformation("IntakeMaintenance::Purged {Messages} intake message(s) and {Sessions} voice session(s)", purged.Messages, purged.VoiceSessions);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
			{
				Resgrid.Framework.Logging.LogException(ex, "Intake maintenance purge");
			}

			try
			{
				using var scope = beginScope();
				var changed = await scope.Resolve<IDepartmentPhoneNumbersService>().ProcessLifecycleAsync(nowUtc, cancellationToken);
				if (changed > 0)
					logger?.LogInformation("IntakeMaintenance::Dedicated number lifecycle changed {Count} number(s)", changed);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
			{
				Resgrid.Framework.Logging.LogException(ex, "Dedicated number lifecycle");
			}

			try
			{
				// Intake SMS conversations (plan §8 D15): close threads idle past IntakeConfig.ConversationIdleCloseMinutes and those whose
				// call was closed, cancelled or deleted (the pending call itself stays as it is).
				using var scope = beginScope();
				var closed = await scope.Resolve<IIntakeConversationService>().CloseIdleAsync(nowUtc, cancellationToken);
				if (closed > 0)
					logger?.LogInformation("IntakeMaintenance::Closed {Count} intake conversation(s)", closed);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
			{
				Resgrid.Framework.Logging.LogException(ex, "Intake conversation idle close");
			}
		}
	}
}
