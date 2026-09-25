using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;

namespace Resgrid.Services.AdminAssist
{
	public sealed class SecurityImpactService(IAdminAssistAccessService access, IAdminAssistRepository repository,
		IAdminAssistCatalog catalog, ISecurityImpactStore store, TimeProvider clock) : ISecurityImpactService
	{
		public static IReadOnlyList<string> Supported { get; } = Array.AsReadOnly(new[] {
			"RequireMfa", "RequireSso", "SessionTimeoutMinutes", "MaxConcurrentSessions", "PasswordExpirationDays", "MinPasswordLength" });
		public async Task<ConfigurationImpactReport> PreviewAsync(AdminAssistActor actor, ConfigurationImpactRequest request, CancellationToken ct = default)
		{
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
			var entry = catalog.Settings.SingleOrDefault(s => s.Id == request?.SettingId && s.Binding.StartsWith("DepartmentSecurityPolicy.", StringComparison.Ordinal));
			var field = entry?.Binding.Substring("DepartmentSecurityPolicy.".Length);
			if (field == null || !Supported.Contains(field)) throw new ArgumentException("Unsupported security proposal.");
			var boolean = field is "RequireMfa" or "RequireSso";
			if (boolean ? !request.Boolean.HasValue || request.Number.HasValue : request.Boolean.HasValue || !request.Number.HasValue ||
				request.Number != decimal.Truncate(request.Number.Value) || request.Number < (field == "MinPasswordLength" ? 8 : 0) ||
				request.Number > (field == "MinPasswordLength" ? 128 : field == "SessionTimeoutMinutes" ? 43200 : field == "MaxConcurrentSessions" ? 1000 : 36500))
				throw new ArgumentException("Invalid security proposal.");
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(Config.AdminAssistConfig.SnapshotTimeoutSeconds, 1, 60))); ct = timeout.Token;
			var revision = (await repository.GetConfigurationRevisionAsync(actor.DepartmentId, ct)).ToString(CultureInfo.InvariantCulture);
			if (request.ExpectedRevision != revision) throw new AdminAssistConcurrencyException();
			var now = clock.GetUtcNow().UtcDateTime;
			var metrics = new List<ConfigurationImpactMetric>();
			try
			{
				var current = await ReadPolicyAsync(actor.DepartmentId, ct);
				var proposed = Copy(current); Apply(proposed, field, request);
				var bound = Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000);
				var input = await store.ReadSecurityImpactAsync(actor.DepartmentId, now, bound, ct);
				Validate(input, actor.DepartmentId, bound, now);
				var gate = Config.SessionSecurityConfig.DepartmentSessionPolicyEnforcementAfterUtc;
				if (JsonConvert.SerializeObject(current) != JsonConvert.SerializeObject(await ReadPolicyAsync(actor.DepartmentId, ct)) ||
					JsonConvert.SerializeObject(input) != JsonConvert.SerializeObject(await store.ReadSecurityImpactAsync(actor.DepartmentId, now, bound, ct)) ||
					gate != Config.SessionSecurityConfig.DepartmentSessionPolicyEnforcementAfterUtc) throw new AdminAssistConcurrencyException();
				void Add(string key, decimal before, decimal after) => metrics.Add(new("Impact.Security" + key, EvidenceState.Known, before, after));
				void Unknown(string key) => metrics.Add(new("Impact.Security" + key, EvidenceState.Unknown, null, null, "NotVerified"));
				Add("Members", input.Members.Count, input.Members.Count);
				Add("IdentityUnknown", input.Members.Count(m => !m.TwoFactorEnabled.HasValue), input.Members.Count(m => !m.TwoFactorEnabled.HasValue));
				if (boolean) Add("PolicyValue", field == "RequireMfa" ? current.RequireMfa ? 1 : 0 : current.RequireSso ? 1 : 0, request.Boolean.Value ? 1 : 0);
				else Add("PolicyValue", Value(current, field), Value(proposed, field));
				if (field == "RequireMfa")
				{
					var missing = input.Members.Count(m => m.TwoFactorEnabled == false); var unknown = input.Members.Count(m => !m.TwoFactorEnabled.HasValue);
					Add("MfaEnrollmentMinimum", current.RequireMfa ? missing : 0, proposed.RequireMfa ? missing : 0);
					Add("MfaEnrollmentMaximum", current.RequireMfa ? missing + unknown : 0, proposed.RequireMfa ? missing + unknown : 0);
					Add("MfaCompletionRequired", DepartmentSecurityPolicyDecisions.RequiresMfaCompletion(current.RequireMfa, false) ? input.Members.Count : 0,
						DepartmentSecurityPolicyDecisions.RequiresMfaCompletion(proposed.RequireMfa, false) ? input.Members.Count : 0);
					Unknown("RecoveryReadiness");
				}
				if (field == "RequireSso")
				{
					Add("EnabledProviders", input.EnabledSsoProviders, input.EnabledSsoProviders);
					Add("PasswordPathBlocked", DepartmentSecurityPolicyDecisions.BlocksPasswordLogin(current.RequireSso, input.EnabledSsoProviders > 0, false) ? input.Members.Count : 0,
						DepartmentSecurityPolicyDecisions.BlocksPasswordLogin(proposed.RequireSso, input.EnabledSsoProviders > 0, false) ? input.Members.Count : 0);
					Add("SsoSafetyValve", current.RequireSso && input.EnabledSsoProviders == 0 ? 1 : 0, proposed.RequireSso && input.EnabledSsoProviders == 0 ? 1 : 0);
					Unknown("ProviderAndRecovery");
				}
				if (field == "PasswordExpirationDays")
				{
					Add("UntrackedPasswordAge", input.Members.Count(m => !m.PasswordLastSetOn.HasValue), input.Members.Count(m => !m.PasswordLastSetOn.HasValue));
					Add("ExpiredPasswordAge", input.Members.Count(m => DepartmentSecurityPolicyDecisions.PasswordExpired(current.PasswordExpirationDays, m.PasswordLastSetOn, now)),
						input.Members.Count(m => DepartmentSecurityPolicyDecisions.PasswordExpired(proposed.PasswordExpirationDays, m.PasswordLastSetOn, now)));
				}
				if (field == "MinPasswordLength")
				{
					Add("MinimumLength", DepartmentSecurityPolicyDecisions.MinimumPasswordLength(current.MinPasswordLength), DepartmentSecurityPolicyDecisions.MinimumPasswordLength(proposed.MinPasswordLength));
					Unknown("ExistingPasswordCompliance");
				}
				if (field is "SessionTimeoutMinutes" or "MaxConcurrentSessions")
				{
					var parsed = DepartmentSecurityPolicyDecisions.TryGetSessionGate(gate, out var gateUtc);
					Add("ActiveSessionSample", input.Sessions.Count, input.Sessions.Count);
					Add("SessionGateActive", parsed && now >= gateUtc ? 1 : 0, parsed && now >= gateUtc ? 1 : 0);
					var managed = input.Sessions.Where(s => parsed && s.CreatedOn >= gateUtc).ToArray();
					Add("ManagedSessions", managed.Length, managed.Length);
					if (field == "SessionTimeoutMinutes")
					{
						var validGeneration = managed.Where(s => s.CurrentGeneration.HasValue && s.AuthenticationGeneration == s.CurrentGeneration).ToArray();
						Add("IdleExpiryCandidates", validGeneration.Count(s => DepartmentSecurityPolicyDecisions.IdleExpired(current.SessionTimeoutMinutes, s.LastActiveOn, now)),
							validGeneration.Count(s => DepartmentSecurityPolicyDecisions.IdleExpired(proposed.SessionTimeoutMinutes, s.LastActiveOn, now)));
						Unknown("ActualReauthentication");
					}
					else
					{
						int AtLimit(int limit) => parsed && now >= gateUtc && limit > 0 ? managed.GroupBy(s => s.UserId, StringComparer.OrdinalIgnoreCase).Count(g => g.Count() >= limit) : 0;
						Add("AtSessionLimit", AtLimit(current.MaxConcurrentSessions), AtLimit(proposed.MaxConcurrentSessions));
					}
				}
			}
			catch (AdminAssistConcurrencyException) { throw; }
			catch (UnauthorizedAccessException) { throw; }
			catch (OperationCanceledException) { throw; }
			catch (Exception) { metrics.Clear(); metrics.Add(new("Impact.SecurityMembers", EvidenceState.Unknown, null, null, "SourceUnavailableOrBoundExceeded")); }
			if ((await repository.GetConfigurationRevisionAsync(actor.DepartmentId, ct)).ToString(CultureInfo.InvariantCulture) != revision) throw new AdminAssistConcurrencyException();
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
			return new(entry.Id, revision, now, "security-policy-impact-v1", entry.Impact, metrics, Array.Empty<ConfigurationImpactRule>(),
				new[] { "Impact.NoMutation", "Impact.SecurityScope", "Impact.Security" + field + "Scope", "Impact.Window" }, entry.Location.Url);
		}
		private async Task<DepartmentSecurityPolicy> ReadPolicyAsync(int departmentId, CancellationToken ct)
		{
			var row = await store.ReadSecurityPolicyAsync(departmentId, ct);
			if (row != null && row.DepartmentId != departmentId) throw new InvalidOperationException();
			return Copy(row ?? new DepartmentSecurityPolicy { DepartmentId = departmentId });
		}
		private static DepartmentSecurityPolicy Copy(DepartmentSecurityPolicy p) => new() { DepartmentId = p.DepartmentId, RequireMfa = p.RequireMfa, RequireSso = p.RequireSso,
			SessionTimeoutMinutes = p.SessionTimeoutMinutes, MaxConcurrentSessions = p.MaxConcurrentSessions, PasswordExpirationDays = p.PasswordExpirationDays, MinPasswordLength = p.MinPasswordLength };
		private static decimal Value(DepartmentSecurityPolicy p, string field) => field switch { "SessionTimeoutMinutes" => p.SessionTimeoutMinutes,
			"MaxConcurrentSessions" => p.MaxConcurrentSessions, "PasswordExpirationDays" => p.PasswordExpirationDays, "MinPasswordLength" => p.MinPasswordLength, _ => throw new ArgumentException() };
		private static void Apply(DepartmentSecurityPolicy p, string field, ConfigurationImpactRequest request)
		{
			switch (field) { case "RequireMfa": p.RequireMfa = request.Boolean.Value; break; case "RequireSso": p.RequireSso = request.Boolean.Value; break;
				case "SessionTimeoutMinutes": p.SessionTimeoutMinutes = (int)request.Number.Value; break; case "MaxConcurrentSessions": p.MaxConcurrentSessions = (int)request.Number.Value; break;
				case "PasswordExpirationDays": p.PasswordExpirationDays = (int)request.Number.Value; break; case "MinPasswordLength": p.MinPasswordLength = (int)request.Number.Value; break; }
		}
		private static void Validate(SecurityImpactEvidence input, int departmentId, int bound, DateTime now)
		{
			if (input?.Members == null || input.Sessions == null || input.Members.Count > bound || input.Sessions.Count > bound || input.EnabledSsoProviders < 0 || input.EnabledSsoProviders > bound ||
				input.Members.Any(m => m == null || m.DepartmentId != departmentId || m.MemberId <= 0 || string.IsNullOrWhiteSpace(m.UserId) || m.PasswordLastSetOn > now) ||
				input.Members.Select(m => m.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.Members.Count ||
				input.Sessions.Any(s => s == null || s.DepartmentId != departmentId || string.IsNullOrWhiteSpace(s.Id) || !input.Members.Any(m => string.Equals(m.UserId, s.UserId, StringComparison.OrdinalIgnoreCase)) ||
					s.CreatedOn == default || s.CreatedOn > now || s.LastActiveOn < s.CreatedOn || s.LastActiveOn > now || s.ExpiresOn <= now) ||
				input.Sessions.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != input.Sessions.Count) throw new InvalidOperationException();
		}
	}
}
