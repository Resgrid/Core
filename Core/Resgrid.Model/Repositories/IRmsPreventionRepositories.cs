using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Paged, filtered occupancy listing.</summary>
	public sealed class RmsOccupancyQuery
	{
		public string Search { get; set; }
		public int? Status { get; set; }
		public int? OccupancyType { get; set; }
		public bool? ReviewOverdue { get; set; }
		public bool? HazmatOnSite { get; set; }
		public int Skip { get; set; }
		public int Take { get; set; } = 50;
	}

	public interface IRmsOccupanciesRepository : IRepository<RmsOccupancy>
	{
		Task<RmsOccupancy> GetByIdForDepartmentAsync(int departmentId, string occupancyId);
		Task<IEnumerable<RmsOccupancy>> GetByIdsAsync(int departmentId, IEnumerable<string> occupancyIds);
		Task<IEnumerable<RmsOccupancy>> QueryAsync(int departmentId, RmsOccupancyQuery query);
		Task<int> CountAsync(int departmentId, RmsOccupancyQuery query);
		Task<IEnumerable<RmsOccupancy>> GetByNormalizedAddressAsync(int departmentId, string normalizedAddress);
		Task<IEnumerable<RmsOccupancy>> GetAllLiveAsync(int departmentId);
		Task<int> CountLiveAsync(int departmentId);
		Task<int> CountReviewOverdueAsync(int departmentId, DateTime utcNow);
		Task<bool> TryBumpRowVersionAsync(int departmentId, string occupancyId, long expectedVersion, CancellationToken cancellationToken = default);
	}

	public interface IRmsOccupancyContactLinksRepository : IRepository<RmsOccupancyContactLink>
	{
		Task<IEnumerable<RmsOccupancyContactLink>> GetForOccupancyAsync(int departmentId, string occupancyId);
		Task<IEnumerable<RmsOccupancyContactLink>> GetForContactAsync(int departmentId, string contactId);
		Task<IEnumerable<RmsOccupancyContactLink>> GetForContactsAsync(int departmentId, IEnumerable<string> contactIds);
		Task<RmsOccupancyContactLink> GetByIdForDepartmentAsync(int departmentId, string linkId);
	}

	public interface IRmsOccupancyHazardsRepository : IRepository<RmsOccupancyHazard>
	{
		Task<IEnumerable<RmsOccupancyHazard>> GetForOccupancyAsync(int departmentId, string occupancyId);
		Task<IEnumerable<RmsOccupancyHazard>> GetForOccupanciesAsync(int departmentId, IEnumerable<string> occupancyIds);
		Task<RmsOccupancyHazard> GetByIdForDepartmentAsync(int departmentId, string hazardId);
	}

	public interface IRmsOccupancyCrosswalksRepository : IRepository<RmsOccupancyCrosswalk>
	{
		Task<RmsOccupancyCrosswalk> GetByIdForDepartmentAsync(int departmentId, string crosswalkId);
		Task<RmsOccupancyCrosswalk> GetBySourceAsync(int departmentId, RmsOccupancyCrosswalkSourceKind sourceKind, string sourceId);
		Task<IEnumerable<RmsOccupancyCrosswalk>> GetByStateAsync(int departmentId, RmsOccupancyCrosswalkState state, int skip, int take);
		Task<IEnumerable<RmsOccupancyCrosswalk>> GetForOccupancyAsync(int departmentId, string occupancyId);
		Task<IEnumerable<RmsOccupancyCrosswalk>> GetForContactAsync(int departmentId, string contactId);
		Task<IEnumerable<RmsOccupancyCrosswalk>> GetForContactsAsync(int departmentId, IEnumerable<string> contactIds);
		Task<IEnumerable<RmsOccupancyCrosswalk>> GetAllForDepartmentAsync(int departmentId);
		Task<int> CountByStateAsync(int departmentId, RmsOccupancyCrosswalkState state);
	}

	public interface IRmsOccupancyFieldProvenancesRepository : IRepository<RmsOccupancyFieldProvenance>
	{
		Task<IEnumerable<RmsOccupancyFieldProvenance>> GetForOccupancyAsync(int departmentId, string occupancyId);
		Task<int> DeleteForOccupancyAsync(int departmentId, string occupancyId, CancellationToken cancellationToken = default);
	}

	public interface IRmsOccupancyOwnershipsRepository : IRepository<RmsOccupancyOwnership>
	{
		Task<RmsOccupancyOwnership> GetForDepartmentAsync(int departmentId);
	}

	public interface IRmsCodeSetsRepository : IRepository<RmsCodeSet>
	{
		Task<RmsCodeSet> GetByIdForDepartmentAsync(int departmentId, string codeSetId);
		Task<IEnumerable<RmsCodeSet>> GetForDepartmentAsync(int departmentId, bool includeInactive);
	}

	public interface IRmsCodeSectionsRepository : IRepository<RmsCodeSection>
	{
		Task<RmsCodeSection> GetByIdForDepartmentAsync(int departmentId, string sectionId);
		Task<IEnumerable<RmsCodeSection>> GetForCodeSetAsync(int departmentId, string codeSetId);
		Task<IEnumerable<RmsCodeSection>> GetByIdsAsync(int departmentId, IEnumerable<string> sectionIds);
	}

	public interface IRmsInspectionProgramsRepository : IRepository<RmsInspectionProgram>
	{
		Task<RmsInspectionProgram> GetByIdForDepartmentAsync(int departmentId, string programId);
		Task<IEnumerable<RmsInspectionProgram>> GetForDepartmentAsync(int departmentId, bool includeInactive);
	}

	public sealed class RmsInspectionQuery
	{
		public string OccupancyId { get; set; }
		public string ProgramId { get; set; }
		public IList<int> States { get; set; }
		public string InspectorUserId { get; set; }
		public DateTime? ScheduledBefore { get; set; }
		public int Skip { get; set; }
		public int Take { get; set; } = 50;
	}

	public interface IRmsInspectionsRepository : IRepository<RmsInspection>
	{
		Task<RmsInspection> GetByIdForDepartmentAsync(int departmentId, string inspectionId);
		Task<IEnumerable<RmsInspection>> QueryAsync(int departmentId, RmsInspectionQuery query);
		Task<int> CountAsync(int departmentId, RmsInspectionQuery query);
		Task<IEnumerable<RmsInspection>> GetForOccupancyAsync(int departmentId, string occupancyId);
		/// <summary>Latest completed inspection per occupancy for a program; the scheduler's frequency anchor.</summary>
		Task<IDictionary<string, DateTime>> GetLastCompletedByOccupancyAsync(int departmentId, string programId);
		Task<IEnumerable<RmsInspection>> GetOpenForProgramAsync(int departmentId, string programId);
		/// <summary>Live inspections whose activity date (CompletedOn, else ScheduledOn, else CreatedOn) is in [start, end), at most <paramref name="take"/> rows.</summary>
		Task<IEnumerable<RmsInspection>> GetForRangeAsync(int departmentId, DateTime startUtc, DateTime endUtc, int take);
		Task<bool> TryBumpRowVersionAsync(int departmentId, string inspectionId, long expectedVersion, CancellationToken cancellationToken = default);
	}

	public interface IRmsViolationsRepository : IRepository<RmsViolation>
	{
		Task<RmsViolation> GetByIdForDepartmentAsync(int departmentId, string violationId);
		Task<IEnumerable<RmsViolation>> GetForInspectionAsync(int departmentId, string inspectionId);
		Task<IEnumerable<RmsViolation>> GetForOccupancyAsync(int departmentId, string occupancyId, bool openOnly);
		Task<IEnumerable<RmsViolation>> GetOpenAsync(int departmentId, int take);
		Task<IEnumerable<RmsViolation>> GetOverdueNotEmittedAsync(int departmentId, DateTime utcNow, int take);
		Task<int> CountOpenAsync(int departmentId);
		Task<int> CountOverdueAsync(int departmentId, DateTime utcNow);
		Task<IDictionary<string, int>> CountOpenByOccupancyAsync(int departmentId, IEnumerable<string> occupancyIds);
		/// <summary>Live violations opened (CreatedOn) in [start, end), at most <paramref name="take"/> rows.</summary>
		Task<IEnumerable<RmsViolation>> GetForRangeAsync(int departmentId, DateTime startUtc, DateTime endUtc, int take);
	}

	public interface IRmsHydrantsRepository : IRepository<RmsHydrant>
	{
		Task<RmsHydrant> GetByIdForDepartmentAsync(int departmentId, string hydrantId);
		Task<RmsHydrant> GetByNumberAsync(int departmentId, string hydrantNumber);
		Task<IEnumerable<RmsHydrant>> GetAllLiveAsync(int departmentId);
		Task<IEnumerable<RmsHydrant>> GetByIdsAsync(int departmentId, IEnumerable<string> hydrantIds);
		Task<IEnumerable<RmsHydrant>> GetInBoundsAsync(int departmentId, decimal minLat, decimal maxLat, decimal minLon, decimal maxLon, int take);
		Task<int> CountLiveAsync(int departmentId);
		Task<int> CountOutOfServiceAsync(int departmentId);
		Task<int> CountTestDueAsync(int departmentId, DateTime cutoffUtc);
	}

	public interface IRmsHydrantFlowTestsRepository : IRepository<RmsHydrantFlowTest>
	{
		Task<IEnumerable<RmsHydrantFlowTest>> GetForHydrantAsync(int departmentId, string hydrantId);
		/// <summary>Flow tests performed in [start, end) across the department, at most <paramref name="take"/> rows.</summary>
		Task<IEnumerable<RmsHydrantFlowTest>> GetForRangeAsync(int departmentId, DateTime startUtc, DateTime endUtc, int take);
	}

	public interface IRmsHydrantMaintenancesRepository : IRepository<RmsHydrantMaintenance>
	{
		Task<IEnumerable<RmsHydrantMaintenance>> GetForHydrantAsync(int departmentId, string hydrantId);
	}

	public interface IRmsPermitTypesRepository : IRepository<RmsPermitType>
	{
		Task<RmsPermitType> GetByIdForDepartmentAsync(int departmentId, string permitTypeId);
		Task<IEnumerable<RmsPermitType>> GetForDepartmentAsync(int departmentId, bool includeInactive);
	}

	public sealed class RmsPermitQuery
	{
		public string OccupancyId { get; set; }
		public string PermitTypeId { get; set; }
		public IList<int> States { get; set; }
		public DateTime? ExpiresBefore { get; set; }
		public int Skip { get; set; }
		public int Take { get; set; } = 50;
	}

	public interface IRmsPermitsRepository : IRepository<RmsPermit>
	{
		Task<RmsPermit> GetByIdForDepartmentAsync(int departmentId, string permitId);
		Task<IEnumerable<RmsPermit>> QueryAsync(int departmentId, RmsPermitQuery query);
		Task<int> CountAsync(int departmentId, RmsPermitQuery query);
		Task<IEnumerable<RmsPermit>> GetForOccupancyAsync(int departmentId, string occupancyId);
		Task<IEnumerable<RmsPermit>> GetExpiringAsync(int departmentId, DateTime utcNow, DateTime horizonUtc, int take);
		Task<int> CountExpiringAsync(int departmentId, DateTime utcNow, DateTime horizonUtc);
		/// <summary>Live permits applied for (AppliedOn) in [start, end), at most <paramref name="take"/> rows.</summary>
		Task<IEnumerable<RmsPermit>> GetForRangeAsync(int departmentId, DateTime startUtc, DateTime endUtc, int take);
		Task<bool> TryBumpRowVersionAsync(int departmentId, string permitId, long expectedVersion, CancellationToken cancellationToken = default);
	}

	public interface IRmsPlanReviewsRepository : IRepository<RmsPlanReview>
	{
		Task<IEnumerable<RmsPlanReview>> GetForPermitAsync(int departmentId, string permitId);
		Task<RmsPlanReview> GetByIdForDepartmentAsync(int departmentId, string reviewId);
	}

	public interface IRmsCrrActivitiesRepository : IRepository<RmsCrrActivity>
	{
		Task<RmsCrrActivity> GetByIdForDepartmentAsync(int departmentId, string activityId);
		Task<IEnumerable<RmsCrrActivity>> GetForRangeAsync(int departmentId, DateTime startUtc, DateTime endUtc, int take);
		Task<IEnumerable<RmsCrrActivity>> GetForOccupancyAsync(int departmentId, string occupancyId);
	}

	public interface IRmsPreventionAttachmentsRepository : IRepository<RmsPreventionAttachment>
	{
		Task<RmsPreventionAttachment> GetByIdForDepartmentAsync(int departmentId, string attachmentId);
		Task<IEnumerable<RmsPreventionAttachment>> GetMetadataForParentAsync(int departmentId, RmsPreventionParentKind parentKind, string parentId);
	}

	public interface IRmsPreventionSequencesRepository : IRepository<RmsPreventionSequence>
	{
		/// <summary>Atomically allocates the next number for a department/kind/year; safe under concurrent callers in either dialect.</summary>
		Task<int> NextAsync(int departmentId, string kind, int year, CancellationToken cancellationToken = default);
	}

	public interface IRmsInvestigationCasesRepository : IRepository<RmsInvestigationCase>
	{
		Task<RmsInvestigationCase> GetByIdForDepartmentAsync(int departmentId, string caseId);
		Task<IEnumerable<RmsInvestigationCase>> GetByIdsAsync(int departmentId, IEnumerable<string> caseIds);
		Task<IEnumerable<RmsInvestigationCase>> GetForDepartmentAsync(int departmentId, bool includeClosed, int skip, int take);
		Task<int> CountOpenAsync(int departmentId);
		Task<bool> TryBumpRowVersionAsync(int departmentId, string caseId, long expectedVersion, CancellationToken cancellationToken = default);
	}

	public interface IRmsInvestigationCaseIncidentsRepository : IRepository<RmsInvestigationCaseIncident>
	{
		Task<IEnumerable<RmsInvestigationCaseIncident>> GetForCaseAsync(int departmentId, string caseId);
		Task<IEnumerable<RmsInvestigationCaseIncident>> GetForRecordAsync(int departmentId, string recordId);
	}

	public interface IRmsInvestigationCaseMembersRepository : IRepository<RmsInvestigationCaseMember>
	{
		Task<IEnumerable<RmsInvestigationCaseMember>> GetForCaseAsync(int departmentId, string caseId);
		Task<IEnumerable<RmsInvestigationCaseMember>> GetActiveForUserAsync(int departmentId, string userId);
		Task<RmsInvestigationCaseMember> GetByIdForDepartmentAsync(int departmentId, string memberId);
	}

	public interface IRmsInvestigationNotesRepository : IRepository<RmsInvestigationNote>
	{
		Task<RmsInvestigationNote> GetByIdForDepartmentAsync(int departmentId, string noteId);
		Task<IEnumerable<RmsInvestigationNote>> GetForCaseAsync(int departmentId, string caseId);
		Task<int> LockForCaseAsync(int departmentId, string caseId, DateTime utcNow, CancellationToken cancellationToken = default);
	}

	public interface IRmsInvestigationEvidenceRepository : IRepository<RmsInvestigationEvidence>
	{
		Task<RmsInvestigationEvidence> GetByIdForDepartmentAsync(int departmentId, string evidenceId);
		Task<IEnumerable<RmsInvestigationEvidence>> GetForCaseAsync(int departmentId, string caseId);
	}

	public interface IRmsInvestigationCustodyRepository : IRepository<RmsInvestigationCustody>
	{
		Task<IEnumerable<RmsInvestigationCustody>> GetForEvidenceAsync(int departmentId, string evidenceId);
	}

	public interface IRmsInvestigationReferralsRepository : IRepository<RmsInvestigationReferral>
	{
		Task<RmsInvestigationReferral> GetByIdForDepartmentAsync(int departmentId, string referralId);
		Task<IEnumerable<RmsInvestigationReferral>> GetForCaseAsync(int departmentId, string caseId);
	}

	public interface IRmsQualityRubricsRepository : IRepository<RmsQualityRubric>
	{
		Task<RmsQualityRubric> GetByIdForDepartmentAsync(int departmentId, string rubricId);
		Task<IEnumerable<RmsQualityRubric>> GetForDepartmentAsync(int departmentId, bool includeInactive);
	}

	public interface IRmsQualityReviewsRepository : IRepository<RmsQualityReview>
	{
		Task<RmsQualityReview> GetByIdForDepartmentAsync(int departmentId, string reviewId);
		Task<IEnumerable<RmsQualityReview>> GetForRecordAsync(int departmentId, string recordId);
		Task<IEnumerable<RmsQualityReview>> GetPendingAsync(int departmentId, int take);
		Task<IEnumerable<RmsQualityReview>> GetScoredSinceAsync(int departmentId, DateTime sinceUtc, int take);
		Task<IEnumerable<string>> GetReviewedRecordIdsAsync(int departmentId, IEnumerable<string> recordIds);
	}
}
