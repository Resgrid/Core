using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(191)]
	public class M0191_AddChecklistWorkflow : Migration
	{
		private static string N(string value) => value;
		public override void Up()
		{
			foreach (var name in new[] { "ChecklistDefinitions", "ChecklistDefinitionVersions", "ChecklistOccurrences", "ChecklistCompletions", "ChecklistCompletionItems", "ChecklistCompletionFiles", "DepartmentChecklistSettings" })
			{
				if (Schema.Table(N(name)).Exists()) continue;
				var table = Create.Table(N(name))
					.WithColumn(N("Id")).AsString(36).PrimaryKey()
					.WithColumn(N("DepartmentId")).AsInt32().NotNullable()
					.WithColumn(N("ParentId")).AsString(36).Nullable()
					.WithColumn(N("Content")).AsString(int.MaxValue).Nullable()
					.WithColumn(N("Revision")).AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn(N("CreatedOn")).AsDateTime2().NotNullable()
					.WithColumn(N("UpdatedOn")).AsDateTime2().NotNullable()
					.WithColumn(N("CreatedBy")).AsString(128).NotNullable()
					.WithColumn(N("IsProtected")).AsBoolean().NotNullable().WithDefaultValue(false);
				switch (name)
				{
					case "ChecklistDefinitions":
						table.WithColumn(N("CurrentVersionId")).AsString(36).Nullable()
							.WithColumn(N("PublishedVersion")).AsInt32().NotNullable().WithDefaultValue(0)
							.WithColumn(N("Retired")).AsBoolean().NotNullable().WithDefaultValue(false)
							.WithColumn(N("DeletedOn")).AsDateTime2().Nullable(); break;
					case "ChecklistDefinitionVersions": table.WithColumn(N("Version")).AsInt32().NotNullable(); break;
					case "ChecklistOccurrences":
						table.WithColumn(N("VersionId")).AsString(36).NotNullable()
							.WithColumn(N("CompletionId")).AsString(36).NotNullable()
							.WithColumn(N("TargetType")).AsInt32().NotNullable()
							.WithColumn(N("TargetId")).AsString(128).NotNullable()
							.WithColumn(N("State")).AsInt32().NotNullable(); break;
					case "ChecklistCompletions":
						table.WithColumn(N("VersionId")).AsString(36).NotNullable()
							.WithColumn(N("OccurrenceId")).AsString(36).NotNullable()
							.WithColumn(N("TargetType")).AsInt32().NotNullable()
							.WithColumn(N("TargetId")).AsString(128).NotNullable()
							.WithColumn(N("State")).AsInt32().NotNullable()
							.WithColumn(N("SubmittedOn")).AsDateTime2().Nullable()
							.WithColumn(N("WitnessUserId")).AsString(128).Nullable()
							.WithColumn(N("TargetGroupId")).AsInt32().Nullable()
							.WithColumn(N("WitnessedOn")).AsDateTime2().Nullable()
							.WithColumn(N("Score")).AsDecimal(7,2).Nullable()
							.WithColumn(N("Passed")).AsBoolean().NotNullable()
							.WithColumn(N("SubmissionHash")).AsString(64).Nullable(); break;
					case "ChecklistCompletionItems": table.WithColumn(N("ItemId")).AsString(36).NotNullable().WithColumn(N("IsFailure")).AsBoolean().NotNullable(); break;
					case "ChecklistCompletionFiles":
						table.WithColumn(N("ItemId")).AsString(36).NotNullable()
							.WithColumn(N("ContentType")).AsString(100).NotNullable()
							.WithColumn(N("Size")).AsInt32().NotNullable()
							.WithColumn(N("Sha256")).AsString(64).NotNullable()
							.WithColumn(N("Data")).AsBinary(int.MaxValue).NotNullable()
							.WithColumn(N("ScanState")).AsInt32().NotNullable(); break;
				}
				Create.Index(N("UX_" + name + "_TenantId")).OnTable(N(name)).OnColumn(N("DepartmentId")).Ascending().OnColumn(N("Id")).Ascending().WithOptions().Unique();
				Create.Index(N("IX_" + name + "_ParentDate")).OnTable(N(name)).OnColumn(N("DepartmentId")).Ascending().OnColumn(N("ParentId")).Ascending().OnColumn(N("CreatedOn")).Descending();
			}
			Unique("ChecklistDefinitionVersions", "Version", "ParentId", "Version");
			Unique("ChecklistCompletions", "Occurrence", "OccurrenceId");
			Unique("ChecklistOccurrences", "Completion", "CompletionId");
			Unique("ChecklistCompletionItems", "Item", "ParentId", "ItemId");
			Unique("DepartmentChecklistSettings", "Department");
			foreach (var child in new[] { "ChecklistDefinitionVersions", "ChecklistOccurrences", "ChecklistCompletions" }) Foreign(child, "ParentId", "ChecklistDefinitions");
			foreach (var child in new[] { "ChecklistOccurrences", "ChecklistCompletions" }) Foreign(child, "VersionId", "ChecklistDefinitionVersions");
			Foreign("ChecklistCompletions", "OccurrenceId", "ChecklistOccurrences");
			Foreign("ChecklistCompletionItems", "ParentId", "ChecklistCompletions");
			Foreign("ChecklistCompletionFiles", "ParentId", "ChecklistCompletions");
		}
		private void Unique(string table, string suffix, params string[] columns)
		{
			var name = N("UX_" + table + "_" + suffix);
			if (Schema.Table(N(table)).Index(name).Exists()) return;
			var index = Create.Index(name).OnTable(N(table)).OnColumn(N("DepartmentId")).Ascending();
			foreach (var column in columns) index = index.OnColumn(N(column)).Ascending();
			index.WithOptions().Unique();
		}
		private void Foreign(string child, string column, string parent)
		{
			var name = N("FK_" + child + "_" + column);
			if (!Schema.Table(N(child)).Constraint(name).Exists())
				Create.ForeignKey(name).FromTable(N(child)).ForeignColumns(N("DepartmentId"), N(column)).ToTable(N(parent)).PrimaryColumns(N("DepartmentId"), N("Id"));
		}
		public override void Down()
		{
			foreach (var name in new[] { "DepartmentChecklistSettings", "ChecklistCompletionFiles", "ChecklistCompletionItems", "ChecklistCompletions", "ChecklistOccurrences", "ChecklistDefinitionVersions", "ChecklistDefinitions" })
				if (Schema.Table(N(name)).Exists()) Delete.Table(N(name));
		}
	}
}
