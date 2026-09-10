using System;
using System.Threading;
using System.Threading.Tasks;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Console.Tasks
{
	public sealed class MaintenanceEscalationTask : IQuidjiboHandler<MaintenanceEscalationCommand>
	{
		public string Name => "Maintenance Escalation";
		public int Priority => 1;
		public async Task ProcessAsync(MaintenanceEscalationCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			var result = await new MaintenanceEscalationLogic().Process(cancellationToken);
			if (!result.Item1) throw new InvalidOperationException(result.Item2);
			progress?.Report(100, result.Item2);
		}
	}
}
