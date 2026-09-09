using System;
using System.Threading;
using System.Threading.Tasks;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Console.Tasks
{
	public sealed class ChecklistSchedulingTask : IQuidjiboHandler<ChecklistSchedulingCommand>
	{
		public string Name => "Checklist Scheduling";
		public int Priority => 1;
		public async Task ProcessAsync(ChecklistSchedulingCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			var result = await new ChecklistSchedulingLogic().Process(cancellationToken);
			if (!result.Item1) throw new InvalidOperationException(result.Item2);
			progress?.Report(100, result.Item2);
		}
	}
}
