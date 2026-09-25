using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>One sender scenario against fresh routing metadata; no inbound body, profile lookup or external call.</summary>
	public sealed class TextImportImpactService(IAdminAssistAccessService access, IAdminAssistRepository repository,
		IAdminAssistCatalog catalog, IDepartmentSettingsRepository settings, INumbersService numbers, TimeProvider clock) : ITextImportImpactService
	{
		private sealed record Inputs(bool Calls, bool Commands, string Patterns);
		public async Task<TextImportImpactReport> PreviewAsync(AdminAssistActor actor, TextImportImpactRequest request, CancellationToken ct = default)
		{
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
			if (request == null || request.ProviderPath is not ("TwilioLegacy" or "SignalWire") || request.SourceNumber == null ||
				request.SourceNumber.Length is < 7 or > 40 || request.SourceNumber.Any(c => !char.IsAsciiDigit(c) && !"+ ().-".Contains(c)))
				throw new ArgumentException("Choose a supported path and a numeric sender scenario.");
			var digits = new string(request.SourceNumber.Where(char.IsAsciiDigit).ToArray());
			if (digits.Length is < 7 or > 15) throw new ArgumentException("Invalid sender length.");
			var path = Enum.Parse<TextIntakePath>(request.ProviderPath);
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(Config.AdminAssistConfig.SnapshotTimeoutSeconds, 1, 60)));
			ct = timeout.Token;
			var revision = (await repository.GetConfigurationRevisionAsync(actor.DepartmentId, ct)).ToString(CultureInfo.InvariantCulture);
			if (revision != request.ExpectedRevision) throw new AdminAssistConcurrencyException();
			ConfigurationImpactMetric[] metrics;
			try
			{
				var input = await ReadAsync(actor.DepartmentId, ct);
				var matched = !string.IsNullOrWhiteSpace(input.Patterns) && numbers.DoesNumberMatchAnyPattern(input.Patterns.Split(',').ToList(), request.SourceNumber);
				var before = TextIntakeRouting.Decide(path, matched, input.Calls, input.Commands);
				var after = TextIntakeRouting.Decide(path, matched, request.CallsEnabled, request.CommandsEnabled);
				if (input != await ReadAsync(actor.DepartmentId, ct)) throw new AdminAssistConcurrencyException();
				ConfigurationImpactMetric Metric(string key, bool oldValue, bool newValue) => new("Impact." + key, EvidenceState.Known, oldValue ? 1 : 0, newValue ? 1 : 0);
				metrics = new[] { new ConfigurationImpactMetric("Impact.TextScenarioCount", EvidenceState.Known, 1, 1),
					Metric("TextPatternMatch", matched, matched), Metric("TextDispatchBranch", before.CallBranch, after.CallBranch),
					Metric("TextCommandBranch", before.CommandBranch, after.CommandBranch),
					new ConfigurationImpactMetric("Impact.TextActualAcceptance", EvidenceState.Unknown, null, null, "ProviderIdentityAndPlanNotVerified") };
			}
			catch (AdminAssistConcurrencyException) { throw; }
			catch (UnauthorizedAccessException) { throw; }
			catch (OperationCanceledException) { throw; }
			catch (Exception) { metrics = new[] { new ConfigurationImpactMetric("Impact.TextDispatchBranch", EvidenceState.Unknown, null, null, "SourceUnavailable") }; }
			if ((await repository.GetConfigurationRevisionAsync(actor.DepartmentId, ct)).ToString(CultureInfo.InvariantCulture) != revision) throw new AdminAssistConcurrencyException();
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
			var entry = catalog.Settings.Single(s => s.Id == "setting.EnableTextToCall");
			return new("••••" + digits[^4..], request.ProviderPath, new(entry.Id, revision, clock.GetUtcNow().UtcDateTime,
				"text-routing-v1", entry.Impact, metrics, Array.Empty<ConfigurationImpactRule>(),
				new[] { "Impact.NoMutation", "Impact.TextScenarioLimits", "Impact.TextProviderLimits", "Impact.Window" }, entry.Location.Url));
		}
		private async Task<Inputs> ReadAsync(int departmentId, CancellationToken ct)
		{
			var rows = (await settings.GetAllByDepartmentIdAsync(departmentId).WaitAsync(ct))?.ToList() ?? throw new InvalidOperationException();
			if (rows.Count > Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000) || rows.Any(r => r.DepartmentId != departmentId)) throw new InvalidOperationException();
			string Value(DepartmentSettingTypes type) => rows.SingleOrDefault(r => r.SettingType == (int)type)?.Setting;
			bool Flag(DepartmentSettingTypes type)
			{
				var row = rows.SingleOrDefault(r => r.SettingType == (int)type);
				return row == null ? false : bool.Parse(row.Setting);
			}
			var patterns = Value(DepartmentSettingTypes.TextToCallSourceNumbers);
			if (patterns?.Length > 8192 || patterns?.Split(',').Length > 100) throw new InvalidOperationException();
			return new(Flag(DepartmentSettingTypes.EnableTextToCall), Flag(DepartmentSettingTypes.EnableTextCommand), patterns);
		}
	}
}
