using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// Keys of the badge counts (v4 Counts/GetBadgeCounts <c>include</c> and each answer's <c>Key</c>). A key is permanent
	/// once shipped: the apps ask for it by name. Each one is computed by the <see cref="Services.IBadgeCountProvider"/>
	/// that declares it.
	/// </summary>
	public static class BadgeCountKeys
	{
		/// <summary>Active calls the member can see.</summary>
		public const string Calls = "calls";

		/// <summary>The member's unread inbox messages.</summary>
		public const string Messages = "messages";

		/// <summary>Unread chat messages across the member's (and the active unit's) channels.</summary>
		public const string Chats = "chats";

		/// <summary>The department's active Extreme and Severe weather alerts.</summary>
		public const string Weather = "weather";

		/// <summary>Active calls dispatched to the member (directly, their group, a role, a unit they have a seat on).</summary>
		public const string CallsImOn = "callsImOn";

		/// <summary>Check-in timers in warning or overdue across the active calls the member can see.</summary>
		public const string CheckIns = "checkIns";

		/// <summary>The member's certification records expired or expiring within the department's reminder horizon.</summary>
		public const string Certifications = "certifications";

		/// <summary>Open deployments the member is on the roster of.</summary>
		public const string Deployments = "deployments";

		/// <summary>Scheduled checks the member (or, in the Unit app, the active unit) can start now.</summary>
		public const string Checklists = "checklists";

		/// <summary>Shift days today (department-local) the member is on the roster of.</summary>
		public const string Shifts = "shifts";

		/// <summary>Shift trade requests awaiting the member's answer, plus their own still open or awaiting approval.</summary>
		public const string ShiftTrades = "shiftTrades";

		/// <summary>Open work orders assigned to the member or a role they hold.</summary>
		public const string WorkOrders = "workOrders";
	}

	/// <summary>Who a badge count is for. Built by the web tier from the signed-in member's claims.</summary>
	public sealed class BadgeCountContext
	{
		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		/// <summary>
		/// The unit the member says they are operating as (Unit app). Caller-supplied: a provider that counts for it must
		/// first check it as the list it counts does (chats: the member crews it; checklists: the member may view it).
		/// </summary>
		public int? ActiveUnitId { get; set; }

		/// <summary>The department's time zone (claims), for counts that depend on the department-local day.</summary>
		public string TimeZone { get; set; }

		public DateTime NowUtc { get; set; } = DateTime.UtcNow;

		private Lazy<Task<DepartmentModuleSettings>> _moduleSettings;
		private Lazy<Task<DateTime>> _localToday;

		/// <summary>
		/// Lookups several providers need, loaded at most once per request (the badge counts service sets them): the
		/// department's module switches and its local date.
		/// </summary>
		public void UseLookups(Func<Task<DepartmentModuleSettings>> moduleSettings, Func<Task<DateTime>> localToday)
		{
			_moduleSettings = moduleSettings == null ? null : new Lazy<Task<DepartmentModuleSettings>>(moduleSettings);
			_localToday = localToday == null ? null : new Lazy<Task<DateTime>>(localToday);
		}

		/// <summary>The department's module switches (DepartmentModuleSettings), or null when unknown.</summary>
		public Task<DepartmentModuleSettings> GetModuleSettingsAsync() => _moduleSettings?.Value ?? Task.FromResult<DepartmentModuleSettings>(null);

		/// <summary>Today in the department's time zone (the UTC date when the department is unknown).</summary>
		public Task<DateTime> GetLocalTodayAsync() => _localToday?.Value ?? Task.FromResult(NowUtc.Date);
	}

	/// <summary>One badge: the number to show, and optionally what it is made of (e.g. weather: extreme, severe).</summary>
	public sealed class BadgeCount
	{
		public BadgeCount(string key, int count, IReadOnlyDictionary<string, int> breakdown = null)
		{
			Key = key;
			Count = Math.Max(0, count);
			Breakdown = breakdown;
		}

		public string Key { get; }

		public int Count { get; }

		/// <summary>The parts of <see cref="Count"/>, by name, when they mean something on their own; null otherwise.</summary>
		public IReadOnlyDictionary<string, int> Breakdown { get; }
	}

	/// <summary>One row of a department's active weather alerts counted per severity.</summary>
	public class WeatherAlertSeverityCountRow
	{
		public int Severity { get; set; }

		public long AlertCount { get; set; }
	}

	/// <summary>A department's active Extreme and Severe weather alerts (the weather badge), as cached.</summary>
	[ProtoContract]
	public class WeatherAlertSeverityCounts
	{
		// IsRequired: written even when 0. Most departments have no alerts, and an all-default instance serializes to an
		// empty payload, which the cache provider reads as a miss (warning + key delete on every read).
		[ProtoMember(1, IsRequired = true)]
		public int Extreme { get; set; }

		[ProtoMember(2)]
		public int Severe { get; set; }
	}

	/// <summary>
	/// Cache keys of the badge counts, shared by the services that change what they count: CallsService rolls the
	/// department's calls version on every call save, delete and reopen (every member's call count, scoped or not, keys on
	/// it), WeatherAlertService drops the department's weather count when alerts arrive, change or expire.
	/// </summary>
	public static class BadgeCountCacheKeys
	{
		/// <summary>How long a counted value is kept; the invalidations above make most refreshes immediate.</summary>
		public static readonly TimeSpan CountCacheLength = TimeSpan.FromSeconds(60);

		/// <summary>How long a department's calls version lives without a call change.</summary>
		public static readonly TimeSpan VersionCacheLength = TimeSpan.FromDays(1);

		public static string OpenCallsVersion(int departmentId) => $"BadgeCounts_CallsVer_{departmentId}";

		/// <param name="scope">"all" for a department-wide viewer, else the member's user id (group-scoped dispatch).</param>
		public static string OpenCalls(int departmentId, string scope, string version) => $"BadgeCounts_OpenCalls_{departmentId}_{scope}_{version}";

		public static string Weather(int departmentId) => $"BadgeCounts_Weather_{departmentId}";

		public static string CallsImOn(int departmentId, string userId, string version) => $"BadgeCounts_CallsImOn_{departmentId}_{userId}_{version}";

		/// <param name="scope">"all" for a department-wide viewer, else the member's user id (group-scoped dispatch).</param>
		public static string CheckIns(int departmentId, string scope, string version) => $"BadgeCounts_CheckIns_{departmentId}_{scope}_{version}";
	}
}
