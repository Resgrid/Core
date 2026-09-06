using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>RMS release hardening: immutable create identity and recoverable destination exchanges.</summary>
	[Migration(173)]
	public class M0173_RmsReleaseHardeningPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmscommandreceipts").Exists())
				Create.Table("rmscommandreceipts")
					.WithColumn("departmentid").AsInt32().NotNullable().PrimaryKey()
					.WithColumn("keyhash").AsString(64).NotNullable().PrimaryKey()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("requestchecksum").AsString(64).NotNullable()
					.WithColumn("reservationid").AsString(36).NotNullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("completedon").AsDateTime2().Nullable();
			foreach (var table in new[] { "rmsoperationalrecords", "rmsincidentreports" })
				if (!Schema.Table(table).Column("searcherasedon").Exists())
					Alter.Table(table).AddColumn("searcherasedon").AsDateTime2().Nullable();
			if (!Schema.Table("rmsevidenceartifacts").Column("capturerequestchecksum").Exists())
				Alter.Table("rmsevidenceartifacts").AddColumn("capturerequestchecksum").AsString(80).Nullable();
			if (!Schema.Table("udfdefinitions").Column("recorddefinitionkey").Exists())
				Alter.Table("udfdefinitions").AddColumn("recorddefinitionkey").AsString(200).Nullable();
			if (!Schema.Table("udfdefinitions").Column("recorddefinitionversion").Exists())
				Alter.Table("udfdefinitions").AddColumn("recorddefinitionversion").AsInt32().Nullable();
			if (!Schema.Table("udffields").Column("rmsclassification").Exists())
				Alter.Table("udffields").AddColumn("rmsclassification").AsInt32().Nullable();
			if (!Schema.Table("rmsoperationalrecords").Column("udfdefinitionid").Exists())
				Alter.Table("rmsoperationalrecords").AddColumn("udfdefinitionid").AsString(128).Nullable();
			if (!Schema.Table("rmsincidentreports").Column("udfdefinitionid").Exists())
				Alter.Table("rmsincidentreports").AddColumn("udfdefinitionid").AsString(128).Nullable();
			if (!Schema.Table("rmsrecordlegalholdmembers").Exists())
			{
				Create.Table("rmsrecordlegalholdmembers").WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("holdid").AsString(36).NotNullable().PrimaryKey().WithColumn("recordid").AsString(36).NotNullable().PrimaryKey().WithColumn("matchedon").AsDateTime2().NotNullable();
				Create.Index("ix_rmslegalholdmembers_record").OnTable("rmsrecordlegalholdmembers").OnColumn("departmentid").Ascending().OnColumn("recordid").Ascending();
			}
			if (!Schema.Table("rmsdisclosureproductions").Column("deliverymethod").Exists())
				Alter.Table("rmsdisclosureproductions").AddColumn("deliverymethod").AsString(200).Nullable();
			if (!Schema.Table("rmsdisclosureproductions").Column("deliveryreference").Exists())
				Alter.Table("rmsdisclosureproductions").AddColumn("deliveryreference").AsString(1000).Nullable();
			if (!Schema.Table("rmsrecordattachments").Column("classification").Exists())
				Alter.Table("rmsrecordattachments").AddColumn("classification").AsInt32().Nullable();
			if (!Schema.Table("rmsoperationalrecords").Column("originalrequestchecksum").Exists())
				Alter.Table("rmsoperationalrecords").AddColumn("originalrequestchecksum").AsString(80).Nullable();
			if (!Schema.Table("rmssubmissions").Column("destinationidentity").Exists())
				Alter.Table("rmssubmissions").AddColumn("destinationidentity").AsCustom("text").Nullable();
			if (!Schema.Table("rmssubmissions").Column("requiresreconciliation").Exists())
				Alter.Table("rmssubmissions").AddColumn("requiresreconciliation").AsBoolean().NotNullable().WithDefaultValue(false);
			if (!Schema.Table("rmssubmissions").Column("creatependingreceipt").Exists())
			{
				Alter.Table("rmssubmissions").AddColumn("creatependingreceipt").AsBoolean().NotNullable().WithDefaultValue(false);
				Execute.Sql("UPDATE rmssubmissions SET creatependingreceipt = TRUE, requiresreconciliation = TRUE WHERE senton IS NOT NULL AND externalid IS NULL AND (responsestatuscode IS NULL OR responsestatuscode >= 500 OR responsestatuscode BETWEEN 200 AND 299)");
				Execute.Sql("UPDATE rmssubmissions SET requiresreconciliation = TRUE WHERE destinationidentity IS NULL AND externalid IS NOT NULL");
			}
			if (!Schema.Table("rmssubmissionexchanges").Exists())
			{
				Create.Table("rmssubmissionexchanges")
					.WithColumn("rmssubmissionexchangeid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("submissionid").AsString(36).NotNullable()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("revisionid").AsString(36).NotNullable()
					.WithColumn("exchangeid").AsString(36).NotNullable()
					.WithColumn("stage").AsString(20).NotNullable()
					.WithColumn("operation").AsString(20).NotNullable()
					.WithColumn("destinationidentity").AsCustom("text").NotNullable()
					.WithColumn("payloadchecksum").AsString(80).NotNullable()
					.WithColumn("outcomejson").AsCustom("text").Nullable()
					.WithColumn("outcomechecksum").AsString(80).Nullable()
					.WithColumn("attemptnumber").AsInt32().NotNullable()
					.WithColumn("occurredon").AsDateTime2().NotNullable();
				Create.Index("ux_rmssubmissionexchanges_stage").OnTable("rmssubmissionexchanges")
					.OnColumn("departmentid").Ascending().OnColumn("submissionid").Ascending().OnColumn("exchangeid").Ascending().OnColumn("stage").Ascending().WithOptions().Unique();
				Create.Index("ix_rmssubmissionexchanges_record").OnTable("rmssubmissionexchanges")
					.OnColumn("departmentid").Ascending().OnColumn("recordid").Ascending().OnColumn("occurredon").Ascending();
			}
		}

		public override void Down() => throw new System.NotSupportedException("Delivery receipts must be retained. Roll back the application without deleting RMS hardening data.");
	}
}
