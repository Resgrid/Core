using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	namespace FeatureToggleUsageFlushTests
	{
		[TestFixture]
		public class the_usage_buffer
		{
			private static readonly DateTime Today = new DateTime(2026, 11, 3, 14, 30, 0, DateTimeKind.Utc);

			[Test]
			public void should_keep_one_entry_per_flag_department_and_day()
			{
				var buffer = new FeatureFlagUsageBuffer();

				buffer.Record(1, 5, Today, true);
				buffer.Record(1, 5, Today.AddHours(2), false);
				buffer.Record(1, 5, Today, null);
				buffer.Record(1, 6, Today, true);
				buffer.Record(1, 5, Today.AddDays(1), true);
				buffer.Record(2, 5, Today, true);

				var drained = buffer.Drain();

				drained.Should().HaveCount(4);
				var entry = drained.Single(u => u.FeatureFlagId == 1 && u.DepartmentId == 5 && u.UsageDate == Today.Date);
				entry.EvaluationCount.Should().Be(3);
				entry.EnabledCount.Should().Be(1);
				entry.DisabledCount.Should().Be(1);
			}

			[Test]
			public void draining_should_empty_it()
			{
				var buffer = new FeatureFlagUsageBuffer();
				buffer.Record(1, 5, Today, true);

				buffer.Drain().Should().HaveCount(1);

				buffer.Count.Should().Be(0);
				buffer.Drain().Should().BeEmpty();
			}

			[Test]
			public void a_restored_entry_should_add_to_evaluations_recorded_since_the_drain()
			{
				var buffer = new FeatureFlagUsageBuffer();
				buffer.Record(1, 5, Today, true);
				buffer.Record(1, 5, Today, false);
				var drained = buffer.Drain().Single();

				buffer.Record(1, 5, Today, true);
				buffer.Restore(drained);

				var merged = buffer.Drain().Single();
				merged.EvaluationCount.Should().Be(3);
				merged.EnabledCount.Should().Be(2);
				merged.DisabledCount.Should().Be(1);
			}

			[Test]
			public async Task should_not_lose_evaluations_recorded_while_it_is_being_drained()
			{
				const int writers = 8;
				const int perWriter = 50000;
				var buffer = new FeatureFlagUsageBuffer();
				long drainedTotal = 0;
				long drainedEnabled = 0;

				var writing = Enumerable.Range(0, writers).Select(w => Task.Run(() =>
				{
					for (var i = 0; i < perWriter; i++)
						buffer.Record(1 + (i % 3), w % 2, Today, i % 2 == 0);
				})).ToArray();

				var allWritten = Task.WhenAll(writing);
				while (!allWritten.IsCompleted)
				{
					foreach (var usage in buffer.Drain())
					{
						drainedTotal += usage.EvaluationCount;
						drainedEnabled += usage.EnabledCount;
					}
				}

				await allWritten;
				foreach (var usage in buffer.Drain())
				{
					drainedTotal += usage.EvaluationCount;
					drainedEnabled += usage.EnabledCount;
				}

				drainedTotal.Should().Be(writers * perWriter);
				drainedEnabled.Should().Be(writers * perWriter / 2);
			}
		}

		/// <summary>
		/// Drives the real service's static buffer through evaluations, so each test uses flag ids no other test evaluates
		/// and only looks at the usage rows for those ids.
		/// </summary>
		[TestFixture, NonParallelizable]
		public class when_flushing_evaluations : TestBase
		{
			private Mock<IFeatureFlagRepository> _flags;
			private Mock<IFeatureFlagUsageRepository> _usage;
			private List<FeatureFlagUsage> _saved;
			private IFeatureToggleService _service;

			protected override void Before_all_tests()
			{
				_flags = new Mock<IFeatureFlagRepository>();
				_usage = new Mock<IFeatureFlagUsageRepository>();
				_saved = new List<FeatureFlagUsage>();
				_usage.Setup(x => x.AddUsageAsync(It.IsAny<FeatureFlagUsage>(), It.IsAny<CancellationToken>()))
					.Callback<FeatureFlagUsage, CancellationToken>((u, _) => _saved.Add(u))
					.Returns(Task.CompletedTask);

				_service = new FeatureToggleService(_flags.Object, new Mock<IFeatureFlagOverrideRepository>().Object,
					new Mock<IFeatureFlagTargetingRuleRepository>().Object, new Mock<IFeatureFlagPrerequisiteRepository>().Object,
					_usage.Object, new Mock<ICacheProvider>().Object, new Mock<IEventAggregator>().Object,
					new Mock<ISubscriptionsService>().Object, new Mock<IDepartmentsService>().Object);
			}

			private FeatureFlag GivenFlag(int id, bool enabled = true)
			{
				var flag = new FeatureFlag { FeatureFlagId = id, FlagKey = "Tests.UsageFlush." + id, IsEnabledGlobally = enabled };
				_flags.Setup(x => x.GetAllAsync()).ReturnsAsync(new List<FeatureFlag> { flag });
				return flag;
			}

			[Test]
			public async Task should_add_each_department_and_days_counts_and_touch_the_flag()
			{
				var flag = GivenFlag(9100001);
				await _service.IsEnabledAsync(flag.FlagKey, 5);
				await _service.IsEnabledAsync(flag.FlagKey, 5);
				await _service.IsEnabledAsync(flag.FlagKey, 5);
				await _service.IsEnabledAsync(flag.FlagKey, 6);

				await _service.FlushEvaluationsAsync();

				var rows = _saved.Where(u => u.FeatureFlagId == flag.FeatureFlagId).ToList();
				rows.Should().HaveCount(2);
				var department5 = rows.Single(u => u.DepartmentId == 5);
				department5.EvaluationCount.Should().Be(3);
				department5.EnabledCount.Should().Be(3);
				department5.DisabledCount.Should().Be(0);
				department5.UsageDate.Should().Be(DateTime.UtcNow.Date);
				rows.Single(u => u.DepartmentId == 6).EvaluationCount.Should().Be(1);
				_flags.Verify(x => x.TouchEvaluationAsync(flag.FeatureFlagId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			}

			[Test]
			public async Task a_second_flush_should_not_write_the_same_counts_again()
			{
				var flag = GivenFlag(9100002);
				await _service.IsEnabledAsync(flag.FlagKey, 5);

				await _service.FlushEvaluationsAsync();
				await _service.FlushEvaluationsAsync();

				_saved.Count(u => u.FeatureFlagId == flag.FeatureFlagId).Should().Be(1);
			}

			[Test]
			public async Task an_evaluation_outside_any_department_should_be_stored_without_one()
			{
				var flag = GivenFlag(9100003, enabled: false);
				await _service.IsEnabledAsync(flag.FlagKey, 0);

				await _service.FlushEvaluationsAsync();

				var row = _saved.Single(u => u.FeatureFlagId == flag.FeatureFlagId);
				row.DepartmentId.Should().BeNull();
				row.DisabledCount.Should().Be(1);
			}

			[Test]
			public async Task counts_that_fail_to_save_should_be_written_by_the_next_flush()
			{
				var flag = GivenFlag(9100004);
				await _service.IsEnabledAsync(flag.FlagKey, 5);
				await _service.IsEnabledAsync(flag.FlagKey, 5);
				_usage.Setup(x => x.AddUsageAsync(It.Is<FeatureFlagUsage>(u => u.FeatureFlagId == flag.FeatureFlagId), It.IsAny<CancellationToken>()))
					.ThrowsAsync(new InvalidOperationException("database down"));

				Func<Task> failingFlush = () => _service.FlushEvaluationsAsync();
				await failingFlush.Should().NotThrowAsync();
				_saved.Should().NotContain(u => u.FeatureFlagId == flag.FeatureFlagId);

				_usage.Setup(x => x.AddUsageAsync(It.Is<FeatureFlagUsage>(u => u.FeatureFlagId == flag.FeatureFlagId), It.IsAny<CancellationToken>()))
					.Callback<FeatureFlagUsage, CancellationToken>((u, _) => _saved.Add(u))
					.Returns(Task.CompletedTask);
				await _service.IsEnabledAsync(flag.FlagKey, 5);
				await _service.FlushEvaluationsAsync();

				_saved.Single(u => u.FeatureFlagId == flag.FeatureFlagId).EvaluationCount.Should().Be(3);
			}

			[Test]
			public async Task a_failed_save_should_not_stop_the_other_rows_or_the_touch()
			{
				var flag = GivenFlag(9100005);
				await _service.IsEnabledAsync(flag.FlagKey, 5);
				await _service.IsEnabledAsync(flag.FlagKey, 6);
				_usage.Setup(x => x.AddUsageAsync(It.Is<FeatureFlagUsage>(u => u.FeatureFlagId == flag.FeatureFlagId && u.DepartmentId == 5), It.IsAny<CancellationToken>()))
					.ThrowsAsync(new InvalidOperationException("constraint"));

				await _service.FlushEvaluationsAsync();

				_saved.Should().ContainSingle(u => u.FeatureFlagId == flag.FeatureFlagId && u.DepartmentId == 6);
				_flags.Verify(x => x.TouchEvaluationAsync(flag.FeatureFlagId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);

				// Leave nothing for later tests: the failed row was put back, so let it through.
				_usage.Setup(x => x.AddUsageAsync(It.IsAny<FeatureFlagUsage>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
				await _service.FlushEvaluationsAsync();
			}

			[Test]
			public async Task usage_reads_should_query_the_flag_and_date_range()
			{
				var flag = GivenFlag(9100006);
				var from = new DateTime(2026, 10, 1, 13, 0, 0);
				var to = new DateTime(2026, 10, 31, 9, 0, 0);
				_usage.Setup(x => x.GetUsageForFlagAsync(flag.FeatureFlagId, from.Date, to.Date))
					.ReturnsAsync(new List<FeatureFlagUsage> { new FeatureFlagUsage { FeatureFlagId = flag.FeatureFlagId, EvaluationCount = 7 } });

				var usage = await _service.GetUsageForFlagAsync(flag.FlagKey, from, to);

				usage.Should().ContainSingle().Which.EvaluationCount.Should().Be(7);
				_usage.Verify(x => x.GetAllAsync(), Times.Never);
			}
		}

		[TestFixture, NonParallelizable]
		public class the_usage_flusher
		{
			private int _savedInterval;

			[SetUp]
			public void SetUp()
			{
				_savedInterval = FeatureFlagsConfig.EvaluationFlushIntervalSeconds;
			}

			[TearDown]
			public void TearDown()
			{
				FeatureFlagsConfig.EvaluationFlushIntervalSeconds = _savedInterval;
			}

			private static IContainer ContainerFor(Func<IFeatureToggleService> create)
			{
				var builder = new ContainerBuilder();
				builder.Register(_ => create()).As<IFeatureToggleService>().InstancePerLifetimeScope();
				return builder.Build();
			}

			[Test]
			public async Task should_flush_on_every_interval_and_once_more_when_stopped()
			{
				var tokens = new ConcurrentQueue<CancellationToken>();
				var twoTicks = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				var service = new Mock<IFeatureToggleService>();
				service.Setup(x => x.FlushEvaluationsAsync(It.IsAny<CancellationToken>()))
					.Callback<CancellationToken>(ct =>
					{
						tokens.Enqueue(ct);
						if (tokens.Count >= 2)
							twoTicks.TrySetResult(true);
					})
					.ReturnsAsync(0);

				using var container = ContainerFor(() => service.Object);
				using var stopping = new CancellationTokenSource();
				var running = new FeatureToggleUsageFlusher(container, TimeSpan.FromMilliseconds(20)).RunAsync(stopping.Token);

				(await Task.WhenAny(twoTicks.Task, Task.Delay(TimeSpan.FromSeconds(10)))).Should().Be(twoTicks.Task);
				stopping.Cancel();
				(await Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds(10)))).Should().Be(running);
				await running;

				var calls = tokens.ToList();
				calls.Count.Should().BeGreaterThanOrEqualTo(3);
				calls.Take(calls.Count - 1).Should().OnlyContain(t => t == stopping.Token);
				var shutdownFlush = calls.Last();
				shutdownFlush.Should().NotBe(stopping.Token);
				shutdownFlush.IsCancellationRequested.Should().BeFalse("the final flush runs on its own budget, not the cancelled stopping token");
			}

			[Test]
			public async Task should_flush_from_a_fresh_lifetime_scope_each_time()
			{
				var created = 0;
				using var container = ContainerFor(() =>
				{
					Interlocked.Increment(ref created);
					return new Mock<IFeatureToggleService>().Object;
				});
				var flusher = new FeatureToggleUsageFlusher(container, TimeSpan.FromMinutes(1));

				await flusher.FlushAsync(CancellationToken.None);
				await flusher.FlushAsync(CancellationToken.None);

				created.Should().Be(2);
			}

			[Test]
			public async Task a_failing_flush_should_not_stop_the_loop()
			{
				var calls = 0;
				var secondCall = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				var service = new Mock<IFeatureToggleService>();
				service.Setup(x => x.FlushEvaluationsAsync(It.IsAny<CancellationToken>()))
					.Returns<CancellationToken>(_ =>
					{
						if (Interlocked.Increment(ref calls) == 1)
							throw new InvalidOperationException("database down");
						secondCall.TrySetResult(true);
						return Task.FromResult(0);
					});

				using var container = ContainerFor(() => service.Object);
				using var stopping = new CancellationTokenSource();
				var running = new FeatureToggleUsageFlusher(container, TimeSpan.FromMilliseconds(20)).RunAsync(stopping.Token);

				(await Task.WhenAny(secondCall.Task, Task.Delay(TimeSpan.FromSeconds(10)))).Should().Be(secondCall.Task);
				stopping.Cancel();
				await running;
			}

			[TestCase(60, 60)]
			[TestCase(300, 300)]
			[TestCase(1, 5)]
			[TestCase(0, 60)]
			[TestCase(-1, 60)]
			public void the_configured_interval_should_never_switch_flushing_off(int configuredSeconds, int expectedSeconds)
			{
				FeatureFlagsConfig.EvaluationFlushIntervalSeconds = configuredSeconds;

				FeatureToggleUsageFlusher.ConfiguredInterval.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
			}
		}
	}
}
