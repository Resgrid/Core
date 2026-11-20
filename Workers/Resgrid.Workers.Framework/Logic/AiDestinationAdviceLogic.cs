using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker 81: processes the due Enhanced AI destination reviews (call-intake-and-ai-destinations-plan.md D4). Each review asks the
	/// operator's self-hosted model about one call and writes advice; nothing here changes a call's destination.
	/// </summary>
	public class AiDestinationAdviceLogic
	{
		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken = default)
		{
			try
			{
				if (!Config.AiDestinationConfig.Enabled)
					return new Tuple<bool, string>(true, "AI destination advice is turned off (AiDestinationConfig.Enabled).");

				// Own scope per pass: root-scope services would share the process-wide root unit of work.
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var result = await scope.Resolve<IAiDestinationAdvisorService>().ProcessDueReviewsAsync(DateTime.UtcNow, cancellationToken);

				return new Tuple<bool, string>(true, result.ToString());
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return new Tuple<bool, string>(true, "Stopped.");
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				return new Tuple<bool, string>(false, ex.ToString());
			}
		}
	}
}
