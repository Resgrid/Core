using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Records (RMS) cross-group visibility (RMS plan section 5.7.1, registry M0157).
	/// RmsRecordGroupScopes is the materialized (RecordId, DepartmentGroupId, AnchorType) visibility set,
	/// recomputed in-transaction on every save/finalize/amend/participant/unit change from the fixed v1
	/// anchor set (record group, author, participants, units, shares); it is the join target for every
	/// list, search, report, export and sync query under group-scoped visibility. RmsRecordShares is an
	/// explicit, audited, optionally time-boxed grant of one Record to one further group.
	/// Existence-guarded for safe retry.
	/// </summary>
	[Migration(157)]
	public class M0157_AddRmsGroupScopeAndShares : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsRecordGroupScopes").Exists())
			{
				Create.Table("RmsRecordGroupScopes")
					.WithColumn("RmsRecordGroupScopeId").AsInt64().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("DepartmentGroupId").AsInt32().NotNullable()
					.WithColumn("AnchorType").AsInt32().NotNullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable();

				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsRecordGroupScopes_Record_Group_Anchor ON RmsRecordGroupScopes (DepartmentId, RecordId, DepartmentGroupId, AnchorType);");
				// Covering index for the visibility join: which Records can group G see.
				Create.Index("IX_RmsRecordGroupScopes_Department_Group_Record").OnTable("RmsRecordGroupScopes")
					.OnColumn("DepartmentId").Ascending().OnColumn("DepartmentGroupId").Ascending().OnColumn("RecordId").Ascending();
			}

			if (!Schema.Table("RmsRecordShares").Exists())
			{
				Create.Table("RmsRecordShares")
					.WithColumn("RmsRecordShareId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("DepartmentGroupId").AsInt32().NotNullable()
					.WithColumn("GrantedByUserId").AsString(128).NotNullable()
					.WithColumn("GrantedOn").AsDateTime2().NotNullable()
					.WithColumn("Reason").AsString(1000).Nullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("RevokedOn").AsDateTime2().Nullable()
					.WithColumn("RevokedByUserId").AsString(128).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				Create.Index("IX_RmsRecordShares_Department_Record").OnTable("RmsRecordShares")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending();
				Create.Index("IX_RmsRecordShares_Department_Group").OnTable("RmsRecordShares")
					.OnColumn("DepartmentId").Ascending().OnColumn("DepartmentGroupId").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("RmsRecordShares").Exists())
				Delete.Table("RmsRecordShares");

			if (Schema.Table("RmsRecordGroupScopes").Exists())
				Delete.Table("RmsRecordGroupScopes");
		}
	}
}
