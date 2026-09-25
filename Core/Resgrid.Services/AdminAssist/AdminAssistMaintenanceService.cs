using System;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>Bounded metadata refresh and explicitly opted-in, generic weekly admin follow-up. No inference.</summary>
	public sealed class AdminAssistMaintenanceService(IAdminAssistMaintenanceStore store, IAdminAssistAccessService access,
		IAdminAssistWorklistService worklist, IDepartmentsService departments, IRecordsAuthorizationService membership,
		ICommunicationService communication, IDepartmentSettingsService settings, IUserProfileService profiles,
		TimeProvider clock) : IAdminAssistMaintenanceService
	{
		private static readonly ResourceManager Labels = new(typeof(Resgrid.Localization.Areas.User.AdminAssist.AdminAssist));
		public async Task RunDepartmentAsync(int departmentId, CancellationToken ct)
		{
			var now = clock.GetUtcNow().UtcDateTime;
			var lease = Guid.NewGuid().ToString("D");
			if (!await store.TryLeaseAsync(departmentId, lease, now, ct)) return;
			DateTime? evaluated = null;
			try
			{
				// Privacy/retention cleanup also runs for departments with no remaining administrator or
				// whose UI rollout was disabled; neither condition should preserve departed-user preferences.
				await store.PurgeExpiredMetadataAsync(departmentId, now, ct);
				// The workload has no decryption grant. Sources retain current member/permission checks;
				// protected or incomplete evidence stays unknown and cannot resolve an existing failure.
				var admins = await departments.GetActiveAdminsForDepartmentAsync(departmentId);
				var admin = admins?.OrderBy(a => a.UserId, StringComparer.Ordinal).FirstOrDefault();
				if (admin == null) return;
				var actor = new AdminAssistActor(departmentId, admin.UserId);
				if (!await access.CanAccessAsync(actor, false, ct)) return;
				await worklist.VerifyAsync(actor, ct);
				evaluated = clock.GetUtcNow().UtcDateTime;
				if (!Config.AdminAssistConfig.SendAdminDigests) return;
				var department = await departments.GetDepartmentByIdAsync(departmentId, true);
				if (department == null) return;
				var zone = TimeZoneInfo.FindSystemTimeZoneById(department.TimeZone);
				foreach (var preference in await store.GetDigestPreferencesAsync(departmentId, ct))
				{
					try
					{
						ct.ThrowIfCancellationRequested(); now = clock.GetUtcNow().UtcDateTime;
						var recipient = new AdminAssistActor(departmentId, preference.UserId, preference.Locale);
						if (!await access.CanAccessAsync(recipient, false, ct) || !await membership.IsAssignableMemberAsync(preference.UserId, departmentId)) continue;
						if (AdminAssistDigestSchedule.IsQuiet(now, zone, preference.QuietStartHour, preference.QuietEndHour)) continue;
						var week = AdminAssistDigestSchedule.Week(now, zone);
						if (preference.LastAttemptWeek == week) continue;
						var profile = await profiles.GetProfileByUserIdAsync(preference.UserId, true);
						if (profile == null) continue;
						var number = await settings.GetTextToCallNumberForDepartmentAsync(departmentId);
						if (!await store.ClaimDigestAsync(preference, week, now, ct)) continue;
						var current = await store.GetPreferencesAsync(departmentId, preference.UserId, ct);
						if (!current.DigestEnabled || current.Revision != preference.Revision || !await access.CanAccessAsync(recipient, false, ct) ||
							!await membership.IsAssignableMemberAsync(preference.UserId, departmentId))
						{
							await store.CompleteDigestAsync(departmentId, preference.UserId, week, "Suppressed", now, ct); continue;
						}
						var outcome = "HandoffUnconfirmed";
						try
						{
							var locale = CultureInfo.GetCultureInfo(preference.Locale);
							var message = Labels.GetString("Ui.DigestMessage", locale) + " " + (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/AdminAssist/Index";
							if (await communication.SendNotificationAsync(preference.UserId, departmentId, message, number, department,
								Labels.GetString("Ui.Title", locale), profile)) outcome = "HandedOff";
						}
						catch (Exception) { /* The provider may have accepted the request. Preserve the weekly claim. */ }
						await store.CompleteDigestAsync(departmentId, preference.UserId, week, outcome, clock.GetUtcNow().UtcDateTime, ct);
					}
					finally { await store.AdvanceDigestCursorAsync(departmentId, preference.UserId, ct); }
				}
			}
			finally { await store.CompleteLeaseAsync(departmentId, lease, evaluated, ct); }
		}
	}
}
