using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="IMfaCredentialStateService"/>
	public sealed class MfaCredentialStateService : IMfaCredentialStateService
	{
		private readonly IUserPasskeyRepository _passkeys;
		private readonly IUserSessionsRepository _sessions;
		private readonly IDepartmentSsoConfigRepository _ssoConfigs;
		private readonly TimeProvider _time;

		public MfaCredentialStateService(IUserPasskeyRepository passkeys, IUserSessionsRepository sessions, IDepartmentSsoConfigRepository ssoConfigs,
			TimeProvider time)
		{
			_passkeys = passkeys;
			_sessions = sessions;
			_ssoConfigs = ssoConfigs;
			_time = time;
		}

		public async Task<MfaCredentialSnapshot> ResolveAsync(string userId, int departmentId, UserSessionClientApplication client,
			MfaEvidenceMethod method, string factorReference, long authenticationGeneration, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(factorReference))
				return null;

			switch (method)
			{
				case MfaEvidenceMethod.Passkey:
				{
					// A passkey grant is only ever for the client the passkey is bound to (plan section 3 item 14).
					var passkey = await PasskeyForReferenceAsync(factorReference, cancellationToken);
					return passkey != null && Owns(passkey, userId) && passkey.IsActive && passkey.ClientApplication == (int)client
						? new MfaCredentialSnapshot { CredentialId = factorReference, StateVersion = passkey.StateVersion }
						: null;
				}
				case MfaEvidenceMethod.PasskeyApproval:
				{
					// The approving Responder passkey (approval still on) and its personal Responder session must both still
					// count; the passkey's state version moves with any change to either credential.
					if (!MfaApprovalRequest.TryParseFactorReference(factorReference, out var passkeyId, out _) ||
						!await ApprovalApprovers.IsValidAsync(_passkeys, _sessions, userId, factorReference, authenticationGeneration, Now, cancellationToken))
						return null;

					var passkey = await _passkeys.GetAsync(passkeyId, cancellationToken);
					return passkey == null ? null : new MfaCredentialSnapshot { CredentialId = factorReference, StateVersion = passkey.StateVersion };
				}
				case MfaEvidenceMethod.Federated:
				{
					// The department's tested provider step-up mapping, at the version the round trip was checked against.
					var config = await TestedConfigAsync(departmentId);
					return config != null && string.Equals(factorReference,
						FederatedMfaMapping.FactorReferenceFor(config.DepartmentSsoConfigId, config.FederatedMfaMappingVersion), StringComparison.Ordinal)
						? new MfaCredentialSnapshot { CredentialId = factorReference, StateVersion = config.FederatedMfaMappingVersion }
						: null;
				}
				default:
					return null;
			}
		}

		public async Task<bool> IsCurrentAsync(ProtectedDataGrant grant, CancellationToken cancellationToken = default)
		{
			if (grant == null || grant.Version < 2 || grant.AuthenticationGeneration == null || grant.MfaStateVersion == null)
				return false;

			var method = grant.MfaMethod switch
			{
				ProtectedDataGrantMfaMethods.Passkey => MfaEvidenceMethod.Passkey,
				ProtectedDataGrantMfaMethods.PasskeyApproval => MfaEvidenceMethod.PasskeyApproval,
				ProtectedDataGrantMfaMethods.Federated => MfaEvidenceMethod.Federated,
				_ => (MfaEvidenceMethod?)null
			};
			if (method == null)
				return false;

			var current = await ResolveAsync(grant.UserId, grant.DepartmentId, (UserSessionClientApplication)grant.ClientApp, method.Value,
				grant.MfaCredentialId, grant.AuthenticationGeneration.Value, cancellationToken);
			return current != null && current.StateVersion == grant.MfaStateVersion.Value &&
				string.Equals(current.CredentialId, grant.MfaCredentialId, StringComparison.Ordinal);
		}

		private DateTime Now => _time.GetUtcNow().UtcDateTime;

		private static bool Owns(UserPasskey passkey, string userId) => string.Equals(passkey.UserId, userId, StringComparison.OrdinalIgnoreCase);

		private Task<UserPasskey> PasskeyForReferenceAsync(string factorReference, CancellationToken cancellationToken)
		{
			const string prefix = "passkey:";
			if (!factorReference.StartsWith(prefix, StringComparison.Ordinal) || factorReference.Length <= prefix.Length)
				return Task.FromResult<UserPasskey>(null);

			return _passkeys.GetAsync(factorReference.Substring(prefix.Length), cancellationToken);
		}

		private async Task<DepartmentSsoConfig> TestedConfigAsync(int departmentId)
		{
			if (departmentId <= 0)
				return null;

			var config = (await _ssoConfigs.GetAllByDepartmentIdAsync(departmentId))?.FirstOrDefault(c => c.IsEnabled);
			return FederatedMfaMapping.IsTested(config) ? config : null;
		}
	}
}
