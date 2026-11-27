using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="IStepUpPolicyService"/>
	public sealed class StepUpPolicyService : IStepUpPolicyService
	{
		private readonly IDepartmentSettingsService _settings;
		private readonly IMfaEvidenceService _evidence;
		private readonly IMfaPolicyService _mfaPolicy;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IEventAggregator _events;
		private readonly TimeProvider _time;

		public StepUpPolicyService(IDepartmentSettingsService settings, IMfaEvidenceService evidence, IMfaPolicyService mfaPolicy,
			IDepartmentDataProtectionService protection, IEventAggregator events, TimeProvider time)
		{
			_settings = settings;
			_evidence = evidence;
			_mfaPolicy = mfaPolicy;
			_protection = protection;
			_events = events;
			_time = time;
		}

		/// <summary>The window of the member-facing areas: the signing window every L2 signature has used (15 minutes by default).</summary>
		public static int SigningWindowMinutes => Math.Max(1, TwoFactorConfig.StepUpVerificationWindowMinutes);

		// ── Rules ─────────────────────────────────────────────────────────────────────

		public async Task<IReadOnlyList<StepUpRule>> GetRulesAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var (stored, fallback) = await ReadStoredAsync(departmentId);
			var readAndSign = await ReadAndSignRequiresStepUpAsync(departmentId);
			var adpWindow = await ProtectedDataWindowAsync(departmentId);
			return StepUpAreas.All.Select(area => Resolve(area, stored, fallback, readAndSign, area == StepUpArea.ProtectedData ? adpWindow : SigningWindowMinutes))
				.ToList();
		}

		public async Task<StepUpRule> GetRuleAsync(int departmentId, StepUpArea area, CancellationToken cancellationToken = default)
		{
			if (!StepUpAreas.IsKnown(area))
				throw new ArgumentOutOfRangeException(nameof(area));

			var (stored, fallback) = await ReadStoredAsync(departmentId);
			var readAndSign = area == StepUpArea.Acknowledgments && await ReadAndSignRequiresStepUpAsync(departmentId);
			var window = area == StepUpArea.ProtectedData ? await ProtectedDataWindowAsync(departmentId) : SigningWindowMinutes;
			return Resolve(area, stored, fallback, readAndSign, window);
		}

		/// <summary>
		/// One area's rule from the stored choices (pure, for tests). Signatures and approvals are required unless the department made them
		/// optional; read-and-sign follows the Protocols switch; protected data is always required here (the ADP exemptions decide per app).
		/// An unreadable policy is the defaults: required where it was, a second factor only.
		/// </summary>
		public static StepUpRule Resolve(StepUpArea area, DepartmentStepUpPolicy stored, bool fallback, bool readAndSignRequiresStepUp, int windowMinutes)
		{
			var setting = fallback ? null : stored?.For(area);
			var required = area switch
			{
				StepUpArea.Acknowledgments => readAndSignRequiresStepUp,
				StepUpArea.ProtectedData => true,
				_ => setting?.Optional != true
			};
			var methods = StepUpMethodClasses.Mfa | (StepUpMethodClasses)((setting?.AlternativeMethods ?? 0) & (int)(StepUpMethodClasses.Sso | StepUpMethodClasses.Password));
			return new StepUpRule { Area = area, Required = required, Methods = methods, WindowMinutes = Math.Max(1, windowMinutes), FromFallback = fallback };
		}

		/// <summary>The stored choices, never cached; an unreadable row (or a failed read) is reported so the defaults apply.</summary>
		private async Task<(DepartmentStepUpPolicy Stored, bool Fallback)> ReadStoredAsync(int departmentId)
		{
			if (departmentId <= 0)
				return (new DepartmentStepUpPolicy(), false);

			try
			{
				var setting = await _settings.GetSettingByTypeAsync(departmentId, DepartmentSettingTypes.StepUpPolicy);
				if (string.IsNullOrWhiteSpace(setting?.Setting))
					return (new DepartmentStepUpPolicy(), false);

				var stored = ObjectSerialization.Deserialize<DepartmentStepUpPolicy>(setting.Setting);
				return stored == null ? (new DepartmentStepUpPolicy(), true) : (stored.Normalize(), false);
			}
			catch (Exception ex)
			{
				// Fail closed: an unknown policy is today's behaviour (step-up required, a second factor only), never a relaxed one.
				Logging.LogException(ex, $"Step-up policy for department {departmentId} could not be read; the defaults apply.");
				return (new DepartmentStepUpPolicy(), true);
			}
		}

		private async Task<bool> ReadAndSignRequiresStepUpAsync(int departmentId)
		{
			if (departmentId <= 0)
				return false;
			return (await _settings.GetProtocolLibrarySettingsAsync(departmentId))?.ReadAndSignRequiresStepUp == true;
		}

		private async Task<int> ProtectedDataWindowAsync(int departmentId)
		{
			try
			{
				var policy = departmentId > 0 ? await _protection.GetPolicyByDepartmentIdAsync(departmentId) : null;
				return ProtectedGrantBinding.EffectiveWindowMinutes(policy?.StepUpWindowMinutes ?? 0);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The ADP step-up window for department {departmentId} could not be read.");
				return ProtectedGrantBinding.EffectiveWindowMinutes(0);
			}
		}

		// ── Saving ────────────────────────────────────────────────────────────────────

		public async Task<StepUpPolicySaveOutcome> SaveAsync(int departmentId, IReadOnlyList<StepUpRuleInput> rules, string userId, bool isManagingMember,
			CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || rules == null || rules.Any(r => r == null || !StepUpAreas.IsKnown(r.Area)) ||
				rules.GroupBy(r => r.Area).Any(g => g.Count() > 1))
				return StepUpPolicySaveOutcome.InvalidRequest;

			try
			{
				var before = await GetRulesAsync(departmentId, cancellationToken);
				var (stored, _) = await ReadStoredAsync(departmentId);
				var next = new DepartmentStepUpPolicy { Areas = new List<StepUpAreaSetting>(stored.Areas ?? new List<StepUpAreaSetting>()) };

				foreach (var input in rules)
				{
					var alternatives = (input.AllowSso ? (int)StepUpMethodClasses.Sso : 0) | (input.AllowPassword ? (int)StepUpMethodClasses.Password : 0);
					next.Areas.RemoveAll(a => a.Area == (int)input.Area);
					next.Areas.Add(new StepUpAreaSetting
					{
						Area = (int)input.Area,
						// Only signatures and approvals keep their "optional" here; the other two areas own that switch elsewhere.
						Optional = input.Area is StepUpArea.Signatures or StepUpArea.Approvals && !input.Required,
						AlternativeMethods = alternatives
					});
				}

				next.Normalize();
				var beforeAdp = before.First(r => r.Area == StepUpArea.ProtectedData).Methods;
				var afterAdp = Resolve(StepUpArea.ProtectedData, next, false, false, 1).Methods;
				// Weakening a protection control is the managing member's decision, as for the ADP exemptions.
				if (beforeAdp != afterAdp && !isManagingMember)
					return StepUpPolicySaveOutcome.ManagingMemberRequired;

				var acknowledgments = rules.FirstOrDefault(r => r.Area == StepUpArea.Acknowledgments);
				var readAndSignBefore = before.First(r => r.Area == StepUpArea.Acknowledgments).Required;
				var storedChanged = !SameStored(stored, next);
				var readAndSignChanged = acknowledgments != null && acknowledgments.Required != readAndSignBefore;
				if (!storedChanged && !readAndSignChanged)
					return StepUpPolicySaveOutcome.Unchanged;

				if (storedChanged)
					await _settings.SaveOrUpdateSettingAsync(departmentId, ObjectSerialization.Serialize(next), DepartmentSettingTypes.StepUpPolicy, cancellationToken);

				if (readAndSignChanged)
				{
					// The Protocols switch stays the one source of truth for read-and-sign; its cache is invalidated by the save. Read raw: the
					// cached getter answers defaults on a fault, and writing those back would wipe the department's other library settings.
					var raw = await _settings.GetSettingByTypeAsync(departmentId, DepartmentSettingTypes.ProtocolLibrarySettings);
					var library = string.IsNullOrWhiteSpace(raw?.Setting)
						? new ProtocolLibrarySettings()
						: ObjectSerialization.Deserialize<ProtocolLibrarySettings>(raw.Setting) ?? throw new InvalidOperationException("Unreadable protocol library settings.");
					library.ReadAndSignRequiresStepUp = acknowledgments.Required;
					await _settings.SaveOrUpdateSettingAsync(departmentId, ObjectSerialization.Serialize(library.Normalize()),
						DepartmentSettingTypes.ProtocolLibrarySettings, cancellationToken);
				}

				// Grants issued under the old protected-data rule must stop now, not when they expire (as for the exemptions, both ways).
				if (beforeAdp != afterAdp && await _protection.GetPolicyByDepartmentIdAsync(departmentId, bypassCache: true) != null)
					await _protection.IncrementPolicyEpochAsync(departmentId, userId, cancellationToken);

				var after = await GetRulesAsync(departmentId, cancellationToken);
				_events?.SendMessage(new AuditEvent
				{
					DepartmentId = departmentId,
					UserId = userId,
					Type = AuditLogTypes.StepUpPolicyChanged,
					Before = JsonConvert.SerializeObject(Describe(before)),
					After = JsonConvert.SerializeObject(Describe(after)),
					Successful = true,
					ServerName = Environment.MachineName
				});
				return StepUpPolicySaveOutcome.Saved;
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, $"Step-up policy for department {departmentId} could not be saved.");
				return StepUpPolicySaveOutcome.Failed;
			}
		}

		private static bool SameStored(DepartmentStepUpPolicy a, DepartmentStepUpPolicy b)
		{
			string Key(DepartmentStepUpPolicy p) => string.Join(";", (p?.Areas ?? new List<StepUpAreaSetting>())
				.Where(s => s.Optional || s.AlternativeMethods != 0)
				.OrderBy(s => s.Area)
				.Select(s => $"{s.Area}:{s.Optional}:{s.AlternativeMethods}"));
			return Key(a) == Key(b);
		}

		private static object Describe(IEnumerable<StepUpRule> rules) =>
			rules.Select(r => new { Area = StepUpAreas.Name(r.Area), r.Required, Methods = StepUpMethodNames.Of(r.Methods) }).ToList();

		// ── Evaluation ────────────────────────────────────────────────────────────────

		public async Task<StepUpEvaluation> EvaluateAsync(StepUpSubject subject, StepUpArea area, CancellationToken cancellationToken = default)
		{
			if (subject == null || string.IsNullOrWhiteSpace(subject.UserId) || subject.DepartmentId <= 0)
				throw new ArgumentException("A signed-in member in a department is required.", nameof(subject));

			var rule = await GetRuleAsync(subject.DepartmentId, area, cancellationToken);
			if (!rule.Required)
				return new StepUpEvaluation { Rule = rule, NotRequired = true };
			if (string.IsNullOrWhiteSpace(subject.SessionKey))
				return new StepUpEvaluation { Rule = rule };

			var now = _time.GetUtcNow().UtcDateTime;
			var window = TimeSpan.FromMinutes(rule.WindowMinutes);

			try
			{
				// A second factor: always allowed, as the department's MFA switches accept it for the area's scope.
				var secondFactor = await _evidence.GetLatestSecondFactorAsync(subject.UserId, subject.SessionKey, subject.AuthenticationGeneration, cancellationToken);
				if (Counts(secondFactor, subject, window, now) && StepUpMethodNames.ClassOf((MfaEvidenceMethod)secondFactor.Method) == StepUpMethodClasses.Mfa &&
					await _mfaPolicy.IsEvidenceAcceptedAsync(subject.DepartmentId, StepUpAreas.MfaScope(area), secondFactor, cancellationToken))
					return Satisfied(rule, secondFactor, window);

				// The password or SSO entered again, only where the department allows that kind for the area.
				if (rule.Allows(StepUpMethodClasses.Sso) || rule.Allows(StepUpMethodClasses.Password))
				{
					var reauthentication = await _evidence.GetLatestReauthenticationAsync(subject.UserId, subject.SessionKey, subject.AuthenticationGeneration,
						cancellationToken);
					if (Counts(reauthentication, subject, window, now) && rule.Allows(StepUpMethodNames.ClassOf((MfaEvidenceMethod)reauthentication.Method)))
						return Satisfied(rule, reauthentication, window);
				}
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// Unknown evidence is no evidence: the member is asked to verify, never waved through.
				Logging.LogException(ex, "Step-up evidence could not be read.");
			}

			return new StepUpEvaluation { Rule = rule };
		}

		/// <summary>
		/// Evidence counts when it is this account generation's, fresh within the window (a few seconds ahead is another node's clock, any
		/// more is not proof), and, when it names a department, this one: a step-up in one department never serves another.
		/// </summary>
		public static bool Counts(MfaEvidence evidence, StepUpSubject subject, TimeSpan window, DateTime utcNow) =>
			evidence != null && subject != null && evidence.AuthenticationGeneration == subject.AuthenticationGeneration &&
			string.Equals(evidence.UserId, subject.UserId, StringComparison.OrdinalIgnoreCase) &&
			(evidence.DepartmentId == null || evidence.DepartmentId == subject.DepartmentId) &&
			MfaEvidenceService.IsFresh(evidence, window, utcNow);

		private static StepUpEvaluation Satisfied(StepUpRule rule, MfaEvidence evidence, TimeSpan window)
		{
			var verified = DateTime.SpecifyKind(evidence.VerifiedOnUtc, DateTimeKind.Utc);
			return new StepUpEvaluation
			{
				Rule = rule,
				Satisfied = true,
				MethodClass = StepUpMethodNames.ClassOf((MfaEvidenceMethod)evidence.Method),
				Method = (MfaEvidenceMethod)evidence.Method,
				VerifiedOnUtc = verified,
				ExpiresOnUtc = verified.Add(window)
			};
		}
	}
}
