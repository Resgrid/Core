using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Inbound email receipts: one row per delivery key (unique): a Postmark MessageID for api/Email/Receive, or a department-scoped
	/// v4 Idempotency-Key for SaveCall / SaveCallFile (the SMTP relay sends the Message-Id). A redelivered email is acknowledged,
	/// held off, or finishes the call or message an earlier attempt saved, instead of creating a second call. Both senders retry
	/// after a failure, and the call row commits before its dispatch rows and broadcast, so each retry after a late failure used to
	/// create another call. A system record: no department column, no foreign key, not part of the department purge; worker
	/// command 13 deletes rows older than a week.
	/// </summary>
	[Migration(271)]
	public class M0271_AddInboundEmailReceipts : Migration
	{
		private const string Table = "InboundEmailReceipts";

		public override void Up()
		{
			if (!Schema.Table(Table).Exists())
			{
				Create.Table(Table)
					.WithColumn("InboundEmailReceiptId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("ProviderMessageId").AsString(128).NotNullable()
					.WithColumn("Kind").AsInt32().NotNullable()
					.WithColumn("State").AsInt32().NotNullable()
					.WithColumn("EntityId").AsInt32().Nullable()
					.WithColumn("PendingSteps").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Deliveries").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("ReceivedOn").AsDateTime2().NotNullable()
					.WithColumn("ClaimedOn").AsDateTime2().Nullable()
					.WithColumn("SavedOn").AsDateTime2().Nullable()
					.WithColumn("CompletedOn").AsDateTime2().Nullable();
			}

			if (!Schema.Table(Table).Index("UX_InboundEmailReceipts_ProviderMessageId").Exists())
			{
				Create.Index("UX_InboundEmailReceipts_ProviderMessageId")
					.OnTable(Table)
					.OnColumn("ProviderMessageId").Ascending()
					.WithOptions().Unique();
			}

			if (!Schema.Table(Table).Index("IX_InboundEmailReceipts_ReceivedOn").Exists())
			{
				Create.Index("IX_InboundEmailReceipts_ReceivedOn")
					.OnTable(Table)
					.OnColumn("ReceivedOn").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table(Table).Exists())
				Delete.Table(Table);
		}
	}
}
