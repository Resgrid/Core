using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Conditional run cards: decision graph nodes, response packages and rows, "any of" targets, pinned decisions per
	/// call, outstanding requirements per call, RunCardAlarmLevels.ResponsePackageId, RunCards.RelaxUnfilledRows and
	/// WaitingCallAutoDispatchOverride, and the optional response code snapshot on CallDispatchUnits. Seeds
	/// Dispatch.RunCardConditions off. PostgreSQL twin of the SQL Server migration: free text is citext like the other
	/// run card tables (user-id columns must match AspNetUsers), node keys are varchar (opaque identifiers).
	/// </summary>
	[Migration(287)]
	public class M0287_AddConditionalRunCardsPg : Migration
	{
		// Keep FlagKey in sync with Resgrid.Model.FeatureFlagKeys.DispatchRunCardConditions.
		private const string FlagKey = "Dispatch.RunCardConditions";

		public override void Up()
		{
			if (!Schema.Table("ResponsePackages".ToLower()).Exists())
			{
				Create.Table("ResponsePackages".ToLower())
					.WithColumn("ResponsePackageId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("Name".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("Description".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("IsRetired".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("AddedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("AddedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("UpdatedOn".ToLower()).AsDateTime2().Nullable()
					.WithColumn("UpdatedByUserId".ToLower()).AsCustom("citext").Nullable();

				Create.Index("IX_ResponsePackages_DepartmentId".ToLower())
					.OnTable("ResponsePackages".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending();
			}

			if (!Schema.Table("ResponsePackageRows".ToLower()).Exists())
			{
				Create.Table("ResponsePackageRows".ToLower())
					.WithColumn("ResponsePackageRowId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("ResponsePackageId".ToLower()).AsInt32().NotNullable()
					.WithColumn("Label".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("FillMode".ToLower()).AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("RequiredCount".ToLower()).AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("StatusFilter".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("StatusBaseTypes".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("ResponseCodeId".ToLower()).AsInt32().Nullable()
					.WithColumn("SubstitutePackageId".ToLower()).AsInt32().Nullable()
					.WithColumn("KeepOutstanding".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("SortOrder".ToLower()).AsInt32().NotNullable().WithDefaultValue(0);

				Create.Index("IX_ResponsePackageRows_ResponsePackageId".ToLower())
					.OnTable("ResponsePackageRows".ToLower())
					.OnColumn("ResponsePackageId".ToLower()).Ascending();
			}

			if (!Schema.Table("RunCardTargets".ToLower()).Exists())
			{
				Create.Table("RunCardTargets".ToLower())
					.WithColumn("RunCardTargetId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("OwnerKind".ToLower()).AsInt32().NotNullable()
					.WithColumn("OwnerId".ToLower()).AsInt32().NotNullable()
					.WithColumn("TargetKind".ToLower()).AsInt32().NotNullable()
					.WithColumn("TargetRefId".ToLower()).AsInt32().Nullable();

				Create.Index("IX_RunCardTargets_Owner".ToLower())
					.OnTable("RunCardTargets".ToLower())
					.OnColumn("OwnerKind".ToLower()).Ascending()
					.OnColumn("OwnerId".ToLower()).Ascending();

				Create.Index("IX_RunCardTargets_DepartmentId".ToLower())
					.OnTable("RunCardTargets".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending();
			}

			if (!Schema.Table("RunCardDecisionNodes".ToLower()).Exists())
			{
				Create.Table("RunCardDecisionNodes".ToLower())
					.WithColumn("RunCardDecisionNodeId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("RunCardAlarmLevelId".ToLower()).AsInt32().NotNullable()
					.WithColumn("NodeKey".ToLower()).AsString(36).NotNullable()
					.WithColumn("IsRoot".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("NodeKind".ToLower()).AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("Label".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("ConditionType".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SubjectStatusFilter".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SubjectStatusBaseTypes".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("ReferenceStatusFilter".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ReferenceStatusBaseTypes".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("ThresholdSeconds".ToLower()).AsInt32().Nullable()
					.WithColumn("RequiredCount".ToLower()).AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("ParametersJson".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("YesNodeKey".ToLower()).AsString(36).Nullable()
					.WithColumn("NoNodeKey".ToLower()).AsString(36).Nullable()
					.WithColumn("ResponsePackageId".ToLower()).AsInt32().Nullable()
					.WithColumn("AdviceText".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("SortOrder".ToLower()).AsInt32().NotNullable().WithDefaultValue(0);

				Create.Index("IX_RunCardDecisionNodes_RunCardAlarmLevelId".ToLower())
					.OnTable("RunCardDecisionNodes".ToLower())
					.OnColumn("RunCardAlarmLevelId".ToLower()).Ascending();

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_runcarddecisionnodes_level_key ON runcarddecisionnodes (runcardalarmlevelid, nodekey);");
			}

			if (!Schema.Table("RunCardDecisions".ToLower()).Exists())
			{
				Create.Table("RunCardDecisions".ToLower())
					.WithColumn("RunCardDecisionId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("CallId".ToLower()).AsInt32().NotNullable()
					.WithColumn("RunCardId".ToLower()).AsInt32().NotNullable()
					.WithColumn("AlarmLevel".ToLower()).AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("LeafNodeKey".ToLower()).AsString(36).Nullable()
					.WithColumn("LeafLabel".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("ResponsePackageId".ToLower()).AsInt32().Nullable()
					.WithColumn("PackageSnapshotJson".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("PathJson".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("Source".ToLower()).AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("CreatedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("CreatedByUserId".ToLower()).AsCustom("citext").Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_runcarddecisions_call_card_level ON runcarddecisions (callid, runcardid, alarmlevel, createdon DESC);");

				Create.Index("IX_RunCardDecisions_DepartmentId".ToLower())
					.OnTable("RunCardDecisions".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending();
			}

			if (!Schema.Table("CallOutstandingRequirements".ToLower()).Exists())
			{
				Create.Table("CallOutstandingRequirements".ToLower())
					.WithColumn("CallOutstandingRequirementId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("CallId".ToLower()).AsInt32().NotNullable()
					.WithColumn("Label".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("TargetsJson".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("RequiredCount".ToLower()).AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("ResolvedCount".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("StatusFilter".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("StatusBaseTypes".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("ResponseCodeId".ToLower()).AsInt32().Nullable()
					.WithColumn("ResponseCode".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("RunCardId".ToLower()).AsInt32().Nullable()
					.WithColumn("AlarmLevel".ToLower()).AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("ResponsePackageRowId".ToLower()).AsInt32().Nullable()
					.WithColumn("SentInstead".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("CreatedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("CreatedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("ResolvedOn".ToLower()).AsDateTime2().Nullable()
					.WithColumn("ResolvedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("Resolution".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ResolutionNote".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("ResolvedByUnitId".ToLower()).AsInt32().Nullable()
					.WithColumn("ClaimedOn".ToLower()).AsDateTime2().Nullable()
					.WithColumn("ClaimedByUnitId".ToLower()).AsInt32().Nullable();

				Create.Index("IX_CallOutstandingRequirements_CallId".ToLower())
					.OnTable("CallOutstandingRequirements".ToLower())
					.OnColumn("CallId".ToLower()).Ascending();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_calloutstandingrequirements_department_open ON calloutstandingrequirements (departmentid) WHERE resolvedon IS NULL;");
			}

			Execute.Sql("ALTER TABLE runcardalarmlevels ADD COLUMN IF NOT EXISTS responsepackageid integer NULL;");
			Execute.Sql("ALTER TABLE runcards ADD COLUMN IF NOT EXISTS relaxunfilledrows boolean NOT NULL DEFAULT false;");
			Execute.Sql("ALTER TABLE runcards ADD COLUMN IF NOT EXISTS waitingcallautodispatchoverride integer NULL;");
			Execute.Sql("ALTER TABLE calldispatchunits ADD COLUMN IF NOT EXISTS responsecodeid integer NULL;");
			Execute.Sql("ALTER TABLE calldispatchunits ADD COLUMN IF NOT EXISTS responsecode citext NULL;");
			Execute.Sql("ALTER TABLE calldispatchunits ADD COLUMN IF NOT EXISTS responsecodename citext NULL;");

			Execute.Sql(
				"INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) " +
				"SELECT '" + FlagKey + "', " +
				"'Conditional Run Cards', " +
				"'Decision graphs, reusable response packages, any-of targets, optional response codes, outstanding requirements with awaiting-resource alerts, and the run card template gallery. Requires Dispatch.RunCards. Seeded off; enable globally or per-department to roll out.', " +
				"'Dispatch', false " +
				"WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = '" + FlagKey + "');");
		}

		public override void Down()
		{
			Delete.FromTable("FeatureFlags".ToLower()).Row(new { flagkey = FlagKey });

			Execute.Sql("ALTER TABLE calldispatchunits DROP COLUMN IF EXISTS responsecodename;");
			Execute.Sql("ALTER TABLE calldispatchunits DROP COLUMN IF EXISTS responsecode;");
			Execute.Sql("ALTER TABLE calldispatchunits DROP COLUMN IF EXISTS responsecodeid;");
			Execute.Sql("ALTER TABLE runcards DROP COLUMN IF EXISTS waitingcallautodispatchoverride;");
			Execute.Sql("ALTER TABLE runcards DROP COLUMN IF EXISTS relaxunfilledrows;");
			Execute.Sql("ALTER TABLE runcardalarmlevels DROP COLUMN IF EXISTS responsepackageid;");

			Execute.Sql("DROP TABLE IF EXISTS calloutstandingrequirements;");
			Execute.Sql("DROP TABLE IF EXISTS runcarddecisions;");
			Execute.Sql("DROP TABLE IF EXISTS runcarddecisionnodes;");
			Execute.Sql("DROP TABLE IF EXISTS runcardtargets;");
			Execute.Sql("DROP TABLE IF EXISTS responsepackagerows;");
			Execute.Sql("DROP TABLE IF EXISTS responsepackages;");
		}
	}
}
