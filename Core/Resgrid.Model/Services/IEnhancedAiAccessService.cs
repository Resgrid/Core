using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Entitlement checks for the paid Enhanced AI add-on (enhanced-ai-addon-plan.md §5). Each paid check is master flag
	/// Ai.Enhanced → capability flag → module switch → billing configured → live PaymentAddons window, exactly as
	/// BusinessOperationsAccessService; no entitlement cache. Admin Assist's free allowance is decided by the Admin Assist
	/// conversation gate (IAiAccessService), which uses HasActiveAddonAsync to choose between the paid and free paths.
	/// </summary>
	public interface IEnhancedAiAccessService
	{
		/// <summary>The Ai.Enhanced rollout flag and the department's AI module switch are on, independently of the purchase.</summary>
		Task<bool> IsEnabledAsync(int departmentId);

		/// <summary>The department may use the paid capability behind <paramref name="capabilityFlag"/> (one of the Ai.* child flags) right now.</summary>
		Task<bool> CanUseAsync(int departmentId, string capabilityFlag);

		/// <summary>The department holds an active Enhanced AI add-on window right now (no flag or module checks). False when unknown.</summary>
		Task<bool> HasActiveAddonAsync(int departmentId);

		/// <summary>
		/// Tri-state form of <see cref="HasActiveAddonAsync"/>: false when billing is not configured or no window is active,
		/// null when the Billing API or the add-on catalog could not be read. Unknown is never a free plan: callers that offer
		/// a free allowance must not present a paying department's billing outage as a used-up allowance.
		/// </summary>
		Task<bool?> GetActiveAddonStateAsync(int departmentId);

		/// <summary>
		/// Whether the department may send its chatbot traffic to its own LLM provider subscription (bring your own key), and
		/// why not. Advanced Data Protection blocks it on every install, in any protection state except Disabled. Otherwise, on
		/// Resgrid's hosted service it needs an active Enhanced AI add-on (no flag or module checks: the department pays its
		/// provider, and the add-on is what unlocks it); open-source installs, which run without the Billing API and have no
		/// add-on to buy, allow it. A protection or billing state that cannot be read is Unknown, which never allows it.
		/// </summary>
		Task<OwnLlmProviderStatus> GetOwnLlmProviderStatusAsync(int departmentId);
	}

	/// <summary>Result of <see cref="IEnhancedAiAccessService.GetOwnLlmProviderStatusAsync"/>. Only Allowed lets a department's own provider run.</summary>
	public enum OwnLlmProviderStatus
	{
		Allowed = 0,
		AddonRequired = 1,
		DataProtectionEnabled = 2,
		Unknown = 3
	}
}
