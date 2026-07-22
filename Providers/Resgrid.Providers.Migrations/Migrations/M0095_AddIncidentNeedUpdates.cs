using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Append-only audit trail for incident-need fulfillment changes (partial fills, fill reductions,
	/// closing an unfilled need) with the caller's note, author, and timestamp.
	/// </summary>
	[Migration(95)]
	public class M0095_AddIncidentNeedUpdates : Migration
	{
		public override void Up()
		{
			if (Schema.Table("IncidentNeedUpdates").Exists())
				return;

			Create.Table("IncidentNeedUpdates")
				.WithColumn("IncidentNeedUpdateId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("IncidentNeedId").AsString(128).NotNullable()
				.WithColumn("IncidentCommandId").AsString(128).NotNullable()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("CallId").AsInt32().NotNullable()
				.WithColumn("PreviousStatus").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("NewStatus").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("PreviousQuantityFulfilled").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("NewQuantityFulfilled").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("Note").AsString(2000).Nullable()
				.WithColumn("CreatedByUserId").AsString(450).Nullable()
				.WithColumn("CreatedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

			Create.Index("IX_IncidentNeedUpdates_Need")
				.OnTable("IncidentNeedUpdates")
				.OnColumn("IncidentNeedId").Ascending();

			Create.Index("IX_IncidentNeedUpdates_Department_Call")
				.OnTable("IncidentNeedUpdates")
				.OnColumn("DepartmentId").Ascending()
				.OnColumn("CallId").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("IncidentNeedUpdates").Exists())
				Delete.Table("IncidentNeedUpdates");
		}
	}
}
