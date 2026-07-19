using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Quidjibo.Misc;
using Resgrid.Config;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Console.Tasks;

namespace Resgrid.Tests.Workers.Console.Tasks
{
	[TestFixture]
	[NonParallelizable]
	public class TtsStaticPromptRefreshTaskTests
	{
		private static readonly FieldInfo WorkerBootstrapperContainerField = typeof(Resgrid.Workers.Framework.Bootstrapper)
			.GetField("_container", BindingFlags.Static | BindingFlags.NonPublic)!;

		private IContainer _originalWorkerContainer;
		private IContainer _testWorkerContainer;
		private string _originalServiceBaseUrl;
		private string _originalStaticPromptAdminKey;
		private TimeSpan _originalRetryDelay;

		[SetUp]
		public void SetUp()
		{
			_originalWorkerContainer = WorkerBootstrapperContainerField.GetValue(null) as IContainer;
			_originalServiceBaseUrl = TtsConfig.ServiceBaseUrl;
			_originalStaticPromptAdminKey = TtsConfig.StaticPromptAdminKey;
			_originalRetryDelay = TtsStaticPromptRefreshTask.RetryDelay;

			TtsConfig.ServiceBaseUrl = "https://tts.example.com";
			TtsConfig.StaticPromptAdminKey = "prompt-admin-key";
			TtsStaticPromptRefreshTask.RetryDelay = TimeSpan.FromMilliseconds(1);
		}

		[TearDown]
		public void TearDown()
		{
			WorkerBootstrapperContainerField.SetValue(null, _originalWorkerContainer);
			_testWorkerContainer?.Dispose();
			TtsConfig.ServiceBaseUrl = _originalServiceBaseUrl;
			TtsConfig.StaticPromptAdminKey = _originalStaticPromptAdminKey;
			TtsStaticPromptRefreshTask.RetryDelay = _originalRetryDelay;
		}

		[Test]
		public async Task process_async_should_throw_after_all_retries_exhausted()
		{
			// Arrange
			var failure = new InvalidOperationException("refresh failed");
			var ttsAudioService = new Mock<ITtsAudioService>(MockBehavior.Strict);
			ttsAudioService
				.Setup(x => x.RegenerateStaticPromptsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(failure);
			SetWorkerContainer(ttsAudioService.Object);

			var task = new TestableTtsStaticPromptRefreshTask(Mock.Of<ILogger>());
			var progress = new Mock<IQuidjiboProgress>(MockBehavior.Loose);

			// Act / Assert
			await FluentActions
				.Awaiting(() => task.ProcessAsync(new TtsStaticPromptRefreshCommand(1), progress.Object, CancellationToken.None))
				.Should()
				.ThrowAsync<InvalidOperationException>()
				.WithMessage("refresh failed");

			ttsAudioService.Verify(
				x => x.RegenerateStaticPromptsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
				Times.Exactly(3));
			task.ReportedExceptions.Should().ContainSingle().Which.Should().BeSameAs(failure);
		}

		[Test]
		public async Task process_async_should_rethrow_cancellation()
		{
			// Arrange
			using var cancellationTokenSource = new CancellationTokenSource();
			var ttsAudioService = new Mock<ITtsAudioService>(MockBehavior.Strict);
			ttsAudioService
				.Setup(x => x.RegenerateStaticPromptsAsync(It.IsAny<IEnumerable<string>>(), It.Is<CancellationToken>(token => token == cancellationTokenSource.Token)))
				.ThrowsAsync(new OperationCanceledException(cancellationTokenSource.Token));
			SetWorkerContainer(ttsAudioService.Object);

			var task = new TestableTtsStaticPromptRefreshTask(Mock.Of<ILogger>());
			var progress = new Mock<IQuidjiboProgress>(MockBehavior.Loose);

			// Act / Assert
			await FluentActions
				.Awaiting(() => task.ProcessAsync(new TtsStaticPromptRefreshCommand(1), progress.Object, cancellationTokenSource.Token))
				.Should()
				.ThrowAsync<OperationCanceledException>();

			ttsAudioService.Verify(
				x => x.RegenerateStaticPromptsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
				Times.Once);
			task.ReportedExceptions.Should().BeEmpty();
		}

		[Test]
		public async Task process_async_should_succeed_on_retry_after_initial_failure()
		{
			// Arrange
			var ttsAudioService = new Mock<ITtsAudioService>(MockBehavior.Strict);
			var callCount = 0;
			ttsAudioService
				.Setup(x => x.RegenerateStaticPromptsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
				.Returns(() =>
				{
					callCount++;
					if (callCount == 1)
						throw new InvalidOperationException("transient failure");

					return Task.CompletedTask;
				});
			SetWorkerContainer(ttsAudioService.Object);

			var task = new TestableTtsStaticPromptRefreshTask(Mock.Of<ILogger>());
			var progress = new Mock<IQuidjiboProgress>(MockBehavior.Loose);

			// Act / Assert
			await FluentActions
				.Awaiting(() => task.ProcessAsync(new TtsStaticPromptRefreshCommand(1), progress.Object, CancellationToken.None))
				.Should()
				.NotThrowAsync();

			ttsAudioService.Verify(
				x => x.RegenerateStaticPromptsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
				Times.Exactly(2));
			task.ReportedExceptions.Should().BeEmpty();
		}

		private void SetWorkerContainer(ITtsAudioService ttsAudioService)
		{
			_testWorkerContainer?.Dispose();

			var builder = new ContainerBuilder();
			builder.RegisterInstance(ttsAudioService).As<ITtsAudioService>();
			_testWorkerContainer = builder.Build();

			WorkerBootstrapperContainerField.SetValue(null, _testWorkerContainer);
		}

		private sealed class TestableTtsStaticPromptRefreshTask : TtsStaticPromptRefreshTask
		{
			public TestableTtsStaticPromptRefreshTask(ILogger logger)
				: base(logger)
			{
			}

			public List<Exception> ReportedExceptions { get; } = new List<Exception>();

			protected override void ReportTerminalFailure(Exception exception)
			{
				ReportedExceptions.Add(exception);
			}
		}
	}
}
