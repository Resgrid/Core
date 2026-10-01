using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="IMfaPolicyService"/>
	public sealed class MfaPolicyService : IMfaPolicyService
	{
		private readonly IDepartmentSsoService _departmentSso;
		private readonly IUserMfaStateRepository _mfaState;
		private readonly IPasskeyFeatureGates _gates;

		public MfaPolicyService(IDepartmentSsoService departmentSso, IUserMfaStateRepository mfaState, IPasskeyFeatureGates gates)
		{
			_departmentSso = departmentSso;
			_mfaState = mfaState;
			_gates = gates;
		}

		public async Task<bool> IsRequireMfaEnforcedAsync(int? departmentId, CancellationToken cancellationToken = default) =>
			TwoFactorConfig.RequireMfaEnforcementEnabled && await DepartmentRequiresMfaAsync(departmentId, cancellationToken);

		public async Task<bool> DepartmentRequiresMfaAsync(int? departmentId, CancellationToken cancellationToken = default) =>
			(await PolicyForAsync(departmentId, cancellationToken)).RequireMfa;

		public async Task<MfaMethodChoice> GetMethodChoiceAsync(string userId, bool totpEnrolled, int? departmentId, MfaMethodScope scope, bool passkeyEnrolled = false,
			bool federatedEnrolled = false, bool approvalEnrolled = false, CancellationToken cancellationToken = default)
		{
			// TOTP, a passkey bound to the calling client, Responder approval (an eligible Responder of the user's), and
			// provider step-up (a tested mapping and an SSO-linked account); the caller says which the user has. The allowed
			// list follows the department's switches.
			var enrolled = new List<string>();
			if (totpEnrolled)
				enrolled.Add(MfaMethodNames.Totp);
			if (passkeyEnrolled)
				enrolled.Add(MfaMethodNames.Passkey);
			if (approvalEnrolled)
				enrolled.Add(MfaMethodNames.PasskeyApproval);
			if (federatedEnrolled)
				enrolled.Add(MfaMethodNames.Federated);
			var allowed = AllowedMethods(await PolicyForAsync(departmentId, cancellationToken), scope, _gates);
			var usable = allowed.Where(enrolled.Contains).ToList();

			string stored = null;
			if (usable.Count > 1 && !string.IsNullOrWhiteSpace(userId))
			{
				try
				{
					var method = await _mfaState.GetPreferredMethodAsync(userId, cancellationToken);
					stored = method == null ? null : MfaMethodNames.From((MfaEvidenceMethod)method.Value);
				}
				catch (Exception ex)
				{
					// A display default only: without it the first usable method is shown.
					Logging.LogException(ex, "MFA method preference lookup failed.");
				}
			}

			return new MfaMethodChoice
			{
				EnrolledMethods = enrolled,
				AllowedMethods = allowed,
				Preferred = stored != null && usable.Contains(stored) ? stored : usable.FirstOrDefault()
			};
		}

		public async Task<bool> IsMethodAcceptedAsync(int? departmentId, MfaMethodScope scope, MfaEvidenceMethod method,
			CancellationToken cancellationToken = default)
		{
			if (method == MfaEvidenceMethod.Totp)
				return true;

			var name = MfaMethodNames.From(method);
			if (name == null || !AllowedMethods(await PolicyForAsync(departmentId, cancellationToken), scope, _gates).Contains(name))
				return false;

			// Provider step-up counts only while the department's mapping stays enabled and tested (plan section 7.8); a
			// mapping change also revokes what the old version verified.
			return method != MfaEvidenceMethod.Federated ||
				(departmentId is > 0 && await _departmentSso.GetTestedFederatedMfaConfigAsync(departmentId.Value, cancellationToken) != null);
		}

		public async Task<bool> IsEvidenceAcceptedAsync(int? departmentId, MfaMethodScope scope, MfaEvidence evidence,
			CancellationToken cancellationToken = default)
		{
			if (evidence == null || !await IsMethodAcceptedAsync(departmentId, scope, (MfaEvidenceMethod)evidence.Method, cancellationToken))
				return false;

			return scope != MfaMethodScope.Adp || evidence.Purpose != (int)MfaEvidencePurpose.SharedUnlock ||
				(await PolicyForAsync(departmentId, cancellationToken)).AcceptRecentUnlockMfaForAdp;
		}

		/// <summary>
		/// The second factors a department accepts for a scope (plan sections 7.6 and 10.1). TOTP always. A passkey where
		/// the deployment accepts passkeys and the scope's passkey switch is on. Responder approval where approval is
		/// deployed and allowed and the row's passkey switch is on, never for security changes or account factors.
		/// Provider step-up where deployed and the scope's federated switch is on, never for account factors.
		/// </summary>
		public static IReadOnlyList<string> AllowedMethods(DepartmentSecurityPolicy policy, MfaMethodScope scope, IPasskeyFeatureGates gates)
		{
			policy ??= new DepartmentSecurityPolicy();
			var allowed = new List<string> { MfaMethodNames.Totp };

			var passkey = scope switch
			{
				MfaMethodScope.Login or MfaMethodScope.SecurityChange => gates.LoginAcceptanceEnabled && policy.AllowPasskeysForLoginMfa,
				MfaMethodScope.Adp => gates.AdpAcceptanceEnabled && policy.AllowPasskeysForAdp,
				MfaMethodScope.Account => gates.LoginAcceptanceEnabled,
				_ => false
			};
			if (passkey)
				allowed.Add(MfaMethodNames.Passkey);

			if (passkey && gates.ResponderApprovalEnabled && policy.AllowResponderApproval &&
				(scope == MfaMethodScope.Login || scope == MfaMethodScope.Adp))
				allowed.Add(MfaMethodNames.PasskeyApproval);

			var federated = scope switch
			{
				MfaMethodScope.Login or MfaMethodScope.SecurityChange => policy.AllowFederatedMfaForLoginMfa,
				MfaMethodScope.Adp => policy.AllowFederatedMfaForAdp,
				_ => false
			};
			if (federated && gates.ProviderStepUpEnabled)
				allowed.Add(MfaMethodNames.Federated);

			return allowed;
		}

		/// <summary>
		/// The department's policy, or the defaults when it has none (a department with no row is unaffected). A failed
		/// read is thrown, never mistaken for the permissive defaults.
		/// </summary>
		private async Task<DepartmentSecurityPolicy> PolicyForAsync(int? departmentId, CancellationToken cancellationToken)
		{
			if (departmentId is not > 0)
				return new DepartmentSecurityPolicy();

			return await _departmentSso.GetSecurityPolicyForDepartmentAsync(departmentId.Value, cancellationToken)
				?? new DepartmentSecurityPolicy { DepartmentId = departmentId.Value };
		}
	}
}
