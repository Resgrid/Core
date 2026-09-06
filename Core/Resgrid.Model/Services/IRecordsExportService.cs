using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Department-authored report exports (RMS plan sections 4.7, 4.10 and 5.6): template management, the
	/// render itself and the schedule sweep behind worker 45. Renders never mutate a record; every rendered
	/// record is audited as an Export against the record, and a render for a Workflow carries the run id.
	/// </summary>
	public interface IRecordsExportService
	{
		Task<List<RmsExportTemplate>> GetTemplatesAsync(int departmentId);

		Task<RmsExportTemplate> GetTemplateAsync(int departmentId, string templateId);

		Task<RmsExportTemplate> GetTemplateByKeyAsync(int departmentId, string templateKey);

		/// <summary>Validates columns, flags, schedule and acknowledgements without saving.</summary>
		Task<RecordsExportTemplateValidation> ValidateAsync(int departmentId, string userId, RmsExportTemplate template);

		/// <summary>Creates or updates; requires ManageRecordReports and, for narrative/restricted columns, the egress acknowledgement (and RecordRestricted_View for restricted).</summary>
		Task<RmsExportTemplate> SaveAsync(int departmentId, string userId, RmsExportTemplate template, bool acknowledgeEgress, CancellationToken cancellationToken = default);

		Task<bool> DeleteAsync(int departmentId, string userId, string templateId, CancellationToken cancellationToken = default);

		/// <summary>Renders and stores a run. Throws UnauthorizedAccessException for an attended caller who may not export.</summary>
		Task<RmsExportRun> RenderAsync(int departmentId, RmsExportTemplate template, RecordsExportRequest request, CancellationToken cancellationToken = default);

		/// <summary>Convenience for a Workflow step: renders the template for the triggering record (TriggeringRecord scope) or returns the scheduled run named by the event (Window scope).</summary>
		Task<RmsExportRun> ResolveForWorkflowAsync(int departmentId, string templateId, string recordId, RmsRecordKind? recordKind, string scheduledRunId, string workflowRunId, CancellationToken cancellationToken = default);

		/// <summary>The run with its bytes, department-scoped; null when expired or purged.</summary>
		Task<RmsExportRun> GetRunAsync(int departmentId, string runId, bool includeData);

		Task<List<RmsExportRun>> GetRunsAsync(int departmentId, string templateId, int take);

		/// <summary>Worker 45: renders every enabled template whose schedule is due, emits RecordExportScheduled per run, and advances NextRunOn.</summary>
		Task<RecordsExportScheduleSweepResult> RunDueSchedulesAsync(CancellationToken cancellationToken = default);

		/// <summary>Removes expired run artifacts (bytes and rows) for the department.</summary>
		Task<int> PurgeExpiredRunsAsync(int departmentId, CancellationToken cancellationToken = default);
	}
}
