using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The desk's side of intake text threads (<see cref="IIntakeConversationRouter"/>; dispatchers-on-duty-plan.md F2): intake
	/// calls it from the Twilio webhook and its own actions, so it never throws (a failure is logged and the thread carries on
	/// with AI or unassisted handling). The work is the routing service's (<c>DispatcherRoutingService.Intake</c>); it is reached
	/// lazily because intake's conversation service, which resolves every router, is itself a dependency of the routing service.
	/// </summary>
	public class DispatcherIntakeConversationRouter : IIntakeConversationRouter
	{
		private readonly Lazy<IDispatcherRoutingService> _routingService;

		public DispatcherIntakeConversationRouter(Lazy<IDispatcherRoutingService> routingService)
		{
			_routingService = routingService;
		}

		public async Task<IntakeConversationRouting> RouteNewConversationAsync(IntakeConversation conversation, Call call, CancellationToken cancellationToken)
		{
			try
			{
				var result = await _routingService.Value.RouteIntakeConversationAsync(conversation, call, cancellationToken);
				switch (result?.Outcome ?? DispatcherRouteOutcomes.NotRouted)
				{
					case DispatcherRouteOutcomes.Offered:
					case DispatcherRouteOutcomes.Queued:
					case DispatcherRouteOutcomes.Duplicate:
					case DispatcherRouteOutcomes.Joined:
						return IntakeConversationRouting.Offered;
					default:
						return IntakeConversationRouting.NotRouted;
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk: routing intake thread {conversation?.IntakeConversationId}");
				return IntakeConversationRouting.NotRouted;
			}
		}

		public Task OnParticipantMessageAsync(IntakeConversation conversation, IntakeConversationMessage message, CancellationToken cancellationToken) =>
			RunAsync("participant message", conversation, () => _routingService.Value.OnIntakeParticipantMessageAsync(conversation, cancellationToken));

		public Task OnDispatcherConnectedAsync(IntakeConversation conversation, string userId, CancellationToken cancellationToken) =>
			RunAsync("dispatcher connected", conversation, () => _routingService.Value.OnIntakeDispatcherConnectedAsync(conversation, userId, cancellationToken));

		public Task OnDispatcherReleasedAsync(IntakeConversation conversation, string userId, CancellationToken cancellationToken) =>
			RunAsync("dispatcher released", conversation, () => _routingService.Value.OnIntakeDispatcherReleasedAsync(conversation, userId, cancellationToken));

		public Task OnConversationClosedAsync(IntakeConversation conversation, CancellationToken cancellationToken) =>
			RunAsync("conversation closed", conversation, () => _routingService.Value.OnIntakeConversationClosedAsync(conversation, cancellationToken));

		private static async Task RunAsync(string what, IntakeConversation conversation, Func<Task> work)
		{
			try
			{
				await work();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk: intake thread {conversation?.IntakeConversationId} {what}");
			}
		}
	}
}
