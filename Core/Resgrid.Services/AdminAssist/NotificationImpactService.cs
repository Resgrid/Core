using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;

namespace Resgrid.Services.AdminAssist
{
	public sealed class NotificationImpactService(IAdminAssistAccessService access, IAdminAssistRepository repository,
		IAdminAssistCatalog catalog, IDepartmentSettingsRepository settings, INotificationImpactStore store, TimeProvider clock) : INotificationImpactService
	{
		public async Task<ConfigurationImpactReport> PreviewAsync(AdminAssistActor actor, NotificationImpactRequest request, CancellationToken ct = default)
		{
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
			if (request == null || request.WindowDays < 1 || request.WindowDays > 30 || request.EventsPerMember < 0 || request.EventsPerMember > 10000)
				throw new ArgumentException("Invalid notification scenario.");
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(Config.AdminAssistConfig.SnapshotTimeoutSeconds, 1, 60))); ct = timeout.Token;
			var revision = (await repository.GetConfigurationRevisionAsync(actor.DepartmentId, ct)).ToString(CultureInfo.InvariantCulture);
			if (request.ExpectedRevision != revision) throw new AdminAssistConcurrencyException();
			var now = clock.GetUtcNow().UtcDateTime;
			var metrics = new List<ConfigurationImpactMetric> {
				new("Impact.NotificationWindowDays", EvidenceState.Known, request.WindowDays, request.WindowDays),
				new("Impact.NotificationScenarioEvents", EvidenceState.Known, request.EventsPerMember, request.EventsPerMember),
				new("Impact.NotificationHistoricalSample", EvidenceState.NotApplicable, null, null, "DeclaredScenario") };
			try
			{
				var bound = Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000);
				var suppression = await ReadSuppressionAsync(actor.DepartmentId, ct);
				var blocked = BroadcastBlocked(actor.DepartmentId);
				var members = await store.ReadNotificationMembersAsync(actor.DepartmentId, bound, ct);
				Validate(members, actor.DepartmentId, bound);
				var before = Count(members, suppression.EnableSupressStaffing, suppression.StaffingLevelsToSupress, blocked);
				var after = Count(members, request.SuppressStaffing, suppression.StaffingLevelsToSupress, blocked);
				if (JsonConvert.SerializeObject(suppression) != JsonConvert.SerializeObject(await ReadSuppressionAsync(actor.DepartmentId, ct)) ||
					JsonConvert.SerializeObject(members) != JsonConvert.SerializeObject(await store.ReadNotificationMembersAsync(actor.DepartmentId, bound, ct)) ||
					blocked != BroadcastBlocked(actor.DepartmentId)) throw new AdminAssistConcurrencyException();
				void Add(string key, decimal current, decimal proposed) => metrics.Add(new("Impact.Notification" + key, EvidenceState.Known, current, proposed));
				Add("MemberSample", members.Count, members.Count);
				Add("MissingProfiles", members.Count(m => !m.ProfileId.HasValue), members.Count(m => !m.ProfileId.HasValue));
				Add("StaffingUnknown", before.UnknownStaffing, after.UnknownStaffing);
				Add("Suppressed", before.Suppressed, after.Suppressed);
				Add("RecipientsMinimum", before.RecipientsMinimum, after.RecipientsMinimum);
				Add("RecipientsMaximum", before.RecipientsMaximum, after.RecipientsMaximum);
				Add("SmsMinimum", before.SmsMinimum, after.SmsMinimum); Add("SmsMaximum", before.SmsMaximum, after.SmsMaximum);
				Add("EmailMinimum", before.EmailMinimum, after.EmailMinimum); Add("EmailMaximum", before.EmailMaximum, after.EmailMaximum);
				Add("PushMinimum", before.PushMinimum, after.PushMinimum); Add("PushMaximum", before.PushMaximum, after.PushMaximum);
				Add("NoChannels", before.NoChannels, after.NoChannels);
				Add("VolumeMinimum", 0, 0);
				Add("VolumeMaximum", (decimal)request.EventsPerMember * (before.SmsMaximum + before.EmailMaximum + before.PushMaximum),
					(decimal)request.EventsPerMember * (after.SmsMaximum + after.EmailMaximum + after.PushMaximum));
				Add("BroadcastBlocked", blocked ? 1 : 0, blocked ? 1 : 0);
			}
			catch (AdminAssistConcurrencyException) { throw; }
			catch (UnauthorizedAccessException) { throw; }
			catch (OperationCanceledException) { throw; }
			catch (Exception) { metrics.Add(new("Impact.NotificationMemberSample", EvidenceState.Unknown, null, null, "SourceUnavailableOrBoundExceeded")); }
			if ((await repository.GetConfigurationRevisionAsync(actor.DepartmentId, ct)).ToString(CultureInfo.InvariantCulture) != revision) throw new AdminAssistConcurrencyException();
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
			var entry = catalog.Settings.Single(e => e.Binding == "DepartmentSuppressStaffingInfo.EnableSupressStaffing");
			return new(entry.Id, revision, now, "notification-cohort-impact-v1", entry.Impact, metrics, Array.Empty<ConfigurationImpactRule>(),
				new[] { "Impact.NoMutation", "Impact.NotificationScenarioScope", "Impact.NotificationChannelScope", "Impact.NotificationTiming", "Impact.Window" }, entry.Location.Url);
		}
		private async Task<DepartmentSuppressStaffingInfo> ReadSuppressionAsync(int departmentId, CancellationToken ct)
		{
			var rows = (await settings.GetAllByDepartmentIdAsync(departmentId).WaitAsync(ct))?.ToList() ?? throw new InvalidOperationException();
			if (rows.Count > 500 || rows.Any(s => s.DepartmentId != departmentId)) throw new InvalidOperationException();
			var row = rows.SingleOrDefault(s => s.SettingType == (int)DepartmentSettingTypes.StaffingSuppressStaffingLevels);
			var value = row == null ? new DepartmentSuppressStaffingInfo() : ObjectSerialization.Deserialize<DepartmentSuppressStaffingInfo>(row.Setting) ?? throw new InvalidOperationException();
			if (value.StaffingLevelsToSupress == null || value.StaffingLevelsToSupress.Count > 1000) throw new InvalidOperationException();
			return value;
		}
		private static bool BroadcastBlocked(int departmentId) => Config.SystemBehaviorConfig.DoNotBroadcast &&
			!(Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments?.Contains(departmentId) ?? false);
		private static void Validate(IReadOnlyList<NotificationMemberEvidence> rows, int departmentId, int bound)
		{
			if (rows == null || rows.Count > bound || rows.Any(m => m == null || m.DepartmentId != departmentId || m.MemberId <= 0 ||
				string.IsNullOrWhiteSpace(m.UserId) || m.ProfileId <= 0 || m.StaffingKnown && !m.Staffing.HasValue ||
				m.ProfileId.HasValue && (!m.Sms.HasValue || !m.Email.HasValue || !m.Push.HasValue)) ||
				rows.Select(m => m.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count) throw new InvalidOperationException();
		}
		private sealed class Counts
		{
			public int Suppressed, UnknownStaffing, RecipientsMinimum, RecipientsMaximum, SmsMinimum, SmsMaximum,
				EmailMinimum, EmailMaximum, PushMinimum, PushMaximum, NoChannels;
		}
		private static Counts Count(IReadOnlyList<NotificationMemberEvidence> rows, bool suppress, List<int> levels, bool blocked)
		{
			var result = new Counts();
			foreach (var row in rows)
			{
				if (blocked) { result.Suppressed++; continue; }
				if (suppress && row.StaffingKnown && levels.Contains(row.Staffing.Value)) { result.Suppressed++; continue; }
				var staffingUnknown = suppress && levels.Count > 0 && !row.StaffingKnown;
				if (staffingUnknown) result.UnknownStaffing++;
				if (!row.ProfileId.HasValue)
				{
					result.RecipientsMaximum++; result.SmsMaximum++; result.EmailMaximum++; result.PushMaximum++; continue;
				}
				var channels = NotificationChannelSelection.From(row.Sms.Value, row.MobileVerified, row.Email.Value, row.EmailVerified, row.Push.Value);
				if (channels.Sms) { result.SmsMaximum++; if (!staffingUnknown) result.SmsMinimum++; }
				if (channels.Email) { result.EmailMaximum++; if (!staffingUnknown) result.EmailMinimum++; }
				if (channels.Push) { result.PushMaximum++; if (!staffingUnknown) result.PushMinimum++; }
				if (channels.Sms || channels.Email || channels.Push) { result.RecipientsMaximum++; if (!staffingUnknown) result.RecipientsMinimum++; }
				else result.NoChannels++;
			}
			return result;
		}
	}
}
