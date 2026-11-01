using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Resgrid.Model.Dashboard;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Services;
using WidgetStrings = Resgrid.Localization.Areas.User.Home.DashboardWidgets;

namespace Resgrid.Services.Dashboard.Widgets.IncidentBackOffice
{
	/// <summary>
	/// The "incidentActivations" option source (ibo.desk and the other activation-scoped IBO widgets). The list starts with the
	/// widgets' default, "recent" (the most recently opened open activation, W.ibo.Option.recent), so the settings dialog can always
	/// return to it, followed by the department's open activations, newest opened first, labelled "Name (CODE)". A stored id is
	/// re-loaded through the department-checked ledger read by the provider, never trusted. Nothing is listed when the module is off.
	/// </summary>
	[DashboardOptionSource(DashboardOptionSources.IncidentActivations)]
	public class IncidentActivationsOptionSource : IDashboardOptionSource
	{
		public const string MostRecentKey = "W.ibo.Option.recent";

		private readonly IIncidentBackOfficeAccessService _accessService;
		private readonly IIncidentActivationService _activationService;
		private readonly IStringLocalizer<WidgetStrings> _strings;

		public IncidentActivationsOptionSource(IIncidentBackOfficeAccessService accessService, IIncidentActivationService activationService,
			IStringLocalizer<WidgetStrings> strings)
		{
			_accessService = accessService;
			_activationService = activationService;
			_strings = strings;
		}

		public async Task<List<KeyValuePair<string, string>>> GetOptionsAsync(int departmentId, string userId)
		{
			// The navigation check (flag + module, no billing round trip): bootstrap must not call the Billing API per option list.
			// Access level None means no back-office data exists, so the list below is empty then anyway.
			if (!await _accessService.IsEnabledAsync(departmentId))
				return new List<KeyValuePair<string, string>>();

			var label = _strings?[MostRecentKey];
			var options = new List<KeyValuePair<string, string>>
			{
				new KeyValuePair<string, string>(IncidentWidgetSupport.MostRecent, label == null || label.ResourceNotFound ? "Most recent incident" : label.Value)
			};

			// Activation reads are department-scoped only; the admin flag plays no part in them.
			var actor = new IncidentActor { DepartmentId = departmentId, UserId = userId };
			var open = await _activationService.GetActivationsAsync(actor, false) ?? Array.Empty<IncidentActivation>();
			options.AddRange(open.Where(a => a.DepartmentId == departmentId && !a.IsDeleted)
				.OrderByDescending(a => a.OpenedOn ?? a.AddedOn)
				.Select(a => new KeyValuePair<string, string>(a.IncidentActivationId,
					string.IsNullOrWhiteSpace(a.ShortCode) ? a.Name : $"{a.Name} ({a.ShortCode})")));
			return options;
		}
	}
}
