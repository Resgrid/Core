using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Model.Repositories;
using IdentityEntity = Resgrid.Model.ChatbotIdentity;

namespace Resgrid.Chatbot.Services
{
	/// <summary>
	/// Database-backed identity service. Persists chat-platform identity links via
	/// <see cref="IChatbotIdentityRepository"/> and maps between the persistence entity
	/// (<see cref="IdentityEntity"/>, platform as int) and the chatbot model
	/// (<see cref="ChatbotUserIdentity"/>, platform as enum).
	/// </summary>
	public class ChatbotUserIdentityService : IChatbotUserIdentityService
	{
		private readonly IChatbotIdentityRepository _identityRepository;

		public ChatbotUserIdentityService(IChatbotIdentityRepository identityRepository)
		{
			_identityRepository = identityRepository;
		}

		public async Task<ChatbotUserIdentity> GetIdentityAsync(ChatbotPlatform platform, string platformUserId)
		{
			var entity = await _identityRepository.GetByPlatformAndUserAsync((int)platform, platformUserId?.Trim());
			return ToModel(entity);
		}

		public async Task<ChatbotUserIdentity> GetIdentityByPhoneAsync(string phoneNumber)
		{
			var clean = CleanPhone(phoneNumber);
			if (string.IsNullOrWhiteSpace(clean))
				return null;

			// A phone number only ever identifies an SMS-platform identity, so scope the lookup to the
			// SMS platforms (Twilio / SignalWire). This prevents a cleaned number from coincidentally
			// matching a platformUserId stored by a non-SMS platform (Telegram/Discord/OAuth ids, etc.).
			var entity = await _identityRepository.GetByPlatformAndUserAsync((int)ChatbotPlatform.SmsTwilio, clean)
				?? await _identityRepository.GetByPlatformAndUserAsync((int)ChatbotPlatform.SmsSignalWire, clean);
			return ToModel(entity);
		}

		public async Task<List<ChatbotUserIdentity>> GetUserIdentitiesAsync(string userId)
		{
			var entities = await _identityRepository.GetAllByUserIdAsync(userId);
			return entities == null
				? new List<ChatbotUserIdentity>()
				: entities.Select(e => ToModel(e)).ToList();
		}

		public Task<ChatbotUserIdentity> LinkUserAsync(string userId, ChatbotPlatform platform, string platformUserId, string platformUserName, string linkingMethod)
			=> LinkUserAsync(userId, platform, platformUserId, platformUserName, linkingMethod, null);

		public async Task<ChatbotUserIdentity> LinkUserAsync(string userId, ChatbotPlatform platform, string platformUserId, string platformUserName, string linkingMethod, string linkingCode)
		{
			var trimmedPlatformUserId = platformUserId?.Trim();

			// Upsert: a platform identity maps to exactly one Resgrid user (unique on platform + platformUserId).
			var entity = await _identityRepository.GetByPlatformAndUserAsync((int)platform, trimmedPlatformUserId);
			if (entity != null)
			{
				entity.UserId = userId;
				entity.PlatformUserName = platformUserName ?? entity.PlatformUserName;
				entity.IsActive = true;
				entity.LastUsedAt = DateTime.UtcNow;
				entity.LinkingMethod = linkingMethod;
				await _identityRepository.UpdateAsync(entity, CancellationToken.None);
			}
			else
			{
				entity = new IdentityEntity
				{
					Id = Guid.NewGuid().ToString("N"),
					UserId = userId,
					Platform = (int)platform,
					PlatformUserId = trimmedPlatformUserId,
					PlatformUserName = platformUserName,
					IsActive = true,
					CreatedAt = DateTime.UtcNow,
					LastUsedAt = DateTime.UtcNow,
					LinkingMethod = linkingMethod
				};
				await _identityRepository.InsertAsync(entity, CancellationToken.None);
			}

			return ToModel(entity, linkingCode);
		}

		public async Task UnlinkUserAsync(string identityId)
		{
			var entity = await _identityRepository.GetByIdAsync(identityId);
			if (entity != null)
				await _identityRepository.DeleteAsync(entity, CancellationToken.None);
		}

		public async Task RemoveLinkAsync(string userId, ChatbotPlatform platform)
		{
			var entities = await _identityRepository.GetAllByUserIdAsync(userId);
			if (entities == null)
				return;

			foreach (var entity in entities.Where(e => e.Platform == (int)platform))
				await _identityRepository.DeleteAsync(entity, CancellationToken.None);
		}

		public async Task<bool> IsUserLinkedAsync(string userId, ChatbotPlatform platform)
		{
			var entities = await _identityRepository.GetAllByUserIdAsync(userId);
			return entities != null && entities.Any(e => e.Platform == (int)platform && e.IsActive);
		}

		private static ChatbotUserIdentity ToModel(IdentityEntity entity, string linkingCode = null)
		{
			if (entity == null)
				return null;

			return new ChatbotUserIdentity
			{
				Id = entity.Id,
				UserId = entity.UserId,
				Platform = (ChatbotPlatform)entity.Platform,
				PlatformUserId = entity.PlatformUserId,
				PlatformUserName = entity.PlatformUserName,
				IsActive = entity.IsActive,
				CreatedAt = entity.CreatedAt,
				LastUsedAt = entity.LastUsedAt,
				LinkingMethod = entity.LinkingMethod,
				LinkingCode = linkingCode
			};
		}

		private static string CleanPhone(string phoneNumber)
			=> phoneNumber?.Replace("+", "").Replace("-", "").Replace(" ", "").Trim();
	}
}
