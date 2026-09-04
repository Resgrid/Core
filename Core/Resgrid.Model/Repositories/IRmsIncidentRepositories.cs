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

	public interface IRmsValidationIssuesRepository : IRepository<RmsValidationIssue>
	{
		Task<IEnumerable<RmsValidationIssue>> GetForRecordAsync(int departmentId, string recordId);
		/// <summary>Replaces every open issue of one source for the record (a validation run is a whole answer, not a delta).</summary>
		Task ReplaceForRecordAsync(int departmentId, string recordId, RmsValidationSource source, IEnumerable<RmsValidationIssue> issues, CancellationToken cancellationToken = default);
	}

	public interface IRmsSubmissionsRepository : IRepository<RmsSubmission>
	{
		Task<RmsSubmission> GetByIdForDepartmentAsync(int departmentId, string submissionId);
		Task<IEnumerable<RmsSubmission>> GetForRecordAsync(int departmentId, string recordId);
		Task<RmsSubmission> GetByIdempotencyKeyAsync(string idempotencyKey);
		/// <summary>Leases due Queued/AwaitingDestination submissions across departments for one worker sweep (plan 5.3: no transaction across the destination call).</summary>
		Task<IEnumerable<RmsSubmission>> ClaimDueBatchAsync(string leaseOwner, TimeSpan leaseDuration, int batchSize, DateTime utcNow, CancellationToken cancellationToken = default);
		Task<int> CountByStateAsync(int state);
		/// <summary>Marks every non-terminal submission of the record Superseded (a new revision issued a new idempotency key).</summary>
		Task<int> SupersedeOpenForRecordAsync(int departmentId, string recordId, string exceptSubmissionId, DateTime utcNow, CancellationToken cancellationToken = default);
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
