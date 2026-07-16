using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Services
{
	public class ChatbotDepartmentConfigService : IChatbotDepartmentConfigService
	{
		private static readonly TimeSpan CacheLength = TimeSpan.FromMinutes(5);

		private readonly IChatbotDepartmentConfigRepository _repository;
		private readonly ICacheProvider _cacheProvider;
		private readonly IEncryptionService _encryptionService;

		public ChatbotDepartmentConfigService(
			IChatbotDepartmentConfigRepository repository,
			ICacheProvider cacheProvider,
			IEncryptionService encryptionService)
		{
			_repository = repository;
			_cacheProvider = cacheProvider;
			_encryptionService = encryptionService;
		}

		public async Task<ChatbotDepartmentConfig> GetConfigAsync(int departmentId, bool bypassCache = false)
		{
			async Task<ChatbotDepartmentConfig> fetch() => await _repository.GetByDepartmentIdAsync(departmentId);

			if (!bypassCache && Resgrid.Config.SystemBehaviorConfig.CacheEnabled)
			{
				try
				{
					return await _cacheProvider.RetrieveAsync(CacheKey(departmentId), fetch, CacheLength);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					return await fetch();
				}
			}

			return await fetch();
		}

		public async Task<bool> IsChatbotUsableForDepartmentAsync(int departmentId, ChatbotPlatform platform)
		{
			var config = await GetConfigAsync(departmentId);
			if (config == null)
				return true; // No row: system defaults — enabled, all platforms.

			if (!config.IsEnabled)
				return false;

			return IsPlatformAllowed(config.AllowedPlatforms, platform);
		}

		// Mirrors ChatbotIngressService.IsPlatformAllowed: null/blank/"*" = all platforms, otherwise a
		// comma-separated list of ChatbotPlatform names.
		private static bool IsPlatformAllowed(string allowedPlatforms, ChatbotPlatform platform)
		{
			if (string.IsNullOrWhiteSpace(allowedPlatforms) || allowedPlatforms.Trim() == "*")
				return true;

			var platformName = platform.ToString();
			foreach (var entry in allowedPlatforms.Split(','))
			{
				if (string.Equals(entry.Trim(), platformName, StringComparison.OrdinalIgnoreCase))
					return true;
			}

			return false;
		}

		public async Task<DepartmentLlmOverride> GetLlmOverrideAsync(int departmentId)
		{
			var config = await GetConfigAsync(departmentId);
			if (config == null || string.IsNullOrWhiteSpace(config.LlmApiEndpoint) || string.IsNullOrWhiteSpace(config.LlmApiKey))
				return null;

			string apiKey;
			try
			{
				apiKey = _encryptionService.Decrypt(config.LlmApiKey);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return null;
			}

			if (string.IsNullOrWhiteSpace(apiKey))
				return null;

			return new DepartmentLlmOverride
			{
				Endpoint = config.LlmApiEndpoint,
				ApiKey = apiKey,
				Model = config.LlmModelName
			};
		}

		public async Task<ChatbotDepartmentConfig> SaveConfigAsync(ChatbotDepartmentConfig config, string newPlaintextLlmKey = null)
		{
			if (config == null)
				throw new ArgumentNullException(nameof(config));

			var existing = await _repository.GetByDepartmentIdAsync(config.DepartmentId);

			// Key handling: null = keep existing, empty = clear, value = encrypt new plaintext.
			if (newPlaintextLlmKey == null)
				config.LlmApiKey = existing?.LlmApiKey;
			else if (newPlaintextLlmKey.Length == 0)
				config.LlmApiKey = null;
			else
				config.LlmApiKey = _encryptionService.Encrypt(newPlaintextLlmKey);

			if (existing == null)
			{
				config.Id = Guid.NewGuid().ToString("N");
				config.CreatedAt = DateTime.UtcNow;
				await _repository.InsertAsync(config, CancellationToken.None);
			}
			else
			{
				config.Id = existing.Id;
				config.CreatedAt = existing.CreatedAt;
				config.UpdatedAt = DateTime.UtcNow;
				await _repository.UpdateAsync(config, CancellationToken.None);
			}

			await InvalidateCacheAsync(config.DepartmentId);
			return config;
		}

		public async Task InvalidateCacheAsync(int departmentId)
		{
			try
			{
				await _cacheProvider.RemoveAsync(CacheKey(departmentId));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		private static string CacheKey(int departmentId) => $"ChatbotDeptConfig_{departmentId}";
	}
}
