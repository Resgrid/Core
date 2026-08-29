using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Hosting;
using Resgrid.Framework;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Web.Broker.Services
{
	/// <summary>
	/// Hosts the ADP migration coordinator on the broker — the only process whose engine can
	/// actually move data, because only the broker resolves a real IKeyWrappingProvider (plan
	/// sections 2.2 and 19; deviation from 19.1's separate deployable recorded in the coordinator).
	/// Workers.Console keeps its scheduled sweep for lock liveness and offboarding flips, but its
	/// engine reports unavailable there, so nights run exclusively here. Disable with
	/// DataProtectionConfig.BrokerRunsMigrations=false when ops later splits a dedicated migration
	/// deployable off the broker.
	/// </summary>
	public class AdpMigrationSweepService : BackgroundService
	{
		private readonly ILifetimeScope _rootScope;

		public AdpMigrationSweepService(ILifetimeScope rootScope)
		{
			_rootScope = rootScope;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			if (!Config.DataProtectionConfig.BrokerRunsMigrations)
			{
				Logging.LogInfo("ADP migration sweep is disabled on this broker (DataProtectionConfig.BrokerRunsMigrations=false).");
				return;
			}

			var interval = TimeSpan.FromSeconds(Math.Max(60, Config.DataProtectionConfig.BrokerMigrationSweepSeconds));

			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					// A fresh scope per sweep: the coordinator's per-lifetime-scope dependencies
					// (repositories, services, the engine with the real KMS provider) live and die
					// with the sweep.
					using var scope = _rootScope.BeginLifetimeScope();
					var logic = new AdpMigrationLogic(
						scope.Resolve<IDepartmentLockService>(),
						scope.Resolve<IDepartmentDataProtectionPolicyRepository>(),
						scope.Resolve<IDepartmentDataProtectionService>(),
						scope.Resolve<IDepartmentKeyService>(),
						scope.Resolve<IDepartmentDataMigrationEngine>(),
						scope.Resolve<IProtectedFieldCatalog>(),
						scope.Resolve<IDepartmentsService>(),
						scope.Resolve<IEmailService>(),
						scope.Resolve<IMemberProfileRelocationService>());

					var result = await logic.Process(stoppingToken);
					Logging.LogInfo($"ADP broker migration sweep: {result.Item2}");
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					break;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "ADP broker migration sweep failed; next sweep continues on schedule.");
				}

				try
				{
					await Task.Delay(interval, stoppingToken);
				}
				catch (OperationCanceledException)
				{
					break;
				}
			}
		}
	}
}
