using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Quidjibo.Misc;
using Quidjibo.Models;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Console.SystemOperations;
using Resgrid.Workers.Console.Tasks;

namespace Resgrid.Tests.Workers.Console
{
	[TestFixture]
	[NonParallelizable]
	public class SystemOperationsTests
	{
		private static readonly FieldInfo WorkerBootstrapperContainerField = typeof(Resgrid.Workers.Framework.Bootstrapper)
			.GetField("_container", BindingFlags.Static | BindingFlags.NonPublic)!;

		private IContainer _originalWorkerContainer;
		private IContainer _testWorkerContainer;
		private bool _originalCacheEnabled;
		private bool _originalUtf8CleanupEnabled;
		private bool _originalLocationRetentionEnabled;
		private string _originalTtsBaseUrl;
		private string _originalTtsAdminKey;

		[SetUp]
		public void SetUp()
		{
			_originalWorkerContainer = WorkerBootstrapperContainerField.GetValue(null) as IContainer;
			_originalCacheEnabled = SystemBehaviorConfig.CacheEnabled;
			_originalUtf8CleanupEnabled = SystemBehaviorConfig.Utf8CleanupEnabled;
			_originalLocationRetentionEnabled = UnitTrackingConfig.LocationRetentionWorkerEnabled;
			_originalTtsBaseUrl = TtsConfig.ServiceBaseUrl;
			_originalTtsAdminKey = TtsConfig.StaticPromptAdminKey;
		}

		[TearDown]
		public void TearDown()
		{
			WorkerBootstrapperContainerField.SetValue(null, _originalWorkerContainer);
			_testWorkerContainer?.Dispose();
			_testWorkerContainer = null;
			SystemBehaviorConfig.CacheEnabled = _originalCacheEnabled;
			SystemBehaviorConfig.Utf8CleanupEnabled = _originalUtf8CleanupEnabled;
			UnitTrackingConfig.LocationRetentionWorkerEnabled = _originalLocationRetentionEnabled;
			TtsConfig.ServiceBaseUrl = _originalTtsBaseUrl;
			TtsConfig.StaticPromptAdminKey = _originalTtsAdminKey;
		}

		#region Catalog, runner and schedule stay in step

		[Test]
		public void Every_operation_type_has_one_catalog_entry()
		{
			var types = Enum.GetValues<SystemOperationTypes>();

			SystemOperationCatalog.All.Select(x => x.Type).Should().OnlyHaveUniqueItems();
			SystemOperationCatalog.All.Select(x => x.Type).Should().BeEquivalentTo(types);
			SystemOperationCatalog.All.Should().OnlyContain(x => !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.Description));
		}

		[Test]
		public void The_worker_can_run_every_catalog_entry()
		{
			SystemOperationRunner.SupportedTypes.Should().BeEquivalentTo(SystemOperationCatalog.All.Select(x => x.Type));
		}

		[Test]
		public void Every_scheduled_job_the_catalog_mirrors_is_scheduled_under_that_command_id()
		{
			var program = FindRepositoryFile(Path.Combine("Workers", "Resgrid.Workers.Console", "Program.cs"));
			if (program == null)
			{
				Assert.Inconclusive("Workers.Console Program.cs not found relative to the test assembly; source-scan check skipped.");
				return;
			}

			var source = System.IO.File.ReadAllText(program);

			foreach (var descriptor in SystemOperationCatalog.All.Where(x => x.WorkerCommandId.HasValue))
				Regex.IsMatch(source, $@"Command\s*\(\s*{descriptor.WorkerCommandId.Value}\s*\)").Should()
					.BeTrue($"{descriptor.Type} says it runs worker {descriptor.WorkerCommandId} early, so Program.cs must schedule that command");

			Regex.IsMatch(source, @"SystemOperationsCommand\s*\(\s*76\s*\)").Should().BeTrue("worker 76 runs the requests");
		}

		[Test]
		public void Only_cache_operations_can_target_one_department()
		{
			SystemOperationCatalog.All.Where(x => x.SupportsDepartmentScope).Select(x => x.Type).Should()
				.BeEquivalentTo(new[] { SystemOperationTypes.RebuildSecurityMatrices, SystemOperationTypes.ClearDepartmentCaches });
		}

		#endregion

		#region Runner

		[Test]
		public async Task An_operation_this_build_does_not_know_fails_instead_of_running()
		{
			var outcome = await new SystemOperationRunner(Mock.Of<ILogger>()).RunAsync(Request((SystemOperationTypes)999), new SystemOperationProgress(), CancellationToken.None);

			outcome.Succeeded.Should().BeFalse();
			outcome.Message.Should().Contain("999");
		}

		[Test]
		public async Task A_department_target_on_a_system_wide_job_is_refused()
		{
			var outcome = await new SystemOperationRunner(Mock.Of<ILogger>()).RunAsync(Request(SystemOperationTypes.ChatRetention, 12),
				new SystemOperationProgress(), CancellationToken.None);

			outcome.Succeeded.Should().BeFalse();
			outcome.Message.Should().Contain("cannot target one department");
		}

		[Test]
		public async Task Jobs_turned_off_in_the_worker_configuration_are_not_run()
		{
			SystemBehaviorConfig.Utf8CleanupEnabled = false;
			UnitTrackingConfig.LocationRetentionWorkerEnabled = false;
			TtsConfig.ServiceBaseUrl = null;
			var runner = new SystemOperationRunner(Mock.Of<ILogger>());

			foreach (var type in new[] { SystemOperationTypes.Utf8Cleanup, SystemOperationTypes.UnitTrackingLocationRetention, SystemOperationTypes.RefreshTtsStaticPrompts })
			{
				var outcome = await runner.RunAsync(Request(type), new SystemOperationProgress(), CancellationToken.None);

				outcome.Succeeded.Should().BeFalse();
				outcome.Message.Should().StartWith("Not run:");
			}
		}

		[Test]
		public async Task Cache_operations_do_not_report_success_when_nothing_can_be_written()
		{
			var runner = new SystemOperationRunner(Mock.Of<ILogger>(), () => Scope(cacheConnected: false));

			SystemBehaviorConfig.CacheEnabled = false;
			var disabled = await runner.RunAsync(Request(SystemOperationTypes.RebuildSecurityMatrices), new SystemOperationProgress(), CancellationToken.None);
			disabled.Succeeded.Should().BeFalse();
			disabled.Message.Should().Contain("CacheEnabled");

			SystemBehaviorConfig.CacheEnabled = true;
			var unreachable = await runner.RunAsync(Request(SystemOperationTypes.ClearDepartmentCaches, 12), new SystemOperationProgress(), CancellationToken.None);
			unreachable.Succeeded.Should().BeFalse();
			unreachable.Message.Should().Contain("not reachable");
		}

		[Test]
		public async Task Clearing_every_department_reports_progress_and_the_departments_that_failed()
		{
			SystemBehaviorConfig.CacheEnabled = true;
			var operations = new Mock<ISystemOperationsService>();
			operations.Setup(x => x.ClearDepartmentCachesAsync(It.IsAny<int>())).ReturnsAsync(new List<string>());
			operations.Setup(x => x.ClearDepartmentCachesAsync(2)).ReturnsAsync(new List<string> { "groups" });
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetAllAsync()).ReturnsAsync(new List<Department>
			{
				new Department { DepartmentId = 1 }, new Department { DepartmentId = 2 }, new Department { DepartmentId = 3 }
			});
			var runner = new SystemOperationRunner(Mock.Of<ILogger>(), () => Scope(cacheConnected: true, operations.Object, departments.Object));
			var progress = new SystemOperationProgress();

			var outcome = await runner.RunAsync(Request(SystemOperationTypes.ClearDepartmentCaches), progress, CancellationToken.None);

			outcome.Succeeded.Should().BeFalse();
			outcome.Message.Should().Contain("1 of 3").And.Contain("2 (groups)");
			progress.Latest.Should().Be("Cleared 3 of 3 departments.");
			operations.Verify(x => x.ClearDepartmentCachesAsync(It.IsAny<int>()), Times.Exactly(3));
		}

		[Test]
		public async Task Clearing_one_department_touches_only_that_department()
		{
			SystemBehaviorConfig.CacheEnabled = true;
			var operations = new Mock<ISystemOperationsService>();
			operations.Setup(x => x.ClearDepartmentCachesAsync(12)).ReturnsAsync(new List<string>());
			var runner = new SystemOperationRunner(Mock.Of<ILogger>(), () => Scope(cacheConnected: true, operations.Object));

			var outcome = await runner.RunAsync(Request(SystemOperationTypes.ClearDepartmentCaches, 12), new SystemOperationProgress(), CancellationToken.None);

			outcome.Succeeded.Should().BeTrue();
			operations.Verify(x => x.ClearDepartmentCachesAsync(12), Times.Once);
			operations.Verify(x => x.ClearDepartmentCachesAsync(It.Is<int>(id => id != 12)), Times.Never);
		}

		[Test]
		public void The_job_logger_remembers_the_first_error_line_and_passes_everything_through()
		{
			var inner = new Mock<ILogger>();
			var logger = new ErrorCapturingLogger(inner.Object);

			logger.LogInformation("starting");
			logger.LogError("System.InvalidOperationException: first failure\r\n   at Somewhere()");
			logger.LogError("second failure");

			logger.FirstError.Should().Be("System.InvalidOperationException: first failure");
			inner.Verify(x => x.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
				It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Exactly(3));
		}

		[Test]
		public void Progress_keeps_the_latest_non_blank_line_from_either_report_shape()
		{
			var progress = new SystemOperationProgress();

			progress.Report(10, "first");
			progress.Report(20, "  ");
			((IQuidjiboProgress)progress).Report(new Tracker(30, "from tracker"));

			progress.Latest.Should().Be("from tracker");
		}

		#endregion

		#region Worker 76 tick

		[Test]
		public async Task A_tick_queues_a_matrix_rebuild_when_redis_lost_its_data_and_records_each_claimed_request()
		{
			SystemBehaviorConfig.Utf8CleanupEnabled = false;
			var claimed = Request(SystemOperationTypes.Utf8Cleanup);
			var operations = new Mock<ISystemOperationsService>();
			operations.Setup(x => x.DetectCacheDataLossAsync()).ReturnsAsync(true);
			operations.Setup(x => x.RequestAsync(It.IsAny<SystemOperationTypes>(), It.IsAny<int?>(), It.IsAny<SystemOperationSources>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new SystemOperationRequestResult { Created = true, Request = Request(SystemOperationTypes.RebuildSecurityMatrices) });
			operations.SetupSequence(x => x.ClaimNextRequestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(claimed)
				.ReturnsAsync((SystemOperationRequest)null);
			SetWorkerContainer(operations.Object);

			await new SystemOperationsTask(Mock.Of<ILogger>()).ProcessAsync(new SystemOperationsCommand(76), Mock.Of<IQuidjiboProgress>(), CancellationToken.None);

			operations.Verify(x => x.FailAbandonedRequestsAsync(It.IsAny<CancellationToken>()), Times.Once);
			operations.Verify(x => x.RequestAsync(SystemOperationTypes.RebuildSecurityMatrices, null, SystemOperationSources.CacheDataLossDetected,
				"system", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
			operations.Verify(x => x.CompleteRequestAsync(claimed.SystemOperationRequestId, false, It.Is<string>(m => m.StartsWith("Not run:")),
				It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_tick_with_an_intact_cache_queues_nothing_of_its_own()
		{
			var operations = new Mock<ISystemOperationsService>();
			operations.Setup(x => x.DetectCacheDataLossAsync()).ReturnsAsync(false);
			SetWorkerContainer(operations.Object);

			await new SystemOperationsTask(Mock.Of<ILogger>()).ProcessAsync(new SystemOperationsCommand(76), Mock.Of<IQuidjiboProgress>(), CancellationToken.None);

			operations.Verify(x => x.RequestAsync(It.IsAny<SystemOperationTypes>(), It.IsAny<int?>(), It.IsAny<SystemOperationSources>(), It.IsAny<string>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			operations.Verify(x => x.ClaimNextRequestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		#endregion

		private static SystemOperationRequest Request(SystemOperationTypes type, int? departmentId = null) => new SystemOperationRequest
		{
			SystemOperationRequestId = Guid.NewGuid().ToString(),
			OperationType = (int)type,
			TargetDepartmentId = departmentId,
			Status = (int)SystemOperationStatuses.Running,
			RequestedBy = "ops@resgrid.com"
		};

		private static ILifetimeScope Scope(bool cacheConnected, ISystemOperationsService operations = null, IDepartmentsService departments = null)
		{
			var cache = new Mock<ICacheProvider>();
			cache.Setup(x => x.IsConnected()).Returns(cacheConnected);

			var builder = new ContainerBuilder();
			builder.RegisterInstance(cache.Object).As<ICacheProvider>();
			builder.RegisterInstance(operations ?? Mock.Of<ISystemOperationsService>()).As<ISystemOperationsService>();
			builder.RegisterInstance(departments ?? Mock.Of<IDepartmentsService>()).As<IDepartmentsService>();

			return builder.Build();
		}

		private void SetWorkerContainer(ISystemOperationsService operations)
		{
			var builder = new ContainerBuilder();
			builder.RegisterInstance(operations).As<ISystemOperationsService>();
			_testWorkerContainer = builder.Build();
			WorkerBootstrapperContainerField.SetValue(null, _testWorkerContainer);
		}

		private static string FindRepositoryFile(string relativePath)
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null)
			{
				var candidate = Path.Combine(directory.FullName, relativePath);
				if (System.IO.File.Exists(candidate))
					return candidate;
				directory = directory.Parent;
			}

			return null;
		}
	}
}
