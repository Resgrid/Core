using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// External ordering-system connectors for mutual-aid deployments (RMS plan section 4.1, RMS-1C completion,
	/// registry M0181): the per-department connector (provider, feed root, encrypted credential, inbound token
	/// hash, read/write authority, rate limit, terms acknowledgement) and its bounded run log, plus the ownership
	/// marker on RmsExternalOrders that says whether a connector or a person maintains the source view. The
	/// credential is stored only as ciphertext and the inbound token only as a hash. Guarded for safe retry.
	/// </summary>
	[Migration(181)]
	public class M0181_AddRmsExternalOrderConnectors : Migration
	{
		public override void Up()
		{
			if (Schema.Table("RmsExternalOrders").Exists())
			{
				if (!Schema.Table("RmsExternalOrders").Column("ConnectorId").Exists())
					Alter.Table("RmsExternalOrders").AddColumn("ConnectorId").AsString(36).Nullable();
				if (!Schema.Table("RmsExternalOrders").Column("OwnershipMarker").Exists())
					Alter.Table("RmsExternalOrders").AddColumn("OwnershipMarker").AsString(16).NotNullable().WithDefaultValue("manual");
			}

			if (!Schema.Table("RmsExternalOrderConnectors").Exists())
			{
				Create.Table("RmsExternalOrderConnectors")
					.WithColumn("RmsExternalOrderConnectorId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("ProviderKey").AsString(32).NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("SourceSystem").AsString(200).Nullable()
					.WithColumn("SourceScheme").AsString(64).NotNullable()
					.WithColumn("ProfileKey").AsString(32).NotNullable()
					.WithColumn("BaseUrl").AsString(1000).NotNullable()
					.WithColumn("CredentialKind").AsString(16).NotNullable()
					.WithColumn("CredentialHeaderName").AsString(100).Nullable()
					.WithColumn("CredentialCiphertext").AsString(int.MaxValue).Nullable()
					.WithColumn("InboundTokenHash").AsString(128).Nullable()
					.WithColumn("ReadEnabled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("WriteEnabled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("PollIntervalMinutes").AsInt32().NotNullable().WithDefaultValue(60)
					.WithColumn("MaxRequestsPerHour").AsInt32().NotNullable().WithDefaultValue(12)
					.WithColumn("RequestsThisHour").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RateWindowStartedOn").AsDateTime2().Nullable()
					.WithColumn("TermsReference").AsString(500).Nullable()
					.WithColumn("TermsAcknowledgedOn").AsDateTime2().Nullable()
					.WithColumn("TermsAcknowledgedByUserId").AsString(128).Nullable()
					.WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("LastCursor").AsString(1000).Nullable()
					.WithColumn("LastPolledOn").AsDateTime2().Nullable()
					.WithColumn("LastSuccessOn").AsDateTime2().Nullable()
					.WithColumn("LastError").AsString(500).Nullable()
					.WithColumn("ConsecutiveFailures").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedByUserId").AsString(128).Nullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsExternalOrderConnectors_Department").OnTable("RmsExternalOrderConnectors")
					.OnColumn("DepartmentId").Ascending().OnColumn("DeletedOn").Ascending();
				Create.Index("IX_RmsExternalOrderConnectors_Due").OnTable("RmsExternalOrderConnectors")
					.OnColumn("IsEnabled").Ascending().OnColumn("LastPolledOn").Ascending();
			}

			if (!Schema.Table("RmsExternalOrderConnectorRuns").Exists())
			{
				Create.Table("RmsExternalOrderConnectorRuns")
					.WithColumn("RmsExternalOrderConnectorRunId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("RmsExternalOrderConnectorId").AsString(36).NotNullable()
					.WithColumn("Trigger").AsString(16).NotNullable()
					.WithColumn("TriggeredByUserId").AsString(128).Nullable()
					.WithColumn("StartedOn").AsDateTime2().NotNullable()
					.WithColumn("FinishedOn").AsDateTime2().Nullable()
					.WithColumn("Outcome").AsString(16).NotNullable()
					.WithColumn("Error").AsString(1000).Nullable()
					.WithColumn("RequestCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("OrdersSeen").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("OrdersCreated").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SnapshotsRecorded").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RequestsAdded").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Unchanged").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Rejected").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Conflicts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SourceVersion").AsString(64).Nullable();

				Create.Index("IX_RmsExternalOrderConnectorRuns_Connector").OnTable("RmsExternalOrderConnectorRuns")
					.OnColumn("DepartmentId").Ascending().OnColumn("RmsExternalOrderConnectorId").Ascending().OnColumn("StartedOn").Descending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("RmsExternalOrderConnectorRuns").Exists())
				Delete.Table("RmsExternalOrderConnectorRuns");
			if (Schema.Table("RmsExternalOrderConnectors").Exists())
				Delete.Table("RmsExternalOrderConnectors");
			if (Schema.Table("RmsExternalOrders").Exists())
			{
				if (Schema.Table("RmsExternalOrders").Column("OwnershipMarker").Exists())
					Delete.Column("OwnershipMarker").FromTable("RmsExternalOrders");
				if (Schema.Table("RmsExternalOrders").Column("ConnectorId").Exists())
					Delete.Column("ConnectorId").FromTable("RmsExternalOrders");
			}
		}
	}
}
