using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Console.Tasks
{
	/// <summary>Worker 100 (ContentSetsLogic): set updates accepted automatically, by deadline and applied; revoked links cut.</summary>
	public class ContentSetsTask : IQuidjiboHandler<ContentSetsCommand>
	{
		public string Name => "Content Sets";
		public int Priority => 1;
		public ILogger _logger;

		public ContentSetsTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(ContentSetsCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var result = await new ContentSetsLogic().Process(cancellationToken);

				if (result.Accepted > 0 || result.Applied > 0 || result.Failed > 0 || result.Suspended > 0 || result.SponsorshipsEnded > 0 || result.CountersignaturesLapsed > 0)
					_logger.LogInformation($"ContentSets::Accepted {result.Accepted}, applied {result.Applied}, failed {result.Failed}, suspended {result.Suspended}, " +
						$"sponsorships ended {result.SponsorshipsEnded}, countersignatures lapsed {result.CountersignaturesLapsed}");

				progress.Report(100, $"Finishing the {Name} Task");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				_logger.LogError(ex.ToString());
			}
		}
	}
}
