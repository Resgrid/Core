using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Resgrid.Web.Tts.Configuration;
using Resgrid.Web.Tts.Services;

namespace Resgrid.Tests.Web.Tts
{
	[TestFixture]
	public class PromptWarmupHostedServiceTests
	{
		[Test]
		public async Task execute_async_should_log_and_swallow_unexpected_warmup_failures()
		{
			var failure = new InvalidOperationException("warmup failed");
			var ttsService = new Mock<ITtsService>(MockBehavior.Strict);
			ttsService
				.Setup(x => x.WarmPromptsAsync(It.IsAny<CancellationToken>()))
				.ThrowsAsync(failure);
			var logger = new RecordingLogger<PromptWarmupHostedService>();
			var service = new PromptWarmupHostedService(
				ttsService.Object,
				Options.Create(new TtsOptions
				{
					WarmupEnabled = true,
					PreGeneratedPrompts = new List<string> { "Prompt 1" }
				}),
				logger);

			await InvokeExecuteAsync(service, CancellationToken.None);

			logger.Entries.Should().ContainSingle(x =>
				x.Level == LogLevel.Error &&
				x.Exception == failure &&
				x.Message == "TTS prompt warmup failed but will not stop host.");
			ttsService.Verify(x => x.WarmPromptsAsync(It.IsAny<CancellationToken>()), Times.Once);
		}

		private static Task InvokeExecuteAsync(PromptWarmupHostedService service, CancellationToken cancellationToken)
		{
			var method = typeof(PromptWarmupHostedService).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic);
			return (Task)method!.Invoke(service, new object[] { cancellationToken })!;
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
