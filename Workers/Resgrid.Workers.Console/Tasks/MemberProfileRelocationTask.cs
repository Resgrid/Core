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
	/// <summary>
	/// Drains the legacy member-profile relocation backlog (ADP plan section 5.1) — the identification
	/// numbers and addresses that M0134 could not move in SQL because their department is already
	/// enrolled in ADP, plus members who joined after that migration ran. A finite, self-terminating
	/// job: once the backlog is empty each run is a single indexed query.
	/// </summary>
	public sealed class MemberProfileRelocationTask : IQuidjiboHandler<MemberProfileRelocationCommand>
	{
		private readonly ILogger _logger;

		public MemberProfileRelocationTask(ILogger logger)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public string Name => "Member Profile Relocation";
		public int Priority => 1;

		public async Task ProcessAsync(MemberProfileRelocationCommand command, IQuidjiboProgress progress,
			CancellationToken cancellationToken)
		{
			progress?.Report(1, $"Starting the {Name} Task");

			try
			{
				var logic = new MemberProfileRelocationLogic();
				var result = await logic.Process(cancellationToken);

				if (!result.Item1)
					throw new InvalidOperationException(result.Item2);

				_logger.LogInformation("MemberProfileRelocation::{Summary}", result.Item2);
				progress?.Report(100, $"Finishing the {Name} Task");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
		}
	}
}
