using System;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Localization;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>Hands off generic notices using routing metadata, without reading Inventory content or obtaining a grant.</summary>
	public sealed class InventoryAlertNotifications
	{
		private readonly IInventoryAlertService _alerts;
		private readonly IDepartmentsService _departments;
		private readonly IUserProfileService _profiles;
		private readonly IDepartmentSettingsService _settings;
		private readonly ICommunicationService _communication;
		private readonly TimeProvider _clock;
		private static readonly ResourceManager Strings = new ResourceManager("Resgrid.Localization.Areas.User.Inventory.Inventory", typeof(SupportedLocales).Assembly);

		public InventoryAlertNotifications(IInventoryAlertService alerts, IDepartmentsService departments, IUserProfileService profiles,
			IDepartmentSettingsService settings, ICommunicationService communication, TimeProvider clock = null)
		{ _alerts = alerts; _departments = departments; _profiles = profiles; _settings = settings; _communication = communication; _clock = clock ?? TimeProvider.System; }

		public async Task<int> ProcessDepartmentAsync(int departmentId, CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();
			var members = await _departments.GetAllMembersForDepartmentUnlimitedAsync(departmentId, true);
			var users = members.Where(m => m.DepartmentId == departmentId && !m.IsDeleted && m.IsDisabled != true && !string.IsNullOrWhiteSpace(m.UserId))
				.Select(m => m.UserId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();
			var handedOff = 0; var failed = false;
			foreach (var user in users)
			{
				// Core claim/access checks apply current Inventory permissions, including delegated managers.
				for (var batch = 0; batch < 100; batch++)
				{
					ct.ThrowIfCancellationRequested();
					var delivery = await _alerts.ClaimAlertAsync(departmentId, user);
					if (delivery == null) break;
					if (delivery.DepartmentId != departmentId || delivery.UserId != user || string.IsNullOrEmpty(delivery.Id)
						|| string.IsNullOrEmpty(delivery.AlertId) || string.IsNullOrEmpty(delivery.ClaimToken))
						throw new InvalidOperationException("Inventory alert claim metadata is invalid.");
					try
					{
						ct.ThrowIfCancellationRequested();
						var profile = await _profiles.GetProfileByUserIdAsync(user, true);
						ct.ThrowIfCancellationRequested();
						var department = await _departments.GetDepartmentByIdAsync(departmentId, true);
						var number = await _settings.GetTextToCallNumberForDepartmentAsync(departmentId);
						var culture = Culture(profile?.Language);
						var title = Strings.GetString("M5AlertNotificationTitle", culture) ?? "Inventory alerts";
						var message = (Strings.GetString("M5AlertNotificationMessage", culture) ?? "Inventory needs attention. Sign in to review current alerts.")
							+ " " + (Config.SystemBehaviorConfig.ResgridBaseUrl ?? "").TrimEnd('/') + "/User/Inventory/Operations?tab=Alerts";
						ct.ThrowIfCancellationRequested();
						// Slow routing/profile lookups precede the final current-state and permission gate.
						var allowed = department?.DepartmentId == departmentId && await _alerts.CanReceiveAlertAsync(departmentId, user, delivery.AlertId);
						ct.ThrowIfCancellationRequested();
						if (allowed && (!delivery.LeaseUntil.HasValue || delivery.LeaseUntil.Value <= _clock.GetUtcNow().UtcDateTime.AddSeconds(30)))
							throw new InvalidOperationException("Inventory alert notification lease is expiring.");
						var sent = allowed && await _communication.SendNotificationAsync(user, departmentId, message, number, department, title, profile);
						// Persist an accepted handoff even if cancellation arrived during the provider call.
						await _alerts.FinishAlertAsync(departmentId, delivery.Id, delivery.ClaimToken, sent);
						if (sent) handedOff++;
					}
					catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
					catch (Exception ex)
					{
						failed = true;
						Resgrid.Framework.Logging.LogError($"Inventory alert notification failed for department {departmentId}: {ex.GetType().FullName}.");
						// Completion is claim-token fenced. A completed or superseded handoff cannot be reset.
						try { await _alerts.FinishAlertAsync(departmentId, delivery.Id, delivery.ClaimToken, false); }
						catch { /* The lease recovers a failed release without storing exception or message content. */ }
					}
				}
			}
			ct.ThrowIfCancellationRequested();
			if (failed) throw new InvalidOperationException("Inventory alert notifications require a retry.");
			return handedOff;
		}

		private static CultureInfo Culture(string language)
		{
			try
			{
				var culture = CultureInfo.GetCultureInfo(language ?? "en");
				if (SupportedLocales.GetSupportedCultures().Contains(culture.TwoLetterISOLanguageName)) return culture;
			}
			catch (CultureNotFoundException) { }
			return CultureInfo.GetCultureInfo("en");
		}
	}
}
