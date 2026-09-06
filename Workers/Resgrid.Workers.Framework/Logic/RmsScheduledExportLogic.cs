using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker command 45 (RMS plan section 5.6, department report exports): renders every export template whose
	/// schedule is due, stores the run, and raises RecordExportScheduled (160) so a Workflow can carry the file
	/// to an agency that has no API. Hourly; a template is due at most once per period and a failure defers it
	/// an hour, so a broken template cannot wedge the sweep.
	/// </summary>
	public sealed class RmsScheduledExportLogic
	{
		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken)
		{
			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var service = scope.Resolve<IRecordsExportService>();
				var result = await service.RunDueSchedulesAsync(cancellationToken);

				var summary = $"Scheduled exports: {result.TemplatesEvaluated} due, {result.RunsRendered} rendered, {result.Errors} error(s).";
				if (result.Errors > 0)
					Logging.LogError(summary);

				return new Tuple<bool, string>(true, summary);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new Tuple<bool, string>(false, ex.ToString());
			}
		}
	}
}
