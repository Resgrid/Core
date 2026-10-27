using System;
using System.Threading;
using System.Threading.Tasks;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Console.Tasks
{
	public sealed class IncidentSnapshotProjectionTask : IQuidjiboHandler<IncidentSnapshotProjectionCommand>
	{
		public string Name => "Incident Snapshot Projection";
		public int Priority => 1;
		public async Task ProcessAsync(IncidentSnapshotProjectionCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			var result = await new IncidentBackOfficeLogic().IncidentSnapshotProjection(cancellationToken);
			if (!result.Item1) throw new InvalidOperationException(result.Item2);
			progress?.Report(100, result.Item2);
		}
	}
}
