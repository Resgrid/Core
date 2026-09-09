using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Checklists;

namespace Resgrid.Web.Services.Controllers.v4
{
	public sealed partial class ChecklistRunsController
	{
		/// <summary>Completion rates and missed deadlines within a UTC start-date cohort (at most 93 days), filtered by current result permissions and ADP.</summary>
		[HttpGet("GetComplianceSummary")]
		public async Task<IActionResult> GetComplianceSummary([FromQuery] ChecklistReportQuery query) => Reply(await Checklists.GetComplianceSummaryAsync(Actor, query));
		/// <summary>Checklist evidence at the call time, with explicit unavailable historical inventory/deployment sources. This is a preview; RMS captures an immutable artifact separately.</summary>
		[HttpGet("GetReadinessPacket")]
		public async Task<IActionResult> GetReadinessPacket(int callId, int lookbackDays = 30) => Reply(await Checklists.GetReadinessPacketForCallAsync(Actor, callId, lookbackDays));
		[HttpGet("GetEntityChecklistHistory")]
		public async Task<IActionResult> GetEntityChecklistHistory(ChecklistTargetType entityType, string entityId, DateTime fromUtc, DateTime untilUtc) => Reply(await Checklists.GetEntityChecklistHistoryAsync(Actor, entityType, entityId, fromUtc, untilUtc));
	}
}
