using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// External ordering-system connectors for mutual-aid deployments (RMS plan section 4.1, RMS-1C completion,
	/// registry M0181): the per-department connector (provider, feed root, encrypted credential, inbound token
	/// hash, read/write authority, rate limit, terms acknowledgement) and its bounded run log, plus the ownership
	/// marker on RmsExternalOrders that says whether a connector or a person maintains the source view. The
	/// credential is stored only as ciphertext and the inbound token only as a hash. Guarded for safe retry.
	/// </summary>
	[Migration(181)]
	public class M0181_AddRmsExternalOrderConnectorsPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("rmsexternalorders").Exists())
			{
				if (!Schema.Table("rmsexternalorders").Column("connectorid").Exists())
					Alter.Table("rmsexternalorders").AddColumn("connectorid").AsString(36).Nullable();
				if (!Schema.Table("rmsexternalorders").Column("ownershipmarker").Exists())
					Alter.Table("rmsexternalorders").AddColumn("ownershipmarker").AsString(16).NotNullable().WithDefaultValue("manual");
			}

			if (!Schema.Table("rmsexternalorderconnectors").Exists())
			{
				Create.Table("rmsexternalorderconnectors")
					.WithColumn("rmsexternalorderconnectorid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("providerkey").AsString(32).NotNullable()
					.WithColumn("name").AsString(200).NotNullable()
					.WithColumn("sourcesystem").AsString(200).Nullable()
					.WithColumn("sourcescheme").AsString(64).NotNullable()
					.WithColumn("profilekey").AsString(32).NotNullable()
					.WithColumn("baseurl").AsString(1000).NotNullable()
					.WithColumn("credentialkind").AsString(16).NotNullable()
					.WithColumn("credentialheadername").AsString(100).Nullable()
					.WithColumn("credentialciphertext").AsCustom("text").Nullable()
					.WithColumn("inboundtokenhash").AsString(128).Nullable()
					.WithColumn("readenabled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("writeenabled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("pollintervalminutes").AsInt32().NotNullable().WithDefaultValue(60)
					.WithColumn("maxrequestsperhour").AsInt32().NotNullable().WithDefaultValue(12)
					.WithColumn("requeststhishour").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ratewindowstartedon").AsDateTime2().Nullable()
					.WithColumn("termsreference").AsString(500).Nullable()
					.WithColumn("termsacknowledgedon").AsDateTime2().Nullable()
					.WithColumn("termsacknowledgedbyuserid").AsString(128).Nullable()
					.WithColumn("isenabled").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("lastcursor").AsString(1000).Nullable()
					.WithColumn("lastpolledon").AsDateTime2().Nullable()
					.WithColumn("lastsuccesson").AsDateTime2().Nullable()
					.WithColumn("lasterror").AsString(500).Nullable()
					.WithColumn("consecutivefailures").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("modifiedbyuserid").AsString(128).Nullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsexternalorderconnectors_department ON rmsexternalorderconnectors (departmentid, deletedon);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsexternalorderconnectors_due ON rmsexternalorderconnectors (isenabled, lastpolledon);");
			}

			if (!Schema.Table("rmsexternalorderconnectorruns").Exists())
			{
				Create.Table("rmsexternalorderconnectorruns")
					.WithColumn("rmsexternalorderconnectorrunid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("rmsexternalorderconnectorid").AsString(36).NotNullable()
					.WithColumn("trigger").AsString(16).NotNullable()
					.WithColumn("triggeredbyuserid").AsString(128).Nullable()
					.WithColumn("startedon").AsDateTime2().NotNullable()
					.WithColumn("finishedon").AsDateTime2().Nullable()
					.WithColumn("outcome").AsString(16).NotNullable()
					.WithColumn("error").AsString(1000).Nullable()
					.WithColumn("requestcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ordersseen").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("orderscreated").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("snapshotsrecorded").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("requestsadded").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("unchanged").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("rejected").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("conflicts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("sourceversion").AsString(64).Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsexternalorderconnectorruns_connector ON rmsexternalorderconnectorruns (departmentid, rmsexternalorderconnectorid, startedon DESC);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("rmsexternalorderconnectorruns").Exists())
				Delete.Table("rmsexternalorderconnectorruns");
			if (Schema.Table("rmsexternalorderconnectors").Exists())
				Delete.Table("rmsexternalorderconnectors");
			if (Schema.Table("rmsexternalorders").Exists())
			{
				if (Schema.Table("rmsexternalorders").Column("ownershipmarker").Exists())
					Delete.Column("ownershipmarker").FromTable("rmsexternalorders");
				if (Schema.Table("rmsexternalorders").Column("connectorid").Exists())
					Delete.Column("connectorid").FromTable("rmsexternalorders");
			}
		}
	}
}
