using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>
	/// Admin Assist conversation admission. Paths, in order: operator-listed self-hosted department; Enhanced AI add-on
	/// (monthly token budget); free allowance (answered questions per window, enhanced-ai-addon-plan.md §5.4). The
	/// Ai.Enhanced and Ai.AdminAssist rollout flags apply to every path.
	/// </summary>
	public sealed class AiAccessService(IAdminAssistAccessService access, IFeatureToggleService flags, IDepartmentSettingsService settings,
		IEnhancedAiAccessService enhancedAi, IAiUsageMeter usage, IAiFreeAllowanceStore allowance, IDepartmentDataProtectionService protection, TimeProvider clock) : IAiAccessService
	{
		public async Task<AdminAssistAskStatus> CanUseAdminAssistAsync(AdminAssistActor actor, CancellationToken ct, bool requireBudget = true)
		{
			try
			{
				if (!AiConfig.AdminAssistEnabled || !await access.CanAccessAsync(actor, false, ct) ||
					(await flags.EvaluateFreshAsync(FeatureFlagKeys.AiEnhanced, actor.DepartmentId).WaitAsync(ct))?.IsEnabled != true ||
					(await flags.EvaluateFreshAsync(FeatureFlagKeys.AiAdminAssist, actor.DepartmentId).WaitAsync(ct))?.IsEnabled != true) return new(false, "Disabled", 0);
				var modules = await settings.GetDepartmentModuleSettingsAsync(actor.DepartmentId, true).WaitAsync(ct);
				if (modules == null || modules.AiDisabled) return new(false, "Disabled", 0);
				var local = (AiConfig.SelfHostedDepartmentIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
					.Any(id => int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value == actor.DepartmentId);
				// Unknown billing is never the free allowance: a paying department must not see its outage as used-up questions.
				var paid = local ? true : await enhancedAi.GetActiveAddonStateAsync(actor.DepartmentId).WaitAsync(ct);
				if (paid == null) return new(false, "EntitlementUnavailable", 0);
				var tier = local ? AdminAssistAskTiers.SelfHosted : paid == true ? AdminAssistAskTiers.EnhancedAi : AiAddonConfig.AdminAssistFreeEnabled ? AdminAssistAskTiers.Free : null;
				if (tier == null) return new(false, "EntitlementUnavailable", 0);
				if (await protection.ShouldEncryptNewWritesAsync(actor.DepartmentId).WaitAsync(ct) && await protection.GetPinnedCatalogVersionAsync(actor.DepartmentId).WaitAsync(ct) < ProtectedFieldCatalog.AiGenerationsCatalogVersion) return new(false, "ProtectionUpgradeRequired", 0);
				Resgrid.Llm.OperatorEndpointPolicy.ValidateUri(AiConfig.Endpoint, AiConfig.AllowPrivateEndpoint);
				if (string.IsNullOrWhiteSpace(SecurityConfig.EncryptionKey) || SecurityConfig.EncryptionKey.Length < 32 || SecurityConfig.EncryptionKey.Contains("CHANGEME", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(SecurityConfig.EncryptionSaltValue) || SecurityConfig.EncryptionSaltValue.Contains("CHANGEME", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(AiConfig.ApiKey) || !Resgrid.Ai.AiOperatorSettings.IsReviewedModel(AiConfig.Model) || !Regex.IsMatch(AiConfig.ModelRevision ?? "", "\\A[0-9a-f]{40}\\z") ||
					!Regex.IsMatch(AiConfig.RuntimeDigest ?? "", "\\Asha256:[0-9a-f]{64}\\z") || !HasAuditKey(AiConfig.AuditHmacKey)) return new(false, "Unconfigured", 0);
				if (await usage.IsDisabledAsync(actor.DepartmentId, ct)) return new(false, "Disabled", 0);
				var now = clock.GetUtcNow().UtcDateTime;
				var turnTokens = Math.Clamp(AiConfig.TurnTokenLimit, 8192, 32768);
				if (tier == AdminAssistAskTiers.Free)
				{
					var window = await FreeWindowAsync(actor.DepartmentId, now, ct);
					var used = await allowance.GetFreeUsageAsync(actor.DepartmentId, window, now, ct);
					var left = Math.Max(0, window.Allowance - used.AnsweredInWindow);
					// The post-reservation recheck (requireBudget false) must not count the turn's own in-flight question against it.
					var reason = !requireBudget ? "Available" : left == 0 ? "FreeAllowanceExhausted" :
						used.AttemptsLast24Hours >= AiAddonConfig.AdminAssistFreeDailyAttemptLimit ? "FreeAttemptLimit" : "Available";
					return new(reason == "Available", reason, reason == "Available" ? turnTokens : 0, tier, left, window.Allowance, window.EndUtc);
				}
				var remaining = await usage.RemainingAsync(actor.DepartmentId, now, AiConfig.MonthlyTokenLimit, ct);
				return requireBudget && remaining < turnTokens ? new(false, "BudgetExhausted", remaining, tier) : new(true, "Available", remaining, tier);
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception) { return new(false, "Unavailable", 0); }
		}

		public async Task<AiUsageReservation> ReserveTurnAsync(AdminAssistActor actor, AdminAssistAskStatus status, CancellationToken ct)
		{
			if (actor == null || status == null || !status.Available) throw new UnauthorizedAccessException();
			var now = clock.GetUtcNow().UtcDateTime;
			var turnTokens = Math.Clamp(AiConfig.TurnTokenLimit, 8192, 32768);
			if (status.Tier != AdminAssistAskTiers.Free)
				return await usage.ReserveAsync(actor, now, turnTokens, AiConfig.MonthlyTokenLimit, ct);
			// The window and allowance are rechecked inside the admission lock; a question used meanwhile returns null (Busy).
			var reserved = await allowance.ReserveFreeAsync(actor, now, turnTokens, await FreeWindowAsync(actor.DepartmentId, now, ct), AiAddonConfig.AdminAssistFreeDailyAttemptLimit, ct);
			return reserved.Reservation;
		}

		private async Task<AdminAssistFreeWindow> FreeWindowAsync(int departmentId, DateTime now, CancellationToken ct) =>
			AdminAssistFreeAllowance.Current(await allowance.GetFirstAnsweredAsync(departmentId, ct), now,
				AiAddonConfig.AdminAssistFreeStarterQuestions, AiAddonConfig.AdminAssistFreeStarterDays, AiAddonConfig.AdminAssistFreeMonthlyQuestions);

		// A mistyped key is a configuration problem, not an outage: parse without throwing so it reports Unconfigured.
		private static bool HasAuditKey(string value)
		{
			var buffer = new byte[(value?.Length ?? 0) * 3 / 4 + 3];
			return Convert.TryFromBase64String(value ?? "", buffer, out var written) && written >= 32;
		}
	}
}
