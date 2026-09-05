using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Public-records and disclosure workflow (RMS plan section 4.7, registry M0171, package RMS-3):
	/// RmsDisclosureRequests and RmsDisclosureProductions.
	/// <para>
	/// Section 5.8 requires public-records export and redaction as a control; for a public agency it is a
	/// statutory obligation with a clock, which is why the request is a record with a due date rather than an
	/// ad-hoc export. A production is a new immutable artifact — never a mutation of the source revisions — and
	/// it freezes the produced set so a later amendment cannot silently change what was released.
	/// </para>
	/// Existence-guarded for safe retry.
	/// </summary>
	[Migration(171)]
	public class M0171_AddRmsDisclosures : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsDisclosureRequests").Exists())
			{
				Create.Table("RmsDisclosureRequests")
					.WithColumn("RmsDisclosureRequestId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RequestNumber").AsString(50).Nullable()
					.WithColumn("RequesterName").AsString(255).Nullable()
					.WithColumn("RequesterOrganization").AsString(255).Nullable()
					.WithColumn("RequesterContact").AsString(500).Nullable()
					.WithColumn("ReceivedOn").AsDateTime2().NotNullable()
					.WithColumn("StatutoryDueOn").AsDateTime2().Nullable()
					.WithColumn("JurisdictionProfile").AsString(20).Nullable()
					.WithColumn("ScopeNarrative").AsString(int.MaxValue).Nullable()
					.WithColumn("ScopeQueryJson").AsString(int.MaxValue).Nullable()
					.WithColumn("State").AsInt32().NotNullable()
					.WithColumn("AssignedToUserId").AsString(128).Nullable()
					.WithColumn("RedactionProfile").AsString(50).Nullable()
					.WithColumn("ClosedOn").AsDateTime2().Nullable()
					.WithColumn("ClosedByUserId").AsString(128).Nullable()
					.WithColumn("DispositionReason").AsString(int.MaxValue).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedByUserId").AsString(128).Nullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsDisclosureRequests_Department_State").OnTable("RmsDisclosureRequests")
					.OnColumn("DepartmentId").Ascending().OnColumn("State").Ascending();
				// The statutory clock is the thing an officer opens this screen to check.
				Create.Index("IX_RmsDisclosureRequests_Department_Due").OnTable("RmsDisclosureRequests")
					.OnColumn("DepartmentId").Ascending().OnColumn("StatutoryDueOn").Ascending();
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsDisclosureRequests_Number ON RmsDisclosureRequests (DepartmentId, RequestNumber) WHERE RequestNumber IS NOT NULL AND DeletedOn IS NULL;");
			}

			if (!Schema.Table("RmsDisclosureProductions").Exists())
			{
				Create.Table("RmsDisclosureProductions")
					.WithColumn("RmsDisclosureProductionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("DisclosureRequestId").AsString(36).NotNullable()
					.WithColumn("ProductionNumber").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("RedactionProfile").AsString(50).Nullable()
					.WithColumn("ProducedSetJson").AsString(int.MaxValue).Nullable()
					.WithColumn("ArtifactJson").AsString(int.MaxValue).Nullable()
					.WithColumn("Checksum").AsString(80).Nullable()
					.WithColumn("ByteSize").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("RecordCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("WithheldFieldsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("WithheldFieldCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("PreparedByUserId").AsString(128).Nullable()
					.WithColumn("PreparedOn").AsDateTime2().NotNullable()
					.WithColumn("ReleasedByUserId").AsString(128).Nullable()
					.WithColumn("ReleasedOn").AsDateTime2().Nullable()
					.WithColumn("ProtectedEnvelope").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				Create.Index("IX_RmsDisclosureProductions_Department_Request").OnTable("RmsDisclosureProductions")
					.OnColumn("DepartmentId").Ascending().OnColumn("DisclosureRequestId").Ascending();
			}
		}

		public override void Down()
		{
			foreach (var table in new[] { "RmsDisclosureProductions", "RmsDisclosureRequests" })
			{
				if (Schema.Table(table).Exists())
					Delete.Table(table);
			}
		}
	}
}
