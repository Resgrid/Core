using Resgrid.Model.Services;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Periodic personnel accountability (PAR) sweep. For every active call that has check-in timers enabled,
	/// asks the incident-command service to raise <c>CriticalParDetectedEvent</c> for any member newly overdue.
	/// The service is idempotent (timeline-deduped), so running this frequently only alerts on real transitions
	/// into Critical. This worker is the backstop for when nobody is viewing the board — the board read path runs
	/// the same sweep on demand. The per-call evaluation short-circuits cheaply when a call has no active command.
	/// </summary>
	public class ParEvaluationLogic
	{
		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken = default)
		{
			try
			{
				// Own scope per sweep: root-scope services would share the process-wide root unit of work.
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var departmentsService = scope.Resolve<IDepartmentsService>();
				var callsService = scope.Resolve<ICallsService>();
				var incidentCommandService = scope.Resolve<IIncidentCommandService>();

				var departments = await departmentsService.GetAllAsync();
				if (departments == null)
					return new Tuple<bool, string>(true, "No departments to sweep.");

				int callsSwept = 0;
				int membersFlagged = 0;

				foreach (var department in departments)
				{
					if (cancellationToken.IsCancellationRequested)
						break;

					var activeCalls = await callsService.GetActiveCallsByDepartmentAsync(department.DepartmentId);
					if (activeCalls == null)
						continue;

					foreach (var call in activeCalls.Where(c => c.CheckInTimersEnabled))
					{
						// EvaluateCriticalParAsync no-ops cheaply when the call has no active incident command,
						// so we can sweep every check-in-enabled active call without pre-filtering by command.
						var flagged = await incidentCommandService.EvaluateCriticalParAsync(
							department.DepartmentId, call.CallId, cancellationToken);

						callsSwept++;
						membersFlagged += flagged?.Count ?? 0;
					}
				}

				return new Tuple<bool, string>(true, $"Swept {callsSwept} call(s); flagged {membersFlagged} member(s) critical.");
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				return new Tuple<bool, string>(false, ex.ToString());
			}
		}
	}
}
