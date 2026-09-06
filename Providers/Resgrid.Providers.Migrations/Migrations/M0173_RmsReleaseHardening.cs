using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>RMS release hardening: immutable create identity and recoverable destination exchanges.</summary>
	[Migration(173)]
	public class M0173_RmsReleaseHardening : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsCommandReceipts").Exists())
				Create.Table("RmsCommandReceipts")
					.WithColumn("DepartmentId").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("KeyHash").AsString(64).NotNullable().PrimaryKey()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RequestChecksum").AsString(64).NotNullable()
					.WithColumn("ReservationId").AsString(36).NotNullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CompletedOn").AsDateTime2().Nullable();
			foreach (var table in new[] { "RmsOperationalRecords", "RmsIncidentReports" })
				if (!Schema.Table(table).Column("SearchErasedOn").Exists())
					Alter.Table(table).AddColumn("SearchErasedOn").AsDateTime2().Nullable();
			if (!Schema.Table("RmsEvidenceArtifacts").Column("CaptureRequestChecksum").Exists())
				Alter.Table("RmsEvidenceArtifacts").AddColumn("CaptureRequestChecksum").AsString(80).Nullable();
			if (!Schema.Table("UdfDefinitions").Column("RecordDefinitionKey").Exists())
				Alter.Table("UdfDefinitions").AddColumn("RecordDefinitionKey").AsString(200).Nullable();
			if (!Schema.Table("UdfDefinitions").Column("RecordDefinitionVersion").Exists())
				Alter.Table("UdfDefinitions").AddColumn("RecordDefinitionVersion").AsInt32().Nullable();
			if (!Schema.Table("UdfFields").Column("RmsClassification").Exists())
				Alter.Table("UdfFields").AddColumn("RmsClassification").AsInt32().Nullable();
			if (!Schema.Table("RmsOperationalRecords").Column("UdfDefinitionId").Exists())
				Alter.Table("RmsOperationalRecords").AddColumn("UdfDefinitionId").AsString(128).Nullable();
			if (!Schema.Table("RmsIncidentReports").Column("UdfDefinitionId").Exists())
				Alter.Table("RmsIncidentReports").AddColumn("UdfDefinitionId").AsString(128).Nullable();
			if (!Schema.Table("RmsRecordLegalHoldMembers").Exists())
			{
				Create.Table("RmsRecordLegalHoldMembers").WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("HoldId").AsString(36).NotNullable().PrimaryKey().WithColumn("RecordId").AsString(36).NotNullable().PrimaryKey().WithColumn("MatchedOn").AsDateTime2().NotNullable();
				Create.Index("IX_RmsLegalHoldMembers_Record").OnTable("RmsRecordLegalHoldMembers").OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending();
			}
			if (!Schema.Table("RmsDisclosureProductions").Column("DeliveryMethod").Exists())
				Alter.Table("RmsDisclosureProductions").AddColumn("DeliveryMethod").AsString(200).Nullable();
			if (!Schema.Table("RmsDisclosureProductions").Column("DeliveryReference").Exists())
				Alter.Table("RmsDisclosureProductions").AddColumn("DeliveryReference").AsString(1000).Nullable();
			if (!Schema.Table("RmsRecordAttachments").Column("Classification").Exists())
				Alter.Table("RmsRecordAttachments").AddColumn("Classification").AsInt32().Nullable();
			if (!Schema.Table("RmsOperationalRecords").Column("OriginalRequestChecksum").Exists())
				Alter.Table("RmsOperationalRecords").AddColumn("OriginalRequestChecksum").AsString(80).Nullable();
			if (!Schema.Table("RmsSubmissions").Column("DestinationIdentity").Exists())
				Alter.Table("RmsSubmissions").AddColumn("DestinationIdentity").AsString(int.MaxValue).Nullable();
			if (!Schema.Table("RmsSubmissions").Column("RequiresReconciliation").Exists())
				Alter.Table("RmsSubmissions").AddColumn("RequiresReconciliation").AsBoolean().NotNullable().WithDefaultValue(false);
			if (!Schema.Table("RmsSubmissions").Column("CreatePendingReceipt").Exists())
			{
				Alter.Table("RmsSubmissions").AddColumn("CreatePendingReceipt").AsBoolean().NotNullable().WithDefaultValue(false);
				// Pre-journal uncertain creates must not silently become new POSTs after upgrading.
				Execute.Sql("UPDATE RmsSubmissions SET CreatePendingReceipt = 1, RequiresReconciliation = 1 WHERE SentOn IS NOT NULL AND ExternalId IS NULL AND (ResponseStatusCode IS NULL OR ResponseStatusCode >= 500 OR ResponseStatusCode BETWEEN 200 AND 299)");
				Execute.Sql("UPDATE RmsSubmissions SET RequiresReconciliation = 1 WHERE DestinationIdentity IS NULL AND ExternalId IS NOT NULL");
			}
			if (!Schema.Table("RmsSubmissionExchanges").Exists())
			{
				Create.Table("RmsSubmissionExchanges")
					.WithColumn("RmsSubmissionExchangeId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("SubmissionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RevisionId").AsString(36).NotNullable()
					.WithColumn("ExchangeId").AsString(36).NotNullable()
					.WithColumn("Stage").AsString(20).NotNullable()
					.WithColumn("Operation").AsString(20).NotNullable()
					.WithColumn("DestinationIdentity").AsString(int.MaxValue).NotNullable()
					.WithColumn("PayloadChecksum").AsString(80).NotNullable()
					.WithColumn("OutcomeJson").AsString(int.MaxValue).Nullable()
					.WithColumn("OutcomeChecksum").AsString(80).Nullable()
					.WithColumn("AttemptNumber").AsInt32().NotNullable()
					.WithColumn("OccurredOn").AsDateTime2().NotNullable();
				Create.Index("UX_RmsSubmissionExchanges_Stage").OnTable("RmsSubmissionExchanges")
					.OnColumn("DepartmentId").Ascending().OnColumn("SubmissionId").Ascending().OnColumn("ExchangeId").Ascending().OnColumn("Stage").Ascending().WithOptions().Unique();
				Create.Index("IX_RmsSubmissionExchanges_Record").OnTable("RmsSubmissionExchanges")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending().OnColumn("OccurredOn").Ascending();
			}
		}

		public override void Down() => throw new System.NotSupportedException("Delivery receipts must be retained. Roll back the application without deleting RMS hardening data.");
	}
}
