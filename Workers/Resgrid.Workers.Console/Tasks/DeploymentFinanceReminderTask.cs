using System;
using System.Threading;
using System.Threading.Tasks;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Console.Tasks
{
	public sealed class DeploymentFinanceReminderTask : IQuidjiboHandler<DeploymentFinanceReminderCommand>
	{
		public string Name => "Deployment Finance Reminder";
		public int Priority => 1;
		public async Task ProcessAsync(DeploymentFinanceReminderCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			var result = await new DeploymentFinanceReminderLogic().Process(cancellationToken);
			if (!result.Item1) throw new InvalidOperationException(result.Item2);
			progress?.Report(100, result.Item2);
		}
	}
}
