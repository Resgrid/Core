using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Repositories;

namespace Resgrid.Model.Services
{
	public class InspectionAggregate
	{
		public RmsInspection Inspection { get; set; }
		public RmsInspectionProgram Program { get; set; }
		public RmsOccupancy Occupancy { get; set; }
		public List<RmsInspectionChecklistItem> Checklist { get; set; } = new List<RmsInspectionChecklistItem>();
		public List<RmsInspectionItemResult> Items { get; set; } = new List<RmsInspectionItemResult>();
		public List<RmsViolation> Violations { get; set; } = new List<RmsViolation>();
		public List<RmsPreventionAttachment> Attachments { get; set; } = new List<RmsPreventionAttachment>();
		public ProtectedReadResult Protection { get; set; } = new ProtectedReadResult();
	}

	/// <summary>Inspection programs, code sets, inspections, violations and re-inspection (RMS plan section 4.3, RMS-5).</summary>
	public interface IRecordsInspectionsService
	{
		Task<bool> IsModuleEnabledAsync(int departmentId);

		Task<List<RmsCodeSet>> GetCodeSetsAsync(int departmentId, string userId, bool includeInactive);
		Task<RmsCodeSet> SaveCodeSetAsync(int departmentId, string userId, RmsCodeSet input, CancellationToken cancellationToken = default);
		Task<List<RmsCodeSection>> GetCodeSectionsAsync(int departmentId, string userId, string codeSetId);
		Task<RmsCodeSection> SaveCodeSectionAsync(int departmentId, string userId, RmsCodeSection input, CancellationToken cancellationToken = default);
		/// <summary>CSV import: section,title,text,severity,days. Returns the number of sections created.</summary>
		Task<int> ImportCodeSectionsAsync(int departmentId, string userId, string codeSetId, string csv, CancellationToken cancellationToken = default);

		Task<List<RmsInspectionProgram>> GetProgramsAsync(int departmentId, string userId, bool includeInactive);
		Task<RmsInspectionProgram> GetProgramAsync(int departmentId, string userId, string programId);
		Task<RmsInspectionProgram> SaveProgramAsync(int departmentId, string userId, RmsInspectionProgram input, List<RmsInspectionChecklistItem> checklist, CancellationToken cancellationToken = default);

		Task<List<RmsInspection>> ListAsync(int departmentId, string userId, RmsInspectionQuery query);
		Task<int> CountAsync(int departmentId, string userId, RmsInspectionQuery query);
		Task<InspectionAggregate> GetAsync(int departmentId, string userId, string inspectionId);
		Task<RmsInspection> ScheduleAsync(int departmentId, string userId, string occupancyId, string programId, DateTime scheduledOn, string inspectorUserId, CancellationToken cancellationToken = default);
		Task<RmsInspection> StartAsync(int departmentId, string userId, string inspectionId, CancellationToken cancellationToken = default);
		/// <summary>Records item results and notes; failed items become Open violations citing the item's code section.</summary>
		Task<InspectionAggregate> CompleteAsync(int departmentId, string userId, string inspectionId, List<RmsInspectionItemResult> items, string notes, string signatureName, CancellationToken cancellationToken = default);
		Task<RmsInspection> ScheduleReinspectionAsync(int departmentId, string userId, string inspectionId, DateTime scheduledOn, CancellationToken cancellationToken = default);
		Task<RmsInspection> CloseAsync(int departmentId, string userId, string inspectionId, CancellationToken cancellationToken = default);
		Task<RmsInspection> CancelAsync(int departmentId, string userId, string inspectionId, string reason, CancellationToken cancellationToken = default);
		Task<RmsInspection> IssueNoticeAsync(int departmentId, string userId, string inspectionId, string noticeReference, CancellationToken cancellationToken = default);

		Task<List<RmsViolation>> GetViolationsForOccupancyAsync(int departmentId, string userId, string occupancyId, bool openOnly);
		Task<List<RmsViolation>> GetOpenViolationsAsync(int departmentId, string userId, int take);
		Task<RmsViolation> SaveViolationAsync(int departmentId, string userId, RmsViolation input, CancellationToken cancellationToken = default);
		Task<RmsViolation> TransitionViolationAsync(int departmentId, string userId, string violationId, RmsViolationState target, string note, CancellationToken cancellationToken = default);

		/// <summary>Creates Scheduled inspections for occupancies whose program frequency has lapsed; idempotent per occupancy/program.</summary>
		Task<int> GenerateDueInspectionsAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default);
		/// <summary>Raises trigger 162 once per violation as it passes its due date.</summary>
		Task<int> EmitOverdueViolationsAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default);
	}

	public class HydrantAggregate
	{
		public RmsHydrant Hydrant { get; set; }
		public List<RmsHydrantFlowTest> FlowTests { get; set; } = new List<RmsHydrantFlowTest>();
		public List<RmsHydrantMaintenance> Maintenance { get; set; } = new List<RmsHydrantMaintenance>();
		public List<RmsPreventionAttachment> Attachments { get; set; } = new List<RmsPreventionAttachment>();
	}

	/// <summary>Hydrants and water sources, flow tests, maintenance, service state, CSV import and the response-map layer.</summary>
	public interface IRecordsHydrantsService
	{
		Task<bool> IsModuleEnabledAsync(int departmentId);
		Task<List<RmsHydrant>> ListAsync(int departmentId, string userId);
		Task<HydrantAggregate> GetAsync(int departmentId, string userId, string hydrantId);
		Task<RmsHydrant> SaveAsync(int departmentId, string userId, RmsHydrant input, CancellationToken cancellationToken = default);
		Task DeleteAsync(int departmentId, string userId, string hydrantId, CancellationToken cancellationToken = default);
		Task<RmsHydrant> SetServiceStateAsync(int departmentId, string userId, string hydrantId, bool inService, string reason, CancellationToken cancellationToken = default);
		Task<RmsHydrantFlowTest> RecordFlowTestAsync(int departmentId, string userId, RmsHydrantFlowTest input, CancellationToken cancellationToken = default);
		Task<RmsHydrantMaintenance> RecordMaintenanceAsync(int departmentId, string userId, RmsHydrantMaintenance input, CancellationToken cancellationToken = default);
		Task<HydrantImportResult> ImportCsvAsync(int departmentId, string userId, string csv, CancellationToken cancellationToken = default);
		Task<List<HydrantMapPoint>> GetMapLayerAsync(int departmentId, string userId, decimal? minLat, decimal? maxLat, decimal? minLon, decimal? maxLon);
		Task<List<RmsHydrant>> GetNearestAsync(int departmentId, decimal latitude, decimal longitude, int take, double maxMeters);
		Task<int> CountTestDueAsync(int departmentId, DateTime utcNow);
	}

	public class PermitAggregate
	{
		public RmsPermit Permit { get; set; }
		public RmsPermitType Type { get; set; }
		public RmsOccupancy Occupancy { get; set; }
		public List<RmsPlanReview> PlanReviews { get; set; } = new List<RmsPlanReview>();
		public List<RmsPreventionAttachment> Attachments { get; set; } = new List<RmsPreventionAttachment>();
		public ProtectedReadResult Protection { get; set; } = new ProtectedReadResult();
	}

	/// <summary>Permits, plan review cycles, conditions, expiration and the optional fee reference.</summary>
	public interface IRecordsPermitsService
	{
		Task<bool> IsModuleEnabledAsync(int departmentId);
		Task<List<RmsPermitType>> GetTypesAsync(int departmentId, string userId, bool includeInactive);
		Task<RmsPermitType> SaveTypeAsync(int departmentId, string userId, RmsPermitType input, CancellationToken cancellationToken = default);
		Task<List<RmsPermit>> ListAsync(int departmentId, string userId, RmsPermitQuery query);
		Task<int> CountAsync(int departmentId, string userId, RmsPermitQuery query);
		Task<PermitAggregate> GetAsync(int departmentId, string userId, string permitId);
		Task<RmsPermit> ApplyAsync(int departmentId, string userId, RmsPermit input, CancellationToken cancellationToken = default);
		Task<RmsPermit> UpdateAsync(int departmentId, string userId, RmsPermit input, CancellationToken cancellationToken = default);
		Task<RmsPermit> TransitionAsync(int departmentId, string userId, string permitId, RmsPermitState target, string reason, DateTime? effectiveOn, DateTime? expiresOn, CancellationToken cancellationToken = default);
		Task<RmsPlanReview> RecordPlanReviewAsync(int departmentId, string userId, string permitId, RmsPlanReviewOutcome outcome, string comments, CancellationToken cancellationToken = default);
		Task<RmsPermit> RecordFeePaidAsync(int departmentId, string userId, string permitId, decimal amount, string invoiceReference, CancellationToken cancellationToken = default);
		/// <summary>Expires issued permits past ExpiresOn and raises trigger 163 once per permit within the notice window.</summary>
		Task<(int Expired, int Notified)> SweepExpiryAsync(int departmentId, DateTime utcNow, int noticeDays, CancellationToken cancellationToken = default);
	}

	public class CrrSummary
	{
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
		public int Activities { get; set; }
		public int Audience { get; set; }
		public int SmokeAlarmsInstalled { get; set; }
		public decimal Hours { get; set; }
		public Dictionary<int, int> ByKind { get; set; } = new Dictionary<int, int>();
	}

	/// <summary>Community risk reduction activities and their summary.</summary>
	public interface IRecordsCrrService
	{
		Task<bool> IsModuleEnabledAsync(int departmentId);
		Task<List<RmsCrrActivity>> ListAsync(int departmentId, string userId, DateTime startUtc, DateTime endUtc, int take);
		Task<RmsCrrActivity> GetAsync(int departmentId, string userId, string activityId);
		Task<RmsCrrActivity> SaveAsync(int departmentId, string userId, RmsCrrActivity input, CancellationToken cancellationToken = default);
		Task DeleteAsync(int departmentId, string userId, string activityId, CancellationToken cancellationToken = default);
		Task<CrrSummary> GetSummaryAsync(int departmentId, string userId, DateTime startUtc, DateTime endUtc);
	}

	/// <summary>Attachments on prevention and investigation aggregates; hygiene and scanning are the record-attachment pipeline.</summary>
	public interface IRecordsPreventionAttachmentsService
	{
		Task<RmsPreventionAttachment> AddAsync(int departmentId, string userId, RmsPreventionParentKind parentKind, string parentId, string fileName, string contentType, byte[] data, string description, bool restricted, CancellationToken cancellationToken = default);
		Task<List<RmsPreventionAttachment>> GetMetadataAsync(int departmentId, string userId, RmsPreventionParentKind parentKind, string parentId);
		Task<RmsPreventionAttachment> GetWithDataAsync(int departmentId, string userId, string attachmentId);
		Task RemoveAsync(int departmentId, string userId, string attachmentId, CancellationToken cancellationToken = default);
	}

	/// <summary>Daily prevention sweep, run by worker 42 alongside the record due-state evaluation.</summary>
	public interface IRecordsPreventionSweepService
	{
		Task<RecordsPreventionSweepResult> SweepAsync(CancellationToken cancellationToken = default);
		Task<RecordsPreventionSweepResult> SweepDepartmentAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default);
		Task<PreventionSummary> GetSummaryAsync(int departmentId, string userId);
	}
}
