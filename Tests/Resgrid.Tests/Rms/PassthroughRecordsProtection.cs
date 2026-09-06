using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// The RMS protection seam for fixtures that test everything except Advanced Data Protection: writes pass
	/// through untouched, reads reveal nothing because nothing is sealed, and the workload lanes always open.
	/// The ADP behaviour itself is covered by RecordsProtectionServiceTests against the real service.
	/// </summary>
	public sealed class PassthroughRecordsProtection : IRecordsProtectionService
	{
		public bool Enforced { get; set; }
		public int CatalogVersion { get; set; }
		public bool AllowWorkload { get; set; } = true;
		public List<string> Writes { get; } = new List<string>();

		private Task Write(string what) { Writes.Add(what); return Task.CompletedTask; }
		private static Task<ProtectedReadResult> Empty() => Task.FromResult(new ProtectedReadResult());

		public Task<int> GetCatalogVersionAsync(int departmentId) => Task.FromResult(CatalogVersion);
		public Task<bool> IsEnforcedAsync(int departmentId) => Task.FromResult(Enforced);
		public Task ProtectDetailsAsync(int departmentId, RmsOperationalRecordDetail row, RmsOperationalRecordDetail existing, string userId = null, CancellationToken cancellationToken = default) => Write("details");
		public Task ProtectAttachmentAsync(int departmentId, RmsRecordAttachment row, RmsRecordAttachment existing, string userId = null, CancellationToken cancellationToken = default) => Write("attachment");
		public Task ProtectRevisionAsync(int departmentId, RmsRevision row, string userId = null, CancellationToken cancellationToken = default) => Write("revision");
		public Task ProtectLocationAsync(int departmentId, RmsLocation row, RmsLocation existing, string userId = null, CancellationToken cancellationToken = default) => Write("location");
		public Task ProtectNarrativeAsync(int departmentId, RmsNarrative row, RmsNarrative existing, string userId = null, CancellationToken cancellationToken = default) => Write("narrative");
		public Task ProtectSourceFactAsync(int departmentId, RmsSourceFact row, RmsSourceFact existing, string userId = null, CancellationToken cancellationToken = default) => Write("fact");
		public Task ProtectCasualtyAsync(int departmentId, RmsCasualtyRescue row, RmsCasualtyRescue existing, string userId = null, CancellationToken cancellationToken = default) => Write("casualty");
		public Task ProtectExposureAsync(int departmentId, RmsExposure row, RmsExposure existing, string userId = null, CancellationToken cancellationToken = default) => Write("exposure");
		public Task ProtectModuleAsync(int departmentId, RmsIncidentModule row, RmsIncidentModule existing, string userId = null, CancellationToken cancellationToken = default) => Write("module");
		public Task ProtectPropertyAsync(int departmentId, RmsIncidentProperty row, RmsIncidentProperty existing, string userId = null, CancellationToken cancellationToken = default) => Write("property");
		public Task ProtectVehicleAsync(int departmentId, RmsIncidentVehicle row, RmsIncidentVehicle existing, string userId = null, CancellationToken cancellationToken = default) => Write("vehicle");
		public Task ProtectResourceAsync(int departmentId, RmsIncidentResource row, RmsIncidentResource existing, string userId = null, CancellationToken cancellationToken = default) => Write("resource");
		public Task ProtectSubmissionAsync(int departmentId, RmsSubmission row, string userId = null, CancellationToken cancellationToken = default) => Write("submission");
		public Task ProtectSignatureAsync(int departmentId, RmsSignature row, string userId = null, CancellationToken cancellationToken = default) => Write("signature");
		public Task ProtectEvidenceAsync(int departmentId, RmsEvidenceArtifact row, string userId = null, CancellationToken cancellationToken = default) => Write("evidence");
		public Task ProtectDisclosureRequestAsync(int departmentId, RmsDisclosureRequest row, RmsDisclosureRequest existing, string userId = null, CancellationToken cancellationToken = default) => Write("disclosure-request");
		public Task ProtectDisclosureProductionAsync(int departmentId, RmsDisclosureProduction row, string userId = null, CancellationToken cancellationToken = default) => Write("disclosure-production");
		public Task ProtectLegalHoldAsync(int departmentId, RmsRecordLegalHold row, RmsRecordLegalHold existing, string userId = null, CancellationToken cancellationToken = default) => Write("legal-hold");
		public Task ProtectExportRunAsync(int departmentId, RmsExportRun row, string userId = null, CancellationToken cancellationToken = default) => Write("export-run");

		public Task<ProtectedReadResult> RevealAsync(int departmentId, RecordAggregate aggregate, CancellationToken cancellationToken = default) { var r = new ProtectedReadResult(); if (aggregate != null) aggregate.Protection = r; return Task.FromResult(r); }
		public Task<ProtectedReadResult> RevealAsync(int departmentId, IncidentReportAggregate aggregate, CancellationToken cancellationToken = default) { var r = new ProtectedReadResult(); if (aggregate != null) aggregate.Protection = r; return Task.FromResult(r); }
		public Task<ProtectedReadResult> RevealAsync(int departmentId, IncidentAnalysisAggregate aggregate, CancellationToken cancellationToken = default) { var r = new ProtectedReadResult(); if (aggregate != null) aggregate.Protection = r; return Task.FromResult(r); }
		public Task<ProtectedReadResult> RevealDetailsAsync(int departmentId, IReadOnlyList<RmsOperationalRecordDetail> rows, CancellationToken cancellationToken = default) => Empty();
		public Task<ProtectedReadResult> RevealRevisionsAsync(int departmentId, IReadOnlyList<RmsRevision> rows, CancellationToken cancellationToken = default) => Empty();
		public Task<ProtectedReadResult> RevealAttachmentsAsync(int departmentId, IReadOnlyList<RmsRecordAttachment> rows, bool includeData, CancellationToken cancellationToken = default) => Empty();
		public Task<ProtectedReadResult> RevealEvidenceAsync(int departmentId, IReadOnlyList<RmsEvidenceArtifact> rows, CancellationToken cancellationToken = default) => Empty();
		public Task<ProtectedReadResult> RevealDisclosureRequestsAsync(int departmentId, IReadOnlyList<RmsDisclosureRequest> rows, CancellationToken cancellationToken = default) => Empty();
		public Task<ProtectedReadResult> RevealDisclosureProductionsAsync(int departmentId, IReadOnlyList<RmsDisclosureProduction> rows, CancellationToken cancellationToken = default) => Empty();
		public Task<ProtectedReadResult> RevealLegalHoldsAsync(int departmentId, IReadOnlyList<RmsRecordLegalHold> rows, CancellationToken cancellationToken = default) => Empty();
		public Task<ProtectedReadResult> RevealSubmissionsAsync(int departmentId, IReadOnlyList<RmsSubmission> rows, CancellationToken cancellationToken = default) => Empty();
		public Task<ProtectedReadResult> RevealExportRunsAsync(int departmentId, IReadOnlyList<RmsExportRun> rows, bool includeData, CancellationToken cancellationToken = default) => Empty();
		public Task<bool> ResolveSubmissionForWorkloadAsync(int departmentId, RmsSubmission submission, string purpose, CancellationToken cancellationToken = default) => Task.FromResult(AllowWorkload);
		public Task<ProtectedReadResult> RevealForWorkloadAsync(int departmentId, RecordAggregate aggregate, string purpose, CancellationToken cancellationToken = default) => Empty();
		public Task<ProtectedReadResult> RevealForWorkloadAsync(int departmentId, IncidentReportAggregate aggregate, string purpose, CancellationToken cancellationToken = default) => Empty();
	}
}
