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
	/// ibo.checkIn (plan §7.10): the participants desk's enrollment queue (IncidentParticipant_View, the Enrollment action's rule) plus
	/// counts of enrolled participants not checked in and drafts, over the activation's scope.
	/// </summary>
	[DashboardWidget("ibo.checkIn")]
	public class IncidentCheckInWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IIncidentBackOfficeAccessService _accessService;
		private readonly IIncidentActivationService _activationService;
		private readonly IIncidentLedgerService<IncidentActivation> _activations;
		private readonly IIncidentParticipantsService _participantsService;
		private readonly IIncidentLedgerService<IncidentParticipant> _participants;

		public IncidentCheckInWidgetProvider(IIncidentBackOfficeAccessService accessService, IIncidentActivationService activationService,
			IIncidentLedgerService<IncidentActivation> activations, IIncidentParticipantsService participantsService, IIncidentLedgerService<IncidentParticipant> participants)
		{
			_accessService = accessService;
			_activationService = activationService;
			_activations = activations;
			_participantsService = participantsService;
			_participants = participants;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var access = await IncidentWidgetSupport.RequireReadAsync(_accessService, context.DepartmentId);
			var actor = IncidentWidgetSupport.Actor(context);
			var max = context.GetInt("maxRows", 5);
			var activation = await IncidentWidgetSupport.ResolveActivationAsync(_activationService, _activations, actor,
				context.GetString(IncidentWidgetSupport.ActivationSetting, IncidentWidgetSupport.MostRecent), cancellationToken);
			var data = IncidentWidgetSupport.Header(new IncidentCheckInWidgetData(), access, activation);
			if (activation == null)
				return data;

			var queue = (await _participantsService.GetEnrollmentQueueAsync(actor, activation.IncidentActivationId, cancellationToken) ?? new List<IncidentEnrollmentCandidate>()).ToList();
			data.QueueCount = queue.Count;
			data.Candidates = queue.OrderBy(c => c.SeenSince ?? System.DateTime.MaxValue).Take(max)
				.Select(c => new IncidentCheckInCandidate { Name = c.Name, Kind = c.Kind, Source = c.Source, SeenSince = c.SeenSince }).ToList();

			var scope = await IncidentWidgetSupport.ScopeAsync(_activationService, actor, activation, cancellationToken);
			data.AwaitingCheckIn = await _participants.CountAsync(actor, new IncidentLedgerQuery { ActivationIds = scope }.Where("IsDraft", false).WhereNull("CheckedInOn"), cancellationToken);
			data.Drafts = await _participants.CountAsync(actor, new IncidentLedgerQuery { ActivationIds = scope }.Where("IsDraft", true), cancellationToken);
			return data;
		}
	}
}
