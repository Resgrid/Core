using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Records (RMS-1C) external orders and fills (plan section 4.1 external-order fill contract, registry M0163): one manually entered, checksummed order snapshot per deployment Record with both home and host profiles, and one row per request/fill the department answers with its lifecycle times captured with local offset. No connector, no write-back.
	/// Existence-guarded for safe retry.
	/// </summary>
	[Migration(163)]
	public class M0163_AddRmsExternalOrderReferences : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsExternalOrders").Exists())
			{
				Create.Table("RmsExternalOrders")
					.WithColumn("RmsExternalOrderId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("ProfileKey").AsString(64).NotNullable()
					.WithColumn("ProfileVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("HomeProfileKey").AsString(64).Nullable()
					.WithColumn("HostProfileKey").AsString(64).Nullable()
					.WithColumn("SourceScheme").AsString(64).Nullable()
					.WithColumn("SourceSystem").AsString(200).Nullable()
					.WithColumn("OrderNumber").AsString(200).NotNullable()
					.WithColumn("IncidentName").AsString(200).NotNullable()
					.WithColumn("IncidentNumber").AsString(200).Nullable()
					.WithColumn("IncidentCountry").AsString(64).Nullable()
					.WithColumn("IncidentSubdivision").AsString(64).Nullable()
					.WithColumn("OrderingOffice").AsString(200).Nullable()
					.WithColumn("DispatchOffice").AsString(200).Nullable()
					.WithColumn("RequestingAgency").AsString(200).Nullable()
					.WithColumn("ReceivingAgency").AsString(200).Nullable()
					.WithColumn("SendingAgency").AsString(200).Nullable()
					.WithColumn("DepartmentRole").AsString(64).Nullable()
					.WithColumn("CostCode").AsString(200).Nullable()
					.WithColumn("AgreementReference").AsString(200).Nullable()
					.WithColumn("CurrencyCode").AsString(64).Nullable()
					.WithColumn("MeasurementSystem").AsString(64).Nullable()
					.WithColumn("TimeZoneId").AsString(128).Nullable()
					.WithColumn("CapturedOffsetMinutes").AsInt32().Nullable()
					.WithColumn("SourceCapturedOn").AsDateTime2().Nullable()
					.WithColumn("SourceVersion").AsString(64).Nullable()
					.WithColumn("ArtifactFileName").AsString(400).Nullable()
					.WithColumn("ArtifactContentType").AsString(200).Nullable()
					.WithColumn("ArtifactChecksum").AsString(128).Nullable()
					.WithColumn("ArtifactData").AsBinary(int.MaxValue).Nullable()
					.WithColumn("ArtifactSafeUrl").AsString(int.MaxValue).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("MobilizedOn").AsDateTime2().Nullable()
					.WithColumn("ReleasedOn").AsDateTime2().Nullable()
					.WithColumn("ClosedOutOn").AsDateTime2().Nullable()
					.WithColumn("ClosedOutByUserId").AsString(128).Nullable()
					.WithColumn("CloseoutNotes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedByUserId").AsString(128).Nullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();
				Execute.Sql("CREATE NONCLUSTERED INDEX IX_RmsExternalOrders_Department_Record ON RmsExternalOrders (DepartmentId, RecordId);");
				Execute.Sql("CREATE NONCLUSTERED INDEX IX_RmsExternalOrders_Department_Order ON RmsExternalOrders (DepartmentId, OrderNumber);");
			}

			if (!Schema.Table("RmsExternalOrderFills").Exists())
			{
				Create.Table("RmsExternalOrderFills")
					.WithColumn("RmsExternalOrderFillId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RmsExternalOrderId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RequestNumber").AsString(200).NotNullable()
					.WithColumn("ParentRequestNumber").AsString(200).Nullable()
					.WithColumn("RequestCategory").AsString(64).Nullable()
					.WithColumn("FillNumber").AsString(200).Nullable()
					.WithColumn("ResourceKind").AsString(200).Nullable()
					.WithColumn("ResourceType").AsString(200).Nullable()
					.WithColumn("ResourceTypeScheme").AsString(64).Nullable()
					.WithColumn("Position").AsString(200).Nullable()
					.WithColumn("PositionScheme").AsString(64).Nullable()
					.WithColumn("IsTrainee").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("HomeUnit").AsString(200).Nullable()
					.WithColumn("HostAgency").AsString(200).Nullable()
					.WithColumn("AgencyUnitId").AsString(200).Nullable()
					.WithColumn("PointOfHire").AsString(400).Nullable()
					.WithColumn("CostCode").AsString(200).Nullable()
					.WithColumn("AgreementReference").AsString(200).Nullable()
					.WithColumn("AssignedUserId").AsString(128).Nullable()
					.WithColumn("AssignedUnitId").AsInt32().Nullable()
					.WithColumn("QualificationsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("RosterJson").AsString(int.MaxValue).Nullable()
					.WithColumn("TravelJson").AsString(int.MaxValue).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("DeclineReason").AsString(int.MaxValue).Nullable()
					.WithColumn("RequestedOn").AsDateTime2().Nullable()
					.WithColumn("NeededOn").AsDateTime2().Nullable()
					.WithColumn("FilledOn").AsDateTime2().Nullable()
					.WithColumn("MobilizedOn").AsDateTime2().Nullable()
					.WithColumn("CheckedInOn").AsDateTime2().Nullable()
					.WithColumn("AssignedOn").AsDateTime2().Nullable()
					.WithColumn("ReleasedOn").AsDateTime2().Nullable()
					.WithColumn("DemobilizedOn").AsDateTime2().Nullable()
					.WithColumn("ReturnedOn").AsDateTime2().Nullable()
					.WithColumn("CapturedOffsetMinutes").AsInt32().Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedByUserId").AsString(128).Nullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();
				Execute.Sql("CREATE NONCLUSTERED INDEX IX_RmsExternalOrderFills_Department_Order ON RmsExternalOrderFills (DepartmentId, RmsExternalOrderId);");
			}

		}

		public override void Down()
		{
			if (Schema.Table("RmsExternalOrderFills").Exists())
				Delete.Table("RmsExternalOrderFills");
			if (Schema.Table("RmsExternalOrders").Exists())
				Delete.Table("RmsExternalOrders");
		}
	}
}
