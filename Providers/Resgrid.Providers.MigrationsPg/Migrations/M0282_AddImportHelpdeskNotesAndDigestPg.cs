using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Department Import support (see the SQL Server M0280): importmessages helpdesk mirroring state, importworkspaces staff
	/// value-view digest marker, and the importevents (action, createdon) index.
	/// </summary>
	[Migration(282)]
	public class M0282_AddImportHelpdeskNotesAndDigestPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("importmessages").Column("helpdeskstate").Exists())
				Alter.Table("importmessages")
					.AddColumn("helpdeskstate").AsInt32().NotNullable().WithDefaultValue(0)
					.AddColumn("helpdeskattempts").AsInt32().NotNullable().WithDefaultValue(0)
					.AddColumn("helpdesknextattempton").AsDateTime2().Nullable();

			if (!Schema.Table("importmessages").Index("ix_importmessages_helpdeskstate_helpdesknextattempton").Exists())
				Create.Index("ix_importmessages_helpdeskstate_helpdesknextattempton").OnTable("importmessages")
					.OnColumn("helpdeskstate").Ascending()
					.OnColumn("helpdesknextattempton").Ascending();

			if (!Schema.Table("importworkspaces").Column("staffviewdigestsenton").Exists())
				Alter.Table("importworkspaces").AddColumn("staffviewdigestsenton").AsDateTime2().Nullable();

			if (!Schema.Table("importevents").Index("ix_importevents_action_createdon").Exists())
				Create.Index("ix_importevents_action_createdon").OnTable("importevents")
					.OnColumn("action").Ascending()
					.OnColumn("createdon").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("importevents").Index("ix_importevents_action_createdon").Exists())
				Delete.Index("ix_importevents_action_createdon").OnTable("importevents");
			if (Schema.Table("importworkspaces").Column("staffviewdigestsenton").Exists())
				Delete.Column("staffviewdigestsenton").FromTable("importworkspaces");
			if (Schema.Table("importmessages").Index("ix_importmessages_helpdeskstate_helpdesknextattempton").Exists())
				Delete.Index("ix_importmessages_helpdeskstate_helpdesknextattempton").OnTable("importmessages");
			foreach (var column in new[] { "helpdesknextattempton", "helpdeskattempts", "helpdeskstate" })
				if (Schema.Table("importmessages").Column(column).Exists())
					Delete.Column(column).FromTable("importmessages");
		}
	}
}
