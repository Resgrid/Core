using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Protocols P5 QA (protocols-training-knowledge-plan §5 P5 "QA: dispatch case review"): DispatchCaseReviews — one review of one call
	/// protocol's recorded case (P4), scored against a Records QA rubric (RmsQualityRubrics, DefinitionKey "protocol:dispatch-case"; the rubric's
	/// criteria are snapshotted on the row), with the questions and instructions scored one by one (ItemsJson), the per-kind compliance counts,
	/// the officers' queue assignment, the send to the dispatcher, their one acknowledgment and one reply. One review per call protocol
	/// (UX_DispatchCaseReviews_CallProtocol). DepartmentId and no foreign keys: UnlinkedDepartmentCleanup and ProtocolDepartmentCleanup remove
	/// the rows. Existence-guarded, so a retry is safe.
	/// </summary>
	[Migration(391)]
	public class M0391_AddDispatchCaseReviews : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("DispatchCaseReviews").Exists())
			{
				Create.Table("DispatchCaseReviews")
					.WithColumn("DispatchCaseReviewId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("CallProtocolId").AsInt32().NotNullable()
					.WithColumn("DispatchProtocolId").AsInt32().NotNullable()
					.WithColumn("ProtocolVersionId").AsInt32().Nullable()
					.WithColumn("Determinant").AsString(32).Nullable()
					.WithColumn("Overridden").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("DispatcherUserId").AsString(128).NotNullable()
					.WithColumn("CallLoggedOn").AsDateTime2().NotNullable()
					.WithColumn("CallNumber").AsString(50).Nullable()
					.WithColumn("CallName").AsString(200).Nullable()
					.WithColumn("ProtocolCode").AsString(50).Nullable()
					.WithColumn("ProtocolName").AsString(200).Nullable()
					.WithColumn("VersionLabel").AsString(50).Nullable()
					.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RmsQualityRubricId").AsString(128).Nullable()
					.WithColumn("RubricName").AsString(200).Nullable()
					.WithColumn("CriteriaJson").AsString(int.MaxValue).Nullable()
					.WithColumn("FindingsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("ItemsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("Score").AsInt32().Nullable()
					.WithColumn("QuestionCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("QuestionPoints").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("InstructionCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("InstructionPoints").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AssignedToUserId").AsString(128).Nullable()
					.WithColumn("SampledByUserId").AsString(128).Nullable()
					.WithColumn("SampledOn").AsDateTime2().NotNullable()
					.WithColumn("ReviewerUserId").AsString(128).Nullable()
					.WithColumn("ReviewerComments").AsString(4000).Nullable()
					.WithColumn("SentOn").AsDateTime2().Nullable()
					.WithColumn("AcknowledgedOn").AsDateTime2().Nullable()
					.WithColumn("DispatcherComment").AsString(2000).Nullable()
					.WithColumn("DispatcherCommentOn").AsDateTime2().Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(0);
			}

			if (!Schema.Table("DispatchCaseReviews").Index("UX_DispatchCaseReviews_CallProtocol").Exists())
				Create.Index("UX_DispatchCaseReviews_CallProtocol").OnTable("DispatchCaseReviews")
					.OnColumn("DepartmentId").Ascending().OnColumn("CallProtocolId").Ascending()
					.WithOptions().Unique();

			if (!Schema.Table("DispatchCaseReviews").Index("IX_DispatchCaseReviews_Status").Exists())
				Create.Index("IX_DispatchCaseReviews_Status").OnTable("DispatchCaseReviews")
					.OnColumn("DepartmentId").Ascending().OnColumn("Status").Ascending();

			if (!Schema.Table("DispatchCaseReviews").Index("IX_DispatchCaseReviews_Dispatcher").Exists())
				Create.Index("IX_DispatchCaseReviews_Dispatcher").OnTable("DispatchCaseReviews")
					.OnColumn("DepartmentId").Ascending().OnColumn("DispatcherUserId").Ascending().OnColumn("Status").Ascending();

			if (!Schema.Table("DispatchCaseReviews").Index("IX_DispatchCaseReviews_CallLoggedOn").Exists())
				Create.Index("IX_DispatchCaseReviews_CallLoggedOn").OnTable("DispatchCaseReviews")
					.OnColumn("DepartmentId").Ascending().OnColumn("CallLoggedOn").Ascending();
		}

		public override void Down()
		{
			if (Schema.Table("DispatchCaseReviews").Exists())
				Delete.Table("DispatchCaseReviews");
		}
	}
}
