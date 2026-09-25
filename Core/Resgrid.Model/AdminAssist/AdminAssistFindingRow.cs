using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model.AdminAssist
{
	[Table("AdminAssistFindings")]
	public sealed class AdminAssistFindingRow : IEntity
	{
		[Key, DatabaseGenerated(DatabaseGeneratedOption.None)] public string AdminAssistFindingId { get; set; }
		public int DepartmentId { get; set; }
		public string RuleId { get; set; }
		public string SubjectId { get; set; }
		public int Episode { get; set; }
		public int Result { get; set; }
		public int Severity { get; set; }
		public int ReviewStatus { get; set; }
		public string OwnerId { get; set; }
		public DateTime? ReviewOn { get; set; }
		public DateTime? ExceptionUntil { get; set; }
		public string Content { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }
		public string SnapshotRevision { get; set; }
		public long Revision { get; set; }
		public DateTime FirstObservedOn { get; set; }
		public DateTime LastObservedOn { get; set; }
		[NotMapped] public object IdValue { get => AdminAssistFindingId; set => AdminAssistFindingId = value?.ToString(); }
		[NotMapped] public string TableName => "AdminAssistFindings";
		[NotMapped] public string IdName => nameof(AdminAssistFindingId);
		[NotMapped] public int IdType => 1;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { nameof(IdValue), nameof(TableName), nameof(IdName), nameof(IdType) };
	}
	public sealed record FindingReviewCommand(string FindingId, long ExpectedRevision, string Operation,
		string OwnerId = null, DateTime? ReviewOnUtc = null, DateTime? ExceptionUntilUtc = null, string Note = null);
	public interface IAdminAssistWorklistService
	{
		System.Threading.Tasks.Task<IReadOnlyList<AdminAssistFindingRow>> GetAsync(AdminAssistActor actor, System.Threading.CancellationToken cancellationToken = default);
		System.Threading.Tasks.Task VerifyAsync(AdminAssistActor actor, System.Threading.CancellationToken cancellationToken = default);
		System.Threading.Tasks.Task ReviewAsync(AdminAssistActor actor, FindingReviewCommand command, System.Threading.CancellationToken cancellationToken = default);
	}
}
