using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Records;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Records
{
	/// <summary>
	/// records.myWork (plan §7.8): IRecordsService.GetOutstandingForOwnerAsync (unfinalized Records the viewer owns) and
	/// IRecordWorkAssignmentsService.GetQueueAsync (open assignments addressed to the viewer, each re-checked for record
	/// visibility by the service). Numbers, states and dates only; display summaries and assignment notes are not carried.
	/// </summary>
	[DashboardWidget("records.myWork")]
	public class RecordsMyWorkWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IRecordsService _recordsService;
		private readonly IRecordWorkAssignmentsService _recordWorkAssignmentsService;
		private readonly IRecordsAuthorizationService _recordsAuthorizationService;

		public RecordsMyWorkWidgetProvider(IRecordsService recordsService, IRecordWorkAssignmentsService recordWorkAssignmentsService,
			IRecordsAuthorizationService recordsAuthorizationService)
		{
			_recordsService = recordsService;
			_recordWorkAssignmentsService = recordWorkAssignmentsService;
			_recordsAuthorizationService = recordsAuthorizationService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var maxRows = context.GetInt("maxRows", 5);
			await RecordsWidgetSupport.RequireActiveMemberAsync(_recordsAuthorizationService, context);

			var owned = (await RecordsWidgetSupport.RunAsync(() => _recordsService.GetOutstandingForOwnerAsync(context.DepartmentId, context.UserId)) ?? new List<RmsOperationalRecord>())
				.Where(r => r != null && r.DepartmentId == context.DepartmentId && !r.DeletedOn.HasValue).ToList();
			var queue = (await RecordsWidgetSupport.RunAsync(() => _recordWorkAssignmentsService.GetQueueAsync(context.DepartmentId, context.UserId, new FieldRecordContext(), 0)) ?? new List<RmsRecordWorkAssignment>())
				.Where(a => a != null && a.DepartmentId == context.DepartmentId && !a.DeletedOn.HasValue
					&& (a.State == (int)RmsWorkAssignmentState.Open || a.State == (int)RmsWorkAssignmentState.Acknowledged)).ToList();
			var now = context.NowUtc;

			return new RecordsMyWorkWidgetData
			{
				OwnedOutstanding = owned.Count,
				Assignments = queue.Count,
				AssignmentsOverdue = queue.Count(a => a.DueOn.HasValue && a.DueOn < now),
				Records = owned.OrderBy(r => r.CreatedOn).Take(maxRows).Select(r => new RecordsMyWorkRecord
				{
					RecordId = r.RmsOperationalRecordId,
					Number = RecordsWidgetSupport.SafeText(r.RecordNumber) ?? RecordsWidgetSupport.SafeText(r.DraftReference),
					DefinitionKey = r.DefinitionKey,
					State = r.State,
					CreatedOn = r.CreatedOn,
					ReviewDueOn = r.ReviewDueOn
				}).ToList(),
				Queue = queue.OrderBy(a => a.DueOn ?? DateTime.MaxValue).ThenBy(a => a.CreatedOn).Take(maxRows).Select(a => new RecordsMyWorkAssignment
				{
					AssignmentId = a.RmsRecordWorkAssignmentId,
					RecordId = a.RecordId,
					Purpose = RmsWorkAssignmentPurposes.IsKnown(a.Purpose) ? a.Purpose.Trim().ToLowerInvariant() : null,
					State = a.State,
					AssigneeKind = a.AssigneeKind,
					DueOn = a.DueOn,
					Overdue = a.DueOn.HasValue && a.DueOn < now,
					CreatedOn = a.CreatedOn
				}).ToList()
			};
		}
	}
}
