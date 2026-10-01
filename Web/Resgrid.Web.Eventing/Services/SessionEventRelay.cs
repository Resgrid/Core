using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using Resgrid.Model.Security;
using Resgrid.Services;

namespace Resgrid.Web.Eventing.Services
{
	/// <summary>
	/// Forwards a session event from the eventing topic to that session's SignalR group (passkey workbook section 7.4). A
	/// malformed or unknown event is dropped: the topic never chooses what a client is told beyond the known shapes.
	/// </summary>
	public static class SessionEventRelay
	{
		public static async Task RelayAsync(IHubClients clients, SessionConnectionRegistry connections, string sessionId, string payload)
		{
			if (clients == null || string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(payload))
				return;

			SessionEventMessage message;
			try
			{
				message = JsonConvert.DeserializeObject<SessionEventMessage>(payload);
			}
			catch (JsonException)
			{
				return;
			}

			if (message == null || !SessionEvents.IsKnown(message.Name))
				return;

			// The session locked or ended: close its connections on this host, and tell the client nothing.
			if (message.Name == SessionEvents.SessionClosed)
			{
				connections?.CloseSession(sessionId);
				return;
			}

			await clients.Group(SessionEvents.GroupFor(sessionId)).SendAsync(message.Name,
				new { approvalRequestId = message.ApprovalRequestId, state = message.State });
		}
	}
}
