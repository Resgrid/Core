using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;
using System.Threading;
using System;
using System.Threading.Tasks;
using Resgrid.Config;
using Microsoft.Extensions.DependencyInjection;
using Resgrid.Web.Services.Models;
using Resgrid.Model.Providers;
using Resgrid.Providers.Bus.Rabbit;
using Resgrid.Web.Eventing.Hubs.Models;
using Resgrid.Model.Events;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Microsoft.AspNetCore.SignalR;
using Resgrid.Web.Eventing.Hubs;

namespace Resgrid.Web.Eventing
{
	public class Worker : BackgroundService
	{
		private readonly IHubContext<EventingHub> _eventingHub;
		private readonly IHubContext<GeolocationHub> _geolocationHub;
		private readonly IHubContext<ChatHub> _chatHub;
		private readonly IServiceProvider _serviceProvider;
		private readonly IRabbitInboundEventProvider _rabbitInboundEventProvider;

		public Worker(IServiceProvider serviceProvider, IHubContext<EventingHub> eventingHub, IHubContext<GeolocationHub> geolocationHub, IHubContext<ChatHub> chatHub)
		{
			_serviceProvider = serviceProvider;
			_eventingHub = eventingHub;
			_geolocationHub = geolocationHub;
			_chatHub = chatHub;

			using var scope = _serviceProvider.CreateScope();
			_rabbitInboundEventProvider = scope.ServiceProvider.GetRequiredService<IRabbitInboundEventProvider>();
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken = default)
		{
			Console.WriteLine("Starting Eventing Worker");
			stoppingToken.ThrowIfCancellationRequested();

			_rabbitInboundEventProvider.RegisterForEvents(PersonnelStatusUpdated,
														  UnitStatusUpdated,
														  CallsUpdated,
														  PersonnelStaffingUpdated,
														  CallAdded,
														  CallClosed,
														  PersonnelLocationUpdated,
														  UnitLocationUpdated,
														  IncidentCommandUpdated);

			_rabbitInboundEventProvider.RegisterForChatEvents(ChatEventReceived);

			await StartProviderAsync();

			// Watchdog: the consumer channel dies silently if the shared Rabbit connection is ever
			// replaced (e.g. RabbitConnection.ForceResetAsync after channel exhaustion, or a failed
			// automatic recovery) — nothing restarts it and SignalR clients stop receiving updates
			// until the pod is bounced. Rebuild the consumer after ~10s of continuous disconnect,
			// retrying at most once a minute so a hard broker outage doesn't spin.
			int disconnectedChecks = 0;
			DateTime lastRestartAttemptUtc = DateTime.MinValue;

			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					await Task.Delay(500, stoppingToken);
				}
				catch (OperationCanceledException)
				{
					break;
				}

				if (_rabbitInboundEventProvider.IsConnected())
				{
					disconnectedChecks = 0;
					continue;
				}

				disconnectedChecks++;

				if (disconnectedChecks >= 20 && (DateTime.UtcNow - lastRestartAttemptUtc) >= TimeSpan.FromSeconds(60))
				{
					lastRestartAttemptUtc = DateTime.UtcNow;

					Console.WriteLine("Eventing Worker: Rabbit consumer disconnected; restarting event monitoring.");
					await StartProviderAsync();

					if (_rabbitInboundEventProvider.IsConnected())
					{
						disconnectedChecks = 0;
						Console.WriteLine("Eventing Worker: Event monitoring restarted.");
					}
				}
			}
		}

		private async Task StartProviderAsync()
		{
			try
			{
				await _rabbitInboundEventProvider.Start("Eventing-Web", "EventingWeb");
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}
		}

		//public async Task StartAsync(CancellationToken cancellationToken = default)
		//{
		//	Console.WriteLine("Starting Eventing Worker");

		//	cancellationToken.ThrowIfCancellationRequested();

		//	_rabbitInboundEventProvider.RegisterForEvents(PersonnelStatusUpdated,
		//												  UnitStatusUpdated,
		//												  CallsUpdated,
		//												  PersonnelStaffingUpdated,
		//												  CallAdded,
		//												  CallClosed,
		//												  PersonnelLocationUpdated,
		//												  UnitLocationUpdated);

		//	await _rabbitInboundEventProvider.Start();
		//}

		public async Task PersonnelStatusUpdated(int departmentId, string id)
		{
			Console.WriteLine($"Processing RabbitMQ PersonnelStatusUpdated Event For {departmentId}");

			var group = _eventingHub.Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("personnelStatusUpdated", id);
		}

		public async Task PersonnelStaffingUpdated(int departmentId, string id)
		{
			Console.WriteLine($"Processing RabbitMQ PersonnelStaffingUpdated Event For {departmentId}");

			var group = _eventingHub.Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("personnelStaffingUpdated", id);
		}

		public async Task UnitStatusUpdated(int departmentId, string id)
		{
			Console.WriteLine($"Processing RabbitMQ UnitStatusUpdated Event For {departmentId}");

			var group = _eventingHub.Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("unitStatusUpdated", id);
		}

		public async Task CallsUpdated(int departmentId, string id)
		{
			Console.WriteLine($"Processing RabbitMQ CallsUpdated Event For {departmentId}");

			var group = _eventingHub.Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("callsUpdated", id);
		}

		public async Task IncidentCommandUpdated(int departmentId, string id)
		{
			Console.WriteLine($"Processing RabbitMQ IncidentCommandUpdated Event For {departmentId}");

			// Resource releases, lane moves, lead changes and command transfers all change who may
			// receive incident chat. Rotate every call-scoped SignalR group before notifying clients;
			// old connections remain in an obsolete group that receives no future message payloads.
			if (int.TryParse(id, out var callId))
				await InvalidateIncidentChatAccessAsync(departmentId, callId);

			var group = _eventingHub.Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("incidentCommandUpdated", id);

			await _chatHub.Clients.Group($"chatdept:{departmentId}").SendAsync(
				ChatEventKinds.ChannelUpdated,
				Newtonsoft.Json.JsonConvert.SerializeObject(new { DepartmentId = departmentId, CallId = id, AuthorizationChanged = true }));
		}

		public async Task DepartmentUpdated(int departmentId)
		{
			Console.WriteLine($"Processing RabbitMQ DepartmentUpdated Event For {departmentId}");

			var group = _eventingHub.Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("departmentUpdated");
		}

		public async Task CallAdded(int departmentId, string id)
		{
			Console.WriteLine($"Processing RabbitMQ CallAdded Event For {departmentId}");

			var group = _eventingHub.Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("callAdded", id);
		}

		public async Task CallClosed(int departmentId, string id)
		{
			Console.WriteLine($"Processing RabbitMQ CallClosed Event For {departmentId}");

			var group = _eventingHub.Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("callClosed", id);
		}

		public async Task PersonnelLocationUpdated(int departmentId, PersonnelLocationUpdatedEvent update)
		{
			var group = _geolocationHub.Clients.Group(departmentId.ToString());

			var location = new PersonnelLocationUpdate();
			location.DepartmentId = update.DepartmentId;
			location.UserId = update.UserId;
			location.Latitude = update.Latitude;
			location.Longitude = update.Longitude;
			location.RecordId = update.RecordId;

			if (group != null)
				await group.SendAsync("onPersonnelLocationUpdated", location);
		}

		public async Task UnitLocationUpdated(int departmentId, UnitLocationUpdatedEvent update)
		{
			var group = _geolocationHub.Clients.Group(departmentId.ToString());

			var location = new UnitLocationUpdate();
			location.DepartmentId = update.DepartmentId;
			location.UnitId = update.UnitId;
			location.Latitude = update.Latitude;
			location.Longitude = update.Longitude;
			location.RecordId = update.RecordId;

			if (group != null)
				await group.SendAsync("onUnitLocationUpdated", location);
		}

		/// <summary>
		/// Routes a chat event envelope to SignalR clients. Targeted events (chatbot, personal badges)
		/// go to the user's personal group; channel-list events go to the department group as a
		/// metadata-free refresh hint and to the channel group with the full DTO; all others go to
		/// the channel group. The client event name is the envelope Kind and the argument is the
		/// payload JSON.
		/// </summary>
		public async Task ChatEventReceived(int departmentId, string payloadJson)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(payloadJson))
					return;

				var chatEvent = Newtonsoft.Json.JsonConvert.DeserializeObject<ChatEventRaised>(payloadJson);
				if (chatEvent == null || string.IsNullOrWhiteSpace(chatEvent.Kind) || departmentId <= 0 ||
					chatEvent.DepartmentId != departmentId)
					return;

				if (chatEvent.Kind == ChatEventKinds.AccessRevoked)
				{
					await ChatAccessRevokedReceived(chatEvent);
					return;
				}

				if (!string.IsNullOrWhiteSpace(chatEvent.TargetUserId))
				{
					await _chatHub.Clients.Group($"chatuser:{chatEvent.DepartmentId}:{chatEvent.TargetUserId.ToLowerInvariant()}")
						.SendAsync(chatEvent.Kind, GuardChatPayloadSize(chatEvent));
					return;
				}

				if (chatEvent.Kind == ChatEventKinds.ChannelUpdated || chatEvent.Kind == ChatEventKinds.ChannelProvisioned ||
					chatEvent.Kind == ChatEventKinds.ModerationApplied)
				{
					var hint = Newtonsoft.Json.JsonConvert.SerializeObject(new
					{
						ChatChannelId = chatEvent.ChatChannelId,
						DepartmentId = chatEvent.DepartmentId,
						eventKind = chatEvent.Kind
					});

					await _chatHub.Clients.Group($"chatdept:{chatEvent.DepartmentId}")
						.SendAsync(chatEvent.Kind, hint);

					if (!string.IsNullOrWhiteSpace(chatEvent.ChatChannelId))
					{
						var channelGroupName = await GetCurrentChannelGroupNameAsync(chatEvent.ChatChannelId);
						if (channelGroupName != null)
							await _chatHub.Clients.Group(channelGroupName)
								.SendAsync(chatEvent.Kind, GuardChatPayloadSize(chatEvent));
					}

					return;
				}

				if (!string.IsNullOrWhiteSpace(chatEvent.ChatChannelId))
				{
					var channelGroupName = await GetCurrentChannelGroupNameAsync(chatEvent.ChatChannelId);
					if (channelGroupName != null)
						await _chatHub.Clients.Group(channelGroupName)
							.SendAsync(chatEvent.Kind, GuardChatPayloadSize(chatEvent));
				}
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}
		}

		/// <summary>
		/// Server-side eviction for revoked channel access (ban/remove/lock): removes every tracked
		/// connection for the user from the channel group, then notifies the user's devices so the
		/// client can drop the channel from its UI.
		/// </summary>
		private async Task ChatAccessRevokedReceived(ChatEventRaised chatEvent)
		{
			ChatAccessRevokedPayload payload = null;

			try
			{
				if (!string.IsNullOrWhiteSpace(chatEvent.PayloadJson))
					payload = Newtonsoft.Json.JsonConvert.DeserializeObject<ChatAccessRevokedPayload>(chatEvent.PayloadJson);
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}

			var userId = payload?.UserId;
			var channelId = !string.IsNullOrWhiteSpace(payload?.ChannelId) ? payload.ChannelId : chatEvent.ChatChannelId;

			if (string.IsNullOrWhiteSpace(userId))
				return;

			var channelGroupName = await GetCurrentChannelGroupNameAsync(channelId);
			if (channelGroupName != null && ChatHub.UserConnections.TryGetValue(userId, out var connections))
			{
				foreach (var connectionId in connections.Keys)
				{
					try
					{
						await _chatHub.Groups.RemoveFromGroupAsync(connectionId, channelGroupName);
					}
					catch (Exception ex)
					{
						Resgrid.Framework.Logging.LogException(ex);
					}
				}
			}

			await _chatHub.Clients.Group($"chatuser:{chatEvent.DepartmentId}:{userId.ToLowerInvariant()}")
				.SendAsync(chatEvent.Kind, chatEvent.PayloadJson);
		}

		private async Task<string> GetCurrentChannelGroupNameAsync(string channelId)
		{
			if (string.IsNullOrWhiteSpace(channelId))
				return null;

			using var scope = _serviceProvider.CreateScope();
			var permissionService = scope.ServiceProvider.GetRequiredService<IChatPermissionService>();
			var version = await permissionService.GetChannelAccessVersionAsync(channelId);
			return ChatHub.BuildChannelGroupName(channelId, version);
		}

		private async Task InvalidateIncidentChatAccessAsync(int departmentId, int callId)
		{
			if (departmentId <= 0 || callId <= 0)
				return;

			using var scope = _serviceProvider.CreateScope();
			var channelRepository = scope.ServiceProvider.GetRequiredService<IChatChannelRepository>();
			var permissionService = scope.ServiceProvider.GetRequiredService<IChatPermissionService>();
			var channels = await channelRepository.GetByCallIdAsync(callId);

			if (channels == null)
				return;

			foreach (var channel in channels)
			{
				if (channel != null && channel.DepartmentId == departmentId)
					await permissionService.InvalidateChannelCacheAsync(channel.ChatChannelId);
			}
		}

		/// <summary>
		/// Defensive guard against oversized realtime payloads (SignalR/Redis backplane limits):
		/// beyond ~64KB the message Body is truncated and flagged; clients fetch the full body via REST.
		/// </summary>
		private static string GuardChatPayloadSize(ChatEventRaised chatEvent)
		{
			const int maxPayloadChars = 64 * 1024;
			var payloadJson = chatEvent.PayloadJson;

			if (string.IsNullOrEmpty(payloadJson) || payloadJson.Length <= maxPayloadChars)
				return payloadJson;

			try
			{
				var obj = Newtonsoft.Json.Linq.JObject.Parse(payloadJson);

				if (obj["Body"] != null)
				{
					var body = obj["Body"].ToString();
					obj["Body"] = body.Length > 1024 ? body.Substring(0, 1024) : body;
					obj["BodyTruncated"] = true;

					Resgrid.Framework.Logging.LogInfo($"Chat event {chatEvent.Kind} payload was {payloadJson.Length} chars; truncated Body for realtime fan-out.");

					return obj.ToString(Newtonsoft.Json.Formatting.None);
				}

				Resgrid.Framework.Logging.LogInfo($"Chat event {chatEvent.Kind} payload was {payloadJson.Length} chars with no Body to truncate; relaying unchanged.");
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}

			return payloadJson;
		}

		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		
	}
}
