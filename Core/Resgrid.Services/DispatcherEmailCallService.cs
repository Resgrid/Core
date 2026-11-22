using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc />
	public class DispatcherEmailCallService : IDispatcherEmailCallService
	{
		private const int SubjectMaxLength = 250;

		private readonly IDispatcherDeskConfigService _dispatcherDeskConfigService;
		private readonly IDispatcherRoutingService _dispatcherRoutingService;
		private readonly IAiDispatchEnrichmentService _aiDispatchEnrichmentService;
		private readonly IEventAggregator _eventAggregator;

		public DispatcherEmailCallService(IDispatcherDeskConfigService dispatcherDeskConfigService, IDispatcherRoutingService dispatcherRoutingService,
			IAiDispatchEnrichmentService aiDispatchEnrichmentService, IEventAggregator eventAggregator)
		{
			_dispatcherDeskConfigService = dispatcherDeskConfigService;
			_dispatcherRoutingService = dispatcherRoutingService;
			_aiDispatchEnrichmentService = aiDispatchEnrichmentService;
			_eventAggregator = eventAggregator;
		}

		public async Task<DispatcherEmailCallModes> GetModeAsync(int departmentId)
		{
			try
			{
				var config = await _dispatcherDeskConfigService.GetConfigAsync(departmentId);
				return config?.GetEmailCallMode() ?? DispatcherEmailCallModes.AutoDispatch;
			}
			catch (Exception ex)
			{
				// An unreadable setting must never stop the emailed call: it goes out as it did before the setting existed.
				Logging.LogException(ex, $"Dispatcher desk email call mode could not be read for department {departmentId}.");
				return DispatcherEmailCallModes.AutoDispatch;
			}
		}

		public void HoldAsPending(Call call)
		{
			if (call == null)
				return;

			// As v4 SaveCall and the web's Save as Pending store a pending call: no dispatch time, never dispatched (null, not false,
			// which marks a scheduled call the scheduler still has to send).
			call.State = (int)CallStates.Pending;
			call.DispatchOn = null;
			call.HasBeenDispatched = null;
		}

		public async Task<bool> ShouldEnrichAsync(int departmentId, int formatType, DispatcherEmailCallModes mode)
		{
			if (formatType == (int)CallEmailTypes.AI)
				return true;

			if (mode != DispatcherEmailCallModes.EnhancedAi)
				return false;

			try
			{
				return await _aiDispatchEnrichmentService.IsAvailableAsync(departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		public async Task OnPendingCallSavedAsync(Call savedCall, string sender, string subject, CancellationToken cancellationToken = default)
		{
			if (savedCall == null || savedCall.CallId <= 0)
				return;

			try
			{
				// NotRouted (desk off, or it does not take emails) and Duplicate are both fine: the call waits in Pending Calls either way,
				// and the desk's own sweep adds pending calls it missed.
				await _dispatcherRoutingService.EnqueueAsync(new DispatcherEnqueueRequest
				{
					DepartmentId = savedCall.DepartmentId,
					Kind = DispatcherQueueItemKinds.Email,
					CallId = savedCall.CallId,
					SourceAddress = string.IsNullOrWhiteSpace(sender) ? null : sender.Trim(),
					Subject = Truncate(subject, SubjectMaxLength),
					Body = savedCall.NatureOfCall,
					CreatedByUserId = savedCall.ReportingUserId
				}, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Pending emailed call {savedCall.CallId} could not be put on the dispatcher desk.");
			}

			try
			{
				// A pending call is not live yet, so no Call Added; the update refreshes dispatcher screens (as v4 SaveCall does).
				_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent { DepartmentId = savedCall.DepartmentId, Call = savedCall });
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		private static string Truncate(string value, int maxLength)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			var trimmed = value.Trim();
			return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength);
		}
	}
}
