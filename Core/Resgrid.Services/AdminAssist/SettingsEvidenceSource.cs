using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>One uncached repository read. Only explicit scalar bindings and secret-presence markers leave it.</summary>
	public sealed class SettingsEvidenceSource(IDepartmentSettingsRepository settings) : IAdminAssistEvidenceSource
	{
		private static readonly DepartmentSettingTypes[] BooleanSettings =
		{
			DepartmentSettingTypes.DispatchShiftInsteadOfGroup, DepartmentSettingTypes.AutoSetStatusForShiftDispatchPersonnel,
			DepartmentSettingTypes.EnableTextToCall, DepartmentSettingTypes.EnableTextCommand,
			DepartmentSettingTypes.MappingUseMapboxOverride, DepartmentSettingTypes.CheckInTimersAutoEnableForNewCalls,
			DepartmentSettingTypes.WeatherAlertsEnabled, DepartmentSettingTypes.RequirePasswordResetViaEmail,
			DepartmentSettingTypes.UnitDispatchAlsoDispatchToAssignedPersonnel, DepartmentSettingTypes.UnitDispatchAlsoDispatchToGroup,
			DepartmentSettingTypes.AllowSignupsForMultipleShiftGroups, DepartmentSettingTypes.DisabledAutoAvailable,
			DepartmentSettingTypes.PersonnelOnUnitSetUnitStatus, DepartmentSettingTypes.DispatchRecommendationAutoDispatch,
			DepartmentSettingTypes.MappingPersonnelAllowStatusWithNoLocationToOverwrite,
			DepartmentSettingTypes.MappingUnitAllowStatusWithNoLocationToOverwrite
		};
		private static readonly Dictionary<DepartmentSettingTypes, int> NumberSettings = new()
		{
			[DepartmentSettingTypes.Require2FAForAdmins] = 0, [DepartmentSettingTypes.MappingPersonnelLocationTTL] = 0,
			[DepartmentSettingTypes.MappingUnitLocationTTL] = 0, [DepartmentSettingTypes.DispatchRecommendationMode] = 0,
			[DepartmentSettingTypes.ShiftCallDispatchPersonnelStatusToSet] = -1,
			[DepartmentSettingTypes.ShiftCallReleasePersonnelStatusToSet] = -1,
			[DepartmentSettingTypes.UnitCallDispatchStatusToSet] = -1, [DepartmentSettingTypes.UnitCallReleaseStatusToSet] = -1
		};
		private static readonly Dictionary<string, DepartmentSettingTypes> PresenceSettings = new()
		{
			["textSourcePresent"] = DepartmentSettingTypes.TextToCallSourceNumbers,
			["mapTokenPresent"] = DepartmentSettingTypes.MappingMapboxAccessToken,
			["mapStylePresent"] = DepartmentSettingTypes.MappingMapboxStyleUrl
		};
		public string SourceId => "DepartmentSettings";
		public IReadOnlyList<string> EvidenceIds { get; } = BooleanSettings.Select(s => s.ToString())
			.Concat(NumberSettings.Keys.Select(s => s.ToString())).Concat(PresenceSettings.Keys).ToArray();

		public async Task<IReadOnlyList<ConfigurationEvidence>> ReadAsync(AdminAssistActor actor, DateTime now, CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();
			var rows = await settings.GetAllByDepartmentIdAsync(actor.DepartmentId).WaitAsync(ct)
				?? throw new InvalidOperationException("Settings source unavailable.");
			var materialized = rows.ToList();
			if (materialized.Count > Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000) || materialized.Any(r => r.DepartmentId != actor.DepartmentId)) throw new InvalidOperationException("Invalid settings scope or bound.");
			var data = materialized.ToDictionary(r => (DepartmentSettingTypes)r.SettingType, r => r.Setting);
			var values = new List<ConfigurationEvidence>();
			foreach (var type in BooleanSettings)
			{
				var known = !data.TryGetValue(type, out var raw) || bool.TryParse(raw, out _);
				values.Add(new ConfigurationEvidence(type.ToString(), known ? EvidenceState.Known : EvidenceState.Unknown,
					SourceId, "1", now, Boolean: known ? raw != null && bool.Parse(raw) : null, ReasonCode: known ? null : "InvalidStoredValue"));
			}
			foreach (var (type, fallback) in NumberSettings)
			{
				var value = (decimal)fallback;
				var known = !data.TryGetValue(type, out var raw) || decimal.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
				values.Add(new ConfigurationEvidence(type.ToString(), known ? EvidenceState.Known : EvidenceState.Unknown,
					SourceId, "1", now, Number: known ? value : null, ReasonCode: known ? null : "InvalidStoredValue"));
			}
			foreach (var (id, type) in PresenceSettings)
				values.Add(new ConfigurationEvidence(id, EvidenceState.Known, SourceId, "1", now,
					Boolean: data.TryGetValue(type, out var value) && !string.IsNullOrWhiteSpace(value)));
			return values;
		}
	}
}
