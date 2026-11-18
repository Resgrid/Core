using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// CallBroadcastOutbox: the page owed to resources an edit added to a live call, written right after the call saves so a broadcast
	/// the queue refuses is retried (worker 12) instead of lost, since a retry of the same edit sees nothing new to page. The filtered
	/// index serves the worker's due scan, the call index the edit's own flush, the department index UnlinkedDepartmentCleanup and the
	/// completed index the purge. No foreign keys. Existence-guarded for safe retry.
	/// </summary>
	[Migration(321)]
	public class M0321_AddCallBroadcastOutbox : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("CallBroadcastOutbox").Exists())
			{
				Create.Table("CallBroadcastOutbox")
					.WithColumn("CallBroadcastOutboxId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("UserIds").AsString(int.MaxValue).Nullable()
					.WithColumn("GroupIds").AsString(int.MaxValue).Nullable()
					.WithColumn("UnitIds").AsString(int.MaxValue).Nullable()
					.WithColumn("RoleIds").AsString(int.MaxValue).Nullable()
					.WithColumn("State").AsInt32().NotNullable()
					.WithColumn("Attempts").AsInt32().NotNullable()
					.WithColumn("NextAttemptOn").AsDateTime2().NotNullable()
					.WithColumn("ClaimToken").AsString(64).Nullable()
					.WithColumn("LeaseExpiresOn").AsDateTime2().Nullable()
					.WithColumn("LastError").AsString(1000).Nullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CompletedOn").AsDateTime2().Nullable();
			}

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CallBroadcastOutbox_Due' AND object_id = OBJECT_ID('CallBroadcastOutbox')) " +
						"CREATE INDEX [IX_CallBroadcastOutbox_Due] ON [CallBroadcastOutbox] ([State], [NextAttemptOn]) WHERE [State] < 2;");

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CallBroadcastOutbox_Call' AND object_id = OBJECT_ID('CallBroadcastOutbox')) " +
						"CREATE INDEX [IX_CallBroadcastOutbox_Call] ON [CallBroadcastOutbox] ([CallId], [State]);");

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CallBroadcastOutbox_Department' AND object_id = OBJECT_ID('CallBroadcastOutbox')) " +
						"CREATE INDEX [IX_CallBroadcastOutbox_Department] ON [CallBroadcastOutbox] ([DepartmentId]);");

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CallBroadcastOutbox_Completed' AND object_id = OBJECT_ID('CallBroadcastOutbox')) " +
						"CREATE INDEX [IX_CallBroadcastOutbox_Completed] ON [CallBroadcastOutbox] ([CompletedOn]) WHERE [CompletedOn] IS NOT NULL;");
		}

		public override void Down()
		{
			if (Schema.Table("CallBroadcastOutbox").Exists())
				Delete.Table("CallBroadcastOutbox");
		}
	}
}
