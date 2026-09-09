using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Checklists
{
	public sealed class ChecklistReportQuery
	{
		public DateTime FromUtc { get; set; }
		public DateTime UntilUtc { get; set; }
		public ChecklistTargetType? TargetType { get; set; }
		public string TargetId { get; set; }
	}
	/// <summary>Authorized snapshot; never includes answer text, locations, signatures or file bodies.</summary>
	public sealed class ChecklistReportEntry
	{
		public string DefinitionId { get; set; }
		public string VersionId { get; set; }
		public int Version { get; set; }
		public string OccurrenceId { get; set; }
		public int OccurrenceRevision { get; set; }
		public string CompletionId { get; set; }
		public int? CompletionRevision { get; set; }
		public string Name { get; set; }
		public ChecklistTarget Target { get; set; }
		public DateTime StartUtc { get; set; }
		public DateTime? DueUtc { get; set; }
		public DateTime? SubmittedUtc { get; set; }
		public bool Scheduled { get; set; }
		public bool Expected { get; set; }
		public bool Completed { get; set; }
		public bool OnTime { get; set; }
		public bool Missed { get; set; }
		public bool Skipped { get; set; }
		public bool? Passed { get; set; }
		public decimal? Score { get; set; }
	}
	public sealed class ChecklistComplianceGroup
	{
		public ChecklistTarget Target { get; set; }
		public int Expected { get; set; }
		public int Completed { get; set; }
		public int OnTime { get; set; }
		public int Missed { get; set; }
		public int Skipped { get; set; }
		public decimal? CompletionRate => Expected == 0 ? null : Math.Round(100m * Completed / Expected, 2);
	}
	public sealed class ChecklistMissedTrend { public DateTime DayUtc { get; set; } public int Expected { get; set; } public int Missed { get; set; } }
	public sealed class ChecklistComplianceSummary
	{
		public ChecklistTargetType? TargetType { get; set; }
		public string TargetId { get; set; }
		public DateTime FromUtc { get; set; }
		public DateTime UntilUtc { get; set; }
		public DateTime AsOfUtc { get; set; }
		public bool IsRedacted { get; set; }
		public List<ChecklistComplianceGroup> Groups { get; set; } = new();
		public List<ChecklistMissedTrend> Trend { get; set; } = new();
		public List<ChecklistReportEntry> Entries { get; set; } = new();
		public List<string> UnavailableSources { get; set; } = new();
	}
	public sealed class ReadinessUnitSnapshot
	{
		public int UnitId { get; set; }
		public string Name { get; set; }
		public int DispatchId { get; set; }
		public DateTime DispatchedUtc { get; set; }
	}
	public sealed class ReadinessAssetSnapshot
	{
		public int DepartmentId { get; set; }
		public string AssetId { get; set; }
		public int? UnitId { get; set; }
		public int? CallId { get; set; }
		public string SourceSubsystem { get; set; }
		public string Name { get; set; }
		public string SourceId { get; set; }
		public string SourceVersion { get; set; }
		public DateTime IssuedUtc { get; set; }
		public DateTime? ReturnedUtc { get; set; }
	}
	/// <summary>Optional historical inventory/deployment seam. Implementations authorize every asset with
	/// the supplied actor/grant, return immutable issue/deployment provenance, and never infer history from
	/// current assignments. A null result means unavailable; an empty list means available with no assets.</summary>
	public interface IChecklistHistoricalAssetSource
	{
		Task<List<ReadinessAssetSnapshot>> AtCallAsync(ChecklistActor actor, int callId, DateTime callUtc, IReadOnlyCollection<int> unitIds, bool contractorEquipment);
	}
	public sealed class ReadinessEvidenceManifestV1
	{
		public string Schema => "ReadinessEvidenceManifestV1";
		public string GeneratorVersion => "checklists-p1m4/1";
		public string ProfileVersion => "checklists-only/1";
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public DateTime CallUtc { get; set; }
		public DateTime GeneratedUtc { get; set; }
		public DateTime CoverageStartUtc { get; set; }
		public DateTime CoverageEndUtc { get; set; }
		public string Classification => "Restricted";
		public List<ReadinessUnitSnapshot> Units { get; set; } = new();
		public List<ReadinessAssetSnapshot> Assets { get; set; } = new();
		public List<ChecklistReportEntry> Checklists { get; set; } = new();
		public List<string> UnavailableSources { get; set; } = new();
	}
	/// <summary>Checksum the exact UTF-8 ManifestJson bytes and decoded PDF bytes, not this wrapper.</summary>
	public sealed class ReadinessEvidencePackage
	{
		public string ManifestJson { get; set; }
		public string ManifestSha256 { get; set; }
		public byte[] Pdf { get; set; }
		public string PdfSha256 { get; set; }
	}
}
