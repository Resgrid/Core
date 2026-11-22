using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Dispatcher desk (dispatchers-on-duty-plan.md): shift dispatch settings and the dispatch roster beside the shift's own
	/// tables, scheduled and taken dispatcher breaks, the dispatcher's desk session in the Dispatch app, and the queue of work
	/// routed to dispatchers with its history and conversation messages. No foreign keys (every table is department-keyed and
	/// purged by UnlinkedDepartmentCleanup; shift rows are removed by the shift delete). Existence-guarded for safe retry.
	/// </summary>
	[Migration(324)]
	public class M0324_AddDispatcherDesk : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("ShiftDispatchSettings").Exists())
			{
				Create.Table("ShiftDispatchSettings")
					.WithColumn("ShiftDispatchSettingId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ShiftId").AsInt32().NotNullable()
					.WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IsDispatchOnly").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RequiredDispatchers").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("MinimumOnDesk").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AllowSelfScheduledBreaks").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("BreakPlanJson").AsString(int.MaxValue).Nullable()
					.WithColumn("UpdatedByUserId").AsString(128).Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().NotNullable();
			}

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ShiftDispatchSettings_Shift' AND object_id = OBJECT_ID('ShiftDispatchSettings')) " +
						"CREATE UNIQUE INDEX [UX_ShiftDispatchSettings_Shift] ON [ShiftDispatchSettings] ([ShiftId]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ShiftDispatchSettings_Department' AND object_id = OBJECT_ID('ShiftDispatchSettings')) " +
						"CREATE INDEX [IX_ShiftDispatchSettings_Department] ON [ShiftDispatchSettings] ([DepartmentId]);");

			if (!Schema.Table("ShiftDispatchers").Exists())
			{
				Create.Table("ShiftDispatchers")
					.WithColumn("ShiftDispatcherId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ShiftId").AsInt32().NotNullable()
					.WithColumn("ShiftDayId").AsInt32().Nullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("Source").AsInt32().NotNullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsSupervisor").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedByUserId").AsString(128).Nullable()
					.WithColumn("AddedOn").AsDateTime2().NotNullable()
					.WithColumn("ReviewedByUserId").AsString(128).Nullable()
					.WithColumn("ReviewedOn").AsDateTime2().Nullable()
					.WithColumn("Note").AsString(500).Nullable();
			}

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ShiftDispatchers_Shift' AND object_id = OBJECT_ID('ShiftDispatchers')) " +
						"CREATE INDEX [IX_ShiftDispatchers_Shift] ON [ShiftDispatchers] ([ShiftId], [ShiftDayId]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ShiftDispatchers_Department_User' AND object_id = OBJECT_ID('ShiftDispatchers')) " +
						"CREATE INDEX [IX_ShiftDispatchers_Department_User] ON [ShiftDispatchers] ([DepartmentId], [UserId]);");

			if (!Schema.Table("DispatcherBreaks").Exists())
			{
				Create.Table("DispatcherBreaks")
					.WithColumn("DispatcherBreakId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("ShiftId").AsInt32().Nullable()
					.WithColumn("ShiftDayId").AsInt32().Nullable()
					.WithColumn("BreakType").AsInt32().NotNullable()
					.WithColumn("State").AsInt32().NotNullable()
					.WithColumn("ScheduledStartOn").AsDateTime2().Nullable()
					.WithColumn("DurationMinutes").AsInt32().NotNullable()
					.WithColumn("StartedOn").AsDateTime2().Nullable()
					.WithColumn("ExpectedReturnOn").AsDateTime2().Nullable()
					.WithColumn("EndedOn").AsDateTime2().Nullable()
					.WithColumn("EndReason").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("EndedByUserId").AsString(128).Nullable()
					.WithColumn("Note").AsString(500).Nullable()
					.WithColumn("ReminderSentOn").AsDateTime2().Nullable()
					.WithColumn("DueAlertSentOn").AsDateTime2().Nullable()
					.WithColumn("ReturnAlertSentOn").AsDateTime2().Nullable()
					.WithColumn("OverdueAlertSentOn").AsDateTime2().Nullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("UpdatedByUserId").AsString(128).Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().Nullable();
			}

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DispatcherBreaks_Department_Start' AND object_id = OBJECT_ID('DispatcherBreaks')) " +
						"CREATE INDEX [IX_DispatcherBreaks_Department_Start] ON [DispatcherBreaks] ([DepartmentId], [ScheduledStartOn]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DispatcherBreaks_Department_User' AND object_id = OBJECT_ID('DispatcherBreaks')) " +
						"CREATE INDEX [IX_DispatcherBreaks_Department_User] ON [DispatcherBreaks] ([DepartmentId], [UserId], [State]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DispatcherBreaks_ShiftDay' AND object_id = OBJECT_ID('DispatcherBreaks')) " +
						"CREATE INDEX [IX_DispatcherBreaks_ShiftDay] ON [DispatcherBreaks] ([ShiftDayId]) WHERE [ShiftDayId] IS NOT NULL;");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DispatcherBreaks_Open' AND object_id = OBJECT_ID('DispatcherBreaks')) " +
						"CREATE INDEX [IX_DispatcherBreaks_Open] ON [DispatcherBreaks] ([State], [ScheduledStartOn]) WHERE [State] < 2;");

			if (!Schema.Table("DispatcherPresences").Exists())
			{
				Create.Table("DispatcherPresences")
					.WithColumn("DispatcherPresenceId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("StateChangedOn").AsDateTime2().NotNullable()
					.WithColumn("SignedInOn").AsDateTime2().Nullable()
					.WithColumn("LastSeenOn").AsDateTime2().Nullable()
					.WithColumn("DutyMode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("DutyShiftId").AsInt32().Nullable()
					.WithColumn("DutyShiftDayId").AsInt32().Nullable()
					.WithColumn("DutyEndsOn").AsDateTime2().Nullable()
					.WithColumn("ActiveBreakId").AsString(36).Nullable()
					.WithColumn("CallbackNumber").AsString(32).Nullable()
					.WithColumn("DeviceKind").AsString(16).Nullable()
					.WithColumn("LastOfferedOn").AsDateTime2().Nullable()
					.WithColumn("MissedOffers").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AwayReason").AsString(250).Nullable()
					.WithColumn("Capacity").AsInt32().Nullable();
			}

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DispatcherPresences_Department_User' AND object_id = OBJECT_ID('DispatcherPresences')) " +
						"CREATE UNIQUE INDEX [UX_DispatcherPresences_Department_User] ON [DispatcherPresences] ([DepartmentId], [UserId]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DispatcherPresences_SignedIn' AND object_id = OBJECT_ID('DispatcherPresences')) " +
						"CREATE INDEX [IX_DispatcherPresences_SignedIn] ON [DispatcherPresences] ([State]) WHERE [State] > 0;");

			if (!Schema.Table("DispatcherQueueItems").Exists())
			{
				Create.Table("DispatcherQueueItems")
					.WithColumn("DispatcherQueueItemId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable()
					.WithColumn("State").AsInt32().NotNullable()
					.WithColumn("Outcome").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SourceAddress").AsString(256).Nullable()
					.WithColumn("SourceName").AsString(200).Nullable()
					.WithColumn("TargetAddress").AsString(256).Nullable()
					.WithColumn("Subject").AsString(250).Nullable()
					.WithColumn("Preview").AsString(500).Nullable()
					.WithColumn("ExternalReference").AsString(128).Nullable()
					.WithColumn("ConversationKey").AsString(300).Nullable()
					.WithColumn("AssignedUserId").AsString(128).Nullable()
					.WithColumn("OfferedOn").AsDateTime2().Nullable()
					.WithColumn("OfferExpiresOn").AsDateTime2().Nullable()
					.WithColumn("OfferAttempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("DeclinedUserIds").AsString(int.MaxValue).Nullable()
					.WithColumn("AcceptedOn").AsDateTime2().Nullable()
					.WithColumn("CompletedOn").AsDateTime2().Nullable()
					.WithColumn("CompletedByUserId").AsString(128).Nullable()
					.WithColumn("DueOn").AsDateTime2().Nullable()
					.WithColumn("Version").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("LastActivityOn").AsDateTime2().NotNullable()
					.WithColumn("OverflowedOn").AsDateTime2().Nullable()
					.WithColumn("OverflowNote").AsString(250).Nullable()
					.WithColumn("EscalatedOn").AsDateTime2().Nullable()
					.WithColumn("Note").AsString(1000).Nullable();
			}

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DispatcherQueueItems_Open' AND object_id = OBJECT_ID('DispatcherQueueItems')) " +
						"CREATE INDEX [IX_DispatcherQueueItems_Open] ON [DispatcherQueueItems] ([DepartmentId], [State]) WHERE [State] < 3;");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DispatcherQueueItems_Offers' AND object_id = OBJECT_ID('DispatcherQueueItems')) " +
						"CREATE INDEX [IX_DispatcherQueueItems_Offers] ON [DispatcherQueueItems] ([State], [OfferExpiresOn]) WHERE [State] < 3;");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DispatcherQueueItems_Call' AND object_id = OBJECT_ID('DispatcherQueueItems')) " +
						"CREATE INDEX [IX_DispatcherQueueItems_Call] ON [DispatcherQueueItems] ([CallId]) WHERE [CallId] IS NOT NULL;");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DispatcherQueueItems_Conversation' AND object_id = OBJECT_ID('DispatcherQueueItems')) " +
						"CREATE INDEX [IX_DispatcherQueueItems_Conversation] ON [DispatcherQueueItems] ([ConversationKey], [LastActivityOn]) WHERE [ConversationKey] IS NOT NULL;");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DispatcherQueueItems_External' AND object_id = OBJECT_ID('DispatcherQueueItems')) " +
						"CREATE INDEX [IX_DispatcherQueueItems_External] ON [DispatcherQueueItems] ([ExternalReference]) WHERE [ExternalReference] IS NOT NULL;");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DispatcherQueueItems_Department_Created' AND object_id = OBJECT_ID('DispatcherQueueItems')) " +
						"CREATE INDEX [IX_DispatcherQueueItems_Department_Created] ON [DispatcherQueueItems] ([DepartmentId], [CreatedOn]);");

			if (!Schema.Table("DispatcherQueueEvents").Exists())
			{
				Create.Table("DispatcherQueueEvents")
					.WithColumn("DispatcherQueueEventId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DispatcherQueueItemId").AsString(36).NotNullable()
					.WithColumn("EventType").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).Nullable()
					.WithColumn("OccurredOn").AsDateTime2().NotNullable()
					.WithColumn("Detail").AsString(500).Nullable();
			}

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DispatcherQueueEvents_Item' AND object_id = OBJECT_ID('DispatcherQueueEvents')) " +
						"CREATE INDEX [IX_DispatcherQueueEvents_Item] ON [DispatcherQueueEvents] ([DispatcherQueueItemId], [OccurredOn]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DispatcherQueueEvents_Department' AND object_id = OBJECT_ID('DispatcherQueueEvents')) " +
						"CREATE INDEX [IX_DispatcherQueueEvents_Department] ON [DispatcherQueueEvents] ([DepartmentId], [OccurredOn]);");

			if (!Schema.Table("DispatcherConversationMessages").Exists())
			{
				Create.Table("DispatcherConversationMessages")
					.WithColumn("DispatcherConversationMessageId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DispatcherQueueItemId").AsString(36).NotNullable()
					.WithColumn("Direction").AsInt32().NotNullable()
					.WithColumn("FromAddress").AsString(256).Nullable()
					.WithColumn("ToAddress").AsString(256).Nullable()
					.WithColumn("Body").AsString(int.MaxValue).Nullable()
					.WithColumn("UserId").AsString(128).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ProviderMessageId").AsString(128).Nullable()
					.WithColumn("DeliveryState").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Error").AsString(500).Nullable();
			}

			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DispatcherConversationMessages_Item' AND object_id = OBJECT_ID('DispatcherConversationMessages')) " +
						"CREATE INDEX [IX_DispatcherConversationMessages_Item] ON [DispatcherConversationMessages] ([DispatcherQueueItemId], [CreatedOn]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DispatcherConversationMessages_Provider' AND object_id = OBJECT_ID('DispatcherConversationMessages')) " +
						"CREATE INDEX [IX_DispatcherConversationMessages_Provider] ON [DispatcherConversationMessages] ([ProviderMessageId]) WHERE [ProviderMessageId] IS NOT NULL;");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DispatcherConversationMessages_Department' AND object_id = OBJECT_ID('DispatcherConversationMessages')) " +
						"CREATE INDEX [IX_DispatcherConversationMessages_Department] ON [DispatcherConversationMessages] ([DepartmentId]);");
		}

		public override void Down()
		{
			foreach (var table in new[] { "DispatcherConversationMessages", "DispatcherQueueEvents", "DispatcherQueueItems", "DispatcherPresences",
				"DispatcherBreaks", "ShiftDispatchers", "ShiftDispatchSettings" })
			{
				if (Schema.Table(table).Exists())
					Delete.Table(table);
			}
		}
	}
}
