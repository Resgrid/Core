using System;
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
	/// ibo.resources (plan §7.10): the activation's resources by kind and support status. Reads the resource ledger over the desk scope
	/// exactly as the Operations desk lists it (its read rule is IncidentBackOffice_View) — one list read instead of the projection
	/// contract's head + components + resources.
	/// </summary>
	[DashboardWidget("ibo.resources")]
	public class IncidentResourcesWidgetProvider : IDashboardWidgetDataProvider
	{
		private static readonly int[] OnIncident = { (int)IncidentSupportStatuses.Arrived, (int)IncidentSupportStatuses.Supported, (int)IncidentSupportStatuses.PendingDemob };
		private static readonly int[] Inbound = { (int)IncidentSupportStatuses.Ordered, (int)IncidentSupportStatuses.Mobilizing, (int)IncidentSupportStatuses.EnRoute };

		private readonly IIncidentBackOfficeAccessService _accessService;
		private readonly IIncidentActivationService _activationService;
		private readonly IIncidentLedgerService<IncidentActivation> _activations;
		private readonly IIncidentLedgerService<IncidentResource> _resources;

		public IncidentResourcesWidgetProvider(IIncidentBackOfficeAccessService accessService, IIncidentActivationService activationService,
			IIncidentLedgerService<IncidentActivation> activations, IIncidentLedgerService<IncidentResource> resources)
		{
			_accessService = accessService;
			_activationService = activationService;
			_activations = activations;
			_resources = resources;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var access = await IncidentWidgetSupport.RequireReadAsync(_accessService, context.DepartmentId);
			var actor = IncidentWidgetSupport.Actor(context);
			var activation = await IncidentWidgetSupport.ResolveActivationAsync(_activationService, _activations, actor,
				context.GetString(IncidentWidgetSupport.ActivationSetting, IncidentWidgetSupport.MostRecent), cancellationToken);
			var data = IncidentWidgetSupport.Header(new IncidentResourcesWidgetData(), access, activation);
			if (activation == null)
				return data;

			var scope = await IncidentWidgetSupport.ScopeAsync(_activationService, actor, activation, cancellationToken);
			var resources = (await _resources.ListAsync(actor, new IncidentLedgerQuery { ActivationIds = scope, Take = 5000 }, cancellationToken))
				.Where(r => r.DepartmentId == context.DepartmentId && !r.IsDeleted).ToList();

			data.Total = resources.Count;
			data.OnIncident = resources.Count(r => OnIncident.Contains(r.SupportStatus));
			data.Inbound = resources.Count(r => Inbound.Contains(r.SupportStatus));
			data.PersonnelOnIncident = resources.Where(r => OnIncident.Contains(r.SupportStatus)).Sum(r => r.PersonnelCount);
			data.ByKind = resources.GroupBy(r => r.Kind).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
				.Select(g => new IncidentKeyCount { Key = Enum.GetName(typeof(IncidentResourceKinds), g.Key) ?? g.Key.ToString(), Count = g.Count() }).ToList();
			data.ByStatus = resources.GroupBy(r => r.SupportStatus).OrderBy(g => g.Key)
				.Select(g => new IncidentKeyCount { Key = Enum.GetName(typeof(IncidentSupportStatuses), g.Key) ?? g.Key.ToString(), Count = g.Count() }).ToList();
			return data;
		}
	}
}
