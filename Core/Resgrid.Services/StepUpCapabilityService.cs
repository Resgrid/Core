using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="IStepUpCapabilityService"/>
	public sealed class StepUpCapabilityService : IStepUpCapabilityService
	{
		private readonly IStepUpPolicyService _policy;
		private readonly IMfaPolicyService _mfaPolicy;
		private readonly IUsersService _users;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentMembersRepository _members;
		private readonly IPasskeyService _passkeys;
		private readonly IMfaApprovalService _approvals;
		private readonly IDepartmentSsoService _departmentSso;
		private readonly IExternalIdentityLinkService _identityLinks;
		private readonly ISsoBrokerService _broker;

		public StepUpCapabilityService(IStepUpPolicyService policy, IMfaPolicyService mfaPolicy, IUsersService users, IDepartmentsService departments,
			IDepartmentMembersRepository members, IPasskeyService passkeys, IMfaApprovalService approvals, IDepartmentSsoService departmentSso,
			IExternalIdentityLinkService identityLinks, ISsoBrokerService broker)
		{
			_policy = policy;
			_mfaPolicy = mfaPolicy;
			_users = users;
			_departments = departments;
			_members = members;
			_passkeys = passkeys;
			_approvals = approvals;
			_departmentSso = departmentSso;
			_identityLinks = identityLinks;
			_broker = broker;
		}

		public async Task<StepUpCapabilities> GetCapabilitiesAsync(int departmentId, string userId, UserSessionClientApplication client, StepUpArea area,
			CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return new StepUpCapabilities();

			var user = _users.GetUserById(userId, true);
			if (user == null)
				return new StepUpCapabilities();

			var scope = StepUpAreas.MfaScope(area);
			var passkey = await _mfaPolicy.IsMethodAcceptedAsync(departmentId, scope, MfaEvidenceMethod.Passkey, cancellationToken) &&
				await _passkeys.HasActiveForClientAsync(userId, client, cancellationToken);
			var approval = await _mfaPolicy.IsMethodAcceptedAsync(departmentId, scope, MfaEvidenceMethod.PasskeyApproval, cancellationToken) &&
				await _approvals.IsAvailableAsync(userId, client, cancellationToken);
			var provider = await _mfaPolicy.IsMethodAcceptedAsync(departmentId, scope, MfaEvidenceMethod.Federated, cancellationToken) &&
				await _departmentSso.IsFederatedMfaAvailableAsync(departmentId, userId, cancellationToken);

			return new StepUpCapabilities
			{
				TotpEnrolled = user.TwoFactorEnabled,
				PasskeyForClient = passkey,
				ApprovalAvailable = approval,
				ProviderMfaAvailable = provider,
				PasswordAvailable = !string.IsNullOrEmpty(user.PasswordHash) && await _identityLinks.IsLocalLoginAllowedAsync(userId, departmentId, cancellationToken),
				SsoAvailable = await DepartmentHasBrokeredSsoAsync(departmentId, cancellationToken) && await IsLinkedAsync(departmentId, userId, null)
			};
		}

		public async Task<StepUpRequirement> DescribeAsync(int departmentId, string userId, UserSessionClientApplication client, StepUpArea area,
			CancellationToken cancellationToken = default)
		{
			var rule = await _policy.GetRuleAsync(departmentId, area, cancellationToken);
			StepUpCapabilities capabilities;
			try
			{
				capabilities = await GetCapabilitiesAsync(departmentId, userId, client, area, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// Advisory: without it the refusal still names the area and the allowed kinds; the verify endpoints recheck everything.
				Logging.LogException(ex, "Step-up capabilities could not be read.");
				capabilities = new StepUpCapabilities();
			}

			return StepUpRequirement.For(rule, capabilities);
		}

		public async Task<IReadOnlyList<StepUpReadiness>> GetReadinessAsync(int departmentId, int maxListed = 50, CancellationToken cancellationToken = default) =>
			await GetReadinessAsync(departmentId, await _policy.GetRulesAsync(departmentId, cancellationToken), maxListed, cancellationToken);

		public async Task<IReadOnlyList<StepUpReadiness>> GetReadinessAsync(int departmentId, IReadOnlyList<StepUpRule> rules, int maxListed = 50,
			CancellationToken cancellationToken = default)
		{
			rules ??= Array.Empty<StepUpRule>();
			maxListed = Math.Clamp(maxListed, 0, 1000);
			var required = rules.Where(r => r != null && r.Required).ToList();
			if (departmentId <= 0 || required.Count == 0)
				return rules.Where(r => r != null).Select(r => new StepUpReadiness { Rule = r }).ToList();

			var members = ((await _members.GetAllDepartmentMembersUnlimitedAsync(departmentId)) ?? Enumerable.Empty<DepartmentMember>())
				.Where(m => DepartmentMemberStateHelper.IsCurrentMember(m, departmentId))
				.GroupBy(m => m.UserId, StringComparer.OrdinalIgnoreCase)
				.Select(g => g.First())
				.ToList();
			var users = ((await _departments.GetAllUsersForDepartmentAsync(departmentId, true, true)) ?? new List<Model.Identity.IdentityUser>())
				.Where(u => u != null && !string.IsNullOrWhiteSpace(u.Id))
				.GroupBy(u => u.Id, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			var names = ((await _departments.GetAllPersonnelNamesForDepartmentAsync(departmentId)) ?? new List<PersonName>())
				.Where(n => n != null && !string.IsNullOrWhiteSpace(n.UserId))
				.GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

			var anyPassword = required.Any(r => r.Allows(StepUpMethodClasses.Password));
			var anySso = required.Any(r => r.Allows(StepUpMethodClasses.Sso)) && await DepartmentHasBrokeredSsoAsync(departmentId, cancellationToken);

			// A member without an authenticator is the only one who can be stuck: a passkey and Responder approval both need one to register,
			// and the provider's MFA needs the department's tested mapping. The other kinds are looked up only for these members.
			var federatedOn = required.Any(r => r.Area != StepUpArea.ProtectedData) &&
				await _mfaPolicy.IsMethodAcceptedAsync(departmentId, MfaMethodScope.Login, MfaEvidenceMethod.Federated, cancellationToken);
			var federatedAdpOn = required.Any(r => r.Area == StepUpArea.ProtectedData) &&
				await _mfaPolicy.IsMethodAcceptedAsync(departmentId, MfaMethodScope.Adp, MfaEvidenceMethod.Federated, cancellationToken);

			var gaps = required.ToDictionary(r => r.Area, _ => new List<StepUpMemberGap>());
			foreach (var member in members)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!users.TryGetValue(member.UserId, out var user) || user.TwoFactorEnabled)
					continue;

				bool? password = null, sso = null, provider = null, providerAdp = null;
				foreach (var rule in required)
				{
					var federatedForRule = rule.Area == StepUpArea.ProtectedData ? federatedAdpOn : federatedOn;
					if (federatedForRule)
					{
						if (rule.Area == StepUpArea.ProtectedData)
							providerAdp ??= await _departmentSso.IsFederatedMfaAvailableAsync(departmentId, member.UserId, cancellationToken);
						else
							provider ??= await _departmentSso.IsFederatedMfaAvailableAsync(departmentId, member.UserId, cancellationToken);
						if ((rule.Area == StepUpArea.ProtectedData ? providerAdp : provider) == true)
							continue;
					}

					if (anyPassword && rule.Allows(StepUpMethodClasses.Password))
					{
						password ??= !string.IsNullOrEmpty(user.PasswordHash) && await _identityLinks.IsLocalLoginAllowedAsync(member.UserId, departmentId, cancellationToken);
						if (password == true)
							continue;
					}

					if (anySso && rule.Allows(StepUpMethodClasses.Sso))
					{
						sso ??= await IsLinkedAsync(departmentId, member.UserId, member);
						if (sso == true)
							continue;
					}

					gaps[rule.Area].Add(new StepUpMemberGap
					{
						UserId = member.UserId,
						Name = NameOf(names, member.UserId),
						Reason = rule.Allows(StepUpMethodClasses.Password) ? StepUpGapReasons.NoPassword
							: rule.Allows(StepUpMethodClasses.Sso) ? StepUpGapReasons.NoSsoLink
							: StepUpGapReasons.NoSecondFactor
					});
				}
			}

			return rules.Where(r => r != null).Select(r =>
			{
				var list = gaps.TryGetValue(r.Area, out var found) ? found.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList() : new List<StepUpMemberGap>();
				return new StepUpReadiness { Rule = r, MemberCount = list.Count, Members = list.Take(maxListed).ToList() };
			}).ToList();
		}

		private static string NameOf(IReadOnlyDictionary<string, PersonName> names, string userId)
		{
			if (names.TryGetValue(userId, out var name))
			{
				var full = $"{name.FirstName} {name.LastName}".Trim();
				if (!string.IsNullOrWhiteSpace(full))
					return full;
			}

			return userId;
		}

		private async Task<bool> DepartmentHasBrokeredSsoAsync(int departmentId, CancellationToken cancellationToken)
		{
			if (!_broker.IsEnabled)
				return false;
			var configs = await _departmentSso.GetSsoConfigsForDepartmentAsync(departmentId, cancellationToken);
			return configs?.Any(c => c != null && c.IsEnabled && _broker.SupportsBrokered(c)) == true;
		}

		/// <summary>An active link to this department's provider, or a legacy one recorded on the membership (the broker's callback finds both).</summary>
		private async Task<bool> IsLinkedAsync(int departmentId, string userId, DepartmentMember member)
		{
			var links = await _identityLinks.GetActiveLinksAsync(userId);
			if (links?.Any(l => l != null && l.IsActive && l.DepartmentId == departmentId) == true)
				return true;

			member ??= await _members.GetDepartmentMemberByDepartmentIdAndUserIdAsync(departmentId, userId);
			return member != null && !member.IsDeleted && !string.IsNullOrWhiteSpace(member.ExternalSsoId);
		}
	}
}
