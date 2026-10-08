using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// System operation requests (see the SQL Server M0267): BackOffice -> System Operations queues work for worker 76,
	/// which claims Pending rows with a conditional update and records the outcome. No foreign key to departments.
	/// </summary>
	[Migration(267)]
	public class M0267_AddSystemOperationRequestsPg : Migration
	{
		private const string Table = "systemoperationrequests";

		public override void Up()
		{
			if (!Schema.Table(Table).Exists())
			{
				Create.Table(Table)
					.WithColumn("systemoperationrequestid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("operationtype").AsInt32().NotNullable()
					.WithColumn("targetdepartmentid").AsInt32().Nullable()
					.WithColumn("status").AsInt32().NotNullable()
					.WithColumn("source").AsInt32().NotNullable()
					.WithColumn("requestedby").AsCustom("citext").NotNullable()
					.WithColumn("reason").AsCustom("citext").Nullable()
					.WithColumn("requestedon").AsDateTime2().NotNullable()
					.WithColumn("startedon").AsDateTime2().Nullable()
					.WithColumn("heartbeaton").AsDateTime2().Nullable()
					.WithColumn("completedon").AsDateTime2().Nullable()
					.WithColumn("workername").AsCustom("citext").Nullable()
					.WithColumn("progress").AsCustom("citext").Nullable()
					.WithColumn("result").AsCustom("citext").Nullable()
					.WithColumn("cancelledby").AsCustom("citext").Nullable();
			}

			if (!Schema.Table(Table).Index("ix_systemoperationrequests_status_requestedon").Exists())
			{
				Create.Index("ix_systemoperationrequests_status_requestedon")
					.OnTable(Table)
					.OnColumn("status").Ascending()
					.OnColumn("requestedon").Ascending();
			}

			if (!Schema.Table(Table).Index("ix_systemoperationrequests_requestedon").Exists())
			{
				Create.Index("ix_systemoperationrequests_requestedon")
					.OnTable(Table)
					.OnColumn("requestedon").Descending();
			}
		}

		public override void Down()
		{
			if (Schema.Table(Table).Exists())
				Delete.Table(Table);
		}
	}
}
