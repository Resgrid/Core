using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Flushes the feature toggle service's in-memory evaluation counters to the FeatureFlagUsages
	/// table and refreshes each touched flag's LastEvaluatedOn (used for stale-flag detection).
	///
	/// Follows the standard worker Logic Process() pattern and resolves the service through the
	/// Service-Locator/Autofac container. It is intended to be invoked on a recurring cadence by the
	/// worker host (the same Quidjibo scheduling that drives the other recurring processors), roughly
	/// every FeatureFlagsConfig.EvaluationFlushIntervalSeconds. The flush is idempotent and append-only,
	/// so an occasional missed or duplicated run only affects aggregated analytics, never evaluation.
	/// </summary>
	public class FeatureToggleUsageProcessor
	{
		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken = default(CancellationToken))
		{
			try
			{
				var featureToggleService = Bootstrapper.GetKernel().Resolve<IFeatureToggleService>();
				var flushed = await featureToggleService.FlushEvaluationsAsync(cancellationToken);

				return new Tuple<bool, string>(true, $"Flushed {flushed} feature toggle usage record(s).");
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new Tuple<bool, string>(false, ex.ToString());
			}
		}
	}
}
