using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.Services
{
	public class ChatbotUserIdentityService : IChatbotUserIdentityService
	{
		// Phase 1: In-memory store. Phase 2+: Replace with database-backed repository.
		private static readonly ConcurrentDictionary<string, ChatbotUserIdentity> _identities = new(StringComparer.OrdinalIgnoreCase);

		private string MakeKey(ChatbotPlatform platform, string platformUserId)
		{
			return $"{(int)platform}:{platformUserId?.Trim().ToLowerInvariant()}";
		}

		public Task<ChatbotUserIdentity> GetIdentityAsync(ChatbotPlatform platform, string platformUserId)
		{
			_identities.TryGetValue(MakeKey(platform, platformUserId), out var identity);
			return Task.FromResult(identity);
		}

		public Task<ChatbotUserIdentity> GetIdentityByPhoneAsync(string phoneNumber)
		{
			var clean = phoneNumber?.Replace("+", "").Replace("-", "").Replace(" ", "").Trim();
			if (string.IsNullOrWhiteSpace(clean))
				return Task.FromResult<ChatbotUserIdentity>(null);

			var identity = _identities.Values.FirstOrDefault(i =>
				i.PlatformUserId?.Replace("+", "").Replace("-", "").Replace(" ", "").Trim() == clean);
			return Task.FromResult(identity);
		}

		public Task<List<ChatbotUserIdentity>> GetUserIdentitiesAsync(string userId)
		{
			var results = _identities.Values
				.Where(i => i.UserId == userId)
				.ToList();
			return Task.FromResult(results);
		}

		public Task<ChatbotUserIdentity> LinkUserAsync(string userId, ChatbotPlatform platform, string platformUserId, string platformUserName, string linkingMethod)
		{
			var identity = new ChatbotUserIdentity
			{
				Id = Guid.NewGuid().ToString("N"),
				UserId = userId,
				Platform = platform,
				PlatformUserId = platformUserId?.Trim(),
				PlatformUserName = platformUserName,
				IsActive = true,
				CreatedAt = DateTime.UtcNow,
				LastUsedAt = DateTime.UtcNow,
				LinkingMethod = linkingMethod
			};

			_identities[MakeKey(platform, platformUserId)] = identity;
			return Task.FromResult(identity);
		}

		public Task<ChatbotUserIdentity> LinkUserAsync(string userId, ChatbotPlatform platform, string platformUserId, string platformUserName, string linkingMethod, string linkingCode)
		{
			var identity = new ChatbotUserIdentity
			{
				Id = Guid.NewGuid().ToString("N"),
				UserId = userId,
				Platform = platform,
				PlatformUserId = platformUserId?.Trim(),
				PlatformUserName = platformUserName ?? platformUserId,
				IsActive = true,
				CreatedAt = DateTime.UtcNow,
				LastUsedAt = DateTime.UtcNow,
				LinkingMethod = linkingMethod,
				LinkingCode = linkingCode
			};

			_identities[MakeKey(platform, platformUserId)] = identity;
			return Task.FromResult(identity);
		}

		public Task UnlinkUserAsync(string identityId)
		{
			var entry = _identities.Values.FirstOrDefault(i => i.Id == identityId);
			if (entry != null)
			{
				entry.IsActive = false;
				_identities.TryRemove(MakeKey(entry.Platform, entry.PlatformUserId), out _);
			}
			return Task.CompletedTask;
		}

		public Task RemoveLinkAsync(string userId, ChatbotPlatform platform)
		{
			var entries = _identities.Values
				.Where(i => i.UserId == userId && i.Platform == platform)
				.ToList();

			foreach (var entry in entries)
			{
				entry.IsActive = false;
				_identities.TryRemove(MakeKey(entry.Platform, entry.PlatformUserId), out _);
			}
			return Task.CompletedTask;
		}

		public Task<bool> IsUserLinkedAsync(string userId, ChatbotPlatform platform)
		{
			var result = _identities.Values.Any(i => i.UserId == userId && i.Platform == platform && i.IsActive);
			return Task.FromResult(result);
		}
	}
}
