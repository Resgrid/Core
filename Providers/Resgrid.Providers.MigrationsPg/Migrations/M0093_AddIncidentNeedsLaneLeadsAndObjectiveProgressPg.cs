using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Command-level incident needs (tracked to fulfillment), objective progress/priority/description,
	/// lane links to objectives/needs, optional primary/secondary lane leads (Resgrid user or external
	/// contact), and command-level estimated end / important information.
	/// </summary>
	[Migration(93)]
	public class M0093_AddIncidentNeedsLaneLeadsAndObjectiveProgressPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("incidentcommands").Exists())
			{
				if (!Schema.Table("incidentcommands").Column("estimatedendon").Exists())
					Alter.Table("incidentcommands").AddColumn("estimatedendon").AsDateTime2().Nullable();

				if (!Schema.Table("incidentcommands").Column("importantinformation").Exists())
					Alter.Table("incidentcommands").AddColumn("importantinformation").AsCustom("citext").Nullable();
			}

			if (Schema.Table("tacticalobjectives").Exists())
			{
				if (!Schema.Table("tacticalobjectives").Column("description").Exists())
					Alter.Table("tacticalobjectives").AddColumn("description").AsCustom("citext").Nullable();

				if (!Schema.Table("tacticalobjectives").Column("progresspercent").Exists())
					Alter.Table("tacticalobjectives").AddColumn("progresspercent").AsInt32().NotNullable().WithDefaultValue(0);

				if (!Schema.Table("tacticalobjectives").Column("priority").Exists())
					Alter.Table("tacticalobjectives").AddColumn("priority").AsInt32().NotNullable().WithDefaultValue(0);

				if (!Schema.Table("tacticalobjectives").Column("targetcompleteon").Exists())
					Alter.Table("tacticalobjectives").AddColumn("targetcompleteon").AsDateTime2().Nullable();
			}

			if (Schema.Table("commandstructurenodes").Exists())
			{
				foreach (var column in new[]
				{
					"primaryobjectiveid", "secondaryobjectiveid", "linkedneedid",
					"primaryleaduserid", "primaryleadname", "primaryleadphone", "primaryleademail",
					"secondaryleaduserid", "secondaryleadname", "secondaryleadphone", "secondaryleademail"
				})
				{
					if (!Schema.Table("commandstructurenodes").Column(column).Exists())
						Alter.Table("commandstructurenodes").AddColumn(column).AsCustom("citext").Nullable();
				}
			}

			if (!Schema.Table("incidentneeds").Exists())
			{
				Create.Table("incidentneeds")
					.WithColumn("incidentneedid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("incidentcommandid").AsCustom("citext").NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("callid").AsInt32().NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("description").AsCustom("citext").Nullable()
					.WithColumn("category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("quantityrequested").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("quantityfulfilled").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("priority").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("metbyuserid").AsCustom("citext").Nullable()
					.WithColumn("meton").AsDateTime2().Nullable()
					.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("modifiedon").AsDateTime2().Nullable();

				Create.Index("ix_incidentneeds_department_call")
					.OnTable("incidentneeds")
					.OnColumn("departmentid").Ascending()
					.OnColumn("callid").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("incidentneeds").Exists())
				Delete.Table("incidentneeds");

			if (Schema.Table("incidentcommands").Exists())
			{
				foreach (var column in new[] { "estimatedendon", "importantinformation" })
				{
					if (Schema.Table("incidentcommands").Column(column).Exists())
						Delete.Column(column).FromTable("incidentcommands");
				}
			}

			if (Schema.Table("tacticalobjectives").Exists())
			{
				foreach (var column in new[] { "description", "progresspercent", "priority", "targetcompleteon" })
				{
					if (Schema.Table("tacticalobjectives").Column(column).Exists())
						Delete.Column(column).FromTable("tacticalobjectives");
				}
			}

			if (Schema.Table("commandstructurenodes").Exists())
			{
				foreach (var column in new[]
				{
					"primaryobjectiveid", "secondaryobjectiveid", "linkedneedid",
					"primaryleaduserid", "primaryleadname", "primaryleadphone", "primaryleademail",
					"secondaryleaduserid", "secondaryleadname", "secondaryleadphone", "secondaryleademail"
				})
				{
					if (Schema.Table("commandstructurenodes").Column(column).Exists())
						Delete.Column(column).FromTable("commandstructurenodes");
				}
			}
		}
	}
}
