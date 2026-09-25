using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Repositories.DataRepository
{
	public sealed partial class AdminAssistRepository
	{
		public async Task<DepartmentSecurityPolicy> ReadSecurityPolicyAsync(int departmentId, CancellationToken ct)
		{
			if (departmentId <= 0) throw new ArgumentException("Invalid department.");
			var rows = (await QueryAsync<DepartmentSecurityPolicy>($"SELECT {(IsPostgres ? "" : "TOP (2) ")}{Cols("DepartmentId", "RequireMfa", "RequireSso", "SessionTimeoutMinutes", "MaxConcurrentSessions", "PasswordExpirationDays", "MinPasswordLength")} " +
				$"FROM {Tbl("DepartmentSecurityPolicies")} WHERE {Col("DepartmentId")}={P}DepartmentId{(IsPostgres ? " LIMIT 2" : "")}", new { DepartmentId = departmentId }, ct)).ToList();
			return rows.SingleOrDefault();
		}
		public async Task<SecurityImpactEvidence> ReadSecurityImpactAsync(int departmentId, DateTime nowUtc, int bound, CancellationToken ct)
		{
			if (departmentId <= 0 || bound < 1 || bound > 10000) throw new ArgumentException("Invalid preview bounds.");
			var top = IsPostgres ? "" : "TOP (" + P + "Take) "; var limit = IsPostgres ? " LIMIT " + P + "Take" : "";
			var args = new { DepartmentId = departmentId, Take = bound + 1, False = false, True = true, Now = DatabaseTimestamp(nowUtc), Active = (int)UserSessionState.Active };
			var current = $"m.{Col("DepartmentId")}={P}DepartmentId AND m.{Col("IsDeleted")}={P}False AND (m.{Col("IsDisabled")} IS NULL OR m.{Col("IsDisabled")}={P}False) AND NULLIF(LTRIM(RTRIM(m.{Col("UserId")})),'') IS NOT NULL";
			var members = (await QueryAsync<SecurityMemberEvidence>($"SELECT {top}m.{Col("DepartmentId")},m.{Col("DepartmentMemberId")} AS {Col("MemberId")},m.{Col("UserId")},m.{Col("PasswordLastSetOn")},u.{Col("TwoFactorEnabled")} " +
				$"FROM {Tbl("DepartmentMembers")} m LEFT JOIN {Tbl("AspNetUsers")} u ON u.{Col("Id")}=m.{Col("UserId")} WHERE {current} ORDER BY m.{Col("DepartmentMemberId")}{limit}", args, ct)).ToList();
			var sessions = (await QueryAsync<SecuritySessionEvidence>($"SELECT {top}s.{Col("DepartmentId")},s.{Col("UserSessionId")} AS {Col("Id")},s.{Col("UserId")},s.{Col("CreatedOn")},s.{Col("LastActiveOn")},s.{Col("ExpiresOn")},s.{Col("AuthenticationGeneration")},u.{Col("AuthenticationGeneration")} AS {Col("CurrentGeneration")} " +
				$"FROM {Tbl("UserSessions")} s LEFT JOIN {Tbl("AspNetUsers")} u ON u.{Col("Id")}=s.{Col("UserId")} WHERE s.{Col("DepartmentId")}={P}DepartmentId AND s.{Col("State")}={P}Active AND s.{Col("ExpiresOn")}>{P}Now " +
				$"AND EXISTS (SELECT 1 FROM {Tbl("DepartmentMembers")} m WHERE m.{Col("UserId")}=s.{Col("UserId")} AND {current}) ORDER BY s.{Col("UserSessionId")}{limit}", args, ct)).ToList();
			var providers = await ScalarAsync<int>($"SELECT COUNT(*) FROM (SELECT {top}1 AS n FROM {Tbl("DepartmentSsoConfigs")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("IsEnabled")}={P}True{limit}) sample", args, ct);
			return new(members, sessions, providers);
		}
	}
}
