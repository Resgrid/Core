using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Twilio
{
	/// <summary>
	/// The pending call a voicemail left on the desk becomes (plan D8 voice overflow). Text and chat overflow is the routing
	/// service's own (it makes the pending call from the conversation), so only the voicemail, which exists only on this webhook,
	/// is made here.
	/// </summary>
	public interface IDispatcherDeskPendingCalls
	{
		/// <summary>
		/// Saves the voicemail as a <see cref="CallStates.Pending"/> call (nobody paged, recording in its notes) and makes the
		/// overflowed voice item that call: its CallId is set and <see cref="IDispatcherRoutingService.MarkOverflowedAsync"/> puts it
		/// back in the queue, so it waits for a dispatcher as the voicemail call with the caller's number (Call back works from it).
		/// No second desk item is made. Null when the item already has a call or nothing could be saved.
		/// </summary>
		Task<Call> FromVoicemailAsync(DispatcherQueueItem item, string from, string recordingUrl, int? durationSeconds, string recordingSid,
			CancellationToken cancellationToken = default);
	}

	public class DispatcherDeskPendingCalls : IDispatcherDeskPendingCalls
	{
		private readonly IDepartmentsService _departmentsService;
		private readonly ICallsService _callsService;
		private readonly IDispatcherRoutingService _dispatcherRoutingService;
		private readonly IEventAggregator _eventAggregator;

		public DispatcherDeskPendingCalls(IDepartmentsService departmentsService, ICallsService callsService, IDispatcherRoutingService dispatcherRoutingService,
			IEventAggregator eventAggregator)
		{
			_departmentsService = departmentsService;
			_callsService = callsService;
			_dispatcherRoutingService = dispatcherRoutingService;
			_eventAggregator = eventAggregator;
		}

		public async Task<Call> FromVoicemailAsync(DispatcherQueueItem item, string from, string recordingUrl, int? durationSeconds, string recordingSid,
			CancellationToken cancellationToken = default)
		{
			if (item == null || item.CallId.HasValue || string.IsNullOrWhiteSpace(recordingUrl))
				return null;

			var department = await _departmentsService.GetDepartmentByIdAsync(item.DepartmentId);
			if (department == null)
				return null;

			var priorities = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(item.DepartmentId);
			var caller = string.IsNullOrWhiteSpace(from) ? "an unknown number" : from;
			var length = durationSeconds.HasValue && durationSeconds.Value > 0 ? $" ({durationSeconds.Value} seconds)" : string.Empty;
			var now = DateTime.UtcNow;

			var call = new Call
			{
				DepartmentId = item.DepartmentId,
				ReportingUserId = department.ManagingUserId,
				Priority = CallPriorityMatch.DefaultForImport(priorities),
				Name = $"Voicemail {now.TimeConverter(department):g}",
				NatureOfCall = $"Voicemail from {caller}",
				Notes = $"Voicemail from {caller}{length}. Recording: {recordingUrl}",
				ContactNumber = from,
				SourceIdentifier = "DQ:" + item.DispatcherQueueItemId,
				CallSource = (int)CallSources.AudioImport,
				LoggedOn = now,
				// As v4 SaveCall and the web's Save as Pending store a pending call: no dispatch time, never dispatched (null, not
				// false, which marks a scheduled call the scheduler still has to send). Nobody is proposed.
				State = (int)CallStates.Pending,
				DispatchOn = null,
				HasBeenDispatched = null,
				Dispatches = new Collection<CallDispatch>()
			};

			// The caller has hung up: the save must finish whatever happens to the webhook request.
			var saved = await _callsService.SaveCallAsync(call, CancellationToken.None);
			if (saved == null)
				return null;

			try
			{
				// The voice item becomes the voicemail call and waits in the queue again (MarkOverflowedAsync reopens an overflowed
				// item that has a call). Done before the call is announced, so the desk's call listener finds it on the desk already.
				item.CallId = saved.CallId;
				await _dispatcherRoutingService.MarkOverflowedAsync(item,
					$"Voicemail saved as pending call {(string.IsNullOrWhiteSpace(saved.Number) ? saved.CallId.ToString() : saved.Number)}", CancellationToken.None);
				await _dispatcherRoutingService.RouteDepartmentAsync(item.DepartmentId, CancellationToken.None);
			}
			catch (Exception ex)
			{
				// The worker's pending call sweep puts it on the desk within a minute.
				Logging.LogException(ex, $"Dispatcher desk: voicemail call {saved.CallId} could not be linked to item {item.DispatcherQueueItemId}.");
			}

			// A pending call is not live yet: dispatcher screens refresh, "call added" waits until it is dispatched (as v4 SaveCall).
			_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent { DepartmentId = saved.DepartmentId, Call = saved });

			return saved;
		}
	}
}
