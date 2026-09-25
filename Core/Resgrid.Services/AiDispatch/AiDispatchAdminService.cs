using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Ai;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Services;

namespace Resgrid.Services.AiDispatch
{
	/// <summary>Department-admin surface for AI dispatch (enhanced-ai-addon-plan.md §4): status, settings and the activity audit.</summary>
	public sealed class AiDispatchAdminService : IAiDispatchAdminService
	{
		private readonly IAiDispatchConfigRepository _settings;
		private readonly IAiDispatchAuditRepository _audits;
		private readonly IAiBackgroundAdmission _admission;
		private readonly IEnhancedAiAccessService _enhancedAi;
		private readonly IFeatureToggleService _flags;
		private readonly IDepartmentsService _departments;
		private readonly TimeProvider _clock;

		public AiDispatchAdminService(IAiDispatchConfigRepository settings, IAiDispatchAuditRepository audits, IAiBackgroundAdmission admission,
			IEnhancedAiAccessService enhancedAi, IFeatureToggleService flags, IDepartmentsService departments, TimeProvider clock)
		{
			_settings = settings;
			_audits = audits;
			_admission = admission;
			_enhancedAi = enhancedAi;
			_flags = flags;
			_departments = departments;
			_clock = clock;
		}

		public async Task<AiDispatchStatus> GetStatusAsync(int departmentId)
		{
			var selfHosted = AiOperatorSettings.IsListedDepartment(AiConfig.SelfHostedDepartmentIds, departmentId);
			var rolledOut = await _enhancedAi.IsEnabledAsync(departmentId) &&
				(await _flags.EvaluateFreshAsync(FeatureFlagKeys.AiDispatchTemplate, departmentId))?.IsEnabled == true;
			var entitled = selfHosted || await _enhancedAi.GetActiveAddonStateAsync(departmentId) == true;
			var format = (await _departments.GetDepartmentEmailSettingsAsync(departmentId))?.FormatType == (int)CallEmailTypes.AI;
			return new AiDispatchStatus(AiDispatchConfig.EnrichEnabled, rolledOut, entitled, format, AiDispatchEnrichmentService.OperatorConfigured());
		}

		public async Task<DepartmentAiDispatchConfig> GetSettingsAsync(int departmentId, CancellationToken cancellationToken) =>
			await _settings.GetAsync(departmentId, cancellationToken) ?? new DepartmentAiDispatchConfig { DepartmentId = departmentId };

		public async Task<IReadOnlyList<string>> SaveSettingsAsync(int departmentId, DepartmentAiDispatchConfig settings, long expectedRevision, string userId,
			CancellationToken cancellationToken)
		{
			if (settings == null || departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				throw new ArgumentException("Invalid AI dispatch settings.");
			var errors = AiDispatchSettingsPolicy.Validate(settings);
			if (errors.Count > 0)
				return errors;

			// The department comes from the signed-in admin, never from the form.
			settings.DepartmentId = departmentId;
			settings.SenderAllowlist = AiDispatchSettingsPolicy.NormalizeAllowlist(settings.SenderAllowlist);
			settings.UpdatedByUserId = userId;
			settings.UpdatedOnUtc = _clock.GetUtcNow().UtcDateTime;
			if (!await _settings.SaveAsync(settings, expectedRevision, cancellationToken))
				return new[] { "Conflict" };
			settings.Revision = expectedRevision + 1;
			return Array.Empty<string>();
		}

		public Task<List<AiDispatchAuditListItem>> GetAuditAsync(int departmentId, int take, CancellationToken cancellationToken) =>
			_audits.GetRecentAsync(departmentId, take, cancellationToken);

		public Task<long> GetMonthlyUsageAsync(int departmentId, CancellationToken cancellationToken) =>
			_admission.GetFeatureUsageAsync(departmentId, AiDispatchEnrichmentService.Feature, _clock.GetUtcNow().UtcDateTime, cancellationToken);

		public async Task<bool> IsSenderAllowedAsync(int departmentId, string sender)
		{
			try
			{
				var settings = await _settings.GetAsync(departmentId, CancellationToken.None);
				return AiDispatchSettingsPolicy.IsSenderAllowed(settings?.SenderAllowlist, sender);
			}
			catch (Exception ex)
			{
				// The allowlist is an injection control: when it cannot be read, the message is not enriched (the call still exists).
				Framework.Logging.LogException(ex);
				return false;
			}
		}
	}
}
