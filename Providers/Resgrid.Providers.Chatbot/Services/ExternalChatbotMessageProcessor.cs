using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Chatbot.Services;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Providers.Chatbot.Interfaces;

namespace Resgrid.Providers.Chatbot.Services
{
	/// <summary>Verified external messages share the existing assistant, but never its SMS transport.</summary>
	public class ExternalChatbotMessageProcessor
	{
		private readonly IChatbotAdapterRegistry _registry;
		private readonly IChatbotIngressService _ingress;
		private readonly CodeLinkingService _linking;
		private readonly IChatbotUserIdentityService _identities;
		private readonly IChatbotRateLimiter _rateLimiter;
		private readonly IDepartmentsService _departments;
		private readonly IProtectedProjectionService _protection;
		private readonly ICacheProvider _cache;
		private readonly IEncryptionService _encryption;
		private readonly IChatbotDepartmentConfigService _config;
		private readonly IAuthorizationService _authorization;
		private readonly IDepartmentLockService _locks;

		public ExternalChatbotMessageProcessor(IChatbotAdapterRegistry registry, IChatbotIngressService ingress,
			CodeLinkingService linking, IChatbotUserIdentityService identities, IChatbotRateLimiter rateLimiter,
			IDepartmentsService departments, IProtectedProjectionService protection, ICacheProvider cache,
			IEncryptionService encryption, IChatbotDepartmentConfigService config, IAuthorizationService authorization, IDepartmentLockService locks)
		{
			_registry = registry; _ingress = ingress; _linking = linking; _identities = identities;
			_rateLimiter = rateLimiter; _departments = departments; _protection = protection;
			_cache = cache; _encryption = encryption; _config = config; _authorization = authorization; _locks = locks;
		}

		public async Task ProcessAsync(ChatbotMessageQueueItem item)
		{
			var platform = (ChatbotPlatform)item.Platform;
			if (_registry.GetAdapter(platform) is not IExternalChatbotAdapter adapter || !adapter.IsConfigured)
				throw new InvalidOperationException("External messaging transport is unavailable.");
			if (string.IsNullOrWhiteSpace(item.MessageId) || item.ReceivedAtUtc == default)
				throw new InvalidOperationException("External message lacks its verified event identity.");
			var message = new ChatbotMessage
			{
				Platform = platform, MessageId = item.MessageId, From = item.From, To = item.To,
				Text = item.Body, Timestamp = item.ReceivedAtUtc,
				PlatformMetadata = item.PlatformMetadata?.ToDictionary(x => x.Key, x => (object)x.Value) ?? new()
			};
			var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{item.Platform}:{item.From}:{item.MessageId}")));
			var key = "Chatbot:External:" + digest;
			if (!_cache.IsConnected()) throw new InvalidOperationException("External messaging requires the shared Redis cache.");
			if (await _cache.GetStringAsync(key + ":done") == "1") return;
			var saved = await _cache.GetStringAsync(key + ":reply");
			// Old commands must not execute, but a checkpointed result may still be delivered after
			// an outage. Each adapter enforces its reply window and the scope is rechecked below.
			if (string.IsNullOrEmpty(saved) && DateTime.UtcNow - item.ReceivedAtUtc > TimeSpan.FromMinutes(15))
				throw new InvalidOperationException("This command expired without a saved result. Ask the sender to send it again.");
			CachedReply reply;
			if (!string.IsNullOrEmpty(saved))
				reply = JsonConvert.DeserializeObject<CachedReply>(_encryption.Decrypt(saved));
			else
			{
				var claim = await _cache.IncrementAsync(key + ":execution", TimeSpan.FromDays(1));
				if (claim != 1)
					throw new InvalidOperationException("External message is already executing or its result requires reconciliation; command will not be repeated.");
				reply = await HandleAsync(message);
				if (!await _cache.SetStringAsync(key + ":reply", _encryption.Encrypt(JsonConvert.SerializeObject(reply)), TimeSpan.FromDays(1)))
					throw new InvalidOperationException("Unable to checkpoint the messaging response.");
			}
			// Retry only delivery of the saved response, never the command that produced it.
			// Recheck current membership/protection before replaying content after a provider outage.
			if (!await CanExposeResponseAsync(message, reply))
				reply = Notice("Please sign in to Resgrid to continue.");
			await adapter.SendReplyAsync(message, reply.Response);
			if (!await _cache.SetStringAsync(key + ":done", "1", TimeSpan.FromDays(1)))
				throw new InvalidOperationException("Unable to checkpoint messaging delivery.");
		}

		private async Task<CachedReply> HandleAsync(ChatbotMessage message)
		{
			if (!await _rateLimiter.TryAcquireAsync("external:" + message.Platform + ":" + message.From, 0, 10, 600))
				return Notice("Please wait a minute before sending another message.");
			var link = Regex.Match(message.Text.Trim(), @"^(?:LINK|/link|/start)\s+([A-Z0-9]{6})$", RegexOptions.IgnoreCase);
			if (link.Success)
			{
				var result = await _linking.ProcessCodeAsync(link.Groups[1].Value, message.Platform, message.From, null);
				return Notice(result.Message);
			}
			if (string.Equals(message.Text.Trim(), "STOP", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(message.Text.Trim(), "UNLINK", StringComparison.OrdinalIgnoreCase))
			{
				var identity = await _identities.GetIdentityAsync(message.Platform, message.From);
				if (identity != null) await _identities.UnlinkUserAsync(identity.Id);
				return Notice("This messaging account is unlinked. Link it again from your Resgrid profile to resume.");
			}
			var linked = await _identities.GetIdentityAsync(message.Platform, message.From);
			if (linked == null || !linked.IsActive)
				return Notice("Open Messaging accounts on your Resgrid profile, generate a code, then send LINK followed by that code here.");
			var scope = await GetScopeAsync(message);
			if (scope == null) return Notice("Please sign in to Resgrid to continue.");
			message.PlatformMetadata["expectedDepartmentId"] = scope.DepartmentId.ToString(System.Globalization.CultureInfo.InvariantCulture);
			scope.Response = await _ingress.ProcessMessageAsync(message);
			if (scope.Response?.DepartmentChanged == true)
			{
				// Never carry the new department's name/content across its protection boundary.
				scope.IdentityOnly = true;
				scope.Response = new ChatbotResponse { Text = "Your active department changed. Send HELP to continue, or sign in to Resgrid if this department restricts external messaging.", Processed = true };
			}
			return scope;
		}

        private async Task<bool> CanExposeResponseAsync(ChatbotMessage message, CachedReply reply)
        {
            if (reply == null) return false;
            if (reply.GenericOnly) return true;
            if (reply.IdentityOnly)
            {
                var identity = await _identities.GetIdentityAsync(message.Platform, message.From);
                return identity != null && identity.IsActive && identity.Id == reply.IdentityId && identity.UserId == reply.UserId;
            }
            var current = await GetScopeAsync(message);
            return current != null && current.IdentityId == reply.IdentityId && current.UserId == reply.UserId
                && current.DepartmentId == reply.DepartmentId;
        }

        private async Task<CachedReply> GetScopeAsync(ChatbotMessage message)
        {
            var identity = await _identities.GetIdentityAsync(message.Platform, message.From);
            if (identity == null || !identity.IsActive) return null;
            var memberships = await _departments.GetAllDepartmentsForUserAsync(identity.UserId);
            var membership = memberships?.Where(x => !x.IsDeleted && x.IsDisabled != true)
                .OrderByDescending(x => x.IsActive).ThenByDescending(x => x.IsDefault).ThenBy(x => x.DepartmentId).FirstOrDefault();
            if (membership == null || !await _authorization.IsUserValidWithinLimitsAsync(identity.UserId, membership.DepartmentId)) return null;
            if (!await _config.IsChatbotUsableForDepartmentAsync(membership.DepartmentId, message.Platform)) return null;
            if (await _locks.IsDepartmentLockedAsync(membership.DepartmentId)) return null;
            if (await _protection.IsChannelSanitizedAsync(membership.DepartmentId, ProtectedDataEgressChannel.ChatPlatform)) return null;
            return new CachedReply { IdentityId = identity.Id, UserId = identity.UserId, DepartmentId = membership.DepartmentId };
        }
        private static CachedReply Notice(string text) => new() { GenericOnly = true, Response = new ChatbotResponse { Text = text, Processed = true } };
        private sealed class CachedReply
        {
            public bool GenericOnly { get; set; }
            public bool IdentityOnly { get; set; }
            public string IdentityId { get; set; }
            public string UserId { get; set; }
            public int DepartmentId { get; set; }
            public ChatbotResponse Response { get; set; }
        }
    }
}
