using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Resgrid.Web.Tts.Configuration;
using Resgrid.Web.Tts.Services;

namespace Resgrid.Tests.Web.Tts
{
	[TestFixture]
	public class TempDirectorySweepHostedServiceTests
	{
		private string _tempRoot;

		[SetUp]
		public void SetUp()
		{
			_tempRoot = Path.Combine(Path.GetTempPath(), $"resgrid-tts-sweep-tests-{Guid.NewGuid():N}");
			Directory.CreateDirectory(_tempRoot);
		}

		[TearDown]
		public void TearDown()
		{
			if (Directory.Exists(_tempRoot))
			{
				Directory.Delete(_tempRoot, recursive: true);
			}
		}

		[Test]
		public void sweep_once_should_delete_orphaned_working_directories_older_than_the_max_age()
		{
			var orphan = CreateWorkingDirectory("aged", DateTime.UtcNow.AddHours(-7));

			var removed = CreateService().SweepOnce();

			removed.Should().Be(1);
			Directory.Exists(orphan).Should().BeFalse();
		}

		[Test]
		public void sweep_once_should_leave_directories_newer_than_the_max_age_alone()
		{
			// An in-flight synthesis writes into a directory whose mtime is minutes old at
			// most; the sweep must never collect one out from under a running job.
			var inFlight = CreateWorkingDirectory("in-flight", DateTime.UtcNow.AddMinutes(-5));

			var removed = CreateService().SweepOnce();

			removed.Should().Be(0);
			Directory.Exists(inFlight).Should().BeTrue();
		}

		[Test]
		public void sweep_once_should_delete_stale_loose_files()
		{
			var stalePath = Path.Combine(_tempRoot, "stale.wav");
			File.WriteAllBytes(stalePath, new byte[] { 1 });
			File.SetLastWriteTimeUtc(stalePath, DateTime.UtcNow.AddHours(-7));

			var removed = CreateService().SweepOnce();

			removed.Should().Be(1);
			File.Exists(stalePath).Should().BeFalse();
		}

		[Test]
		public void sweep_once_should_return_zero_when_the_temp_root_does_not_exist()
		{
			Directory.Delete(_tempRoot, recursive: true);

			CreateService().SweepOnce().Should().Be(0);
		}

		[Test]
		public void sweep_once_should_log_the_number_of_entries_it_removed()
		{
			CreateWorkingDirectory("aged-one", DateTime.UtcNow.AddHours(-7));
			CreateWorkingDirectory("aged-two", DateTime.UtcNow.AddHours(-8));
			CreateWorkingDirectory("fresh", DateTime.UtcNow);
			var logger = new RecordingLogger<TempDirectorySweepHostedService>();

			var removed = CreateService(logger).SweepOnce();

			removed.Should().Be(2);
			logger.Entries.Should().ContainSingle(x =>
				x.Level == LogLevel.Information &&
				x.Message.Contains("Swept 2 orphaned TTS temp entries"));
		}

		private string CreateWorkingDirectory(string name, DateTime lastWriteUtc)
		{
			var path = Path.Combine(_tempRoot, name);
			Directory.CreateDirectory(path);
			File.WriteAllBytes(Path.Combine(path, "raw.wav"), new byte[] { 1 });
			Directory.SetLastWriteTimeUtc(path, lastWriteUtc);
			return path;
		}

		private TempDirectorySweepHostedService CreateService(ILogger<TempDirectorySweepHostedService> logger = null)
		{
			return new TempDirectorySweepHostedService(
				Options.Create(new TtsOptions
				{
					TempDirectory = _tempRoot,
					TempDirectorySweepHours = 6
				}),
				logger ?? NullLogger<TempDirectorySweepHostedService>.Instance);
		}

		private sealed class RecordingLogger<T> : ILogger<T>
		{
			public List<LogEntry> Entries { get; } = new();

			public IDisposable BeginScope<TState>(TState state)
			{
				return NullScope.Instance;
			}

			public bool IsEnabled(LogLevel logLevel)
			{
				return true;
			}

			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
			{
				Entries.Add(new LogEntry(logLevel, exception, formatter(state, exception)));
			}

			public sealed record LogEntry(LogLevel Level, Exception Exception, string Message);

			private sealed class NullScope : IDisposable
			{
				public static readonly NullScope Instance = new();

				public void Dispose()
				{
				}
			}
		}
	}
}
