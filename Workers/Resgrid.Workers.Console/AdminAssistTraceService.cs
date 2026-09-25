using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Resgrid.Model.AdminAssist;
using Resgrid.Workers.Framework;

namespace Resgrid.Workers.Console
{
	/// <summary>Protected durable handoff and idempotent storage consume their own budgets, never a dispatch budget.</summary>
	public sealed class AdminAssistTraceService(DatabaseUpgradeState database, ILogger<AdminAssistTraceService> logger) : BackgroundService
	{
		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			await database.WaitForCompletionAsync(stoppingToken);
			await Task.WhenAll(PublishAsync(stoppingToken), DrainAsync(stoppingToken));
		}
		private async Task PublishAsync(CancellationToken stoppingToken)
		{
			var nextWarning = DateTime.MinValue;
			long lastDropped = 0;
			await foreach (var observation in DispatchTraceTelemetry.Reader.ReadAllAsync(stoppingToken))
			{
				bool saved = false;
				for (int attempt = 0; attempt < 3 && !saved; attempt++)
				{
					try
					{
						using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
						using var budget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
						budget.CancelAfter(TimeSpan.FromSeconds(5));
						var envelope = await scope.Resolve<IAdminAssistTraceWriter>().PrepareAsync(observation, budget.Token);
						if (envelope == null) { saved = true; break; } // Explicitly disabled; no durable capture and no retry warning.
						await scope.Resolve<IAdminAssistTraceQueue>().EnqueueAsync(envelope, budget.Token);
						saved = true;
					}
					catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
					catch (Exception) { if (attempt < 2) await Task.Delay(TimeSpan.FromSeconds(attempt + 1), stoppingToken); }
				}
				var dropped = DispatchTraceTelemetry.Dropped;
				if ((!saved || dropped > lastDropped) && DateTime.UtcNow >= nextWarning)
				{
					logger.LogWarning("Admin Assist dispatch trace evidence is incomplete. Durable trace handoff failure={Failed}; dropped observations={Dropped}. Dispatch delivery proceeds independently.", !saved, dropped);
					nextWarning = DateTime.UtcNow.AddMinutes(1);
				}
				lastDropped = dropped;
			}
		}
		private async Task DrainAsync(CancellationToken stoppingToken)
		{
			var nextWarning = DateTime.MinValue;
			while (!stoppingToken.IsCancellationRequested)
			{
				if (!Config.AdminAssistConfig.CaptureDispatchTraces && !Config.AdminAssistConfig.DrainDispatchTraceQueue)
				{
					await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
					continue;
				}
				try
				{
					using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
					using var budget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
					budget.CancelAfter(TimeSpan.FromSeconds(10));
					var writer = scope.Resolve<IAdminAssistTraceWriter>();
					var result = await scope.Resolve<IAdminAssistTraceQueue>().ProcessNextAsync(writer.PersistPreparedAsync, budget.Token);
					if (result == DispatchTraceReceiveResult.Empty) await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
				catch (Exception)
				{
					if (DateTime.UtcNow >= nextWarning)
					{
						logger.LogWarning("Admin Assist durable trace processing is unavailable. Unacknowledged envelopes remain queued. Check trace queue health, protection and database availability; do not resend calls.");
						nextWarning = DateTime.UtcNow.AddMinutes(1);
					}
					await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
				}
			}
		}
	}
}
