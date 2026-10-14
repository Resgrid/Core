using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Inbound email receipts (see the SQL Server M0271): one row per delivery key (a Postmark MessageID, or a department-scoped v4
	/// Idempotency-Key from the SMTP relay), unique, so a redelivered email never creates a second call. The unique index is also
	/// the ON CONFLICT arbiter of the claim insert. No foreign key to departments.
	/// </summary>
	[Migration(271)]
	public class M0271_AddInboundEmailReceiptsPg : Migration
	{
		private const string Table = "inboundemailreceipts";

		public override void Up()
		{
			if (!Schema.Table(Table).Exists())
			{
				Create.Table(Table)
					.WithColumn("inboundemailreceiptid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("providermessageid").AsCustom("citext").NotNullable()
					.WithColumn("kind").AsInt32().NotNullable()
					.WithColumn("state").AsInt32().NotNullable()
					.WithColumn("entityid").AsInt32().Nullable()
					.WithColumn("pendingsteps").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("deliveries").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("receivedon").AsDateTime2().NotNullable()
					.WithColumn("claimedon").AsDateTime2().Nullable()
					.WithColumn("savedon").AsDateTime2().Nullable()
					.WithColumn("completedon").AsDateTime2().Nullable();
			}

			if (!Schema.Table(Table).Index("ux_inboundemailreceipts_providermessageid").Exists())
			{
				Create.Index("ux_inboundemailreceipts_providermessageid")
					.OnTable(Table)
					.OnColumn("providermessageid").Ascending()
					.WithOptions().Unique();
			}

			if (!Schema.Table(Table).Index("ix_inboundemailreceipts_receivedon").Exists())
			{
				Create.Index("ix_inboundemailreceipts_receivedon")
					.OnTable(Table)
					.OnColumn("receivedon").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table(Table).Exists())
				Delete.Table(Table);
		}
	}
}
