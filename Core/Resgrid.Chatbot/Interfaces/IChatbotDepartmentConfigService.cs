using System.Threading.Tasks;
using Resgrid.Chatbot.Models;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Interfaces
{
	/// <summary>
	/// Loads and persists per-department chatbot configuration (cache-aside), and resolves a
	/// department's own LLM override (decrypted). The system-level <c>ChatbotConfig</c> remains the
	/// default for departments without a row or without an LLM override.
	/// </summary>
	public interface IChatbotDepartmentConfigService
	{
		/// <summary>Gets the department's config (or null if none). The LlmApiKey remains encrypted.</summary>
		Task<ChatbotDepartmentConfig> GetConfigAsync(int departmentId, bool bypassCache = false);

		/// <summary>
		/// Returns the department's own LLM endpoint/key(decrypted)/model when both endpoint and key
		/// are configured and <see cref="GetLlmOverrideStatusAsync"/> is Allowed; otherwise null (caller
		/// falls back to the system provider).
		/// </summary>
		Task<DepartmentLlmOverride> GetLlmOverrideAsync(int departmentId);

		/// <summary>
		/// Whether the department may use its own LLM provider subscription (bring your own key), and why not:
		/// blocked under Advanced Data Protection; otherwise an active Enhanced AI add-on on Resgrid's hosted
		/// service, always on an open-source install (IEnhancedAiAccessService.GetOwnLlmProviderStatusAsync).
		/// Cached for five minutes unless bypassed; settings pages bypass it.
		/// </summary>
		Task<OwnLlmProviderStatus> GetLlmOverrideStatusAsync(int departmentId, bool bypassCache = false);

		/// <summary>
		/// Inserts or updates the department's config. <paramref name="newPlaintextLlmKey"/>:
		/// null = keep existing key, "" = clear it, non-empty = encrypt and store it.
		/// </summary>
		Task<ChatbotDepartmentConfig> SaveConfigAsync(ChatbotDepartmentConfig config, string newPlaintextLlmKey = null);

		/// <summary>
		/// Whether the chatbot is usable for a department on the given platform: no config row means
		/// system defaults (enabled, all platforms); otherwise the row's IsEnabled and AllowedPlatforms
		/// both have to permit it. Used to keep switch/list targets to departments the chatbot can
		/// actually serve on the requesting platform.
		/// </summary>
		Task<bool> IsChatbotUsableForDepartmentAsync(int departmentId, ChatbotPlatform platform);

		Task InvalidateCacheAsync(int departmentId);
	}
}
