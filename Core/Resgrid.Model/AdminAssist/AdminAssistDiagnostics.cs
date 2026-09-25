using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.AdminAssist
{
	public sealed record DiagnosticRequest(string Flow, DateTime FromUtc, DateTime UntilUtc,
		int? CallId = null, string MemberId = null, int? UnitId = null, int? RoleId = null, int? GroupId = null,
		string Permission = null, string CapabilityId = null);
	public sealed record DiagnosticCheck(string Id, string Outcome, string Basis, string ExplanationKey,
		string Source, string Version, DateTime AsOfUtc, decimal? Value = null, string Destination = null);
	public sealed record DiagnosticChange(string Id, DateTime OccurredOnUtc, string SettingId, long Revision,
		string BeforeCode, string AfterCode, string CorrelationId);
	public sealed record DiagnosticTraceEvent(string Id, DateTime OccurredOnUtc, int Sequence, string Stage,
		string Channel, string Reason, string LogicalMessageId);
	public sealed record DiagnosticTraceAttempt(string Id, string ResolverVersion, int Observed, int Missing,
		int Dropped, bool Complete, IReadOnlyList<DiagnosticTraceEvent> Events);
	public sealed record DiagnosticReport(string RunId, long Revision, string Flow, DateTime CreatedOnUtc,
		DateTime CheckedOnUtc, string CatalogVersion, string ConfigurationRevision, string Outcome,
		IReadOnlyList<DiagnosticCheck> Checks, IReadOnlyList<DiagnosticChange> Changes,
		IReadOnlyList<DiagnosticTraceAttempt> Attempts, bool TraceTruncated, bool TimelineTruncated)
	{
		public IReadOnlyList<DiagnosticStatusHeader> StatusHistory { get; init; } = Array.Empty<DiagnosticStatusHeader>();
	}
	public sealed record DiagnosticRunSummary(string Id, string Flow, DateTime CreatedOnUtc, long Revision);
	public sealed record DiagnosticRunCommand(string RunId, long ExpectedRevision = 1, string PreviewDigest = null);
	// Deliberately excludes request/subject ids, names, narrative, destinations, contact data and provider identifiers.
	public sealed record DiagnosticSupportBundle(string RunId, string Flow, DateTime CheckedOnUtc, string CatalogVersion,
		string ConfigurationRevision, string Outcome, IReadOnlyList<DiagnosticCheck> Checks,
		IReadOnlyList<DiagnosticChange> Changes, IReadOnlyList<DiagnosticTraceAttempt> Attempts,
		bool TraceTruncated, bool TimelineTruncated, string PreviewDigest);
	public sealed record DiagnosticSourceResult(IReadOnlyList<DiagnosticCheck> Checks,
		IReadOnlyList<DiagnosticTraceAttempt> Attempts, bool TraceTruncated = false)
	{
		public IReadOnlyList<DiagnosticStatusHeader> StatusHistory { get; init; } = Array.Empty<DiagnosticStatusHeader>();
	}
	public interface IAdminAssistDiagnostics
	{
		Task<DiagnosticReport> RunAsync(AdminAssistActor actor, DiagnosticRequest request, CancellationToken ct);
		Task<DiagnosticReport> ReadAsync(AdminAssistActor actor, DiagnosticRunCommand command, CancellationToken ct);
		Task<IReadOnlyList<DiagnosticRunSummary>> ListAsync(AdminAssistActor actor, CancellationToken ct);
		Task<DiagnosticSupportBundle> PreviewSupportAsync(AdminAssistActor actor, DiagnosticRunCommand command, CancellationToken ct);
		Task<DiagnosticSupportBundle> ExportSupportAsync(AdminAssistActor actor, DiagnosticRunCommand command, CancellationToken ct);
		Task DeleteAsync(AdminAssistActor actor, DiagnosticRunCommand command, CancellationToken ct);
	}
	public interface IAdminAssistDiagnosticSource
	{
		Task RequireScopeAsync(AdminAssistActor actor, DiagnosticRequest request, CancellationToken ct);
		Task<DiagnosticSourceResult> ReadAsync(AdminAssistActor actor, DiagnosticRequest request, DateTime now, CancellationToken ct);
	}
	public interface IAdminAssistDiagnosticProtection
	{
		Task RequireAsync(AdminAssistActor actor, CancellationToken ct);
		Task ProtectAsync(AdminAssistActor actor, AdminAssistDiagnosticRun row, DiagnosticRequest request, CancellationToken ct);
		Task<DiagnosticRequest> ReadAsync(AdminAssistActor actor, AdminAssistDiagnosticRun row, CancellationToken ct);
	}
	public interface IAdminAssistDiagnosticStore
	{
		Task<bool> AcquireDiagnosticLeaseAsync(AdminAssistActor actor, string id, DateTime now, CancellationToken ct);
		Task ReleaseDiagnosticLeaseAsync(AdminAssistActor actor, string id, CancellationToken ct);
		Task SaveDiagnosticAsync(AdminAssistActor actor, AdminAssistDiagnosticRun row, CancellationToken ct);
		Task<AdminAssistDiagnosticRun> ReadDiagnosticAsync(AdminAssistActor actor, string id, CancellationToken ct);
		Task<IReadOnlyList<DiagnosticRunSummary>> ListDiagnosticsAsync(AdminAssistActor actor, CancellationToken ct);
		Task DeleteDiagnosticAsync(AdminAssistActor actor, DiagnosticRunCommand command, CancellationToken ct);
		Task<IReadOnlyList<AdminAssistDispatchTraceRow>> ReadDiagnosticTracesAsync(int departmentId, int callId, DateTime from, DateTime until, int bound, CancellationToken ct);
		Task<IReadOnlyList<DiagnosticChange>> ReadDiagnosticChangesAsync(int departmentId, DateTime from, DateTime until, int bound, CancellationToken ct);
		Task<IReadOnlyList<DiagnosticStatusHeader>> ReadDiagnosticStatusesAsync(int departmentId, string memberId, int? unitId, DateTime from, DateTime until, CancellationToken ct);
	}
	public sealed record DiagnosticStatusHeader(DateTime Timestamp, int Status, string Source);
	[Table("AdminAssistDiagnosticRuns")]
	public sealed class AdminAssistDiagnosticRun : IEntity
	{
		[Key, DatabaseGenerated(DatabaseGeneratedOption.None)] public string Id { get; set; }
		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public string Flow { get; set; }
		public DateTime CreatedOnUtc { get; set; }
		public long Revision { get; set; }
		public bool Deleted { get; set; }
		public string Content { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }
		[NotMapped] public string TableName => "AdminAssistDiagnosticRuns";
		[NotMapped] public string IdName => nameof(Id);
		[NotMapped] public object IdValue { get => Id; set => Id = value?.ToString(); }
		[NotMapped] public int IdType => 1;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { nameof(IdValue), nameof(TableName), nameof(IdName), nameof(IdType), nameof(IgnoredProperties) };
	}
}
