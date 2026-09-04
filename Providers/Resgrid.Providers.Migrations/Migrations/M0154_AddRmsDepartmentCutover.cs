using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Records (RMS) department cutover (RMS plan section 4.1, registry M0154). RmsDepartmentCutovers
	/// is one row per department holding the append-only activation fact: ActivatedOn, actor, reason,
	/// pre-activation legacy Log/UnitLog counts and checksum, State (Active/Reverted) and the
	/// before/after permission table the administrator confirmed. The legacy write guard and the
	/// rollback runbook key off this row, never off a mutable setting or the feature flag alone.
	/// RmsDepartmentCutoverEvents is its audited history. Existence-guarded for safe retry.
	/// </summary>
	[Migration(154)]
	public class M0154_AddRmsDepartmentCutover : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsDepartmentCutovers").Exists())
			{
				Create.Table("RmsDepartmentCutovers")
					.WithColumn("RmsDepartmentCutoverId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("ActivatedOn").AsDateTime2().NotNullable()
					.WithColumn("ActivatedByUserId").AsString(128).NotNullable()
					.WithColumn("Reason").AsString(1000).Nullable()
					.WithColumn("SourceLegacyLogCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SourceLegacyUnitLogCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SourceChecksum").AsString(128).Nullable()
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("RevertedOn").AsDateTime2().Nullable()
					.WithColumn("RevertedByUserId").AsString(128).Nullable()
					.WithColumn("PermissionMappingJson").AsString(int.MaxValue).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsDepartmentCutovers_DepartmentId ON RmsDepartmentCutovers (DepartmentId);");
			}

			if (!Schema.Table("RmsDepartmentCutoverEvents").Exists())
			{
				Create.Table("RmsDepartmentCutoverEvents")
					.WithColumn("RmsDepartmentCutoverEventId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("RmsDepartmentCutoverId").AsInt32().NotNullable()
					.WithColumn("EventType").AsString(50).NotNullable()
					.WithColumn("ActorUserId").AsString(128).Nullable()
					.WithColumn("OccurredOn").AsDateTime2().NotNullable()
					.WithColumn("DetailJson").AsString(int.MaxValue).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable();

				Create.Index("IX_RmsDepartmentCutoverEvents_Department_Cutover").OnTable("RmsDepartmentCutoverEvents")
					.OnColumn("DepartmentId").Ascending().OnColumn("RmsDepartmentCutoverId").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("RmsDepartmentCutoverEvents").Exists())
				Delete.Table("RmsDepartmentCutoverEvents");

			if (Schema.Table("RmsDepartmentCutovers").Exists())
				Delete.Table("RmsDepartmentCutovers");
		}
	}
}
