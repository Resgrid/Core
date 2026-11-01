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
	/// records.qualityReview (plan §7.8): IRecordsQualityReviewService.GetPendingAsync with the Quality page's read size (reviewer
	/// check and per-record visibility in the service), the viewer's own reviews first. No findings, notes or scores.
	/// </summary>
	[DashboardWidget("records.qualityReview")]
	public class RecordsQualityReviewWidgetProvider : IDashboardWidgetDataProvider
	{
		/// <summary>The Quality page reads this many pending reviews.</summary>
		public const int PendingReadSize = 200;

		private readonly IRecordsQualityReviewService _recordsQualityReviewService;

		public RecordsQualityReviewWidgetProvider(IRecordsQualityReviewService recordsQualityReviewService)
		{
			_recordsQualityReviewService = recordsQualityReviewService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var maxRows = context.GetInt("maxRows", 5);
			var pending = (await RecordsWidgetSupport.RunAsync(() => _recordsQualityReviewService.GetPendingAsync(context.DepartmentId, context.UserId, PendingReadSize)) ?? new List<RmsQualityReview>())
				.Where(r => r != null && r.DepartmentId == context.DepartmentId && !r.ScoredOn.HasValue).ToList();

			bool Mine(RmsQualityReview review) => string.Equals(review.ReviewerUserId, context.UserId, StringComparison.OrdinalIgnoreCase);

			return new RecordsQualityReviewWidgetData
			{
				Pending = pending.Count,
				AssignedToMe = pending.Count(Mine),
				HasMore = pending.Count >= PendingReadSize,
				Items = pending.OrderBy(r => Mine(r) ? 0 : 1).ThenBy(r => r.SampledOn).Take(maxRows).Select(r => new RecordsQualityReviewItem
				{
					ReviewId = r.RmsQualityReviewId,
					RecordId = r.RecordId,
					RecordNumber = RecordsWidgetSupport.SafeText(r.RecordNumber),
					DefinitionKey = r.DefinitionKey,
					SampledOn = r.SampledOn,
					AssignedToMe = Mine(r)
				}).ToList()
			};
		}
	}
}
