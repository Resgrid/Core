using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>What one run of worker 94 did.</summary>
	public class LmsProgramsRunResult
	{
		public int ReportsSent { get; set; }
		public int Renewed { get; set; }
		public int Alerts { get; set; }
		public int FamiliarizationWritten { get; set; }
		public int Failures { get; set; }
	}

	/// <summary>
	/// Worker 94 (Training T3 part 2, round 4C): scheduled LMS reports, certification cycle auto-renew and alerts, and
	/// building-familiarization credit from Records. Each step starts from its own due-work query (due report definitions, departments
	/// with cycle rules, departments with program settings) and asks the LMS gate once per department with due work (R-L9). One failing
	/// step never stops the others; the next run picks the work up again.
	/// </summary>
	public class LmsProgramsLogic
	{
		public async Task<LmsProgramsRunResult> Process(CancellationToken cancellationToken = default)
		{
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
			return await Process(scope.Resolve<ILmsReportCatalogService>(), scope.Resolve<ILmsCertificationCycleService>(), scope.Resolve<ILmsFamiliarizationCreditService>(),
				DateTime.UtcNow, cancellationToken);
		}

		/// <summary>The run itself, with its services passed in (tests).</summary>
		public static async Task<LmsProgramsRunResult> Process(ILmsReportCatalogService reports, ILmsCertificationCycleService cycles, ILmsFamiliarizationCreditService familiarization,
			DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new LmsProgramsRunResult();
			result.ReportsSent = await Step(() => reports.RunDueAsync(utcNow, cancellationToken), result, "scheduled reports", cancellationToken);
			var cycle = await Step(async () =>
			{
				var run = await cycles.RunDueAsync(utcNow, cancellationToken);
				result.Renewed = run.Renewed;
				result.Failures += run.Failures;
				return run.Alerts;
			}, result, "certification cycles", cancellationToken);
			result.Alerts = cycle;
			result.FamiliarizationWritten = await Step(async () =>
			{
				var run = await familiarization.RunDueAsync(utcNow, cancellationToken);
				result.Failures += run.Failures;
				return run.Written;
			}, result, "familiarization credit", cancellationToken);
			return result;
		}

		private static async Task<int> Step(Func<Task<int>> work, LmsProgramsRunResult result, string name, CancellationToken cancellationToken)
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
				Logging.LogException(ex, $"LMS programs {name} failed.");
				result.Failures++;
				return 0;
			}
		}
	}
}
