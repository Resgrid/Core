using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(236)]
	public class M0236_AddAdpAudit : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("AdpAuditEvents").Exists())
				Create.Table("AdpAuditEvents")
					.WithColumn("EventId").AsString(32).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Sequence").AsInt64().NotNullable()
					.WithColumn("Layer").AsString(32).NotNullable()
					.WithColumn("Operation").AsString(64).NotNullable()
					.WithColumn("Outcome").AsString(64).NotNullable()
					.WithColumn("ActorId").AsString(128).Nullable()
					.WithColumn("CorrelationId").AsString(128).Nullable()
					.WithColumn("ResourceId").AsString(128).Nullable()
					.WithColumn("PolicyEpoch").AsInt64().NotNullable()
					.WithColumn("OccurredUtc").AsDateTime2().NotNullable()
					.WithColumn("PreviousHash").AsString(64).NotNullable()
					.WithColumn("Hash").AsString(64).NotNullable();
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_AdpAudit_Sequence' AND object_id=OBJECT_ID('AdpAuditEvents')) CREATE UNIQUE INDEX UX_AdpAudit_Sequence ON AdpAuditEvents(DepartmentId, Sequence);");
			if (!Schema.Table("AdpAccessStates").Exists())
				Create.Table("AdpAccessStates")
					.WithColumn("StateId").AsString(200).NotNullable().PrimaryKey()
					.WithColumn("Json").AsString(int.MaxValue).NotNullable()
					.WithColumn("Version").AsInt64().NotNullable();
		}

		// Audit evidence must survive application rollback.
		public override void Down() { }
	}
}
