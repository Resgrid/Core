using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Text for the T3 program features composed off the request thread (worker 94 reports and alerts, notifications to someone else):
	/// read culture-explicitly from the Trainings resources in the recipient's language, never CurrentUICulture
	/// (outbound-message-localization-gap).
	/// </summary>
	public static class LmsProgramText
	{
		private static readonly ResourceManager Strings = new("Resgrid.Localization.Areas.User.Trainings.Trainings", typeof(Resgrid.Localization.SupportedLocales).Assembly);

		/// <summary>A supported culture for a profile language (English otherwise).</summary>
		public static CultureInfo CultureFor(string language)
		{
			try
			{
				if (!string.IsNullOrWhiteSpace(language))
				{
					var culture = CultureInfo.GetCultureInfo(language.Trim());
					if (Resgrid.Localization.SupportedLocales.GetSupportedCultures().Contains(culture.TwoLetterISOLanguageName))
						return culture;
				}
			}
			catch (CultureNotFoundException)
			{
			}

			return CultureInfo.GetCultureInfo("en");
		}

		public static string Get(string key, CultureInfo culture)
		{
			if (string.IsNullOrEmpty(key))
				return string.Empty;
			var language = CultureInfo.GetCultureInfo((culture ?? CultureInfo.GetCultureInfo("en")).TwoLetterISOLanguageName);
			return Strings.GetResourceSet(language, true, false)?.GetString(key)
				?? Strings.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, false)?.GetString(key)
				?? Strings.GetString(key, CultureInfo.InvariantCulture)
				?? key;
		}

		public static string Format(string key, CultureInfo culture, params object[] args)
		{
			var text = Get(key, culture);
			try
			{
				return args == null || args.Length == 0 ? text : string.Format(culture ?? CultureInfo.InvariantCulture, text, args);
			}
			catch (FormatException)
			{
				return text;
			}
		}
	}

	public class LmsProgramNotifier : ILmsProgramNotifier
	{
		private readonly Lazy<ICommunicationService> _communication;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _settings;
		private readonly IUserProfileService _profiles;

		public LmsProgramNotifier(Lazy<ICommunicationService> communication, IDepartmentsService departments, IDepartmentSettingsService settings, IUserProfileService profiles)
		{
			_communication = communication;
			_departments = departments;
			_settings = settings;
			_profiles = profiles;
		}

		public async Task<int> SendAsync(int departmentId, IEnumerable<string> userIds, string titleKey, string messageKey, Func<CultureInfo, object[]> args, string eventCode,
			CancellationToken cancellationToken = default)
		{
			var wanted = (userIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (wanted.Count == 0 || !ConfigHelper.CanTransmit(departmentId))
				return 0;

			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			if (department == null)
				return 0;

			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var number = await _settings.GetTextToCallNumberForDepartmentAsync(departmentId);
			var sent = 0;
			foreach (var userId in wanted.Where(active.Contains))
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					var profile = await _profiles.GetProfileByUserIdAsync(userId);
					var culture = LmsProgramText.CultureFor(profile?.Language);
					var title = LmsProgramText.Get(titleKey, culture);
					var message = LmsProgramText.Format(messageKey, culture, args?.Invoke(culture) ?? Array.Empty<object>());
					if (await _communication.Value.SendNotificationAsync(userId, departmentId, message, number, department, title, profile, false, eventCode))
						sent++;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"LMS program notification to {userId} in department {departmentId} failed.");
				}
			}

			return sent;
		}
	}

	/// <summary>Trainings → Programs → Settings (DepartmentSettingTypes.LmsProgramSettings, 144). Not cached: read on the pages and once per department per worker run.</summary>
	public class LmsProgramSettingsService : ILmsProgramSettingsService
	{
		private readonly IDepartmentSettingsService _settings;
		private readonly ILearningRecordService _learningRecords;
		private readonly IEventAggregator _events;

		public LmsProgramSettingsService(IDepartmentSettingsService settings, ILearningRecordService learningRecords, IEventAggregator events)
		{
			_settings = settings;
			_learningRecords = learningRecords;
			_events = events;
		}

		public Task<LmsProgramSettings> GetAsync(int departmentId) => ReadAsync(_settings, departmentId);

		/// <summary>
		/// Reads setting 144 through the department settings service (normalized; defaults when missing or unreadable). Shared with the class and
		/// public class services, which take the department's training currency from it without depending on this service.
		/// </summary>
		public static async Task<LmsProgramSettings> ReadAsync(IDepartmentSettingsService settings, int departmentId)
		{
			if (departmentId <= 0 || settings == null)
				return new LmsProgramSettings().Normalized();

			var setting = await settings.GetSettingByTypeAsync(departmentId, DepartmentSettingTypes.LmsProgramSettings);
			if (!string.IsNullOrWhiteSpace(setting?.Setting))
			{
				try
				{
					var stored = ObjectSerialization.Deserialize<LmsProgramSettings>(setting.Setting);
					if (stored != null)
						return stored.Normalized();
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Unreadable LMS program settings for department {departmentId}");
				}
			}

			return new LmsProgramSettings().Normalized();
		}

		/// <summary>The department's training currency (setting 144), or the cluster default when it chose none or the read fails.</summary>
		public static async Task<string> TrainingCurrencyAsync(IDepartmentSettingsService settings, int departmentId)
		{
			try
			{
				return LmsTrainingCurrency.Resolve((await ReadAsync(settings, departmentId))?.RequestCurrency);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The training currency of department {departmentId} could not be read.");
				return LmsTrainingCurrency.SystemDefault;
			}
		}

		public async Task<LmsProgramSettings> SaveAsync(int departmentId, LmsProgramSettings settings, string userId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0)
				throw new InvalidOperationException("lms_program_not_found");

			var stored = (settings ?? new LmsProgramSettings()).Normalized();
			if (stored.FamiliarizationCategoryId > 0 && !(await _learningRecords.GetCategoriesAsync(departmentId) ?? new List<TrainingCategory>()).Any(c => c.TrainingCategoryId == stored.FamiliarizationCategoryId && !c.IsDeleted))
				throw new InvalidOperationException("lms_program_category_invalid");
			if (stored.FamiliarizationEnabled && stored.FamiliarizationCategoryId <= 0)
				throw new InvalidOperationException("lms_program_familiarization_category_required");

			var before = await GetAsync(departmentId);
			await _settings.SaveOrUpdateSettingAsync(departmentId, ObjectSerialization.Serialize(stored), DepartmentSettingTypes.LmsProgramSettings, cancellationToken);
			_events?.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = AuditLogTypes.LmsProgramSettingsChanged,
				Before = JsonConvert.SerializeObject(before),
				After = JsonConvert.SerializeObject(stored),
				Successful = true
			});
			return stored;
		}
	}
}
