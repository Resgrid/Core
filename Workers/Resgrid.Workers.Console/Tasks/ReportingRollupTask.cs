using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	/// <summary>
	/// Nightly job that computes the previous UTC day's reporting rollups (call volume, call-processing
	/// time, unit hour utilization) for every department plus a system-wide aggregate, into the
	/// ReportingDailyRollup store. Backed by <see cref="IReportingRollupProcessor"/>.
	/// </summary>
	public class ReportingRollupTask : IQuidjiboHandler<ReportingRollupCommand>
	{
		public string Name => "Reporting Rollup";
		public int Priority => 1;
		private readonly ILogger _logger;

		public ReportingRollupTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(ReportingRollupCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
				var rollupProcessor = Bootstrapper.GetKernel().Resolve<IReportingRollupProcessor>();

				var departments = await departmentsService.GetAllAsync();
				var departmentIds = departments?.Select(d => d.DepartmentId).ToList() ?? new List<int>();

				// Roll up the previous full UTC day.
				var dayUtc = DateTime.UtcNow.Date.AddDays(-1);
				var written = await rollupProcessor.RunDailyRollupForAllAsync(dayUtc, departmentIds, cancellationToken);

				progress.Report(100, $"Finished {Name}: wrote {written} rollup rows for {departmentIds.Count} departments");
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				_logger.LogError(ex.ToString());
			}
		}
	}
}
