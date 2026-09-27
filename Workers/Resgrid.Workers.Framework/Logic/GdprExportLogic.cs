using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	public class GdprExportLogic
	{
		public async Task<Tuple<bool, string>> ProcessAsync(CancellationToken cancellationToken = default)
		{
			bool success = true;
			string result = "";

			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var gdprDataExportService = scope.Resolve<IGdprDataExportService>();
				await gdprDataExportService.ExpireOldRequestsAsync(cancellationToken);
				await gdprDataExportService.ProcessPendingRequestsAsync(cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				result = ex.ToString();
				success = false;
			}

			return new Tuple<bool, string>(success, result);
		}
	}
}
