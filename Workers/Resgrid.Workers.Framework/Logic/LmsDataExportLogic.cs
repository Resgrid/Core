using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker 99 (Training T5 data exit, round 6D): requeues exports a stopped worker left Running, builds the queued department exports
	/// (a handful per run) into the LMS store, and removes zips whose download link expired. Due-work queries only — the queue and the
	/// expiry — and never the add-on gate: data exit works after a lapse (§17.3) and no department is swept (R-L9).
	/// </summary>
	public class LmsDataExportLogic
	{
		public async Task<LmsDataExportRunResult> Process(CancellationToken cancellationToken = default)
		{
			// Own scope per run: root-scope services would share the process-wide root unit of work.
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
			return await Process(scope.Resolve<ILmsDataExportService>(), DateTime.UtcNow, cancellationToken);
		}

		/// <summary>The run itself, with its service passed in (tests).</summary>
		public static async Task<LmsDataExportRunResult> Process(ILmsDataExportService exports, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			try
			{
				return await exports.RunDueAsync(utcNow, cancellationToken) ?? new LmsDataExportRunResult();
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "LMS data export run failed.");
				return new LmsDataExportRunResult { Failed = 1 };
			}
		}
	}
}
