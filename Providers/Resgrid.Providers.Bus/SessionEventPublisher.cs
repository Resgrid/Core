using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model.Security;
using Resgrid.Providers.Bus.Rabbit;

namespace Resgrid.Providers.Bus
{
	/// <summary>
	/// Sends a session event over the eventing topic to the Eventing hosts, which forward it to that session's SignalR group
	/// (passkey workbook section 7.4). Best effort: clients also poll, so a lost event only delays them.
	/// </summary>
	public sealed class SessionEventPublisher : ISessionEventPublisher
	{
		public async Task PublishAsync(string sessionId, SessionEventMessage message, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(sessionId) || message == null || !SessionEvents.IsKnown(message.Name))
				return;

			try
			{
				if (!await new RabbitTopicProvider().SessionEvent(sessionId, JsonConvert.SerializeObject(message)))
					Logging.LogError($"A {message.Name} session event could not be sent; the client falls back to polling.");
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A session event could not be sent; the client falls back to polling.");
			}
		}
	}
}
