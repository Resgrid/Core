using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.AdminAssist;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Services.AdminAssist
{
	public sealed class ConfigurationImpactService(IAdminAssistAccessService access, IConfigurationSnapshotProvider snapshots,
		IAdminAssistCatalog catalog, IAdminAssistRepository repository, TimeProvider clock, IEnumerable<IOperationalImpactProvider> providers = null) : IConfigurationImpactService
	{
		public async Task<ConfigurationImpactReport> PreviewCapacityAsync(AdminAssistActor actor, CapacityImpactRequest request, CancellationToken ct = default)
		{
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
			var snapshot = await snapshots.ReadAsync(actor, ct);
			var result = CapacityImpactEvaluator.Evaluate(snapshot, request, clock.GetUtcNow().UtcDateTime,
				TimeSpan.FromSeconds(Math.Clamp(Config.AdminAssistConfig.EvidenceFreshnessSeconds, 1, 300)));
			if ((await repository.GetConfigurationRevisionAsync(actor.DepartmentId, ct)).ToString(System.Globalization.CultureInfo.InvariantCulture) != snapshot.Revision) throw new AdminAssistConcurrencyException();
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
			return result;
		}

		public async Task<ConfigurationImpactReport> PreviewAsync(AdminAssistActor actor, ConfigurationImpactRequest request, CancellationToken ct = default)
		{
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
			using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
			bounded.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(Config.AdminAssistConfig.SnapshotTimeoutSeconds, 1, 60)));
			ct = bounded.Token;
			var snapshot = await snapshots.ReadAsync(actor, ct);
			var result = new ConfigurationImpactEvaluator(catalog).Evaluate(snapshot, request, clock.GetUtcNow().UtcDateTime,
				TimeSpan.FromSeconds(Math.Clamp(Config.AdminAssistConfig.EvidenceFreshnessSeconds, 1, 300)));
			foreach (var provider in providers ?? Array.Empty<IOperationalImpactProvider>())
			{
				if (!provider.Supports(request.SettingId)) continue;
				var operational = await provider.EvaluateAsync(actor, snapshot, request, ct);
				result = result with { Metrics = result.Metrics.Concat(operational.Metrics).ToArray(),
					LimitKeys = result.LimitKeys.Concat(operational.LimitKeys).Distinct().ToArray(), EvaluatorVersion = result.EvaluatorVersion + ";" + operational.Version };
			}
			if ((await repository.GetConfigurationRevisionAsync(actor.DepartmentId, ct)).ToString(System.Globalization.CultureInfo.InvariantCulture) != snapshot.Revision)
				throw new AdminAssistConcurrencyException();
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
			return result;
		}
	}
}
