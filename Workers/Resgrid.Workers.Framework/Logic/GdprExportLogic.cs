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
		private readonly IGdprDataExportService _gdprDataExportService;

		public GdprExportLogic()
		{
			_gdprDataExportService = Bootstrapper.GetKernel().Resolve<IGdprDataExportService>();
		}

		public async Task<Tuple<bool, string>> ProcessAsync(CancellationToken cancellationToken = default)
		{
			bool success = true;
			string result = "";

			try
			{
				await _gdprDataExportService.ExpireOldRequestsAsync(cancellationToken);
				await _gdprDataExportService.ProcessPendingRequestsAsync(cancellationToken);
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
