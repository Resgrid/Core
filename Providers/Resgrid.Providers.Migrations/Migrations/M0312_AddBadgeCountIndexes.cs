using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Indexes behind the menu and tab badge counts (v4 Counts/GetBadgeCounts), which every app asks for on start, on
	/// return to the foreground and after each call, chat and weather ping.
	/// <list type="bullet">
	/// <item>MessageRecipients had no index on UserId, so the unread inbox count (and the inbox list) scanned the table.
	/// (UserId, IsDeleted, ReadOn, MessageId) turns both into a seek and covers the count's join key.</item>
	/// <item>WeatherAlerts was indexed on DepartmentId alone, so counting a department's active Extreme and Severe alerts
	/// read every alert it ever had. (DepartmentId, Status, Severity, ExpiresUtc) covers that count.</item>
	/// <item>ShiftDays, ShiftSignups, ShiftSignupTradeUsers and ShiftSignupTrades.TargetShiftSignupId had no secondary
	/// index, so the member's shifts-today and trade-request counts (and the shift screens' own day and trade reads)
	/// scanned them: (ShiftId, Day), (ShiftId, ShiftDay, UserId), (UserId, ShiftSignupTradeId) and (TargetShiftSignupId).</item>
	/// </list>
	/// Built ONLINE where the edition supports it (SqlServerOnlineIndex), so neither write path blocks while it builds.
	/// </summary>
	[Migration(312, TransactionBehavior.None)]
	public class M0312_AddBadgeCountIndexes : Migration
	{
		public override void Up()
		{
			Execute.Sql(SqlServerOnlineIndex.Create("IX_MessageRecipients_UserId_IsDeleted_ReadOn", "MessageRecipients",
				new[] { "[UserId] ASC", "[IsDeleted] ASC", "[ReadOn] ASC", "[MessageId] ASC" }));

			Execute.Sql(SqlServerOnlineIndex.Create("IX_WeatherAlerts_DepartmentId_Status_Severity", "WeatherAlerts",
				new[] { "[DepartmentId] ASC", "[Status] ASC", "[Severity] ASC", "[ExpiresUtc] ASC" }));

			Execute.Sql(SqlServerOnlineIndex.Create("IX_ShiftDays_ShiftId_Day", "ShiftDays", new[] { "[ShiftId] ASC", "[Day] ASC" }));

			Execute.Sql(SqlServerOnlineIndex.Create("IX_ShiftSignups_ShiftId_ShiftDay_UserId", "ShiftSignups",
				new[] { "[ShiftId] ASC", "[ShiftDay] ASC", "[UserId] ASC" }));

			Execute.Sql(SqlServerOnlineIndex.Create("IX_ShiftSignupTradeUsers_UserId", "ShiftSignupTradeUsers",
				new[] { "[UserId] ASC", "[ShiftSignupTradeId] ASC" }));

			Execute.Sql(SqlServerOnlineIndex.Create("IX_ShiftSignupTrades_TargetShiftSignupId", "ShiftSignupTrades",
				new[] { "[TargetShiftSignupId] ASC" }, filter: "[TargetShiftSignupId] IS NOT NULL"));
		}

		public override void Down()
		{
			foreach (var (table, index) in new[]
			{
				("ShiftSignupTrades", "IX_ShiftSignupTrades_TargetShiftSignupId"),
				("ShiftSignupTradeUsers", "IX_ShiftSignupTradeUsers_UserId"),
				("ShiftSignups", "IX_ShiftSignups_ShiftId_ShiftDay_UserId"),
				("ShiftDays", "IX_ShiftDays_ShiftId_Day")
			})
			{
				if (Schema.Table(table).Index(index).Exists())
					Delete.Index(index).OnTable(table);
			}

			if (Schema.Table("WeatherAlerts").Index("IX_WeatherAlerts_DepartmentId_Status_Severity").Exists())
				Delete.Index("IX_WeatherAlerts_DepartmentId_Status_Severity").OnTable("WeatherAlerts");

			if (Schema.Table("MessageRecipients").Index("IX_MessageRecipients_UserId_IsDeleted_ReadOn").Exists())
				Delete.Index("IX_MessageRecipients_UserId_IsDeleted_ReadOn").OnTable("MessageRecipients");
		}
	}
}
