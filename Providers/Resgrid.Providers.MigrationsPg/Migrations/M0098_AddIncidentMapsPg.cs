using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Named tactical maps per incident (own framing, description, optional expiry, full audit), markup
	/// linkage (incidentmapannotations.incidentmapid), and lane → map attachment
	/// (commandstructurenodes.linkedmapid).
	/// </summary>
	[Migration(98)]
	public class M0098_AddIncidentMapsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("incidentmaps").Exists())
			{
				Create.Table("incidentmaps")
					.WithColumn("incidentmapid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("incidentcommandid").AsCustom("citext").NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("callid").AsInt32().NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("description").AsCustom("citext").Nullable()
					.WithColumn("centerlatitude").AsCustom("citext").Nullable()
					.WithColumn("centerlongitude").AsCustom("citext").Nullable()
					.WithColumn("zoomlevel").AsCustom("citext").Nullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("updatedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("updatedon").AsDateTime2().Nullable()
					.WithColumn("deletedon").AsDateTime2().Nullable()
					.WithColumn("modifiedon").AsDateTime2().Nullable();

				Create.Index("ix_incidentmaps_department_call")
					.OnTable("incidentmaps")
					.OnColumn("departmentid").Ascending()
					.OnColumn("callid").Ascending();
			}

			if (Schema.Table("incidentmapannotations").Exists() && !Schema.Table("incidentmapannotations").Column("incidentmapid").Exists())
				Alter.Table("incidentmapannotations").AddColumn("incidentmapid").AsCustom("citext").Nullable();

			if (Schema.Table("commandstructurenodes").Exists() && !Schema.Table("commandstructurenodes").Column("linkedmapid").Exists())
				Alter.Table("commandstructurenodes").AddColumn("linkedmapid").AsCustom("citext").Nullable();
		}

		public override void Down()
		{
			if (Schema.Table("commandstructurenodes").Exists() && Schema.Table("commandstructurenodes").Column("linkedmapid").Exists())
				Delete.Column("linkedmapid").FromTable("commandstructurenodes");

			if (Schema.Table("incidentmapannotations").Exists() && Schema.Table("incidentmapannotations").Column("incidentmapid").Exists())
				Delete.Column("incidentmapid").FromTable("incidentmapannotations");

			if (Schema.Table("incidentmaps").Exists())
				Delete.Table("incidentmaps");
		}
	}
}
