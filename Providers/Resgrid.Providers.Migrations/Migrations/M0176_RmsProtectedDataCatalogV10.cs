using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// RMS Advanced Data Protection integration (RMS plan section 5.9, ADP catalog v10): protection markers on
	/// every RMS table that carries a cataloged column, companion envelopes for the numeric coordinates, and the
	/// NERIS profile's protected-egress acknowledgement. Every column is guarded so a table that already shipped
	/// its markers inert (RMS-1/2/3) is left alone.
	/// </summary>
	[Migration(176)]
	public class M0176_RmsProtectedDataCatalogV10 : Migration
	{
		private static readonly string[] MarkerTables =
		{
			"RmsOperationalRecordDetails", "RmsNarratives", "RmsLocations", "RmsSourceFacts", "RmsCasualtyRescues", "RmsExposures",
			"RmsIncidentModules", "RmsIncidentProperties", "RmsIncidentVehicles", "RmsIncidentResources", "RmsRevisions",
			"RmsSubmissions", "RmsSignatures", "RmsEvidenceArtifacts", "RmsDisclosureRequests", "RmsDisclosureProductions",
			"RmsRecordLegalHolds", "RmsRecordAttachments"
		};

		public override void Up()
		{
			foreach (var table in MarkerTables)
			{
				if (!Schema.Table(table).Exists())
					continue;
				if (!Schema.Table(table).Column("IsProtected").Exists())
					Alter.Table(table).AddColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false);
				if (!Schema.Table(table).Column("ProtectedCatalogVersion").Exists())
					Alter.Table(table).AddColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0);
			}

			foreach (var table in new[] { "RmsLocations", "RmsExposures" })
			{
				if (!Schema.Table(table).Column("ProtectedLatitudeEnvelope").Exists())
					Alter.Table(table).AddColumn("ProtectedLatitudeEnvelope").AsString(int.MaxValue).Nullable();
				if (!Schema.Table(table).Column("ProtectedLongitudeEnvelope").Exists())
					Alter.Table(table).AddColumn("ProtectedLongitudeEnvelope").AsString(int.MaxValue).Nullable();
			}

			if (!Schema.Table("RmsNerisProfiles").Column("AllowProtectedContentEgress").Exists())
				Alter.Table("RmsNerisProfiles").AddColumn("AllowProtectedContentEgress").AsBoolean().NotNullable().WithDefaultValue(false);
			if (!Schema.Table("RmsNerisProfiles").Column("ProtectedEgressAcknowledgedOn").Exists())
				Alter.Table("RmsNerisProfiles").AddColumn("ProtectedEgressAcknowledgedOn").AsDateTime2().Nullable();
			if (!Schema.Table("RmsNerisProfiles").Column("ProtectedEgressAcknowledgedByUserId").Exists())
				Alter.Table("RmsNerisProfiles").AddColumn("ProtectedEgressAcknowledgedByUserId").AsString(128).Nullable();
		}

		public override void Down()
		{
			foreach (var table in new[] { "RmsLocations", "RmsExposures" })
			{
				if (Schema.Table(table).Column("ProtectedLatitudeEnvelope").Exists())
					Delete.Column("ProtectedLatitudeEnvelope").FromTable(table);
				if (Schema.Table(table).Column("ProtectedLongitudeEnvelope").Exists())
					Delete.Column("ProtectedLongitudeEnvelope").FromTable(table);
			}
			foreach (var column in new[] { "AllowProtectedContentEgress", "ProtectedEgressAcknowledgedOn", "ProtectedEgressAcknowledgedByUserId" })
				if (Schema.Table("RmsNerisProfiles").Column(column).Exists())
					Delete.Column(column).FromTable("RmsNerisProfiles");
			// Marker columns that predate this migration stay; the ones it added are harmless to keep.
		}
	}
}
