using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Shared command boundary for explicitly opted-in configuration repositories. Never used for operational records.</summary>
	public abstract class AuditedConfigurationRepository<T> : RmsRepositoryBase<T> where T : class, IEntity
	{
		private readonly IConfigurationChangeJournal _journal;
		protected bool HasConfigurationJournal => _journal != null;
		protected AuditedConfigurationRepository(IConnectionProvider connections, SqlConfiguration configuration, IUnitOfWork unit,
			IQueryFactory queries, IConfigurationChangeJournal journal) : base(connections, configuration, unit, queries) { _journal = journal; }

		public override Task<T> InsertAsync(T entity, CancellationToken ct, bool firstLevelOnly = false) =>
			ChangeAsync(entity, () => base.InsertAsync(entity, ct, firstLevelOnly), ct);
		public override Task<T> UpdateAsync(T entity, CancellationToken ct, bool firstLevelOnly = false) =>
			ChangeAsync(entity, () => base.UpdateAsync(entity, ct, firstLevelOnly), ct);
		public override Task<bool> DeleteAsync(T entity, CancellationToken ct) => entity == null ? Task.FromResult(false) :
			ChangeAsync(entity, () => base.DeleteAsync(entity, ct), ct);

		protected virtual Task<int> ConfigurationDepartmentAsync(T entity, CancellationToken ct) => entity switch
		{
			DepartmentSetting row => Task.FromResult(row.DepartmentId),
			Unit row => Task.FromResult(row.DepartmentId),
			DepartmentGroup row => Task.FromResult(row.DepartmentId),
			PersonnelRole row => Task.FromResult(row.DepartmentId),
			PersonnelRoleUser row => ParentDepartmentAsync("PersonnelRoles", "PersonnelRoleId", row.PersonnelRoleId, row.DepartmentId, ct),
			DepartmentGroupMember row => ParentDepartmentAsync("DepartmentGroups", "DepartmentGroupId", row.DepartmentGroupId, row.DepartmentId, ct),
			UnitRole row => ParentDepartmentAsync("Units", "UnitId", row.UnitId, null, ct),
			Shift row => Task.FromResult(row.DepartmentId),
			CallType row => Task.FromResult(row.DepartmentId),
			CallQuickTemplate row => Task.FromResult(row.DepartmentId),
			UnitType row => Task.FromResult(row.DepartmentId),
			CustomState row => Task.FromResult(row.DepartmentId),
			CustomStateDetail row => ParentDepartmentAsync("CustomStates", "CustomStateId", row.CustomStateId, null, ct),
			RunCard row => Task.FromResult(row.DepartmentId),
			RunCardTrigger row => ParentDepartmentAsync("RunCards", "RunCardId", row.RunCardId, null, ct),
			RunCardAlarmLevel row => ParentDepartmentAsync("RunCards", "RunCardId", row.RunCardId, null, ct),
			RunCardAvailabilitySelection row => ParentDepartmentAsync("RunCards", "RunCardId", row.RunCardId, null, ct),
			RunCardUnitRequirement row => NestedDepartmentAsync("RunCardAlarmLevels", "RunCardAlarmLevelId", row.RunCardAlarmLevelId, "RunCardId", "RunCards", ct),
			RunCardRoleRequirement row => NestedDepartmentAsync("RunCardAlarmLevels", "RunCardAlarmLevelId", row.RunCardAlarmLevelId, "RunCardId", "RunCards", ct),
			ShiftDay row => ParentDepartmentAsync("Shifts", "ShiftId", row.ShiftId, null, ct),
			ShiftGroup row => ParentDepartmentAsync("Shifts", "ShiftId", row.ShiftId, null, ct),
			ShiftPerson row => ParentDepartmentAsync("Shifts", "ShiftId", row.ShiftId, null, ct),
			ShiftGroupRole row => NestedDepartmentAsync("ShiftGroups", "ShiftGroupId", row.ShiftGroupId, "ShiftId", "Shifts", ct),
			ShiftGroupAssignment row => NestedDepartmentAsync("ShiftGroups", "ShiftGroupId", row.ShiftGroupId, "ShiftId", "Shifts", ct),
			DepartmentSecurityPolicy row => Task.FromResult(row.DepartmentId),
			DepartmentSsoConfig row => Task.FromResult(row.DepartmentId),
			DepartmentCallEmail row => Task.FromResult(row.DepartmentId),
			ChatbotDepartmentConfig row => Task.FromResult(row.DepartmentId),
			DepartmentNotification row => Task.FromResult(row.DepartmentId),
			DispatchProtocol row => Task.FromResult(row.DepartmentId),
			WeatherAlertZone row => Task.FromResult(row.DepartmentId),
			DispatchProtocolAttachment row => ProtocolDepartmentAsync(row.DispatchProtocolId, ct),
			DispatchProtocolQuestion row => ProtocolDepartmentAsync(row.DispatchProtocolId, ct),
			DispatchProtocolTrigger row => ProtocolDepartmentAsync(row.DispatchProtocolId, ct),
			DispatchProtocolQuestionAnswer row => QuestionDepartmentAsync(row.DispatchProtocolQuestionId, ct),
			_ => throw new InvalidOperationException("Configuration audit scope has no reviewed binding.")
		};
		protected Task<TResult> ExecuteConfigurationMutationAsync<TResult>(int departmentId, string binding,
			Func<Task<ConfigurationChangeStamp>> read, Func<Task<TResult>> write, CancellationToken ct) =>
			_journal == null ? write() : _journal.ExecuteAsync(departmentId, binding, read, write, ct);
		private async Task<int> ParentDepartmentAsync(string table, string key, int id, int? expectedDepartmentId, CancellationToken ct)
		{
			var departmentId = await ScalarAsync<int>($"SELECT {Col("DepartmentId")} FROM {Tbl(table)} WHERE {Col(key)}={P}Id", new { Id = id }, ct);
			if (departmentId <= 0 || expectedDepartmentId > 0 && expectedDepartmentId != departmentId) throw new UnauthorizedAccessException();
			return departmentId;
		}
		private Task<int> ProtocolDepartmentAsync(int id, CancellationToken ct) => ScalarAsync<int>($"SELECT {Col("DepartmentId")} FROM {Tbl("DispatchProtocols")} WHERE {Col("DispatchProtocolId")}={P}Id", new { Id = id }, ct);
		private async Task<int> NestedDepartmentAsync(string childTable, string childKey, int childId, string parentKey, string parentTable, CancellationToken ct) =>
			await ParentDepartmentAsync(parentTable, parentKey, await ScalarAsync<int>($"SELECT {Col(parentKey)} FROM {Tbl(childTable)} WHERE {Col(childKey)}={P}Id", new { Id = childId }, ct), null, ct);
		private async Task<int> QuestionDepartmentAsync(int id, CancellationToken ct) => await ProtocolDepartmentAsync(await ScalarAsync<int>(
			$"SELECT {Col("DispatchProtocolId")} FROM {Tbl("DispatchProtocolQuestions")} WHERE {Col("DispatchProtocolQuestionId")}={P}Id", new { Id = id }, ct), ct);

		private async Task<TResult> ChangeAsync<TResult>(T entity, Func<Task<TResult>> write, CancellationToken ct)
		{
			// The optional constructor exists for legacy repository fixtures; production Autofac resolves the journal.
			if (_journal == null) return await write();
			var departmentId = await ConfigurationDepartmentAsync(entity, ct);
			var binding = entity is DepartmentSetting setting ? "setting." + (DepartmentSettingTypes)setting.SettingType : entity.TableName;
			async Task<ConfigurationChangeStamp> Read()
			{
				var current = entity.IdValue == null ? null : await GetByIdAsync(entity.IdValue);
				if (current != null && await ConfigurationDepartmentAsync(current, ct) != departmentId) throw new UnauthorizedAccessException();
				var stamp = ConfigurationAuditProjection.Project(current);
				if (current == null) return stamp;
				// Legacy child synchronization instantiates RepositoryBase directly. Capture the complete
				// aggregate here so those inserts/updates/deletes cannot bypass protocol audit/revision.
				var children = new List<IEntity>();
				switch (current)
				{
					case DispatchProtocol protocol:
						children.AddRange(await QueryAsync<DispatchProtocolQuestion>($"SELECT * FROM {Tbl("DispatchProtocolQuestions")} WHERE {Col("DispatchProtocolId")}={P}Id", new { Id = protocol.DispatchProtocolId }, ct));
						children.AddRange(await QueryAsync<DispatchProtocolTrigger>($"SELECT * FROM {Tbl("DispatchProtocolTriggers")} WHERE {Col("DispatchProtocolId")}={P}Id", new { Id = protocol.DispatchProtocolId }, ct));
						children.AddRange(await QueryAsync<DispatchProtocolAttachment>($"SELECT * FROM {Tbl("DispatchProtocolAttachments")} WHERE {Col("DispatchProtocolId")}={P}Id", new { Id = protocol.DispatchProtocolId }, ct));
						foreach (var question in children.OfType<DispatchProtocolQuestion>().ToArray())
							children.AddRange(await QueryAsync<DispatchProtocolQuestionAnswer>($"SELECT * FROM {Tbl("DispatchProtocolQuestionAnswers")} WHERE {Col("DispatchProtocolQuestionId")}={P}Id", new { Id = question.DispatchProtocolQuestionId }, ct));
						break;
					case Unit unit:
						children.AddRange(await QueryAsync<UnitRole>($"SELECT * FROM {Tbl("UnitRoles")} WHERE {Col("UnitId")}={P}Id", new { Id = unit.UnitId }, ct)); break;
					case DepartmentGroup group:
						children.AddRange(await QueryAsync<DepartmentGroupMember>($"SELECT * FROM {Tbl("DepartmentGroupMembers")} WHERE {Col("DepartmentGroupId")}={P}Id", new { Id = group.DepartmentGroupId }, ct)); break;
					case PersonnelRole role:
						children.AddRange(await QueryAsync<PersonnelRoleUser>($"SELECT * FROM {Tbl("PersonnelRoleUsers")} WHERE {Col("PersonnelRoleId")}={P}Id", new { Id = role.PersonnelRoleId }, ct)); break;
					case CustomState state:
						children.AddRange(await QueryAsync<CustomStateDetail>($"SELECT * FROM {Tbl("CustomStateDetails")} WHERE {Col("CustomStateId")}={P}Id", new { Id = state.CustomStateId }, ct)); break;
					case RunCard card:
						children.AddRange(await QueryAsync<RunCardTrigger>($"SELECT * FROM {Tbl("RunCardTriggers")} WHERE {Col("RunCardId")}={P}Id", new { Id = card.RunCardId }, ct));
						children.AddRange(await QueryAsync<RunCardAvailabilitySelection>($"SELECT * FROM {Tbl("RunCardAvailabilitySelections")} WHERE {Col("RunCardId")}={P}Id", new { Id = card.RunCardId }, ct));
						children.AddRange(await QueryAsync<RunCardAlarmLevel>($"SELECT * FROM {Tbl("RunCardAlarmLevels")} WHERE {Col("RunCardId")}={P}Id", new { Id = card.RunCardId }, ct));
						foreach (var level in children.OfType<RunCardAlarmLevel>().ToArray())
						{
							children.AddRange(await QueryAsync<RunCardUnitRequirement>($"SELECT * FROM {Tbl("RunCardUnitRequirements")} WHERE {Col("RunCardAlarmLevelId")}={P}Id", new { Id = level.RunCardAlarmLevelId }, ct));
							children.AddRange(await QueryAsync<RunCardRoleRequirement>($"SELECT * FROM {Tbl("RunCardRoleRequirements")} WHERE {Col("RunCardAlarmLevelId")}={P}Id", new { Id = level.RunCardAlarmLevelId }, ct));
						}
						break;
					case Shift shift:
						children.AddRange(await QueryAsync<ShiftDay>($"SELECT * FROM {Tbl("ShiftDays")} WHERE {Col("ShiftId")}={P}Id", new { Id = shift.ShiftId }, ct));
						children.AddRange(await QueryAsync<ShiftPerson>($"SELECT * FROM {Tbl("ShiftPersons")} WHERE {Col("ShiftId")}={P}Id", new { Id = shift.ShiftId }, ct));
						children.AddRange(await QueryAsync<ShiftGroup>($"SELECT * FROM {Tbl("ShiftGroups")} WHERE {Col("ShiftId")}={P}Id", new { Id = shift.ShiftId }, ct));
						foreach (var shiftGroup in children.OfType<ShiftGroup>().ToArray())
						{
							children.AddRange(await QueryAsync<ShiftGroupRole>($"SELECT * FROM {Tbl("ShiftGroupRoles")} WHERE {Col("ShiftGroupId")}={P}Id", new { Id = shiftGroup.ShiftGroupId }, ct));
							children.AddRange(await QueryAsync<ShiftGroupAssignment>($"SELECT * FROM {Tbl("ShiftGroupAssignments")} WHERE {Col("ShiftGroupId")}={P}Id", new { Id = shiftGroup.ShiftGroupId }, ct));
						}
						break;
					default: return stamp;
				}
				var childStamps = children.OrderBy(c => c.TableName, StringComparer.Ordinal).ThenBy(c => c.IdValue.ToString(), StringComparer.Ordinal).Select(ConfigurationAuditProjection.Project).ToArray();
				return new ConfigurationChangeStamp(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp.Fingerprint + string.Join("|", childStamps.Select(c => c.Fingerprint))))),
					JsonConvert.SerializeObject(new { root = stamp.Values, children = children.GroupBy(c => c.TableName).ToDictionary(g => g.Key, g => g.Count()) }));
			}
			return await _journal.ExecuteAsync(departmentId, binding, Read, write, ct);
		}
	}

	/// <summary>Only stored scalar columns are inspected. Strings/binary/identifiers are presence markers; no raw values enter audit.</summary>
	public static class ConfigurationAuditProjection
	{
		private static readonly HashSet<string> SafeNumbers = new(StringComparer.Ordinal)
		{
			"SettingType", "SsoProviderType", "SessionTimeoutMinutes", "MaxConcurrentSessions", "PasswordExpirationDays", "MinPasswordLength",
			"DataClassificationLevel", "EventType", "UpperLimit", "LowerLimit", "MinimumWeight", "State", "Weight", "TriggerType", "Type"
		};
		private static readonly HashSet<DepartmentSettingTypes> SafeScalars = new()
		{
			DepartmentSettingTypes.DisabledAutoAvailable, DepartmentSettingTypes.EnableTextToCall, DepartmentSettingTypes.EnableTextCommand,
			DepartmentSettingTypes.DispatchShiftInsteadOfGroup, DepartmentSettingTypes.AutoSetStatusForShiftDispatchPersonnel,
			DepartmentSettingTypes.ShiftCallDispatchPersonnelStatusToSet, DepartmentSettingTypes.ShiftCallReleasePersonnelStatusToSet,
			DepartmentSettingTypes.AllowSignupsForMultipleShiftGroups, DepartmentSettingTypes.MappingPersonnelLocationTTL,
			DepartmentSettingTypes.MappingUnitLocationTTL, DepartmentSettingTypes.MappingPersonnelAllowStatusWithNoLocationToOverwrite,
			DepartmentSettingTypes.MappingUnitAllowStatusWithNoLocationToOverwrite, DepartmentSettingTypes.UnitDispatchAlsoDispatchToAssignedPersonnel,
			DepartmentSettingTypes.UnitDispatchAlsoDispatchToGroup, DepartmentSettingTypes.PersonnelOnUnitSetUnitStatus, DepartmentSettingTypes.Require2FAForAdmins,
			DepartmentSettingTypes.CheckInTimersAutoEnableForNewCalls, DepartmentSettingTypes.WeatherAlertsEnabled,
			DepartmentSettingTypes.WeatherAlertMinimumSeverity, DepartmentSettingTypes.WeatherAlertAutoMessageSeverity,
			DepartmentSettingTypes.WeatherAlertCallIntegration, DepartmentSettingTypes.WeatherAlertCacheMinutes,
			DepartmentSettingTypes.MappingUseMapboxOverride, DepartmentSettingTypes.UnitCallDispatchStatusToSet, DepartmentSettingTypes.UnitCallReleaseStatusToSet,
			DepartmentSettingTypes.EnableModernNotifications, DepartmentSettingTypes.ForceChatbotSecurityPin, DepartmentSettingTypes.HardwareTrackingStaleAfterSeconds,
			DepartmentSettingTypes.HardwareTrackingMobileFallbackEnabled, DepartmentSettingTypes.HardwareTrackingLocationRetentionDays,
			DepartmentSettingTypes.DispatchRecommendationMode, DepartmentSettingTypes.DispatchRecommendationAutoDispatch,
			DepartmentSettingTypes.RequirePasswordResetViaEmail, DepartmentSettingTypes.RecordsDefaultLifecyclePreset,
			DepartmentSettingTypes.RecordsReviewDueHours, DepartmentSettingTypes.RecordsGroupVisibilityMode
		};
		public static ConfigurationChangeStamp Project(IEntity entity)
		{
			if (entity == null) return null;
			var safe = new SortedDictionary<string, object>(StringComparer.Ordinal);
			var fingerprint = new SortedDictionary<string, object>(StringComparer.Ordinal);
			var ignored = entity.IgnoredProperties.ToHashSet(StringComparer.Ordinal);
			foreach (var property in entity.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && !ignored.Contains(p.Name) && p.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute>() == null))
			{
				var value = property.GetValue(entity);
				var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
				if (!(type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(Guid) || type == typeof(byte[]))) continue;
				fingerprint[property.Name] = value is byte[] bytes ? Convert.ToHexString(SHA256.HashData(bytes)) : value;
				if (property.Name == "Setting" && entity is DepartmentSetting setting && SafeScalars.Contains((DepartmentSettingTypes)setting.SettingType))
				{
					safe[property.Name] = bool.TryParse(setting.Setting, out var boolean) ? boolean : decimal.TryParse(setting.Setting, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : "InvalidStoredValue";
					continue;
				}
				if (property.Name.EndsWith("Id", StringComparison.Ordinal) || type == typeof(string) || type == typeof(byte[]) || type == typeof(Guid))
					safe[property.Name] = value == null || value is string text && string.IsNullOrWhiteSpace(text) || value is byte[] data && data.Length == 0 ? "Absent" : "Present";
				else safe[property.Name] = type == typeof(bool) || type == typeof(DateTime) || SafeNumbers.Contains(property.Name) ? value : value == null ? "Absent" : "Present";
			}
			var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(fingerprint))));
			return new ConfigurationChangeStamp(digest, JsonConvert.SerializeObject(safe));
		}
	}
}
