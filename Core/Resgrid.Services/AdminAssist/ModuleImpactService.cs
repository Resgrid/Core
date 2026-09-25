using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;

namespace Resgrid.Services.AdminAssist
{
	public sealed class ModuleImpactService(IAdminAssistAccessService access, IAdminAssistRepository repository,
		IAdminAssistCatalog catalog, IDepartmentSettingsRepository settings, IModuleImpactStore counts, TimeProvider clock) : IModuleImpactService
	{
		public async Task<ConfigurationImpactReport> PreviewAsync(AdminAssistActor actor, ModuleImpactRequest request, CancellationToken ct = default)
		{
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
			if (request == null || !ModuleImpactSelection.Supported.Contains(request.Module, StringComparer.Ordinal)) throw new ArgumentException("Unsupported module preview.");
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(Config.AdminAssistConfig.SnapshotTimeoutSeconds, 1, 60)));
			ct = timeout.Token;
			var revision = (await repository.GetConfigurationRevisionAsync(actor.DepartmentId, ct)).ToString(CultureInfo.InvariantCulture);
			if (revision != request.ExpectedRevision) throw new AdminAssistConcurrencyException();
			var now = clock.GetUtcNow().UtcDateTime;
			ConfigurationImpactMetric[] metrics;
			try
			{
				var current = await ReadDisabledAsync(actor.DepartmentId, request.Module, ct);
				var input = await counts.ReadModuleImpactCountsAsync(actor.DepartmentId, request.Module, Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000), ct) ?? throw new InvalidOperationException();
				if (input.Members < 0 || input.ContentRows < 0) throw new InvalidOperationException();
				if (current != await ReadDisabledAsync(actor.DepartmentId, request.Module, ct) || input != await counts.ReadModuleImpactCountsAsync(actor.DepartmentId, request.Module, Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000), ct)) throw new AdminAssistConcurrencyException();
				metrics = new[] {
					new ConfigurationImpactMetric("Impact.ModuleMenus", EvidenceState.Known, current ? 0 : input.Members, request.Disabled ? 0 : input.Members),
					new ConfigurationImpactMetric("Impact.ModuleMembersChanged", EvidenceState.Known, 0, current == request.Disabled ? 0 : input.Members),
					new ConfigurationImpactMetric("Impact.ModuleDataRows", input.ContentRows.HasValue ? EvidenceState.Known : EvidenceState.Unknown, input.ContentRows, input.ContentRows, input.ContentRows.HasValue ? null : "CountAdapterUnavailable"),
					new ConfigurationImpactMetric("Impact.ModuleRowsBehindHiddenEntry", input.ContentRows.HasValue ? EvidenceState.Known : EvidenceState.Unknown, input.ContentRows.HasValue ? current ? input.ContentRows : 0 : null,
						input.ContentRows.HasValue ? request.Disabled ? input.ContentRows : 0 : null, input.ContentRows.HasValue ? null : "CountAdapterUnavailable") };
			}
			catch (AdminAssistConcurrencyException) { throw; }
			catch (UnauthorizedAccessException) { throw; }
			catch (OperationCanceledException) { throw; }
			catch (Exception) { metrics = new[] { new ConfigurationImpactMetric("Impact.ModuleMenus", EvidenceState.Unknown, null, null, "SourceUnavailable") }; }
			if ((await repository.GetConfigurationRevisionAsync(actor.DepartmentId, ct)).ToString(CultureInfo.InvariantCulture) != revision) throw new AdminAssistConcurrencyException();
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
			var entry = catalog.Settings.Single(e => e.Binding == "DepartmentModuleSettings." + request.Module + "Disabled");
			return new(entry.Id, revision, now, "module-menu-impact-v1", entry.Impact, metrics, Array.Empty<ConfigurationImpactRule>(),
				new[] { "Impact.NoMutation", "Impact.ModuleScope", "Impact.ModuleTiming", "Impact.Window" }, entry.Location.Url);
		}
		private async Task<bool> ReadDisabledAsync(int departmentId, string module, CancellationToken ct)
		{
			var rows = (await settings.GetAllByDepartmentIdAsync(departmentId).WaitAsync(ct))?.ToList() ?? throw new InvalidOperationException();
			if (rows.Any(s => s.DepartmentId != departmentId)) throw new InvalidOperationException();
			var row = rows.SingleOrDefault(s => s.SettingType == (int)DepartmentSettingTypes.ModuleSettings);
			var value = row == null ? new DepartmentModuleSettings() : ObjectSerialization.Deserialize<DepartmentModuleSettings>(row.Setting) ?? throw new InvalidOperationException();
			return ModuleImpactSelection.Disabled(value, module);
		}
	}
}
