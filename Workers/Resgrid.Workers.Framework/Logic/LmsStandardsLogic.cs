using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>What one run of worker 98 did.</summary>
	public class LmsStandardsRunResult
	{
		public int PackagesProcessed { get; set; }
		public int StatementsDelivered { get; set; }
		public int Purged { get; set; }
		public int Failures { get; set; }
	}

	/// <summary>
	/// Worker 98 (Training T5 standards, round 6C): unpacks queued SCORM / cmi5 packages into the LMS media store, delivers due xAPI statements
	/// to each department's LRS (retry with backoff), and purges delivered statements, expired launch sessions and expired AGS tokens. Every step
	/// starts from a due-work query (queued packages, due statements, expired rows); none sweeps departments or asks billing (R-L9). One failing
	/// step never stops the others; the next run picks the work up again.
	/// </summary>
	public class LmsStandardsLogic
	{
		public const int PackagesPerRun = 3;

		public async Task<LmsStandardsRunResult> Process(CancellationToken cancellationToken = default)
		{
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
			return await Process(scope.Resolve<ILmsContentPackageService>(), scope.Resolve<ILmsXapiService>(), scope.Resolve<ILmsStandardsRuntimeService>(),
				scope.Resolve<ILmsLtiService>(), DateTime.UtcNow, cancellationToken);
		}

		/// <summary>The run itself, with its services passed in (tests).</summary>
		public static async Task<LmsStandardsRunResult> Process(ILmsContentPackageService packages, ILmsXapiService xapi, ILmsStandardsRuntimeService runtime, ILmsLtiService lti,
			DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new LmsStandardsRunResult();
			result.PackagesProcessed = await Step(() => packages.ProcessQueueAsync(PackagesPerRun, cancellationToken), result, "package unpacking", cancellationToken);
			result.StatementsDelivered = await Step(() => xapi.DeliverDueAsync(utcNow, cancellationToken), result, "xAPI delivery", cancellationToken);
			result.Purged += await Step(() => xapi.PurgeDeliveredAsync(utcNow, cancellationToken), result, "xAPI purge", cancellationToken);
			result.Purged += await Step(() => runtime.PurgeExpiredSessionsAsync(utcNow, cancellationToken), result, "launch purge", cancellationToken);
			result.Purged += await Step(() => lti.PurgeExpiredTokensAsync(utcNow, cancellationToken), result, "AGS token purge", cancellationToken);
			return result;
		}

		private static async Task<int> Step(Func<Task<int>> work, LmsStandardsRunResult result, string name, CancellationToken cancellationToken)
		{
			try
			{
				return await work();
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"LMS standards {name} failed.");
				result.Failures++;
				return 0;
			}
		}
	}
}
