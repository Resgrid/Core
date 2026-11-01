using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.IncidentBackOffice;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.IncidentBackOffice
{
	/// <summary>
	/// ibo.activations (plan §7.10): the incident selector's open activations with each one's current operational period (the
	/// planning service's rule: the period marked Current, else the one spanning now) and the position the viewer holds now. Reads:
	/// access, activations, periods of the listed activations, the viewer's open assignments, and their position names only when
	/// the viewer holds one.
	/// </summary>
	[DashboardWidget("ibo.activations")]
	public class IncidentActivationsWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IIncidentBackOfficeAccessService _accessService;
		private readonly IIncidentActivationService _activationService;
		private readonly IIncidentLedgerService<IncidentOperationalPeriod> _periods;
		private readonly IIncidentLedgerService<IncidentPositionAssignment> _assignments;
		private readonly IIncidentLedgerService<IncidentPositionDefinition> _positions;

		public IncidentActivationsWidgetProvider(IIncidentBackOfficeAccessService accessService, IIncidentActivationService activationService,
			IIncidentLedgerService<IncidentOperationalPeriod> periods, IIncidentLedgerService<IncidentPositionAssignment> assignments,
			IIncidentLedgerService<IncidentPositionDefinition> positions)
		{
			_accessService = accessService;
			_activationService = activationService;
			_periods = periods;
			_assignments = assignments;
			_positions = positions;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var access = await IncidentWidgetSupport.RequireReadAsync(_accessService, context.DepartmentId);
			var actor = IncidentWidgetSupport.Actor(context);
			var max = context.GetInt("maxRows", 5);
			var now = context.NowUtc;

			var open = (await _activationService.GetActivationsAsync(actor, false, cancellationToken) ?? Array.Empty<IncidentActivation>())
				.Where(a => a.DepartmentId == context.DepartmentId && !a.IsDeleted).ToList();
			var data = new IncidentActivationsWidgetData { ReadOnly = access.IsLapsed, OpenCount = open.Count };
			if (open.Count == 0)
				return data;

			var ids = open.Select(a => a.IncidentActivationId).ToList();
			var periods = (await _periods.ListAsync(actor, new IncidentLedgerQuery { ActivationIds = ids, Take = 5000 }.Order("Number"), cancellationToken))
				.GroupBy(p => p.IncidentActivationId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

			var mine = string.IsNullOrWhiteSpace(context.UserId) ? new List<IncidentPositionAssignment>()
				: (await _assignments.ListAsync(actor, new IncidentLedgerQuery { ActivationIds = ids, Take = 500 }.Where("UserId", context.UserId), cancellationToken))
					.Where(a => a.StartOn <= now && (a.EndOn == null || a.EndOn > now)).ToList();
			var positionsById = new Dictionary<string, IncidentPositionDefinition>(StringComparer.OrdinalIgnoreCase);
			if (mine.Count > 0)
			{
				var positionIds = mine.Select(a => a.IncidentPositionDefinitionId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
				foreach (var position in await _positions.ListAsync(actor, new IncidentLedgerQuery { Take = 500 }.WhereIn("IncidentPositionDefinitionId", positionIds), cancellationToken))
					positionsById[position.IncidentPositionDefinitionId] = position;
			}

			var rows = open.Select(activation =>
			{
				var current = Current(periods.TryGetValue(activation.IncidentActivationId, out var list) ? list : new List<IncidentOperationalPeriod>(), now);
				var held = mine.Where(a => string.Equals(a.IncidentActivationId, activation.IncidentActivationId, StringComparison.OrdinalIgnoreCase))
					.Select(a => a.IncidentPositionDefinitionId != null && positionsById.TryGetValue(a.IncidentPositionDefinitionId, out var p) ? p : null)
					.FirstOrDefault(p => p != null);
				return new IncidentActivationRow
				{
					ActivationId = activation.IncidentActivationId,
					Name = activation.Name,
					ShortCode = activation.ShortCode,
					Status = Enum.GetName(typeof(IncidentActivationStatuses), activation.Status) ?? activation.Status.ToString(),
					Kind = Enum.GetName(typeof(IncidentActivationKinds), activation.Kind) ?? activation.Kind.ToString(),
					IsExercise = activation.IsExercise,
					OpenedOn = activation.OpenedOn,
					PeriodNumber = current?.Number,
					PeriodStartOn = current?.StartOn,
					PeriodEndOn = current?.EndOn,
					MyPositionCode = held?.Code,
					MyPositionName = held?.Name
				};
			}).ToList();

			data.MineCount = rows.Count(r => r.MyPositionCode != null);
			data.Rows = rows
				.OrderByDescending(r => r.MyPositionCode != null)
				.ThenBy(r => r.Status == nameof(IncidentActivationStatuses.Active) ? 0 : 1)
				.ThenByDescending(r => r.OpenedOn)
				.Take(max).ToList();
			return data;
		}

		/// <summary>IncidentPlanningService.GetCurrentPeriodAsync's rule over an already-loaded list (ordered by number, as GetPeriodsAsync).</summary>
		private static IncidentOperationalPeriod Current(List<IncidentOperationalPeriod> periods, DateTime now) =>
			periods.LastOrDefault(p => p.Status == (int)IncidentPeriodStatuses.Current) ?? periods.LastOrDefault(p => p.StartOn <= now && p.EndOn > now);
	}
}
