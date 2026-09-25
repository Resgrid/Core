using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.AdminAssist
{
	public sealed class AdminAssistPreferences
	{
		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public bool DigestEnabled { get; set; }
		public int QuietStartHour { get; set; } = 20;
		public int QuietEndHour { get; set; } = 8;
		public string Locale { get; set; } = "en";
		public long Revision { get; set; }
		public DateTime? LastAttemptOn { get; set; }
		public string LastAttemptWeek { get; set; }
		public string LastAttemptOutcome { get; set; }
	}
	public sealed record AdminAssistPreferencesCommand(long ExpectedRevision, bool DigestEnabled, int QuietStartHour, int QuietEndHour);
	public sealed record AdminAssistWorkerStatus(DateTime? LastEvaluatedOn, DateTime? LastDigestOn);
	public interface IAdminAssistMaintenanceStore
	{
		Task<IReadOnlyList<int>> GetDueDepartmentsAsync(DateTime nowUtc, int take, CancellationToken ct);
		Task<bool> TryLeaseAsync(int departmentId, string lease, DateTime nowUtc, CancellationToken ct);
		Task CompleteLeaseAsync(int departmentId, string lease, DateTime? evaluatedOn, CancellationToken ct);
		Task<AdminAssistWorkerStatus> GetWorkerStatusAsync(int departmentId, CancellationToken ct);
		Task<AdminAssistPreferences> GetPreferencesAsync(int departmentId, string userId, CancellationToken ct);
		Task<IReadOnlyList<AdminAssistPreferences>> GetDigestPreferencesAsync(int departmentId, CancellationToken ct);
		Task AdvanceDigestCursorAsync(int departmentId, string userId, CancellationToken ct);
		Task SavePreferencesAsync(AdminAssistActor actor, AdminAssistPreferencesCommand command, CancellationToken ct);
		Task<bool> ClaimDigestAsync(AdminAssistPreferences preference, string week, DateTime nowUtc, CancellationToken ct);
		Task CompleteDigestAsync(int departmentId, string userId, string week, string outcome, DateTime nowUtc, CancellationToken ct);
		Task<int> PurgeExpiredMetadataAsync(int departmentId, DateTime nowUtc, CancellationToken ct);
	}
	public interface IAdminAssistMaintenanceService
	{
		Task RunDepartmentAsync(int departmentId, CancellationToken ct);
	}
	public static class AdminAssistDigestSchedule
	{
		public static bool IsQuiet(DateTime utc, TimeZoneInfo zone, int start, int end)
		{
			if (utc.Kind != DateTimeKind.Utc || start is < 0 or > 23 || end is < 0 or > 23) throw new ArgumentException("Invalid digest schedule.");
			var hour = TimeZoneInfo.ConvertTimeFromUtc(utc, zone).Hour;
			return start == end ? false : start < end ? hour >= start && hour < end : hour >= start || hour < end;
		}
		public static string Week(DateTime utc, TimeZoneInfo zone)
		{
			if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC required.");
			var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone).Date;
			return local.AddDays(-(((int)local.DayOfWeek + 6) % 7)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
		}
	}
}
