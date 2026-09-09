using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Checklists
{
	public static class ReadinessHistoryFields
	{
		public const int CatalogVersion = 16;
		public static readonly int[] AuditTypes = Enum.GetValues<AuditLogTypes>().Where(x => (x.ToString().StartsWith("Checklist", StringComparison.Ordinal) || x.ToString().StartsWith("WorkOrder", StringComparison.Ordinal))).Select(x => (int)x).ToArray();
		public static bool IsChecklistAudit(int type) => AuditTypes.Contains(type);
		public static readonly IReadOnlyDictionary<string, (Func<AuditLog, string> Get, Action<AuditLog, string> Set)> Audits = new Dictionary<string, (Func<AuditLog, string>, Action<AuditLog, string>)>
		{ ["auditlogs.data"] = (x => x.Data, (x, v) => x.Data = v) };
		public static readonly IReadOnlyDictionary<string, (Func<DomainEventOutboxEntry, string> Get, Action<DomainEventOutboxEntry, string> Set)> Outbox = new Dictionary<string, (Func<DomainEventOutboxEntry, string>, Action<DomainEventOutboxEntry, string>)>
		{
			["domaineventoutbox.payloadjson"] = (x => x.PayloadJson, (x, v) => x.PayloadJson = v),
			["domaineventoutbox.lasterror"] = (x => x.LastError, (x, v) => x.LastError = v)
		};
		public static readonly IReadOnlyDictionary<string, (Func<WorkflowRun, string> Get, Action<WorkflowRun, string> Set)> Runs = new Dictionary<string, (Func<WorkflowRun, string>, Action<WorkflowRun, string>)>
		{
			["workflowruns.inputpayload"] = (x => x.InputPayload, (x, v) => x.InputPayload = v),
			["workflowruns.errormessage"] = (x => x.ErrorMessage, (x, v) => x.ErrorMessage = v)
		};
		public static readonly IReadOnlyDictionary<string, (Func<WorkflowRunLog, string> Get, Action<WorkflowRunLog, string> Set)> Logs = new Dictionary<string, (Func<WorkflowRunLog, string>, Action<WorkflowRunLog, string>)>
		{
			["workflowrunlogs.renderedoutput"] = (x => x.RenderedOutput, (x, v) => x.RenderedOutput = v),
			["workflowrunlogs.actionresult"] = (x => x.ActionResult, (x, v) => x.ActionResult = v),
			["workflowrunlogs.errormessage"] = (x => x.ErrorMessage, (x, v) => x.ErrorMessage = v)
		};
	}
}
