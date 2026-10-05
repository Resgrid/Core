using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// A workflow step that attaches a Records report export (ActionConfig <c>recordsExportTemplateId</c>) renders that
	/// export on every run with no acting user and sends it outside Resgrid. Whoever decides what such a step sends, and
	/// where, must therefore hold what the Records report export pages require to run and manage that export themselves:
	/// ExportRecords and ManageRecordReports, plus ViewRestrictedRecords when the template includes restricted sections.
	/// The rule is applied when a step naming an export is saved and when a credential such a step delivers through is
	/// changed (a credential holds the destination host or bucket of a file upload).
	/// </summary>
	public static class WorkflowExportAttachmentRule
	{
		public const string TemplateNotFound = "export_template_not_found";
		public const string RecordsRightsRequired = "export_requires_records_rights";
		public const string RestrictedRightsRequired = "export_requires_restricted_rights";

		/// <summary>Why the member may not attach <paramref name="template"/> to a step, or null when they may.</summary>
		public static string Check(RmsExportTemplate template, int departmentId, bool canExportRecords, bool canManageRecordReports,
			bool canViewRestrictedRecords)
		{
			if (template == null || template.DepartmentId != departmentId || template.DeletedOn.HasValue)
				return TemplateNotFound;

			if (!canExportRecords || !canManageRecordReports)
				return RecordsRightsRequired;

			if (template.IncludeRestricted && !canViewRestrictedRecords)
				return RestrictedRightsRequired;

			return null;
		}

		/// <summary>The rule for a step's ActionConfig as saved; null when the step attaches no export.</summary>
		public static async Task<string> CheckStepAsync(IRecordsExportService exports, int departmentId, string actionConfigJson,
			bool canExportRecords, bool canManageRecordReports, bool canViewRestrictedRecords)
		{
			var templateId = WorkflowService.ReadExportTemplateId(actionConfigJson);
			if (templateId == null)
				return null;

			var template = await exports.GetTemplateAsync(departmentId, templateId);
			return Check(template, departmentId, canExportRecords, canManageRecordReports, canViewRestrictedRecords);
		}

		/// <summary>
		/// The rule for changing a credential: every export a step of the department delivers through it must pass. A
		/// template that no longer exists renders nothing, so it never blocks the change.
		/// </summary>
		public static async Task<string> CheckCredentialAsync(IWorkflowService workflows, IRecordsExportService exports, int departmentId,
			string credentialId, bool canExportRecords, bool canManageRecordReports, bool canViewRestrictedRecords,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(credentialId))
				return null;

			foreach (var workflow in await workflows.GetWorkflowsByDepartmentIdAsync(departmentId, cancellationToken) ?? new List<Workflow>())
			{
				foreach (var step in await workflows.GetStepsByWorkflowIdAsync(workflow.WorkflowId, cancellationToken) ?? new List<WorkflowStep>())
				{
					if (!string.Equals(step.WorkflowCredentialId, credentialId, StringComparison.OrdinalIgnoreCase))
						continue;

					var templateId = WorkflowService.ReadExportTemplateId(step.ActionConfig);
					if (templateId == null)
						continue;

					var template = await exports.GetTemplateAsync(departmentId, templateId);
					if (template == null || template.DeletedOn.HasValue)
						continue;

					var error = Check(template, departmentId, canExportRecords, canManageRecordReports, canViewRestrictedRecords);
					if (error != null)
						return error;
				}
			}

			return null;
		}
	}
}
