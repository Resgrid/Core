using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The per-client relying parties from PasskeyConfig.RelyingParties, validated once (workbook section 5). A client with
	/// no valid entry has no passkeys; nothing ever falls back to another client's RP.
	/// </summary>
	public interface IRelyingPartyRegistry
	{
		PasskeyReadiness Readiness { get; }

		/// <summary>The client's relying party, or null when it has none or the configuration is not ready.</summary>
		RelyingPartyDescriptor Get(UserSessionClientApplication client);
	}

	/// <summary>
	/// Effective rollout gates: a PasskeyConfig gate counts only when the relying-party configuration is ready, so a
	/// half-configured deployment fails closed.
	/// </summary>
	public interface IPasskeyFeatureGates
	{
		bool RegistrationEnabled { get; }
		bool LoginAcceptanceEnabled { get; }
		bool AdpAcceptanceEnabled { get; }
		bool EmitGrantV2 { get; }
		bool SharedDeviceModeEnabled { get; }
		bool ResponderApprovalEnabled { get; }
		bool ProviderStepUpEnabled { get; }
	}
}
