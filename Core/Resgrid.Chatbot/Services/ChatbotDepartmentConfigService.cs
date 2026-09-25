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

		// The add-on check is a Billing API call; a short cache keeps it off every chatbot message. A cancelled add-on, or an
		// Advanced Data Protection enrollment, stops the department's own provider within this window. Enrollment is queued
		// for an overnight migration window, so the block is in place long before any field is encrypted.
		private static readonly TimeSpan OwnProviderCacheLength = TimeSpan.FromMinutes(5);

		private readonly IChatbotDepartmentConfigRepository _repository;
		private readonly ICacheProvider _cacheProvider;
		private readonly IEncryptionService _encryptionService;
		private readonly IEnhancedAiAccessService _enhancedAi;

		public ChatbotDepartmentConfigService(
			IChatbotDepartmentConfigRepository repository,
			ICacheProvider cacheProvider,
			IEncryptionService encryptionService,
			IEnhancedAiAccessService enhancedAi)
		{
			_repository = repository;
			_cacheProvider = cacheProvider;
			_encryptionService = encryptionService;
			_enhancedAi = enhancedAi;
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

			// A saved provider stays inert without the Enhanced AI add-on or under Advanced Data Protection; the operator's
			// system provider answers instead.
			if (await GetLlmOverrideStatusAsync(departmentId) != OwnLlmProviderStatus.Allowed)
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

		public async Task<OwnLlmProviderStatus> GetLlmOverrideStatusAsync(int departmentId, bool bypassCache = false)
		{
			if (departmentId <= 0)
				return OwnLlmProviderStatus.Unknown;

			var cacheKey = OwnProviderCacheKey(departmentId);
			if (!bypassCache && Resgrid.Config.SystemBehaviorConfig.CacheEnabled)
			{
				try
				{
					if (int.TryParse(await _cacheProvider.GetStringAsync(cacheKey), out var cached) && Enum.IsDefined(typeof(OwnLlmProviderStatus), cached))
						return (OwnLlmProviderStatus)cached;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			var status = await _enhancedAi.GetOwnLlmProviderStatusAsync(departmentId);
			if (Resgrid.Config.SystemBehaviorConfig.CacheEnabled)
			{
				try
				{
					await _cacheProvider.SetStringAsync(cacheKey, ((int)status).ToString(), OwnProviderCacheLength);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			return status;
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

			ValidateColumnLengths(config);

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
				await _cacheProvider.RemoveAsync(OwnProviderCacheKey(departmentId));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		/// <summary>
		/// Guards against SQL truncation (error 8152) by validating string fields against the
		/// ChatbotDepartmentConfigs column sizes (M0068/M0070) before hitting the database.
		/// LlmApiKey is checked post-encryption since the ciphertext is what gets stored.
		/// Throws ArgumentException so API callers can map the failure to a 400 response;
		/// messages are caller-safe (no parameter-name suffix).
		/// </summary>
		private static void ValidateColumnLengths(ChatbotDepartmentConfig config)
		{
			if (config.AllowedPlatforms != null && config.AllowedPlatforms.Length > 500)
				throw new ArgumentException("AllowedPlatforms cannot exceed 500 characters.");

			if (config.LlmApiEndpoint != null && config.LlmApiEndpoint.Length > 500)
				throw new ArgumentException("LlmApiEndpoint cannot exceed 500 characters.");

			if (config.LlmModelName != null && config.LlmModelName.Length > 200)
				throw new ArgumentException("LlmModelName cannot exceed 200 characters.");

			if (config.LlmApiKey != null && config.LlmApiKey.Length > 1000)
				throw new ArgumentException("Encrypted LlmApiKey cannot exceed 1000 characters; supply a shorter API key.");
		}

		private static string CacheKey(int departmentId) => $"ChatbotDeptConfig_{departmentId}";

		private static string OwnProviderCacheKey(int departmentId) => $"ChatbotOwnLlmStatus_{departmentId}";
	}
}
