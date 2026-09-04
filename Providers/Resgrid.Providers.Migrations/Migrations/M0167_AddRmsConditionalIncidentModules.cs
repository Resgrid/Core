using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// NERIS conditional sections and the separate incident-analysis filing (RMS plan section 4.2, registry M0167,
	/// package RMS-3).
	/// <list type="bullet">
	/// <item>RmsIncidentModules — one row per conditional section instance (fire, hazsit, chemical, medical, the
	/// three alarm sections, both suppression sections, the four emerging-hazard sections, and the analysis-side
	/// origin/outside-fire/products/batteries). Reportable facts are columns; the section body is contract-shaped
	/// JSON validated against the pinned schema named in the row.</item>
	/// <item>RmsIncidentResources — non-unit resources used on the incident.</item>
	/// <item>RmsIncidentAnalyses — the fire/hazmat analysis the contract posts to /incident_analysis, a second
	/// submittable artifact for the same incident with its own state, revisions and idempotency key.</item>
	/// <item>RmsIncidentProperties, RmsIncidentVehicles — the analysis's enumerated property and vehicle rows,
	/// typed because value and loss are summed by department reporting.</item>
	/// </list>
	/// Every table carries DepartmentId and an immutable ProtectionId; child rows are a working draft
	/// (RevisionId NULL) plus immutable revision copies. Protected-candidate holders carry the inert envelope
	/// columns of plan section 5.9.1. Existence-guarded for safe retry.
	/// </summary>
	[Migration(167)]
	public class M0167_AddRmsConditionalIncidentModules : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsIncidentModules").Exists())
			{
				Create.Table("RmsIncidentModules")
					.WithColumn("RmsIncidentModuleId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RecordKind").AsInt32().NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("ModuleKind").AsInt32().NotNullable()
					.WithColumn("SchemaName").AsString(100).Nullable()
					.WithColumn("ProfileVersion").AsString(20).Nullable()
					.WithColumn("PrimaryCode").AsString(150).Nullable()
					.WithColumn("SecondaryCode").AsString(150).Nullable()
					.WithColumn("Quantity").AsDecimal(18, 4).Nullable()
					.WithColumn("QuantityUnit").AsString(50).Nullable()
					.WithColumn("OccurredOn").AsDateTime2().Nullable()
					.WithColumn("DetailJson").AsString(int.MaxValue).Nullable()
					.WithColumn("ProtectedEnvelope").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				Create.Index("IX_RmsIncidentModules_Department_Record_Revision").OnTable("RmsIncidentModules")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending().OnColumn("RevisionId").Ascending();
				Create.Index("IX_RmsIncidentModules_Department_Kind").OnTable("RmsIncidentModules")
					.OnColumn("DepartmentId").Ascending().OnColumn("ModuleKind").Ascending().OnColumn("PrimaryCode").Ascending();
			}

			if (!Schema.Table("RmsIncidentResources").Exists())
			{
				Create.Table("RmsIncidentResources")
					.WithColumn("RmsIncidentResourceId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("ResourceCode").AsString(150).NotNullable()
					.WithColumn("Quantity").AsInt32().Nullable()
					.WithColumn("Detail").AsString(400).Nullable()
					.WithColumn("Ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				Create.Index("IX_RmsIncidentResources_Department_Record_Revision").OnTable("RmsIncidentResources")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending().OnColumn("RevisionId").Ascending();
			}

			if (!Schema.Table("RmsIncidentAnalyses").Exists())
			{
				Create.Table("RmsIncidentAnalyses")
					.WithColumn("RmsIncidentAnalysisId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("IncidentReportId").AsString(36).NotNullable()
					.WithColumn("ReportingEntityId").AsString(100).Nullable()
					.WithColumn("ProfileVersion").AsString(20).Nullable()
					.WithColumn("State").AsInt32().NotNullable()
					.WithColumn("GeneralCause").AsString(100).Nullable()
					.WithColumn("InvestigationTypesCsv").AsString(500).Nullable()
					.WithColumn("EstimatedLossTotal").AsDecimal(18, 2).Nullable()
					.WithColumn("EstimatedValueTotal").AsDecimal(18, 2).Nullable()
					.WithColumn("CurrencyCode").AsString(3).Nullable()
					.WithColumn("AuthorUserId").AsString(128).NotNullable()
					.WithColumn("OwnerUserId").AsString(128).Nullable()
					.WithColumn("FinalizedOn").AsDateTime2().Nullable()
					.WithColumn("FinalizedByUserId").AsString(128).Nullable()
					.WithColumn("CurrentRevisionId").AsString(36).Nullable()
					.WithColumn("RevisionCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("NerisAnalysisId").AsString(100).Nullable()
					.WithColumn("LastSubmissionId").AsString(36).Nullable()
					.WithColumn("LastSubmissionState").AsInt32().Nullable()
					.WithColumn("LastSubmittedOn").AsDateTime2().Nullable()
					.WithColumn("AcceptedOn").AsDateTime2().Nullable()
					.WithColumn("RejectedOn").AsDateTime2().Nullable()
					.WithColumn("RejectionSummary").AsString(int.MaxValue).Nullable()
					.WithColumn("VoidedOn").AsDateTime2().Nullable()
					.WithColumn("VoidedByUserId").AsString(128).Nullable()
					.WithColumn("VoidReasonCode").AsString(50).Nullable()
					.WithColumn("VoidReasonText").AsString(int.MaxValue).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedByUserId").AsString(128).Nullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsIncidentAnalyses_Department_State").OnTable("RmsIncidentAnalyses")
					.OnColumn("DepartmentId").Ascending().OnColumn("State").Ascending();
				// One analysis per incident report; a second one would be a competing filing for the same incident.
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsIncidentAnalyses_Report ON RmsIncidentAnalyses (DepartmentId, IncidentReportId) WHERE DeletedOn IS NULL;");
			}

			if (!Schema.Table("RmsIncidentProperties").Exists())
			{
				Create.Table("RmsIncidentProperties")
					.WithColumn("RmsIncidentPropertyId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("LocationUse").AsString(150).Nullable()
					.WithColumn("ConstructionType").AsString(50).Nullable()
					.WithColumn("Foundation").AsString(50).Nullable()
					.WithColumn("ExteriorFinish").AsString(50).Nullable()
					.WithColumn("RoofMaterial").AsString(50).Nullable()
					.WithColumn("StoriesAboveGrade").AsInt32().Nullable()
					.WithColumn("StoriesBelowGrade").AsInt32().Nullable()
					.WithColumn("YearBuilt").AsInt32().Nullable()
					.WithColumn("Vacancy").AsString(50).Nullable()
					.WithColumn("DamageType").AsString(50).Nullable()
					.WithColumn("FireSpread").AsString(50).Nullable()
					.WithColumn("EstimatedValue").AsDecimal(18, 2).Nullable()
					.WithColumn("EstimatedLoss").AsDecimal(18, 2).Nullable()
					.WithColumn("ContentsValue").AsDecimal(18, 2).Nullable()
					.WithColumn("ContentsLoss").AsDecimal(18, 2).Nullable()
					.WithColumn("CurrencyCode").AsString(3).Nullable()
					.WithColumn("DetailJson").AsString(int.MaxValue).Nullable()
					.WithColumn("ProtectedEnvelope").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				Create.Index("IX_RmsIncidentProperties_Department_Record_Revision").OnTable("RmsIncidentProperties")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending().OnColumn("RevisionId").Ascending();
			}

			if (!Schema.Table("RmsIncidentVehicles").Exists())
			{
				Create.Table("RmsIncidentVehicles")
					.WithColumn("RmsIncidentVehicleId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("VehicleKind").AsString(20).NotNullable()
					.WithColumn("Make").AsString(100).Nullable()
					.WithColumn("Model").AsString(100).Nullable()
					.WithColumn("ModelYear").AsInt32().Nullable()
					.WithColumn("BodyStyle").AsString(50).Nullable()
					.WithColumn("Powertrain").AsString(50).Nullable()
					.WithColumn("DamageType").AsString(50).Nullable()
					.WithColumn("Vin").AsString(50).Nullable()
					.WithColumn("LicensePlate").AsString(50).Nullable()
					.WithColumn("LicenseState").AsString(10).Nullable()
					.WithColumn("WasOccupied").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("EstimatedValue").AsDecimal(18, 2).Nullable()
					.WithColumn("EstimatedLoss").AsDecimal(18, 2).Nullable()
					.WithColumn("CurrencyCode").AsString(3).Nullable()
					.WithColumn("DetailJson").AsString(int.MaxValue).Nullable()
					.WithColumn("ProtectedEnvelope").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				Create.Index("IX_RmsIncidentVehicles_Department_Record_Revision").OnTable("RmsIncidentVehicles")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending().OnColumn("RevisionId").Ascending();
			}
		}

		public override void Down()
		{
			foreach (var table in new[] { "RmsIncidentVehicles", "RmsIncidentProperties", "RmsIncidentAnalyses", "RmsIncidentResources", "RmsIncidentModules" })
			{
				if (Schema.Table(table).Exists())
					Delete.Table(table);
			}
		}
	}
}
