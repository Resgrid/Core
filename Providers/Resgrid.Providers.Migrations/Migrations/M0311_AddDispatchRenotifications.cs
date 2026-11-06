using System.Data;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Dispatch re-notification (DepartmentSettingTypes.DispatchRenotifyConfig, worker 78): one row per person or unit a call
	/// broadcast reached while the department re-notifies, advanced by the sweep until the recipient sets a status, the attempts
	/// run out, or the row no longer applies. Ids and times only. Rows cascade with their call; the sweep purges finished rows.
	/// </summary>
	[Migration(311)]
	public class M0311_AddDispatchRenotifications : Migration
	{
		private const string Table = "DispatchRenotifications";

		public override void Up()
		{
			if (!Schema.Table(Table).Exists())
			{
				Create.Table(Table)
					.WithColumn("DispatchRenotificationId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("RecipientType").AsInt32().NotNullable()
					.WithColumn("RecipientId").AsString(128).NotNullable()
					.WithColumn("SourceType").AsInt32().NotNullable()
					.WithColumn("SourceId").AsString(128).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AcknowledgeAfter").AsDateTime2().NotNullable()
					.WithColumn("NextNotifyOn").AsDateTime2().Nullable()
					.WithColumn("LastNotifiedOn").AsDateTime2().Nullable()
					.WithColumn("AcknowledgedOn").AsDateTime2().Nullable()
					.WithColumn("CompletedOn").AsDateTime2().Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable();

				Create.ForeignKey("FK_DispatchRenotifications_Calls")
					.FromTable(Table).ForeignColumn("CallId")
					.ToTable("Calls").PrimaryColumn("CallId")
					.OnDelete(Rule.Cascade);
			}

			if (!Schema.Table(Table).Index("UX_DispatchRenotifications_Call_Recipient").Exists())
			{
				Create.Index("UX_DispatchRenotifications_Call_Recipient")
					.OnTable(Table)
					.OnColumn("CallId").Ascending()
					.OnColumn("RecipientType").Ascending()
					.OnColumn("RecipientId").Ascending()
					.WithOptions().Unique();
			}

			if (!Schema.Table(Table).Index("IX_DispatchRenotifications_Status_NextNotifyOn").Exists())
			{
				Create.Index("IX_DispatchRenotifications_Status_NextNotifyOn")
					.OnTable(Table)
					.OnColumn("Status").Ascending()
					.OnColumn("NextNotifyOn").Ascending();
			}

			if (!Schema.Table(Table).Index("IX_DispatchRenotifications_CompletedOn").Exists())
			{
				Create.Index("IX_DispatchRenotifications_CompletedOn")
					.OnTable(Table)
					.OnColumn("CompletedOn").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table(Table).Exists())
				Delete.Table(Table);
		}
	}
}
