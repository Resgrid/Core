using System;
using System.Threading;
using System.Threading.Tasks;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Console.Tasks
{
	public sealed class BidExpirationTask : IQuidjiboHandler<BidExpirationCommand>
	{
		public string Name => "Bid Expiration";
		public int Priority => 1;
		public async Task ProcessAsync(BidExpirationCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			var result = await new BidExpirationLogic().Process(cancellationToken);
			if (!result.Item1) throw new InvalidOperationException(result.Item2);
			progress?.Report(100, result.Item2);
		}
	}
}
