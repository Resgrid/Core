using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// A department QA rubric (RMS plan section 4.7, post-finalization quality review, RMS-4 optional module): the
	/// criteria a reviewer scores a finalized Record against. Criteria are bounded JSON
	/// (<see cref="RmsQualityCriterion"/> list). Editing a rubric never changes a recorded review — each review
	/// snapshots the criteria it was scored against.
	/// </summary>
	public class RmsQualityRubric : IEntity
	{
		public string RmsQualityRubricId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string Name { get; set; }
		/// <summary>Definition key this rubric applies to; null = any definition.</summary>
		public string DefinitionKey { get; set; }
		public string CriteriaJson { get; set; }
		/// <summary>Sample size per sampling run.</summary>
		public int SampleSize { get; set; }
		public bool IsActive { get; set; }
		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public DateTime? DeletedOn { get; set; }

		public object IdValue { get => RmsQualityRubricId; set => RmsQualityRubricId = (string)value; }
		public string TableName => "RmsQualityRubrics";
		public string IdName => "RmsQualityRubricId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	public class RmsQualityCriterion
	{
		public string Key { get; set; }
		public string Text { get; set; }
		/// <summary>Relative weight; scores are weighted averages on a 0–100 scale.</summary>
		public int Weight { get; set; } = 1;
	}

	/// <summary>The reviewer's score for one criterion (stored in RmsQualityReview.FindingsJson).</summary>
	public class RmsQualityFinding
	{
		public string Key { get; set; }
		/// <summary>0–100.</summary>
		public int Score { get; set; }
		public string Note { get; set; }
	}

	/// <summary>
	/// One QA review of one finalized revision. Non-mutating by construction: it references the record and revision,
	/// never edits either, and a substantive correction is an amendment through the normal lifecycle.
	/// </summary>
	public class RmsQualityReview : IEntity
	{
		public string RmsQualityReviewId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RmsQualityRubricId { get; set; }
		public string RecordId { get; set; }
		/// <summary><see cref="RmsRecordKind"/>.</summary>
		public int RecordKind { get; set; }
		public string RevisionId { get; set; }
		public string DefinitionKey { get; set; }
		public string RecordNumber { get; set; }
		public string AuthorUserId { get; set; }
		public int? UnitId { get; set; }
		public string ReviewerUserId { get; set; }
		public DateTime SampledOn { get; set; }
		public DateTime? ScoredOn { get; set; }
		/// <summary>Weighted 0–100; null until scored.</summary>
		public int? Score { get; set; }
		/// <summary>Snapshot of the rubric criteria at sampling time.</summary>
		public string CriteriaJson { get; set; }
		public string FindingsJson { get; set; }
		public string Note { get; set; }
		public bool AmendmentRecommended { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		public object IdValue { get => RmsQualityReviewId; set => RmsQualityReviewId = (string)value; }
		public string TableName => "RmsQualityReviews";
		public string IdName => "RmsQualityReviewId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Aggregate line of the QA trend report.</summary>
	public class RecordsQualityTrendRow
	{
		public string Key { get; set; }
		public string Label { get; set; }
		public int Reviews { get; set; }
		public double AverageScore { get; set; }
		public int AmendmentsRecommended { get; set; }
	}

	public class RecordsQualityTrends
	{
		public DateTime Since { get; set; }
		public int Sampled { get; set; }
		public int Scored { get; set; }
		public double AverageScore { get; set; }
		public List<RecordsQualityTrendRow> ByAuthor { get; set; } = new List<RecordsQualityTrendRow>();
		public List<RecordsQualityTrendRow> ByUnit { get; set; } = new List<RecordsQualityTrendRow>();
		public List<RecordsQualityTrendRow> ByDefinition { get; set; } = new List<RecordsQualityTrendRow>();
		public List<RecordsQualityTrendRow> ByCriterion { get; set; } = new List<RecordsQualityTrendRow>();
	}
}
