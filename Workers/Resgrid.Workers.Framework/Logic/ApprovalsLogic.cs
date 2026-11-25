using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>The work one run of worker 97 did.</summary>
	public class ApprovalsRunResult
	{
		public int Announced { get; set; }
		public int Escalated { get; set; }
		public int Synced { get; set; }
		public int Total => Announced + Escalated + Synced;
	}

	/// <summary>
	/// Worker 97 (e-Signature S4 approval chains): announces approval steps whose turn started (delegating to the delegate of a named
	/// approver who is on leave, and scheduling the escalation), escalates steps whose time has come, and brings pending approvals whose
	/// signature request closed elsewhere (completed, declined, expired, voided) to their state. Request expiry and signer reminders
	/// stay with worker 90. Every step starts from a due-work query, never a sweep of departments, and asks no billing gate. One failing
	/// step never stops the others.
	/// </summary>
	public class ApprovalsLogic
	{
		public const int BatchSize = 50;

		public async Task<ApprovalsRunResult> Process(CancellationToken cancellationToken = default)
		{
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
			return await Process(scope.Resolve<IApprovalChainService>(), DateTime.UtcNow, cancellationToken);
		}

		/// <summary>The run itself, with its service passed in (tests).</summary>
		public static async Task<ApprovalsRunResult> Process(IApprovalChainService approvals, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new ApprovalsRunResult();
			result.Synced = await StepAsync(() => approvals.SyncClosedAsync(utcNow, BatchSize, cancellationToken), "closed requests", cancellationToken);
			result.Announced = await StepAsync(() => approvals.AnnounceTurnsAsync(utcNow, BatchSize, cancellationToken), "step announcements", cancellationToken);
			result.Escalated = await StepAsync(() => approvals.EscalateDueAsync(utcNow, BatchSize, cancellationToken), "escalations", cancellationToken);
			return result;
		}

		private static async Task<int> StepAsync(Func<Task<int>> step, string name, CancellationToken cancellationToken)
		{
			try
			{
				return await step();
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Approvals worker step failed: " + name);
				return 0;
			}
		}
	}
}
