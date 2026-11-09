using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Operations;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Operations
{
	/// <summary>
	/// incidentCommand.active (plan §7.1): v4 IncidentCommand/GetCommandList — the department's open commands — under the
	/// same rule: Command_View (the definition's gate) and the board read gate (ICommandAccessService.CanViewCommandBoardsAsync,
	/// the ViewCommandBoards permission). A viewer who may not read every board sees the incidents they are on; one who is
	/// on none gets the widget's "forbidden" state.
	/// </summary>
	[DashboardWidget("incidentCommand.active")]
	public class IncidentCommandActiveWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IIncidentCommandService _incidentCommandService;
		private readonly ICommandAccessService _commandAccessService;

		public IncidentCommandActiveWidgetProvider(IIncidentCommandService incidentCommandService, ICommandAccessService commandAccessService)
		{
			_incidentCommandService = incidentCommandService;
			_commandAccessService = commandAccessService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var commands = (await _incidentCommandService.GetCommandSummariesForDepartmentAsync(context.DepartmentId, false) ?? new List<IncidentCommandSummary>())
				.Where(c => c != null && c.DepartmentId == context.DepartmentId && c.Status == (int)IncidentCommandStatus.Active)
				.OrderByDescending(c => c.EstablishedOn)
				.ToList();

			if (!await _commandAccessService.CanViewCommandBoardsAsync(context.DepartmentId, context.UserId))
			{
				var viewable = await _incidentCommandService.GetViewableBoardCallIdsAsync(context.DepartmentId, commands.Select(c => c.CallId), context.UserId);
				var visible = commands.Where(c => viewable.Contains(c.CallId)).ToList();

				if (visible.Count == 0)
					throw new DashboardWidgetStateException(DashboardWidgetErrorCodes.Forbidden, "WidgetErrorForbidden");

				commands = visible;
			}

			return new IncidentCommandActiveWidgetData
			{
				TotalCount = commands.Count,
				Commands = commands.Take(context.GetInt("maxRows", 5)).Select(c => new IncidentCommandActiveRow
				{
					IncidentCommandId = c.IncidentCommandId,
					CallId = c.CallId,
					Name = string.IsNullOrWhiteSpace(c.Name) ? null : c.Name,
					CallName = ProtectedDataEnvelope.SafeDisplay(c.CallName),
					CallNumber = c.CallNumber,
					CallAddress = ProtectedDataEnvelope.SafeDisplay(c.CallAddress),
					CommanderName = c.CommanderName,
					EstablishedOnUtc = c.EstablishedOn,
					AssignedUnitCount = c.AssignedUnitCount,
					AssignedPersonnelCount = c.AssignedPersonnelCount,
					CallUrl = DashboardCallReads.ViewCallUrl(c.CallId)
				}).ToList()
			};
		}
	}
}
