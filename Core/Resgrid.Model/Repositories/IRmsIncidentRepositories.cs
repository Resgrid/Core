using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Paging/filter for the incident report queue (Records Index for RecordKind = IncidentReport).</summary>
	public sealed class RmsIncidentReportQuery
	{
		public IList<int> States { get; set; }
		public int? Year { get; set; }
		public int? CallId { get; set; }
		public string OwnerUserId { get; set; }
		public int? StationGroupId { get; set; }

		/// <summary>
		/// When non-null, restricts to reports whose group scope intersects these ids or whose author, owner or
		/// reviewer is <see cref="ViewerUserId"/> — the same rule <c>CanUserViewRecordAsync</c> applies per record.
		/// Filtering here rather than after paging is what keeps <c>CountAsync</c> and the page links honest.
		/// </summary>
		public IList<int> VisibleGroupIds { get; set; }

		public string ViewerUserId { get; set; }
		public int Skip { get; set; }
		public int Take { get; set; } = 50;
	}

	public interface IRmsIncidentReportsRepository : IRepository<RmsIncidentReport>
	{
		Task<RmsIncidentReport> GetByIdForDepartmentAsync(int departmentId, string reportId);
		/// <summary>The department's own report for a Call (SingleAuthoritative, plan 5.2.1); null when none exists.</summary>
		Task<RmsIncidentReport> GetByCallAsync(int departmentId, int callId, string reportingEntityId);
		Task<IEnumerable<RmsIncidentReport>> GetByCallAnyEntityAsync(int departmentId, int callId);
		Task<RmsIncidentReport> GetByIdempotencyKeyAsync(int departmentId, string idempotencyKey);
		Task<RmsIncidentReport> GetByNerisIncidentIdAsync(int departmentId, string nerisIncidentId);
		Task<IEnumerable<RmsIncidentReport>> QueryAsync(int departmentId, RmsIncidentReportQuery query);
		Task<int> CountAsync(int departmentId, RmsIncidentReportQuery query);
		Task<IEnumerable<int>> GetYearsAsync(int departmentId);
		Task<int> GetMaxRecordNumberSequenceAsync(int departmentId, string numberPrefix);
		Task<bool> TryBumpRowVersionAsync(int departmentId, string reportId, long expectedRowVersion, CancellationToken cancellationToken = default);
		/// <summary>Retention candidates (RMS-3, worker 43): live, closed reports finalized before the cutoff, oldest first.</summary>
		Task<IEnumerable<RmsIncidentReport>> GetRetentionCandidatesAsync(int departmentId, DateTime cutoffUtc, int take, string afterId = null);
	}

	/// <summary>Shared contract of every per-report child row set: a working draft (RevisionId null) and immutable revision copies.</summary>
	public interface IRmsIncidentChildRepository<T> : IRepository<T> where T : class, IEntity
	{
		Task<IEnumerable<T>> GetForRecordAsync(int departmentId, string recordId, string revisionId);
		Task<int> DeleteDraftForRecordAsync(int departmentId, string recordId, CancellationToken cancellationToken = default);
	}

	public interface IRmsSourceFactsRepository : IRmsIncidentChildRepository<RmsSourceFact> { }
	public interface IRmsUnitResponsesRepository : IRmsIncidentChildRepository<RmsUnitResponse> { }
	public interface IRmsIncidentTypesRepository : IRmsIncidentChildRepository<RmsIncidentType> { }
	public interface IRmsActionTacticsRepository : IRmsIncidentChildRepository<RmsActionTactic> { }
	public interface IRmsAidsRepository : IRmsIncidentChildRepository<RmsAid> { }
	public interface IRmsLocationsRepository : IRmsIncidentChildRepository<RmsLocation> { }
	public interface IRmsNarrativesRepository : IRmsIncidentChildRepository<RmsNarrative> { }

	// RMS-3 conditional sections (registry M0167) and the restricted casualty/exposure classes (M0168). Each is
	// the same draft/revision child shape, so nothing about revisioning, export or print needs a special case.
	public interface IRmsIncidentModulesRepository : IRmsIncidentChildRepository<RmsIncidentModule>
	{
		/// <summary>The draft rows of one module kind, in ordinal order; the authoring surface edits a kind at a time.</summary>
		Task<IEnumerable<RmsIncidentModule>> GetForRecordByKindAsync(int departmentId, string recordId, string revisionId, RmsIncidentModuleKind kind);
	}

	public interface IRmsIncidentResourcesRepository : IRmsIncidentChildRepository<RmsIncidentResource> { }
	public interface IRmsCasualtyRescuesRepository : IRmsIncidentChildRepository<RmsCasualtyRescue> { }
	public interface IRmsExposuresRepository : IRmsIncidentChildRepository<RmsExposure> { }
	public interface IRmsIncidentPropertiesRepository : IRmsIncidentChildRepository<RmsIncidentProperty> { }
	public interface IRmsIncidentVehiclesRepository : IRmsIncidentChildRepository<RmsIncidentVehicle> { }

	public interface IRmsIncidentAnalysesRepository : IRepository<RmsIncidentAnalysis>
	{
		Task<bool> TryBumpRowVersionAsync(int departmentId, string analysisId, long expectedRowVersion, CancellationToken cancellationToken = default);
		Task<RmsIncidentAnalysis> GetByIdForDepartmentAsync(int departmentId, string analysisId);
		/// <summary>The analysis for an incident report; one per report, null when none has been started.</summary>
		Task<RmsIncidentAnalysis> GetForReportAsync(int departmentId, string incidentReportId);
		/// <summary>Analyses finalized but not yet filed because their incident had no NERIS id at the time.</summary>
		Task<IEnumerable<RmsIncidentAnalysis>> GetAwaitingIncidentAsync(int departmentId, int take);
		Task<int> CountByStateAsync(int departmentId, RmsIncidentAnalysisState state);
		Task<int> CountVisibleByStateAsync(int departmentId, RmsIncidentAnalysisState state, List<int> visibleGroupIds, string userId);
	}

	public interface IRmsValidationIssuesRepository : IRepository<RmsValidationIssue>
	{
		Task<IEnumerable<RmsValidationIssue>> GetForRecordAsync(int departmentId, string recordId);
		/// <summary>Replaces every open issue of one source for the record (a validation run is a whole answer, not a delta).</summary>
		Task ReplaceForRecordAsync(int departmentId, string recordId, RmsValidationSource source, IEnumerable<RmsValidationIssue> issues, CancellationToken cancellationToken = default);
	}

	public interface IRmsSubmissionsRepository : IRepository<RmsSubmission>
	{
		Task<bool> TryReconcileReceiptAsync(int departmentId, string submissionId, long expectedVersion, string externalId, string destinationIdentity, DateTime now, CancellationToken cancellationToken = default);
		Task<bool> TryBindUnsentAsync(int departmentId, string submissionId, long expectedVersion, string destinationIdentity, DateTime now, CancellationToken cancellationToken = default);
		Task<bool> TryConfirmNotCreatedAsync(int departmentId, string submissionId, long expectedVersion, string destinationIdentity, DateTime now, CancellationToken cancellationToken = default);
		/// <summary>Transaction-local completion/dispatch fence: locks and bumps only the current, unexpired lease.</summary>
		Task<bool> TryFenceLeaseAsync(int departmentId, string submissionId, long expectedVersion, string leaseOwner, DateTime now, CancellationToken cancellationToken = default);
		Task<RmsSubmission> GetByIdForDepartmentAsync(int departmentId, string submissionId);
		Task<IEnumerable<RmsSubmission>> GetForRecordAsync(int departmentId, string recordId);
		Task<RmsSubmission> GetByIdempotencyKeyAsync(string idempotencyKey);
		/// <summary>Leases due Queued/AwaitingDestination submissions across departments for one worker sweep (plan 5.3: no transaction across the destination call).</summary>
		Task<IEnumerable<RmsSubmission>> ClaimDueBatchAsync(string leaseOwner, TimeSpan leaseDuration, int batchSize, DateTime utcNow, CancellationToken cancellationToken = default);
		/// <summary>Queue depth for one department; the settings screen must never show another department's rows.</summary>
		Task<int> CountByStateAsync(int departmentId, int state);
		/// <summary>Marks every non-terminal submission of the record Superseded (a new revision issued a new idempotency key).</summary>
		Task<int> SupersedeOpenForRecordAsync(int departmentId, string recordId, string exceptSubmissionId, DateTime utcNow, CancellationToken cancellationToken = default);
	}

	public interface IRmsSubmissionExchangesRepository : IRepository<RmsSubmissionExchange>
	{
		Task<IEnumerable<RmsSubmissionExchange>> GetForSubmissionAsync(int departmentId, string submissionId);
	}

	public interface IRmsSignaturesRepository : IRepository<RmsSignature>
	{
		Task<IEnumerable<RmsSignature>> GetForRecordAsync(int departmentId, string recordId);
		Task<RmsSignature> GetForRevisionAsync(int departmentId, string revisionId, RmsSignatureIntent intent);
	}

	public interface IRmsNerisProfilesRepository : IRepository<RmsNerisProfile>
	{
		Task<RmsNerisProfile> GetByDepartmentIdAsync(int departmentId);
	}

	public interface IRmsNerisValueSetsRepository : IRepository<RmsNerisValueSetEntry>
	{
		Task<IEnumerable<RmsNerisValueSetEntry>> GetSetAsync(string contractVersion, string setKey);
		Task<int> CountForVersionAsync(string contractVersion);
		Task<bool> ExistsAsync(string contractVersion, string setKey, string code);
	}

	public interface IRmsNerisCrosswalksRepository : IRepository<RmsNerisCrosswalk>
	{
		Task<IEnumerable<RmsNerisCrosswalk>> GetForDepartmentAsync(int departmentId, string contractVersion);
		Task<RmsNerisCrosswalk> GetAsync(int departmentId, string contractVersion, string setKey, string localSource, string localCode);
	}
}
