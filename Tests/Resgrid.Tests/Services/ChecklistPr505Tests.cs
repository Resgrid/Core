using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	public partial class ChecklistWorkflowTests
	{
		[TestCase(""), TestCase("null"), TestCase("{}")]
		public async Task Desktop_and_mobile_history_fall_back_to_the_target_ID_for_empty_snapshots(string content)
		{
			var setup = await Start();
			var completion = await _store.GetAsync<ChecklistCompletion>(77, setup.Run);
			var occurrence = await _store.GetAsync<ChecklistOccurrence>(77, completion.OccurrenceId);
			occurrence.Content = content; await _store.WriteAsync(occurrence, false);
			(await _service.HistoryAsync(_actor, completion.ParentId)).Single().TargetName.Should().Be(completion.TargetId);
			(await _service.MobileHistoryAsync(_actor, new ChecklistMobileQuery { DefinitionId = completion.ParentId })).Single().TargetName.Should().Be(completion.TargetId);
		}

		[Test]
		public async Task Schedule_lookahead_keeps_fifty_row_page_offsets()
		{
			var setup = await Scheduled();
			var original = await _store.GetAsync<ChecklistSchedule>(77, setup.Input.Id);
			for (var i = 0; i < 51; i++)
			{
				var row = JsonConvert.DeserializeObject<ChecklistSchedule>(JsonConvert.SerializeObject(original));
				row.Id = Guid.NewGuid().ToString(); await _store.WriteAsync(row, true);
			}
			var first = await _service.SchedulesAsync(_actor, original.ParentId, 0, true);
			var next = await _service.SchedulesAsync(_actor, original.ParentId, 1, true);
			first.Should().HaveCount(51); next.Should().HaveCount(2);
			first.Take(50).Concat(next).Select(v => v.Schedule.Id).Should().HaveCount(52).And.OnlyHaveUniqueItems();
		}

		[TestCase("th-TH"), TestCase("ar-SA")]
		public async Task Compliance_trend_dates_keep_the_Gregorian_year(string culture)
		{
			await SeedReportMonth(); var report = await _service.GetComplianceSummaryAsync(_actor, Month());
			var previous = CultureInfo.CurrentCulture;
			try
			{
				CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
				ChecklistReportDocuments.Compliance(report).Should().Contain("<td>2026-08-01</td>");
			}
			finally { CultureInfo.CurrentCulture = previous; }
		}

		[Test, NonParallelizable]
		public async Task Failed_post_commit_cache_eviction_does_not_skip_other_keys_or_report_the_committed_command_failed()
		{
			var oldEnabled = FeatureFlagsConfig.FeatureFlagsEnabled; var oldCache = SystemBehaviorConfig.CacheEnabled;
			try
			{
				FeatureFlagsConfig.FeatureFlagsEnabled = true; SystemBehaviorConfig.CacheEnabled = true;
				var flag = new FeatureFlag { FeatureFlagId = 19, FlagKey = FeatureFlagKeys.ChecklistsSystem, IsEnabledGlobally = true };
				var flags = new Mock<IFeatureFlagRepository>(); flags.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { flag });
				flags.Setup(r => r.SaveOrUpdateAsync(It.IsAny<FeatureFlag>(), It.IsAny<CancellationToken>(), false)).ReturnsAsync((FeatureFlag row, CancellationToken ct, bool first) => row);
				var cache = new Mock<ICacheProvider>(); var attempts = 0;
				cache.Setup(c => c.RemoveAsync(It.IsAny<string>())).Returns(() => ++attempts == 1 ? Task.FromException<bool>(new InvalidOperationException("Synthetic cache failure")) : Task.FromResult(true));
				var events = new Mock<IEventAggregator>();
				var service = new FeatureToggleService(flags.Object, Mock.Of<IFeatureFlagOverrideRepository>(), Mock.Of<IFeatureFlagTargetingRuleRepository>(), Mock.Of<IFeatureFlagPrerequisiteRepository>(), Mock.Of<IFeatureFlagUsageRepository>(), cache.Object, events.Object, Mock.Of<ISubscriptionsService>(), Mock.Of<IDepartmentsService>(), _uow.Object, Mock.Of<IFeatureFlagMutationObserver>());
				await service.SetGlobalEnabledAsync(flag.FlagKey, false, "author");
				attempts.Should().Be(3); events.Invocations.Should().ContainSingle(i => i.Method.Name == "SendMessage");
				_uow.Verify(u => u.CommitChanges(), Times.Once); _uow.Verify(u => u.DiscardChanges(), Times.Never);
			}
			finally { FeatureFlagsConfig.FeatureFlagsEnabled = oldEnabled; SystemBehaviorConfig.CacheEnabled = oldCache; }
		}
	}

	public partial class ChecklistDatabaseTests
	{
		[Test]
		public async Task A_completion_ID_owned_by_another_department_returns_conflict_without_reading_its_data()
		{
			var connections = Connections(); using var uow = new Resgrid.Repositories.DataRepository.Transactions.UnitOfWork(connections); var store = Repository(connections, uow);
			await uow.CreateOrGetConnectionAsync();
			async Task<ChecklistCompletion> Completion(int department)
			{
				var definition = Row<ChecklistDefinition>(); definition.DepartmentId = department; await store.WriteAsync(definition, true);
				var version = Row<ChecklistDefinitionVersion>(definition.Id); version.DepartmentId = department; version.Version = 1; await store.WriteAsync(version, true);
				var occurrence = Row<ChecklistOccurrence>(definition.Id); occurrence.DepartmentId = department; occurrence.VersionId = version.Id; occurrence.CompletionId = Guid.NewGuid().ToString(); occurrence.TargetId = "77"; await store.WriteAsync(occurrence, true);
				var completion = Row<ChecklistCompletion>(definition.Id); completion.DepartmentId = department; completion.VersionId = version.Id; completion.OccurrenceId = occurrence.Id; completion.TargetId = "77";
				return completion;
			}
			var existing = await Completion(77); await store.WriteAsync(existing, true);
			var foreign = await Completion(88); foreign.Id = existing.Id; uow.CommitChanges();
			(await store.GetAsync<ChecklistCompletion>(88, existing.Id)).Should().BeNull();
			await uow.CreateOrGetConnectionAsync();
			await FluentActions.Awaiting(() => store.WriteAsync(foreign, true)).Should().ThrowAsync<ChecklistException>().Where(e => e.StatusCode == 409);
			uow.DiscardChanges();
			(await store.GetAsync<ChecklistCompletion>(77, existing.Id)).Content.Should().Be(existing.Content);
		}
	}
}
