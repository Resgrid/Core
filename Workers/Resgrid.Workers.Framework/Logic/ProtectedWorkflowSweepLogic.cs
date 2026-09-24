using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker 71 (ADP Protected Workflows), daily tick. Expires releases whose ExpiresOn has passed (the run-time check
	/// catches the same thing at the next send), revokes every release of a department whose ADP is offboarding or
	/// disabled, suspends releases of a department that turned the toggle off, and emails department administrators
	/// 30 and 7 days before a release expires. One department's failure never stops the others; logs are value-free.
	/// </summary>
	public sealed class ProtectedWorkflowSweepLogic
	{
		public async Task<Tuple<bool, string>> Process(CancellationToken ct)
		{
			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var protectedWorkflows = scope.Resolve<IProtectedWorkflowService>();
				var result = await protectedWorkflows.RunSweepAsync(DateTime.UtcNow, ct);

				return Tuple.Create(true,
					$"Protected workflow sweep: expired={result.Expired} revoked={result.Revoked} suspended={result.Suspended} notices={result.NoticesSent} failed={result.Failed}");
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogError($"Protected workflow sweep failed: {ex.GetType().FullName}.");
				return Tuple.Create(false, "Protected workflow sweep failed.");
			}
		}
	}
}
