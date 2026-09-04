using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// NERIS incident report core (RMS plan sections 4.2/5.2, registry M0164, package RMS-2): RmsIncidentReports
	/// (aggregate root, SingleAuthoritative per (DepartmentId, CallId, ReportingEntityId, DefinitionKey)),
	/// RmsSourceFacts (provenance ledger for every prefilled value), RmsUnitResponses, RmsIncidentTypes,
	/// RmsActionTactics, RmsAids, RmsLocations, RmsNarratives (protected-candidate holder with the inert
	/// envelope) and RmsValidationIssues. Every table carries DepartmentId and an immutable ProtectionId; child
	/// rows are a working draft (RevisionId NULL) plus immutable revision copies. Existence-guarded for safe retry.
	/// </summary>
	[Migration(164)]
	public class M0164_AddRmsIncidentReportCore : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsIncidentReports").Exists())
			{
				Create.Table("RmsIncidentReports")
					.WithColumn("RmsIncidentReportId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("ReportingEntityId").AsString(100).NotNullable()
					.WithColumn("DefinitionKey").AsString(100).NotNullable()
					.WithColumn("DefinitionVersion").AsInt32().NotNullable()
					.WithColumn("ProfileVersion").AsString(20).Nullable()
					.WithColumn("LifecyclePreset").AsInt32().NotNullable()
					.WithColumn("State").AsInt32().NotNullable()
					.WithColumn("RecordNumber").AsString(50).Nullable()
					.WithColumn("DraftReference").AsString(50).Nullable()
					.WithColumn("IncidentNumber").AsString(100).Nullable()
					.WithColumn("DisplaySummary").AsString(400).Nullable()
					.WithColumn("StationGroupId").AsInt32().Nullable()
					.WithColumn("AuthorUserId").AsString(128).NotNullable()
					.WithColumn("OwnerUserId").AsString(128).Nullable()
					.WithColumn("ReviewerUserId").AsString(128).Nullable()
					.WithColumn("ApproverUserId").AsString(128).Nullable()
					.WithColumn("ReviewDueOn").AsDateTime2().Nullable()
					.WithColumn("SubmittedForReviewOn").AsDateTime2().Nullable()
					.WithColumn("ReturnedOn").AsDateTime2().Nullable()
					.WithColumn("ReturnReasonCode").AsString(50).Nullable()
					.WithColumn("ReturnReasonText").AsString(int.MaxValue).Nullable()
					.WithColumn("ReturnCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ApprovedOn").AsDateTime2().Nullable()
					.WithColumn("FinalizedOn").AsDateTime2().Nullable()
					.WithColumn("FinalizedByUserId").AsString(128).Nullable()
					.WithColumn("CurrentRevisionId").AsString(36).Nullable()
					.WithColumn("RevisionCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AmendsRevisionId").AsString(36).Nullable()
					.WithColumn("VoidedOn").AsDateTime2().Nullable()
					.WithColumn("VoidedByUserId").AsString(128).Nullable()
					.WithColumn("VoidReasonCode").AsString(50).Nullable()
					.WithColumn("VoidReasonText").AsString(int.MaxValue).Nullable()
					.WithColumn("CancelledOn").AsDateTime2().Nullable()
					.WithColumn("CancelledByUserId").AsString(128).Nullable()
					.WithColumn("NerisIncidentId").AsString(100).Nullable()
					.WithColumn("LastSubmissionId").AsString(36).Nullable()
					.WithColumn("LastSubmissionState").AsInt32().Nullable()
					.WithColumn("LastSubmittedOn").AsDateTime2().Nullable()
					.WithColumn("AcceptedOn").AsDateTime2().Nullable()
					.WithColumn("RejectedOn").AsDateTime2().Nullable()
					.WithColumn("RejectionSummary").AsString(int.MaxValue).Nullable()
					.WithColumn("CallCreatedOn").AsDateTime2().Nullable()
					.WithColumn("CallAnsweredOn").AsDateTime2().Nullable()
					.WithColumn("CallArrivalOn").AsDateTime2().Nullable()
					.WithColumn("IncidentClearedOn").AsDateTime2().Nullable()
					.WithColumn("DispatchCenterId").AsString(100).Nullable()
					.WithColumn("DeterminantCode").AsString(100).Nullable()
					.WithColumn("DispatchIncidentCode").AsString(200).Nullable()
					.WithColumn("Disposition").AsString(200).Nullable()
					.WithColumn("PeoplePresent").AsBoolean().Nullable()
					.WithColumn("DisplacementCount").AsInt32().Nullable()
					.WithColumn("AnimalsRescued").AsInt32().Nullable()
					.WithColumn("SpecialModifiersCsv").AsString(400).Nullable()
					.WithColumn("IdempotencyKey").AsString(64).Nullable()
					.WithColumn("OriginClient").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedByUserId").AsString(128).Nullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsIncidentReports_Department_State_Created").OnTable("RmsIncidentReports")
					.OnColumn("DepartmentId").Ascending().OnColumn("State").Ascending().OnColumn("CreatedOn").Descending();
				Create.Index("IX_RmsIncidentReports_Department_Call").OnTable("RmsIncidentReports")
					.OnColumn("DepartmentId").Ascending().OnColumn("CallId").Ascending();
				Create.Index("IX_RmsIncidentReports_Department_Owner").OnTable("RmsIncidentReports")
					.OnColumn("DepartmentId").Ascending().OnColumn("OwnerUserId").Ascending();
				// SingleAuthoritative cardinality (plan 5.2.1): one report per responding entity per Call.
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsIncidentReports_Call_Entity ON RmsIncidentReports (DepartmentId, CallId, ReportingEntityId, DefinitionKey) WHERE DeletedOn IS NULL;");
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsIncidentReports_Idempotency ON RmsIncidentReports (DepartmentId, IdempotencyKey) WHERE IdempotencyKey IS NOT NULL;");
			}

			if (!Schema.Table("RmsSourceFacts").Exists())
			{
				Create.Table("RmsSourceFacts")
					.WithColumn("RmsSourceFactId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("FactKey").AsString(150).NotNullable()
					.WithColumn("SourceKind").AsInt32().NotNullable()
					.WithColumn("SourceSystem").AsString(50).Nullable()
					.WithColumn("SourceEntityType").AsString(100).Nullable()
					.WithColumn("SourceEntityId").AsString(100).Nullable()
					.WithColumn("SourceValue").AsString(int.MaxValue).Nullable()
					.WithColumn("CurrentValue").AsString(int.MaxValue).Nullable()
					.WithColumn("SourceTime").AsDateTime2().Nullable()
					.WithColumn("ImportedOn").AsDateTime2().NotNullable()
					.WithColumn("CorrectedOn").AsDateTime2().Nullable()
					.WithColumn("CorrectedByUserId").AsString(128).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);
				Create.Index("IX_RmsSourceFacts_Department_Record_Revision").OnTable("RmsSourceFacts")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending().OnColumn("RevisionId").Ascending();
			}

			if (!Schema.Table("RmsUnitResponses").Exists())
			{
				Create.Table("RmsUnitResponses")
					.WithColumn("RmsUnitResponseId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("UnitId").AsInt32().Nullable()
					.WithColumn("UnitNameSnapshot").AsString(200).Nullable()
					.WithColumn("UnitTypeSnapshot").AsString(100).Nullable()
					.WithColumn("UnitNerisId").AsString(50).Nullable()
					.WithColumn("StationGroupIdSnapshot").AsInt32().Nullable()
					.WithColumn("Staffing").AsInt32().Nullable()
					.WithColumn("UnableToDispatch").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("DispatchedOn").AsDateTime2().Nullable()
					.WithColumn("EnrouteOn").AsDateTime2().Nullable()
					.WithColumn("OnSceneOn").AsDateTime2().Nullable()
					.WithColumn("CanceledEnrouteOn").AsDateTime2().Nullable()
					.WithColumn("StagingOn").AsDateTime2().Nullable()
					.WithColumn("ClearedOn").AsDateTime2().Nullable()
					.WithColumn("ResponseMode").AsString(20).Nullable()
					.WithColumn("TransportMode").AsString(20).Nullable()
					.WithColumn("TimesSourceKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Disposition").AsString(100).Nullable()
					.WithColumn("Ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);
				Create.Index("IX_RmsUnitResponses_Department_Record_Revision").OnTable("RmsUnitResponses")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending().OnColumn("RevisionId").Ascending();
			}

			if (!Schema.Table("RmsIncidentTypes").Exists())
			{
				Create.Table("RmsIncidentTypes")
					.WithColumn("RmsIncidentTypeId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("TypeCode").AsString(200).NotNullable()
					.WithColumn("IsPrimary").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("LocalCode").AsString(200).Nullable()
					.WithColumn("ValueSetVersion").AsString(20).Nullable()
					.WithColumn("Ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);
				Create.Index("IX_RmsIncidentTypes_Department_Record_Revision").OnTable("RmsIncidentTypes")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending().OnColumn("RevisionId").Ascending();
			}

			if (!Schema.Table("RmsActionTactics").Exists())
			{
				Create.Table("RmsActionTactics")
					.WithColumn("RmsActionTacticId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("TacticCode").AsString(200).NotNullable()
					.WithColumn("ActorUnitId").AsInt32().Nullable()
					.WithColumn("OccurredOn").AsDateTime2().Nullable()
					.WithColumn("SourceKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Outcome").AsString(200).Nullable()
					.WithColumn("Ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);
				Create.Index("IX_RmsActionTactics_Department_Record_Revision").OnTable("RmsActionTactics")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending().OnColumn("RevisionId").Ascending();
			}

			if (!Schema.Table("RmsAids").Exists())
			{
				Create.Table("RmsAids")
					.WithColumn("RmsAidId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("Direction").AsString(10).NotNullable()
					.WithColumn("AidType").AsString(30).NotNullable()
					.WithColumn("CounterpartNerisId").AsString(50).Nullable()
					.WithColumn("CounterpartName").AsString(200).Nullable()
					.WithColumn("IsNonFireDepartment").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("NonFdType").AsString(50).Nullable()
					.WithColumn("Ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);
				Create.Index("IX_RmsAids_Department_Record_Revision").OnTable("RmsAids")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending().OnColumn("RevisionId").Ascending();
			}

			if (!Schema.Table("RmsLocations").Exists())
			{
				Create.Table("RmsLocations")
					.WithColumn("RmsLocationId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("AddressText").AsString(int.MaxValue).Nullable()
					.WithColumn("Number").AsString(20).Nullable()
					.WithColumn("NumberPrefix").AsString(20).Nullable()
					.WithColumn("NumberSuffix").AsString(20).Nullable()
					.WithColumn("Street").AsString(200).Nullable()
					.WithColumn("UnitValue").AsString(50).Nullable()
					.WithColumn("Municipality").AsString(200).Nullable()
					.WithColumn("County").AsString(200).Nullable()
					.WithColumn("State").AsString(10).Nullable()
					.WithColumn("PostalCode").AsString(20).Nullable()
					.WithColumn("Country").AsString(2).Nullable()
					.WithColumn("PlaceType").AsString(100).Nullable()
					.WithColumn("LocationUse").AsString(100).Nullable()
					.WithColumn("CrossStreet1").AsString(200).Nullable()
					.WithColumn("CrossStreet2").AsString(200).Nullable()
					.WithColumn("Latitude").AsDecimal(12, 8).Nullable()
					.WithColumn("Longitude").AsDecimal(12, 8).Nullable()
					.WithColumn("Jurisdiction").AsString(200).Nullable()
					.WithColumn("SourceKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);
				Create.Index("IX_RmsLocations_Department_Record_Revision").OnTable("RmsLocations")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending().OnColumn("RevisionId").Ascending();
			}

			if (!Schema.Table("RmsNarratives").Exists())
			{
				Create.Table("RmsNarratives")
					.WithColumn("RmsNarrativeId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("Narrative").AsString(int.MaxValue).Nullable()
					.WithColumn("ImpedimentNarrative").AsString(int.MaxValue).Nullable()
					.WithColumn("OutcomeNarrative").AsString(int.MaxValue).Nullable()
					.WithColumn("SupplementalJson").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedEnvelope").AsString(int.MaxValue).Nullable()
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);
				Create.Index("IX_RmsNarratives_Department_Record_Revision").OnTable("RmsNarratives")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending().OnColumn("RevisionId").Ascending();
			}

			if (!Schema.Table("RmsValidationIssues").Exists())
			{
				Create.Table("RmsValidationIssues")
					.WithColumn("RmsValidationIssueId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("ProfileVersion").AsString(20).Nullable()
					.WithColumn("RuleKey").AsString(100).NotNullable()
					.WithColumn("Severity").AsInt32().NotNullable()
					.WithColumn("FieldPath").AsString(200).Nullable()
					.WithColumn("Message").AsString(int.MaxValue).Nullable()
					.WithColumn("Source").AsInt32().NotNullable()
					.WithColumn("ResolvedOn").AsDateTime2().Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable();
				Create.Index("IX_RmsValidationIssues_Department_Record").OnTable("RmsValidationIssues")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending();
			}
		}

		public override void Down()
		{
			foreach (var table in new[] { "RmsValidationIssues", "RmsNarratives", "RmsLocations", "RmsAids", "RmsActionTactics", "RmsIncidentTypes", "RmsUnitResponses", "RmsSourceFacts", "RmsIncidentReports" })
			{
				if (Schema.Table(table).Exists())
					Delete.Table(table);
			}
		}
	}
}
