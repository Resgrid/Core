using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(101)]
	public class M0101_AddUnitTracking : Migration
	{
		private const string DevicesTable = "UnitTrackingDevices";
		private const string CredentialsTable = "UnitTrackingCredentials";

		public override void Up()
		{
			if (!Schema.Table(DevicesTable).Exists())
			{
				Create.Table(DevicesTable)
					.WithColumn("UnitTrackingDeviceId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UnitId").AsInt32().NotNullable()
					.WithColumn("DisplayName").AsString(200).Nullable()
					.WithColumn("ManufacturerKey").AsString(64).Nullable()
					.WithColumn("ModelKey").AsString(64).Nullable()
					.WithColumn("TransportType").AsInt32().NotNullable()
					.WithColumn("ProtocolKey").AsString(64).Nullable()
					.WithColumn("PayloadAdapterKey").AsString(64).Nullable()
					.WithColumn("DeviceIdentifier").AsString(128).Nullable()
					.WithColumn("SecondaryIdentifier").AsString(128).Nullable()
					.WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("SourcePriority").AsInt32().NotNullable().WithDefaultValue(100)
					.WithColumn("AllowedSourceCidrs").AsString(int.MaxValue).Nullable()
					.WithColumn("LastSeenOn").AsDateTime().Nullable()
					.WithColumn("LastPositionOn").AsDateTime().Nullable()
					.WithColumn("LastReceivedOn").AsDateTime().Nullable()
					.WithColumn("LastStatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("LastErrorCode").AsString(64).Nullable()
					.WithColumn("FirmwareVersion").AsString(128).Nullable()
					.WithColumn("CreatedByUserId").AsString(128).NotNullable()
					.WithColumn("CreatedOn").AsDateTime().NotNullable()
					.WithColumn("UpdatedByUserId").AsString(128).Nullable()
					.WithColumn("UpdatedOn").AsDateTime().Nullable();

			if (!Schema.Table("Units").Constraint("UQ_Units_DepartmentId_UnitId").Exists())
			{
				Create.UniqueConstraint("UQ_Units_DepartmentId_UnitId")
					.OnTable("Units")
					.Columns("DepartmentId", "UnitId");
			}

			Create.ForeignKey("FK_UnitTrackingDevices_Units_Department_Unit")
				.FromTable(DevicesTable).ForeignColumns("DepartmentId", "UnitId")
				.ToTable("Units").PrimaryColumns("DepartmentId", "UnitId");
			}

			if (!Schema.Table(DevicesTable).Index("IX_UnitTrackingDevices_Department_Unit_Deleted").Exists())
			{
				Create.Index("IX_UnitTrackingDevices_Department_Unit_Deleted")
					.OnTable(DevicesTable)
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("UnitId").Ascending()
					.OnColumn("IsDeleted").Ascending();
			}

			if (!Schema.Table(DevicesTable).Index("IX_UnitTrackingDevices_Department_Enabled_Deleted").Exists())
			{
				Create.Index("IX_UnitTrackingDevices_Department_Enabled_Deleted")
					.OnTable(DevicesTable)
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("IsEnabled").Ascending()
					.OnColumn("IsDeleted").Ascending();
			}

			if (!Schema.Table(DevicesTable).Index("IX_UnitTrackingDevices_LastSeenOn").Exists())
			{
				Create.Index("IX_UnitTrackingDevices_LastSeenOn")
					.OnTable(DevicesTable)
					.OnColumn("LastSeenOn").Ascending();
			}

			Execute.Sql(@"
				IF NOT EXISTS (
					SELECT 1
					FROM sys.indexes
					WHERE name = 'UX_UnitTrackingDevices_Protocol_DeviceIdentifier'
					  AND object_id = OBJECT_ID(N'UnitTrackingDevices'))
				BEGIN
					CREATE UNIQUE NONCLUSTERED INDEX UX_UnitTrackingDevices_Protocol_DeviceIdentifier
					ON UnitTrackingDevices (ProtocolKey, DeviceIdentifier)
					WHERE DeviceIdentifier IS NOT NULL AND IsDeleted = 0;
				END");

			if (!Schema.Table(CredentialsTable).Exists())
			{
				Create.Table(CredentialsTable)
					.WithColumn("UnitTrackingCredentialId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("UnitTrackingDeviceId").AsString(128).NotNullable()
					.WithColumn("AuthMode").AsInt32().NotNullable()
					.WithColumn("HeaderName").AsString(128).Nullable()
					.WithColumn("BasicUsername").AsString(128).Nullable()
					.WithColumn("KeyPrefix").AsString(20).NotNullable()
					.WithColumn("SecretHash").AsString(64).NotNullable()
					.WithColumn("ValidFrom").AsDateTime().NotNullable()
					.WithColumn("ExpiresOn").AsDateTime().Nullable()
					.WithColumn("RevokedOn").AsDateTime().Nullable()
					.WithColumn("LastUsedOn").AsDateTime().Nullable()
					.WithColumn("CreatedByUserId").AsString(128).NotNullable()
					.WithColumn("CreatedOn").AsDateTime().NotNullable();

				Create.ForeignKey("FK_UnitTrackingCredentials_Devices")
					.FromTable(CredentialsTable).ForeignColumn("UnitTrackingDeviceId")
					.ToTable(DevicesTable).PrimaryColumn("UnitTrackingDeviceId");
			}

			if (!Schema.Table(CredentialsTable).Index("UX_UnitTrackingCredentials_SecretHash").Exists())
			{
				Create.Index("UX_UnitTrackingCredentials_SecretHash")
					.OnTable(CredentialsTable)
					.OnColumn("SecretHash").Ascending()
					.WithOptions().Unique();
			}

			if (!Schema.Table(CredentialsTable).Index("IX_UnitTrackingCredentials_Device_Revoked_Expires").Exists())
			{
				Create.Index("IX_UnitTrackingCredentials_Device_Revoked_Expires")
					.OnTable(CredentialsTable)
					.OnColumn("UnitTrackingDeviceId").Ascending()
					.OnColumn("RevokedOn").Ascending()
					.OnColumn("ExpiresOn").Ascending();
			}

			if (!Schema.Table(CredentialsTable).Index("IX_UnitTrackingCredentials_KeyPrefix").Exists())
			{
				Create.Index("IX_UnitTrackingCredentials_KeyPrefix")
					.OnTable(CredentialsTable)
					.OnColumn("KeyPrefix").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table(CredentialsTable).Exists())
				Delete.Table(CredentialsTable);

			if (Schema.Table(DevicesTable).Exists())
				Delete.Table(DevicesTable);

			if (Schema.Table("Units").Constraint("UQ_Units_DepartmentId_UnitId").Exists())
				Delete.UniqueConstraint("UQ_Units_DepartmentId_UnitId").FromTable("Units");
		}
	}
}
