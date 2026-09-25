using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public sealed class AdpReleaseService(IAdpAccessStore store, IAdpAuditRepository audit,
		IDepartmentDataProtectionService protection, IProtectedDataGrantService grants,
		IAdpReleaseReceiptService receipts, IProtectedDataBrokerClient broker, ICallsService calls,
		IAuthorizationService authorization, IUserProfileService profiles, IDepartmentsService departments,
		IPermissionsService permissions, IDepartmentGroupsService groups, IPersonnelRolesService roles,
		IPhoneNumberProcesserProvider phoneNumbers) : IAdpReleaseService
	{
		public async Task<bool> EnrollPinAsync(int departmentId, string userId, string grantToken, string pin, CancellationToken cancellationToken = default)
		{
			if (pin == null || !Regex.IsMatch(pin, "^[0-9]{6,12}$")) return false;
			var policy = await protection.GetPolicyByDepartmentIdAsync(departmentId, bypassCache: true);
			if (policy == null || grants.ValidateGrant(grantToken, departmentId, policy.PolicyEpoch, ProtectedDataGrantScopes.Read,
				out var grant) != ProtectedDataGrantValidationOutcome.Valid || grant.UserId != userId || grant.StepUpExempt ||
				grant.MfaAtUtc < DateTime.UtcNow.AddMinutes(-5) || grant.MfaAtUtc > DateTime.UtcNow.AddSeconds(30)) return false;
			var key = PinKey(departmentId, userId);
			var previous = await store.GetAsync(key, cancellationToken);
			var salt = RandomNumberGenerator.GetBytes(32);
			var credential = new PinCredential { Salt = Convert.ToBase64String(salt), Hash = Convert.ToBase64String(HashPin(pin, salt)),
				Generation = Guid.NewGuid().ToString("N") };
			await Audit(departmentId, userId, "pin-enroll", "requested", cancellationToken);
			return await store.SaveAsync(key, JsonConvert.SerializeObject(credential), previous?.Version ?? 0, cancellationToken);
		}

		public async Task<string> CreateChallengeAsync(int departmentId, int callId, string userId, string phone,
			ProtectedDataEgressChannel channel, CancellationToken cancellationToken = default)
		{
			if (!await Eligible(departmentId, callId, userId, phone, channel)) return null;
			var policy = await protection.GetPolicyByDepartmentIdAsync(departmentId, bypassCache: true);
			var egress = await protection.GetEgressPolicyByDepartmentIdAsync(departmentId, bypassCache: true);
			var credentialState = await store.GetAsync(PinKey(departmentId, userId), cancellationToken);
			var credential = credentialState == null ? null : JsonConvert.DeserializeObject<PinCredential>(credentialState.Json);
			if (credential == null || credential.LockedUntilUtc > DateTime.UtcNow) return null;
			var challenge = new Challenge { DepartmentId = departmentId, CallId = callId, UserId = userId,
				PhoneHash = Digest(NormalizePhone(phone)), Channel = channel, Epoch = policy.PolicyEpoch, PinGeneration = credential.Generation,
				ExpiresUtc = DateTime.UtcNow.AddMinutes(Math.Clamp(egress.PinChallengeExpiryMinutes, 1, 10)) };
			var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
			await Audit(departmentId, userId, "pin-challenge", "created", cancellationToken);
			return await store.SaveAsync("challenge:" + id, JsonConvert.SerializeObject(challenge), 0, cancellationToken) ? id : null;
		}

		public async Task<string> ReleaseAsync(string challengeId, string phone, string pin, ProtectedDataEgressChannel channel,
			CancellationToken cancellationToken = default)
		{
			if (challengeId == null || !Regex.IsMatch(challengeId, "^[A-F0-9]{24}$") || pin == null ||
				!Regex.IsMatch(pin, "^[0-9]{6,12}$")) return null;
			var state = await store.GetAsync("challenge:" + challengeId, cancellationToken);
			var challenge = state == null ? null : JsonConvert.DeserializeObject<Challenge>(state.Json);
			if (challenge == null || challenge.Used || challenge.ExpiresUtc <= DateTime.UtcNow || challenge.Channel != channel ||
				challenge.PhoneHash != Digest(NormalizePhone(phone))) return null;
			var dept = challenge.DepartmentId;
			var policy = await protection.GetPolicyByDepartmentIdAsync(dept, bypassCache: true);
			if (policy == null || policy.PolicyEpoch != challenge.Epoch ||
				!await Eligible(dept, challenge.CallId, challenge.UserId, phone, channel)) return null;
			var egress = await protection.GetEgressPolicyByDepartmentIdAsync(dept, bypassCache: true);
			var key = PinKey(dept, challenge.UserId);
			bool verified = false;
			for (var attempt = 0; attempt < 8; attempt++)
			{
				var credentialState = await store.GetAsync(key, cancellationToken);
				var credential = credentialState == null ? null : JsonConvert.DeserializeObject<PinCredential>(credentialState.Json);
				if (credential == null || credential.Generation != challenge.PinGeneration || credential.LockedUntilUtc > DateTime.UtcNow) return null;
				if (credential.LockedUntilUtc.HasValue) { credential.Failures = 0; credential.LockedUntilUtc = null; }
				verified = CryptographicOperations.FixedTimeEquals(HashPin(pin, Convert.FromBase64String(credential.Salt)), Convert.FromBase64String(credential.Hash));
				credential.Failures = verified ? 0 : credential.Failures + 1;
				if (credential.Failures >= Math.Clamp(egress.PinMaxAttempts, 1, 5))
					credential.LockedUntilUtc = DateTime.UtcNow.AddMinutes(Math.Clamp(egress.PinLockoutMinutes, 1, 60));
				if (!await store.SaveAsync(key, JsonConvert.SerializeObject(credential), credentialState.Version, cancellationToken)) { verified = false; continue; }
				await Audit(dept, challenge.UserId, "pin-verify", verified ? "verified" : "denied", cancellationToken);
				break;
			}
			if (!verified) return null;
			challenge.Used = true;
			if (!await store.SaveAsync(state.StateId, JsonConvert.SerializeObject(challenge), state.Version, cancellationToken)) return null;
			// Read fresh, never the outbound queue's old object; authorization was checked immediately above.
			var call = await calls.GetCallByIdAsync(challenge.CallId);
			if (call == null || call.DepartmentId != dept || call.IsDeleted || call.State is (int)CallStates.Closed or (int)CallStates.Cancelled) return null;
			var fields = AdpDispatchRelease.Fields(call);
			var envelopes = fields.Where(f => ProtectedDataEnvelope.HasEnvelopePrefix(f.Value)).ToArray();
			if (envelopes.Length > 0)
			{
				var token = await receipts.IssueAsync(dept, policy.PolicyEpoch, challenge.UserId,
					channel == ProtectedDataEgressChannel.Sms ? "sms-pin" : "voice-pin", envelopes, cancellationToken);
				var result = await broker.DecryptAsync(dept, token, Guid.NewGuid().ToString("N"), envelopes, cancellationToken);
				if (!result.Success || result.Items.Count != envelopes.Length || result.Items.Any(f => f.ErrorCode != null)) return null;
				foreach (var field in fields)
					if (ProtectedDataEnvelope.HasEnvelopePrefix(field.Value))
						field.Value = result.Items.Single(f => f.FieldId == field.FieldId && f.RowKey == field.RowKey).Value;
			}
			var finalPolicy = await protection.GetPolicyByDepartmentIdAsync(dept, bypassCache: true);
			var finalCredential = await store.GetAsync(key, cancellationToken);
			if (challenge.ExpiresUtc <= DateTime.UtcNow || finalPolicy?.PolicyEpoch != challenge.Epoch || finalCredential == null ||
				JsonConvert.DeserializeObject<PinCredential>(finalCredential.Json).Generation != challenge.PinGeneration ||
				!await Eligible(dept, challenge.CallId, challenge.UserId, phone, channel)) return null;
			await Audit(dept, challenge.UserId, "pin-release", "disclosed", cancellationToken);
			var text = string.Join(". ", fields.Select(f => f.Value).Where(v => !string.IsNullOrWhiteSpace(v)));
			if (channel == ProtectedDataEgressChannel.Sms && text.Length > 1200)
			{
				var length = char.IsHighSurrogate(text[1199]) ? 1199 : 1200;
				var profile = await profiles.GetProfileByUserIdAsync(challenge.UserId);
				text = text.Substring(0, length) + " … " + Resgrid.Localization.Areas.User.SystemMessages.SystemMessagesResources.Get("AdpProtectedDispatchNotice", profile?.Language);
			}
			return challenge.ExpiresUtc > DateTime.UtcNow ? text : null;
		}

		private async Task<bool> Eligible(int departmentId, int callId, string userId, string phone, ProtectedDataEgressChannel channel)
		{
			if (channel is not (ProtectedDataEgressChannel.Sms or ProtectedDataEgressChannel.Voice) || string.IsNullOrWhiteSpace(userId) ||
				NormalizePhone(phone).Length < 7 || !await protection.IsProtectionEnforcedAsync(departmentId) ||
				!await authorization.CanUserViewCallAsync(userId, callId)) return false;
			var protectionPolicy = await protection.GetPolicyByDepartmentIdAsync(departmentId, bypassCache: true);
			if (protectionPolicy == null || protectionPolicy.State is not ((int)DepartmentDataProtectionState.Enabled) and not ((int)DepartmentDataProtectionState.Rotating)) return false;
			var policy = await protection.GetEgressPolicyByDepartmentIdAsync(departmentId, bypassCache: true);
			if ((channel == ProtectedDataEgressChannel.Sms ? policy.SmsMode : policy.VoiceMode) != (int)ProtectedDataEgressMode.ProtectedAfterPin ||
				!policy.AcknowledgedOn.HasValue || string.IsNullOrWhiteSpace(policy.AcknowledgementVersion)) return false;
			var call = await calls.GetCallByIdAsync(callId);
			if (call == null || call.DepartmentId != departmentId || call.IsDeleted || call.State is (int)CallStates.Closed or (int)CallStates.Cancelled) return false;
			var member = await departments.GetDepartmentMemberAsync(userId, departmentId, bypassCache: true);
			if (member == null || member.IsDeleted || member.IsDisabled == true) return false;
			var permission = await permissions.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.ViewProtectedCallData)
				?? new Permission { DepartmentId = departmentId, Action = (int)AdpPermissionDefaults.For(PermissionTypes.ViewProtectedCallData) };
			var department = await departments.GetDepartmentByIdAsync(departmentId);
			var group = await groups.GetGroupForUserAsync(userId, departmentId);
			if (!permissions.IsUserAllowed(permission, department?.IsUserAnAdmin(userId) == true,
				group?.IsUserGroupAdmin(userId) == true, await roles.GetRolesForUserAsync(userId, departmentId))) return false;
			var profile = await profiles.GetProfileByUserIdAsync(userId);
			return profile != null && (profile.MobileNumberVerified == true && NormalizePhone(profile.MobileNumber) == NormalizePhone(phone) ||
				channel == ProtectedDataEgressChannel.Voice && profile.HomeNumberVerified == true && NormalizePhone(profile.HomeNumber) == NormalizePhone(phone));
		}
		private Task Audit(int dept, string userId, string operation, string outcome, CancellationToken ct) => audit.AppendAsync(
			new AdpAuditEvent { DepartmentId = dept, ActorId = userId, Layer = "application", Operation = operation, Outcome = outcome }, ct);
		private static string PinKey(int departmentId, string userId) => "pin:" + departmentId + ":" + Digest(userId);
		private string NormalizePhone(string phone)
		{
			// Use the same canonical number as direct SMS delivery, including legacy national formats.
			var parsed = phoneNumbers.Process(phone);
			return parsed?.IsValid == true ? parsed.InternationalNumber : string.Empty;
		}
		private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
		private static byte[] HashPin(string pin, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(pin, salt, 600000, HashAlgorithmName.SHA256, 32);
		private sealed class PinCredential
		{
			public string Salt { get; set; }
			public string Hash { get; set; }
			public string Generation { get; set; }
			public int Failures { get; set; }
			public DateTime? LockedUntilUtc { get; set; }
		}
		private sealed class Challenge
		{
			public int DepartmentId { get; set; }
			public int CallId { get; set; }
			public string UserId { get; set; }
			public string PhoneHash { get; set; }
			public ProtectedDataEgressChannel Channel { get; set; }
			public long Epoch { get; set; }
			public string PinGeneration { get; set; }
			public DateTime ExpiresUtc { get; set; }
			public bool Used { get; set; }
		}
	}

	public static class AdpDispatchRelease
	{
		public static ProtectedFieldOperationItem[] Fields(Call call) => new[]
		{
			new ProtectedFieldOperationItem { FieldId = "calls.name", RowKey = call.CallId.ToString(), Value = call.Name },
			new ProtectedFieldOperationItem { FieldId = "calls.address", RowKey = call.CallId.ToString(), Value = call.Address },
			new ProtectedFieldOperationItem { FieldId = "calls.natureofcall", RowKey = call.CallId.ToString(), Value = call.NatureOfCall }
		};
	}
}
