using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Localization.Areas.User.ContentNotices;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The language each recipient of a Protocols / Training notice reads (round 9C): the member's own profile language, else the
	/// department's default (its managing member's language: the department has none of its own), else English. One instance per send;
	/// profiles are read once per member and the department default once.
	/// </summary>
	public sealed class ContentNoticeCultures
	{
		private readonly IUserProfileService _profiles;
		private readonly Department _department;
		private readonly Dictionary<string, UserProfile> _read = new Dictionary<string, UserProfile>(StringComparer.OrdinalIgnoreCase);
		private string _departmentLanguage;
		private bool _departmentLanguageRead;

		public ContentNoticeCultures(IUserProfileService profiles, Department department)
		{
			_profiles = profiles;
			_department = department;
		}

		/// <summary>The department's default language (its managing member's profile language), or null.</summary>
		public async Task<string> DepartmentLanguageAsync()
		{
			if (_departmentLanguageRead)
				return _departmentLanguage;
			_departmentLanguageRead = true;
			_departmentLanguage = string.IsNullOrWhiteSpace(_department?.ManagingUserId) ? null : (await ProfileAsync(_department.ManagingUserId))?.Language;
			return _departmentLanguage;
		}

		/// <summary>A member's culture and profile (the profile goes on to the sender so it is not read twice).</summary>
		public async Task<(CultureInfo Culture, UserProfile Profile)> ForMemberAsync(string userId)
		{
			var profile = await ProfileAsync(userId);
			var culture = ContentNoticeText.SupportedCulture(profile?.Language) ?? ContentNoticeText.CultureFor(await DepartmentLanguageAsync());
			return (culture, profile);
		}

		/// <summary>The culture for a stored language (an external learner's): that language, else the department default, else English.</summary>
		public async Task<CultureInfo> ForLanguageAsync(string language) =>
			ContentNoticeText.SupportedCulture(language) ?? ContentNoticeText.CultureFor(await DepartmentLanguageAsync());

		private async Task<UserProfile> ProfileAsync(string userId)
		{
			if (string.IsNullOrWhiteSpace(userId) || _profiles == null)
				return null;
			if (_read.TryGetValue(userId, out var cached))
				return cached;
			UserProfile profile = null;
			try
			{
				profile = await _profiles.GetProfileByUserIdAsync(userId);
			}
			catch (Exception ex)
			{
				// No profile means the default language, never a lost notice.
				Logging.LogException(ex, $"The profile language of {userId} could not be read for a notice.");
			}
			_read[userId] = profile;
			return profile;
		}

		/// <summary>A UTC moment as the department's local date, in the reader's culture ("d").</summary>
		public static string LocalDate(DateTime? utc, Department department, CultureInfo culture)
		{
			if (!utc.HasValue)
				return null;
			var value = DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc);
			var local = department == null ? value : value.TimeConverter(department);
			return local.ToString("d", culture ?? ContentNoticeText.English);
		}
	}

	/// <summary>
	/// The common send loop of the Protocols / Training notifiers (round 9C): active members only (inactive-member policy), nothing where
	/// the department cannot transmit, each member's text composed in their own language, one failing member never stops the rest.
	/// </summary>
	public static class ContentNoticeDelivery
	{
		/// <summary>One notice: its title and message in the culture it was composed for.</summary>
		public readonly struct Notice
		{
			public Notice(string title, string message)
			{
				Title = title;
				Message = message;
			}

			public string Title { get; }

			public string Message { get; }
		}

		public static async Task<int> SendAsync(ICommunicationService communication, IDepartmentsService departments, IDepartmentSettingsService settings,
			IUserProfileService profiles, int departmentId, IEnumerable<string> userIds, Func<CultureInfo, Department, Notice> compose, string eventCode,
			string failure, CancellationToken cancellationToken)
		{
			var wanted = (userIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (wanted.Count == 0 || compose == null || !ConfigHelper.CanTransmit(departmentId))
				return 0;

			var department = await departments.GetDepartmentByIdAsync(departmentId, false);
			if (department == null)
				return 0;

			// Inactive-member policy: deleted, disabled and hidden members are never addressed by automation.
			var active = new HashSet<string>(await departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
			var number = await settings.GetTextToCallNumberForDepartmentAsync(departmentId);
			var cultures = new ContentNoticeCultures(profiles, department);
			var composed = new Dictionary<string, Notice>(StringComparer.Ordinal);
			var sent = 0;
			foreach (var userId in wanted.Where(active.Contains))
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					var (culture, profile) = await cultures.ForMemberAsync(userId);
					if (!composed.TryGetValue(culture.Name, out var notice))
						composed[culture.Name] = notice = compose(culture, department);
					if (await communication.SendNotificationAsync(userId, departmentId, notice.Message, number, department, notice.Title, profile, false, eventCode))
						sent++;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					// One member's channel failing never stops the others.
					Logging.LogException(ex, $"{failure ?? "A notice"} to {userId} in department {departmentId} failed.");
				}
			}

			return sent;
		}
	}
}
