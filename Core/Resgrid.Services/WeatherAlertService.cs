using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class WeatherAlertService : IWeatherAlertService
	{
		private readonly IWeatherAlertRepository _weatherAlertRepository;
		private readonly IWeatherAlertSourceRepository _weatherAlertSourceRepository;
		private readonly IWeatherAlertZoneRepository _weatherAlertZoneRepository;
		private readonly IWeatherAlertProviderFactory _weatherAlertProviderFactory;
		private readonly IDepartmentSettingsRepository _departmentSettingsRepository;
		private readonly IDepartmentsService _departmentsService;
		private readonly IMessageService _messageService;
		private readonly ICallNotesRepository _callNotesRepository;
		private readonly ICacheProvider _cacheProvider;
		private readonly IEventAggregator _eventAggregator;

		public WeatherAlertService(
			IWeatherAlertRepository weatherAlertRepository,
			IWeatherAlertSourceRepository weatherAlertSourceRepository,
			IWeatherAlertZoneRepository weatherAlertZoneRepository,
			IWeatherAlertProviderFactory weatherAlertProviderFactory,
			IDepartmentSettingsRepository departmentSettingsRepository,
			IDepartmentsService departmentsService,
			IMessageService messageService,
			ICallNotesRepository callNotesRepository,
			ICacheProvider cacheProvider,
			IEventAggregator eventAggregator)
		{
			_weatherAlertRepository = weatherAlertRepository;
			_weatherAlertSourceRepository = weatherAlertSourceRepository;
			_weatherAlertZoneRepository = weatherAlertZoneRepository;
			_weatherAlertProviderFactory = weatherAlertProviderFactory;
			_departmentSettingsRepository = departmentSettingsRepository;
			_departmentsService = departmentsService;
			_messageService = messageService;
			_callNotesRepository = callNotesRepository;
			_cacheProvider = cacheProvider;
			_eventAggregator = eventAggregator;
		}

		#region Source CRUD

		public async Task<WeatherAlertSource> GetSourceByIdAsync(Guid sourceId)
		{
			return await _weatherAlertSourceRepository.GetByIdAsync(sourceId.ToString());
		}

		public async Task<List<WeatherAlertSource>> GetSourcesByDepartmentIdAsync(int departmentId)
		{
			var items = await _weatherAlertSourceRepository.GetSourcesByDepartmentIdAsync(departmentId);
			return items?.ToList() ?? new List<WeatherAlertSource>();
		}

		public async Task<WeatherAlertSource> SaveSourceAsync(WeatherAlertSource source, CancellationToken ct = default)
		{
			if (source.WeatherAlertSourceId == Guid.Empty)
			{
				source.CreatedOn = DateTime.UtcNow;
			}

			return await _weatherAlertSourceRepository.SaveOrUpdateAsync(source, ct, true);
		}

		public async Task<bool> DeleteSourceAsync(Guid sourceId, CancellationToken ct = default)
		{
			var source = await GetSourceByIdAsync(sourceId);
			if (source == null)
				return false;

			return await _weatherAlertSourceRepository.DeleteAsync(source, ct);
		}

		#endregion

		#region Alert Queries

		public async Task<WeatherAlert> GetAlertByIdAsync(Guid alertId)
		{
			return await _weatherAlertRepository.GetByIdAsync(alertId.ToString());
		}

		public async Task<List<WeatherAlert>> GetActiveAlertsByDepartmentIdAsync(int departmentId)
		{
			var items = await _weatherAlertRepository.GetActiveAlertsByDepartmentIdAsync(departmentId);
			return items?.ToList() ?? new List<WeatherAlert>();
		}

		public async Task<List<WeatherAlert>> GetAlertsByDepartmentAndSeverityAsync(int departmentId, WeatherAlertSeverity maxSeverity)
		{
			var items = await _weatherAlertRepository.GetAlertsByDepartmentAndSeverityAsync(departmentId, (int)maxSeverity);
			return items?.ToList() ?? new List<WeatherAlert>();
		}

		public async Task<List<WeatherAlert>> GetAlertsByDepartmentAndCategoryAsync(int departmentId, WeatherAlertCategory category)
		{
			var items = await _weatherAlertRepository.GetAlertsByDepartmentAndCategoryAsync(departmentId, (int)category);
			return items?.ToList() ?? new List<WeatherAlert>();
		}

		public async Task<List<WeatherAlert>> GetAlertHistoryAsync(int departmentId, DateTime startDate, DateTime endDate)
		{
			var items = await _weatherAlertRepository.GetAlertHistoryByDepartmentAsync(departmentId, startDate, endDate);
			return items?.ToList() ?? new List<WeatherAlert>();
		}

		public async Task<List<WeatherAlert>> GetActiveAlertsNearLocationAsync(int departmentId, double lat, double lng, double radiusMiles = 25)
		{
			// Get all active alerts for the department, then filter by proximity
			var alerts = await GetActiveAlertsByDepartmentIdAsync(departmentId);
			return alerts.Where(a =>
			{
				if (string.IsNullOrEmpty(a.CenterGeoLocation))
					return false;

				var parts = a.CenterGeoLocation.Split(',');
				if (parts.Length != 2 || !double.TryParse(parts[0], out var alertLat) || !double.TryParse(parts[1], out var alertLng))
					return false;

				var distance = CalculateDistanceMiles(lat, lng, alertLat, alertLng);
				return distance <= radiusMiles;
			}).ToList();
		}

		#endregion

		#region Zone CRUD

		public async Task<WeatherAlertZone> GetZoneByIdAsync(Guid zoneId)
		{
			return await _weatherAlertZoneRepository.GetByIdAsync(zoneId.ToString());
		}

		public async Task<List<WeatherAlertZone>> GetZonesByDepartmentIdAsync(int departmentId)
		{
			var items = await _weatherAlertZoneRepository.GetZonesByDepartmentIdAsync(departmentId);
			return items?.ToList() ?? new List<WeatherAlertZone>();
		}

		public async Task<WeatherAlertZone> SaveZoneAsync(WeatherAlertZone zone, CancellationToken ct = default)
		{
			if (zone.WeatherAlertZoneId == Guid.Empty)
			{
				zone.CreatedOn = DateTime.UtcNow;
			}

			return await _weatherAlertZoneRepository.SaveOrUpdateAsync(zone, ct, true);
		}

		public async Task<bool> DeleteZoneAsync(Guid zoneId, CancellationToken ct = default)
		{
			var zone = await GetZoneByIdAsync(zoneId);
			if (zone == null)
				return false;

			return await _weatherAlertZoneRepository.DeleteAsync(zone, ct);
		}

		#endregion

		#region Ingestion

		public async Task ProcessWeatherAlertSourceAsync(Guid sourceId, CancellationToken ct = default)
		{
			var source = await GetSourceByIdAsync(sourceId);
			if (source == null || !source.Active)
				return;

			// Populate the department admin contact email for upstream API User-Agent headers
			try
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(source.DepartmentId);
				if (department?.ManagingUser != null)
					source.ContactEmail = department.ManagingUser.Email;
			}
			catch { }

			try
			{
				var provider = _weatherAlertProviderFactory.GetProvider((WeatherAlertSourceType)source.SourceType);
				var fetchedAlerts = await provider.FetchAlertsAsync(source, ct);

				foreach (var alert in fetchedAlerts)
				{
					var existing = await _weatherAlertRepository.GetByExternalIdAndSourceIdAsync(
						alert.ExternalId, source.WeatherAlertSourceId);

					TruncateAlertFields(alert);

					if (existing == null)
					{
						// New alert
						await _weatherAlertRepository.InsertAsync(alert, ct, true);
					}
					else
					{
						// Update existing
						existing.Severity = alert.Severity;
						existing.Urgency = alert.Urgency;
						existing.Certainty = alert.Certainty;
						existing.Headline = alert.Headline;
						existing.Description = alert.Description;
						existing.Instruction = alert.Instruction;
						existing.AreaDescription = alert.AreaDescription;
						existing.Polygon = alert.Polygon;
						existing.Geocodes = alert.Geocodes;
						existing.CenterGeoLocation = alert.CenterGeoLocation;
						existing.ExpiresUtc = alert.ExpiresUtc;
						existing.LastUpdatedUtc = DateTime.UtcNow;
						await _weatherAlertRepository.UpdateAsync(existing, ct, true);
					}

					// Handle reference cancellations
					if (!string.IsNullOrEmpty(alert.ReferencesExternalId))
					{
						var referenced = await _weatherAlertRepository.GetByExternalIdAndSourceIdAsync(
							alert.ReferencesExternalId, source.WeatherAlertSourceId);
						if (referenced != null && referenced.Status == (int)WeatherAlertStatus.Active)
						{
							referenced.Status = (int)WeatherAlertStatus.Cancelled;
							referenced.LastUpdatedUtc = DateTime.UtcNow;
							await _weatherAlertRepository.UpdateAsync(referenced, ct, true);
						}
					}
				}

				source.LastPollUtc = DateTime.UtcNow;
				source.LastSuccessUtc = DateTime.UtcNow;
				source.IsFailure = false;
				source.ErrorMessage = null;
				await _weatherAlertSourceRepository.UpdateAsync(source, ct, true);
			}
			catch (Exception ex)
			{
				source.LastPollUtc = DateTime.UtcNow;
				source.IsFailure = true;
				source.ErrorMessage = ex.Message;
				await _weatherAlertSourceRepository.UpdateAsync(source, ct, true);
				throw;
			}
		}

		public async Task ProcessAllActiveSourcesAsync(CancellationToken ct = default)
		{
			var sources = await _weatherAlertSourceRepository.GetActiveSourcesForPollingAsync();
			if (sources == null)
				return;

			foreach (var source in sources)
			{
				// Check if it's time to poll based on interval
				if (source.LastPollUtc.HasValue)
				{
					var nextPoll = source.LastPollUtc.Value.AddMinutes(source.PollIntervalMinutes);
					if (DateTime.UtcNow < nextPoll)
						continue;
				}

				try
				{
					await ProcessWeatherAlertSourceAsync(source.WeatherAlertSourceId, ct);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}
		}

		public async Task ExpireOldAlertsAsync(CancellationToken ct = default)
		{
			var expired = await _weatherAlertRepository.GetExpiredUnprocessedAlertsAsync();
			if (expired == null)
				return;

			foreach (var alert in expired)
			{
				alert.Status = (int)WeatherAlertStatus.Expired;
				alert.LastUpdatedUtc = DateTime.UtcNow;
				await _weatherAlertRepository.UpdateAsync(alert, ct, true);
			}
		}

		public async Task SendPendingNotificationsAsync(CancellationToken ct = default)
		{
			var unnotified = await _weatherAlertRepository.GetUnnotifiedAlertsAsync();
			if (unnotified == null)
				return;

			// Group by department for efficient processing
			var byDepartment = unnotified.ToList().GroupBy(a => a.DepartmentId);

			foreach (var group in byDepartment)
			{
				var departmentId = group.Key;

				// Check if weather alerts are enabled for this department
				var enabledSetting = await _departmentSettingsRepository.GetDepartmentSettingByIdTypeAsync(
					departmentId, DepartmentSettingTypes.WeatherAlertsEnabled);
				if (enabledSetting != null && bool.TryParse(enabledSetting.Setting, out var isEnabled) && !isEnabled)
				{
					// Mark all as notified without sending
					foreach (var a in group)
					{
						a.NotificationSent = true;
						a.LastUpdatedUtc = DateTime.UtcNow;
						await _weatherAlertRepository.UpdateAsync(a, ct, true);
					}
					continue;
				}

				// Get the auto-message severity threshold setting
				var thresholdSetting = await _departmentSettingsRepository.GetDepartmentSettingByIdTypeAsync(
					departmentId, DepartmentSettingTypes.WeatherAlertAutoMessageSeverity);

				int threshold = (int)WeatherAlertSeverity.Severe; // Default: Severe=1
				if (thresholdSetting != null && int.TryParse(thresholdSetting.Setting, out var parsed))
					threshold = parsed;

				// Load department for sender info
				Department department = null;
				try
				{
					department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}

				foreach (var alert in group)
				{
					// Only send notifications for alerts meeting severity threshold
					// Lower enum value = higher severity (Extreme=0, Severe=1, etc.)
					if (alert.Severity <= threshold)
					{
						try
						{
							var members = await _departmentsService.GetAllMembersForDepartmentAsync(departmentId);
							if (members != null && members.Any())
							{
								// Use department managing user as sender for system messages
								var senderId = department?.ManagingUserId ?? members.First().UserId;

								var subject = FormatAlertSubject(alert);
								var body = FormatAlertMessageBody(alert, department);

								var message = new Message
								{
									Subject = subject,
									Body = body,
									SendingUserId = senderId,
									SentOn = DateTime.UtcNow,
									SystemGenerated = true,
									IsBroadcast = true,
									Type = 0,
									Recipients = string.Join(",", members.Select(m => m.UserId))
								};

								// Use SendMessageAsync which saves AND enqueues for push/SMS/email delivery
								var sent = await _messageService.SendMessageAsync(
									message, "Weather Alert System", departmentId, true, ct);

								if (sent)
								{
									alert.SystemMessageId = message.MessageId;
								}
							}
						}
						catch (Exception ex)
						{
							Logging.LogException(ex);
						}
					}

					alert.NotificationSent = true;
					alert.LastUpdatedUtc = DateTime.UtcNow;
					await _weatherAlertRepository.UpdateAsync(alert, ct, true);
				}
			}
		}

		#endregion

		#region Call Integration

		public async Task AttachWeatherAlertsToCallAsync(Call call, CancellationToken ct = default)
		{
			if (call == null || call.CallId == 0)
				return;

			try
			{
				// Check if call integration is enabled for this department
				var callIntSetting = await _departmentSettingsRepository.GetDepartmentSettingByIdTypeAsync(
					call.DepartmentId, DepartmentSettingTypes.WeatherAlertCallIntegration);

				if (callIntSetting == null || !bool.TryParse(callIntSetting.Setting, out var enabled) || !enabled)
					return;

				// Get active alerts for the department
				var activeAlerts = await GetActiveAlertsByDepartmentIdAsync(call.DepartmentId);
				if (activeAlerts == null || activeAlerts.Count == 0)
					return;

				// If the call has geolocation, filter to nearby alerts; otherwise attach all active alerts
				var alertsToAttach = activeAlerts;
				if (!string.IsNullOrEmpty(call.GeoLocationData))
				{
					var parts = call.GeoLocationData.Split(',');
					if (parts.Length >= 2 && double.TryParse(parts[0].Trim(), out var lat) && double.TryParse(parts[1].Trim(), out var lng))
					{
						alertsToAttach = activeAlerts.Where(a =>
						{
							if (string.IsNullOrEmpty(a.CenterGeoLocation))
								return true; // Include alerts without a center point (area-wide alerts)

							var alertParts = a.CenterGeoLocation.Split(',');
							if (alertParts.Length != 2 || !double.TryParse(alertParts[0].Trim(), out var aLat) || !double.TryParse(alertParts[1].Trim(), out var aLng))
								return true;

							return CalculateDistanceMiles(lat, lng, aLat, aLng) <= 50;
						}).ToList();
					}
				}

				if (alertsToAttach.Count == 0)
					return;

				// Get existing call notes to check for duplicates
				var existingNotes = await _callNotesRepository.GetCallNotesByCallIdAsync(call.CallId);
				var existingAlertIds = new HashSet<string>();
				if (existingNotes != null)
				{
					foreach (var note in existingNotes)
					{
						if (note.Source == (int)CallNoteSources.System && note.Note != null && note.Note.StartsWith("[WeatherAlert:"))
						{
							var endIdx = note.Note.IndexOf(']');
							if (endIdx > 15)
								existingAlertIds.Add(note.Note.Substring(15, endIdx - 15));
						}
					}
				}

				foreach (var alert in alertsToAttach)
				{
					var alertIdStr = alert.WeatherAlertId.ToString();

					// Skip if this exact alert was already attached
					if (existingAlertIds.Contains(alertIdStr))
						continue;

					var noteText = $"[WeatherAlert:{alertIdStr}] {alert.Event}";

					if (!string.IsNullOrEmpty(alert.Headline))
						noteText += $"\n{alert.Headline}";

					if (!string.IsNullOrEmpty(alert.AreaDescription))
						noteText += $"\nArea: {alert.AreaDescription}";

					var severityNames = new[] { "Extreme", "Severe", "Moderate", "Minor", "Unknown" };
					noteText += $"\nSeverity: {severityNames[Math.Min(alert.Severity, 4)]}";

					if (alert.ExpiresUtc.HasValue)
						noteText += $"\nExpires: {alert.ExpiresUtc.Value:yyyy-MM-dd HH:mm} UTC";

					if (!string.IsNullOrEmpty(alert.Instruction))
						noteText += $"\nInstructions: {alert.Instruction}";

					var callNote = new CallNote
					{
						CallId = call.CallId,
						UserId = call.ReportingUserId,
						Note = noteText,
						Source = (int)CallNoteSources.System,
						Timestamp = DateTime.UtcNow
					};

					await _callNotesRepository.SaveOrUpdateAsync(callNote, ct);
				}
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}
		}

		#endregion

		#region Cache

		public async Task<bool> InvalidateDepartmentWeatherCacheAsync(int departmentId)
		{
			// Cache invalidation - can be expanded when caching is implemented
			return await Task.FromResult(true);
		}

		#endregion

		#region Helpers

		private static readonly string[] SeverityNames = { "Extreme", "Severe", "Moderate", "Minor", "Unknown" };
		private static readonly string[] UrgencyNames = { "Immediate", "Expected", "Future", "Past", "Unknown" };
		private static readonly string[] CertaintyNames = { "Observed", "Likely", "Possible", "Unlikely", "Unknown" };
		private static readonly string[] CategoryNames = { "Meteorological", "Fire", "Health", "Environmental", "Other" };

		private static string FormatAlertSubject(WeatherAlert alert)
		{
			var sev = SeverityNames[Math.Min(alert.Severity, 4)];
			// Subject max 150 chars per Message entity
			var subject = $"[{sev}] Weather Alert: {alert.Event}";
			return subject.Length > 150 ? subject.Substring(0, 147) + "..." : subject;
		}

		private static string FormatAlertMessageBody(WeatherAlert alert, Department department)
		{
			var sb = new System.Text.StringBuilder();

			sb.AppendLine($"WEATHER ALERT: {alert.Event?.ToUpper()}");
			sb.AppendLine($"Severity: {SeverityNames[Math.Min(alert.Severity, 4)]}");

			if (alert.ExpiresUtc.HasValue)
			{
				if (department != null)
					sb.AppendLine($"Expires: {alert.ExpiresUtc.Value.TimeConverter(department):MM/dd/yyyy h:mm tt}");
				else
					sb.AppendLine($"Expires: {alert.ExpiresUtc.Value:yyyy-MM-dd HH:mm} UTC");
			}

			sb.AppendLine();

			if (!string.IsNullOrEmpty(alert.Headline))
				sb.AppendLine(alert.Headline);

			if (!string.IsNullOrEmpty(alert.Instruction))
			{
				sb.AppendLine();
				sb.AppendLine(alert.Instruction);
			}

			sb.AppendLine();
			sb.AppendLine("View active weather alerts for full details.");

			var body = sb.ToString();
			if (body.Length > 3950)
				body = body.Substring(0, 3947) + "...";

			return body;
		}

		private static double CalculateDistanceMiles(double lat1, double lng1, double lat2, double lng2)
		{
			const double R = 3959; // Earth's radius in miles
			var dLat = ToRadians(lat2 - lat1);
			var dLng = ToRadians(lng2 - lng1);
			var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
					Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
					Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
			var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
			return R * c;
		}

		private static double ToRadians(double degrees) => degrees * Math.PI / 180;

		#endregion

		private static void TruncateAlertFields(WeatherAlert alert)
		{
			alert.ExternalId = Truncate(alert.ExternalId, 500);
			alert.Sender = Truncate(alert.Sender, 500);
			alert.Event = Truncate(alert.Event, 500);
			alert.Headline = Truncate(alert.Headline, 500);
			alert.AreaDescription = Truncate(alert.AreaDescription, 500);
			alert.CenterGeoLocation = Truncate(alert.CenterGeoLocation, 100);
			alert.ReferencesExternalId = Truncate(alert.ReferencesExternalId, 500);
		}

		private static string Truncate(string value, int maxLength)
		{
			if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
				return value;

			return value.Substring(0, maxLength);
		}
	}
}
