using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// RMS Advanced Data Protection integration (RMS plan section 5.9, ADP catalog v10): protection markers on
	/// every RMS table that carries a cataloged column, companion envelopes for the numeric coordinates, and the
	/// NERIS profile's protected-egress acknowledgement. Every column is guarded so a table that already shipped
	/// its markers inert (RMS-1/2/3) is left alone.
	/// </summary>
	[Migration(176)]
	public class M0176_RmsProtectedDataCatalogV10Pg : Migration
	{
		private static readonly string[] MarkerTables =
		{
			"rmsoperationalrecorddetails", "rmsnarratives", "rmslocations", "rmssourcefacts", "rmscasualtyrescues", "rmsexposures",
			"rmsincidentmodules", "rmsincidentproperties", "rmsincidentvehicles", "rmsincidentresources", "rmsrevisions",
			"rmssubmissions", "rmssignatures", "rmsevidenceartifacts", "rmsdisclosurerequests", "rmsdisclosureproductions",
			"rmsrecordlegalholds", "rmsrecordattachments"
		};

		public override void Up()
		{
			foreach (var table in MarkerTables)
			{
				if (!Schema.Table(table).Exists())
					continue;
				if (!Schema.Table(table).Column("isprotected").Exists())
					Alter.Table(table).AddColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false);
				if (!Schema.Table(table).Column("protectedcatalogversion").Exists())
					Alter.Table(table).AddColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0);
			}

			foreach (var table in new[] { "rmslocations", "rmsexposures" })
			{
				if (!Schema.Table(table).Column("protectedlatitudeenvelope").Exists())
					Alter.Table(table).AddColumn("protectedlatitudeenvelope").AsCustom("text").Nullable();
				if (!Schema.Table(table).Column("protectedlongitudeenvelope").Exists())
					Alter.Table(table).AddColumn("protectedlongitudeenvelope").AsCustom("text").Nullable();
			}

			if (!Schema.Table("rmsnerisprofiles").Column("allowprotectedcontentegress").Exists())
				Alter.Table("rmsnerisprofiles").AddColumn("allowprotectedcontentegress").AsBoolean().NotNullable().WithDefaultValue(false);
			if (!Schema.Table("rmsnerisprofiles").Column("protectedegressacknowledgedon").Exists())
				Alter.Table("rmsnerisprofiles").AddColumn("protectedegressacknowledgedon").AsDateTime2().Nullable();
			if (!Schema.Table("rmsnerisprofiles").Column("protectedegressacknowledgedbyuserid").Exists())
				Alter.Table("rmsnerisprofiles").AddColumn("protectedegressacknowledgedbyuserid").AsString(128).Nullable();
		}

		public override void Down()
		{
			foreach (var table in new[] { "rmslocations", "rmsexposures" })
			{
				if (Schema.Table(table).Column("protectedlatitudeenvelope").Exists())
					Delete.Column("protectedlatitudeenvelope").FromTable(table);
				if (Schema.Table(table).Column("protectedlongitudeenvelope").Exists())
					Delete.Column("protectedlongitudeenvelope").FromTable(table);
			}
			foreach (var column in new[] { "allowprotectedcontentegress", "protectedegressacknowledgedon", "protectedegressacknowledgedbyuserid" })
				if (Schema.Table("rmsnerisprofiles").Column(column).Exists())
					Delete.Column(column).FromTable("rmsnerisprofiles");
		}
	}
}
