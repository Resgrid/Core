using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Resgrid.Workers.Console;

namespace Resgrid.Tests.Workers.Console
{
	/// <summary>
	/// BackgroundService.StartAsync runs ExecuteAsync inline until its first real yield and Host.StartAsync starts services
	/// one at a time, so a service that blocks before yielding holds every later service back. The events pod hit exactly
	/// that: ScheduledJobsService blocked on WaitHandle.WaitOne, AdminAssistTraceService only started at SIGTERM with a
	/// cancelled token, and the cancellation escaped Host.StartAsync and crashed the process (Sentry, TaskCanceledException
	/// in AdminAssistTraceService.PublishAsync).
	/// </summary>
	[TestFixture]
	public class HostedServiceStartupTests
	{
		[Test]
		public async Task AdminAssistTraceService_StartedWithCancelledToken_CompletesWithoutThrowing()
		{
			var service = new AdminAssistTraceService(new DatabaseUpgradeState(false), NullLogger<AdminAssistTraceService>.Instance);
			using var cts = new CancellationTokenSource();
			cts.Cancel();

			Func<Task> start = () => service.StartAsync(cts.Token);

			await start.Should().NotThrowAsync();
			service.ExecuteTask.Should().NotBeNull();
			service.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
		}

		[Test]
		public void WorkerHostedServices_DoNotBlockOnTheStoppingToken()
		{
			var program = File.ReadAllText(FindRepositoryFile(Path.Combine("Workers", "Resgrid.Workers.Console", "Program.cs")));

			program.Should().NotContain("WaitHandle.WaitOne",
				"a synchronous wait inside a hosted service's ExecuteAsync holds Host.StartAsync open, so services registered after it never start until shutdown");
		}

		private static string FindRepositoryFile(string relativePath)
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null)
			{
				var candidate = Path.Combine(directory.FullName, relativePath);
				if (File.Exists(candidate))
					return candidate;

				directory = directory.Parent;
			}

			throw new FileNotFoundException($"Could not find {relativePath} above the test directory.");
		}
	}
}
