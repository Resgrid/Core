using System;
using Resgrid.Model;
using Resgrid.Model.Security;

namespace Resgrid.Services
{
	/// <summary>Builds an activity row from what a caller knows and the session it was for (plan section 6.5).</summary>
	public static class MfaActivityRecords
	{
		public static MfaActivity Create(MfaActivityEntry entry, UserSession session, DateTime occurredOnUtc) => new()
		{
			MfaActivityId = Guid.NewGuid().ToString(),
			UserId = entry.UserId,
			OccurredOnUtc = occurredOnUtc,
			Method = (int)entry.Method,
			Purpose = (int)entry.Purpose,
			Successful = entry.Successful,
			ClientApplication = (int)entry.ClientApplication,
			InstallationLabel = Limit(entry.InstallationLabel ?? session?.DeviceName),
			SharedMode = entry.SharedMode || session?.SharedMode == true,
			DepartmentId = entry.DepartmentId,
			SessionId = Limit(entry.SessionId),
			ApproverSessionId = Limit(entry.ApproverSessionId)
		};

		private static string Limit(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var sanitized = value.Replace("\r", " ").Replace("\n", " ").Trim();
			return sanitized.Length <= 256 ? sanitized : sanitized.Substring(0, 256);
		}
	}
}
