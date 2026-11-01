using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Communications;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Communications
{
	/// <summary>
	/// comms.tests (plan §7.5). The run list is what the Communication Tests page shows department and group admins alike
	/// (department totals per run). The per-channel breakdown reads the latest run's results; the Report page narrows those
	/// to a group admin's own groups, so the department-wide breakdown is computed for department admins only.
	/// Contact values (phone numbers, addresses) are never selected.
	/// </summary>
	[DashboardWidget("comms.tests")]
	public class CommunicationTestsWidgetProvider : IDashboardWidgetDataProvider
	{
		private static readonly string[] StatusKeys = { "pending", "running", "awaitingResponses", "completed", "failed" };
		private static readonly string[] ChannelKeys = { "sms", "email", "voice", "push" };

		private readonly ICommunicationTestService _communicationTestService;

		public CommunicationTestsWidgetProvider(ICommunicationTestService communicationTestService)
		{
			_communicationTestService = communicationTestService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var runs = (await _communicationTestService.GetRunsByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<CommunicationTestRun>())
				.Where(r => r != null && r.DepartmentId == departmentId)
				.OrderByDescending(r => r.StartedOn)
				.Take(context.GetInt("maxRuns", 5))
				.ToList();

			var data = new CommunicationTestsWidgetData();
			if (runs.Count == 0)
				return data;

			var names = (await _communicationTestService.GetTestsByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<CommunicationTest>())
				.Where(t => t != null && t.DepartmentId == departmentId)
				.GroupBy(t => t.CommunicationTestId)
				.ToDictionary(g => g.Key, g => g.First().Name);

			foreach (var run in runs)
			{
				data.Runs.Add(new CommunicationTestRunItem
				{
					CommunicationTestRunId = run.CommunicationTestRunId,
					TestName = names.TryGetValue(run.CommunicationTestId, out var name) ? name : string.Empty,
					StartedOnUtc = run.StartedOn,
					CompletedOnUtc = run.CompletedOn,
					Status = run.Status,
					StatusKey = run.Status >= 0 && run.Status < StatusKeys.Length ? StatusKeys[run.Status] : StatusKeys[0],
					TotalUsersTested = run.TotalUsersTested,
					TotalResponses = run.TotalResponses,
					ResponseRate = Rate(run.TotalResponses, run.TotalUsersTested),
					Url = "/User/CommunicationTest/Report?runId=" + run.CommunicationTestRunId.ToString("D")
				});
			}

			if (context.Viewer?.IsDepartmentAdmin != true)
				return data;

			var latest = runs.FirstOrDefault(r => r.Status != (int)CommunicationTestRunStatus.Pending) ?? runs[0];
			var results = (await _communicationTestService.GetResultsByRunIdAsync(latest.CommunicationTestRunId) ?? Enumerable.Empty<CommunicationTestResult>())
				.Where(r => r != null && r.DepartmentId == departmentId && r.CommunicationTestRunId == latest.CommunicationTestRunId)
				.ToList();

			data.ChannelsVisible = true;
			data.LatestRunId = latest.CommunicationTestRunId;
			data.LatestRunChannels = results
				.GroupBy(r => r.Channel)
				.OrderBy(g => g.Key)
				.Select(g =>
				{
					var attempted = g.Count(r => r.SendAttempted);
					var delivered = g.Count(r => r.SendSucceeded);
					var responded = g.Count(r => r.Responded);
					return new CommunicationTestChannelItem
					{
						Channel = g.Key,
						ChannelKey = g.Key >= 0 && g.Key < ChannelKeys.Length ? ChannelKeys[g.Key] : "other",
						Targeted = g.Count(),
						Suppressed = g.Count(r => r.Suppressed),
						Attempted = attempted,
						Delivered = delivered,
						Responded = responded,
						DeliveryRate = Rate(delivered, attempted),
						ResponseRate = Rate(responded, delivered)
					};
				})
				.ToList();

			return data;
		}

		internal static int? Rate(int part, int whole) =>
			whole <= 0 ? (int?)null : (int)Math.Round(100d * Math.Min(part, whole) / whole, MidpointRounding.AwayFromZero);
	}
}
