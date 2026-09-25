using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Repositories.DataRepository
{
	public sealed partial class AdminAssistRepository
	{
		public async Task<IReadOnlyList<NotificationMemberEvidence>> ReadNotificationMembersAsync(int departmentId, int bound, CancellationToken ct)
		{
			if (departmentId <= 0 || bound < 1 || bound > 10000) throw new ArgumentException("Invalid preview bounds.");
			// The production staffing getter selects the latest global row by ID. Read a state only if that
			// row belongs to this department; otherwise mark it unknown rather than reading another tenant's state.
			var sql = $"SELECT {(IsPostgres ? "" : "TOP (" + P + "Take) ")}m.{Col("DepartmentId")}, m.{Col("DepartmentMemberId")} AS {Col("MemberId")}, m.{Col("UserId")}, " +
				$"p.{Col("UserProfileId")} AS {Col("ProfileId")}, p.{Col("SendNotificationSms")} AS {Col("Sms")}, p.{Col("MobileNumberVerified")} AS {Col("MobileVerified")}, " +
				$"p.{Col("SendNotificationEmail")} AS {Col("Email")}, p.{Col("EmailVerified")}, p.{Col("SendNotificationPush")} AS {Col("Push")}, " +
				$"CASE WHEN s.{Col("UserStateId")} IS NULL OR s.{Col("DepartmentId")}={P}DepartmentId THEN 1 ELSE 0 END AS {Col("StaffingKnown")}, " +
				$"CASE WHEN s.{Col("UserStateId")} IS NULL THEN 0 WHEN s.{Col("DepartmentId")}={P}DepartmentId THEN s.{Col("State")} ELSE NULL END AS {Col("Staffing")} " +
				$"FROM {Tbl("DepartmentMembers")} m LEFT JOIN {Tbl("UserProfiles")} p ON p.{Col("UserId")}=m.{Col("UserId")} " +
				$"LEFT JOIN {Tbl("UserStates")} s ON s.{Col("UserStateId")}=(SELECT MAX(latest.{Col("UserStateId")}) FROM {Tbl("UserStates")} latest WHERE latest.{Col("UserId")}=m.{Col("UserId")}) " +
				$"WHERE m.{Col("DepartmentId")}={P}DepartmentId AND m.{Col("IsDeleted")}={P}False AND (m.{Col("IsDisabled")} IS NULL OR m.{Col("IsDisabled")}={P}False) " +
				$"AND NULLIF(LTRIM(RTRIM(m.{Col("UserId")})),'') IS NOT NULL ORDER BY m.{Col("DepartmentMemberId")}, p.{Col("UserProfileId")}{(IsPostgres ? " LIMIT " + P + "Take" : "")}";
			return (await QueryAsync<NotificationMemberEvidence>(sql, new { DepartmentId = departmentId, Take = bound + 1, False = false }, ct)).ToList();
		}
	}
}
