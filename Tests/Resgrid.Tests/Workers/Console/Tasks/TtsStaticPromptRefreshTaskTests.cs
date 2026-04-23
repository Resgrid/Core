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
	public class TtsStaticPromptRefreshTaskTests
	{
		private static readonly FieldInfo WorkerBootstrapperContainerField = typeof(Resgrid.Workers.Framework.Bootstrapper)
			.GetField("_container", BindingFlags.Static | BindingFlags.NonPublic)!;

		private IContainer _originalWorkerContainer;
		private IContainer _testWorkerContainer;
		private string _originalServiceBaseUrl;
		private string _originalStaticPromptAdminKey;

		[SetUp]
		public void SetUp()
		{
			_originalWorkerContainer = WorkerBootstrapperContainerField.GetValue(null) as IContainer;
			_originalServiceBaseUrl = TtsConfig.ServiceBaseUrl;
			_originalStaticPromptAdminKey = TtsConfig.StaticPromptAdminKey;

			TtsConfig.ServiceBaseUrl = "https://tts.example.com";
			TtsConfig.StaticPromptAdminKey = "prompt-admin-key";
		}

		[TearDown]
		public void TearDown()
		{
			WorkerBootstrapperContainerField.SetValue(null, _originalWorkerContainer);
			_testWorkerContainer?.Dispose();
			TtsConfig.ServiceBaseUrl = _originalServiceBaseUrl;
			TtsConfig.StaticPromptAdminKey = _originalStaticPromptAdminKey;
		}

		[Test]
		public async Task process_async_should_rethrow_refresh_failures()
		{
			var failure = new InvalidOperationException("refresh failed");
			var ttsAudioService = new Mock<ITtsAudioService>(MockBehavior.Strict);
			ttsAudioService
				.Setup(x => x.RegenerateStaticPromptsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(failure);
			SetWorkerContainer(ttsAudioService.Object);

			var task = new TtsStaticPromptRefreshTask(Mock.Of<ILogger>());
			var progress = new Mock<IQuidjiboProgress>(MockBehavior.Loose);

			await FluentActions
				.Awaiting(() => task.ProcessAsync(new TtsStaticPromptRefreshCommand(1), progress.Object, CancellationToken.None))
				.Should()
				.ThrowAsync<InvalidOperationException>()
				.WithMessage("refresh failed");
		}

		[Test]
		public async Task process_async_should_rethrow_cancellation()
		{
			using var cancellationTokenSource = new CancellationTokenSource();
			var ttsAudioService = new Mock<ITtsAudioService>(MockBehavior.Strict);
			ttsAudioService
				.Setup(x => x.RegenerateStaticPromptsAsync(It.IsAny<IEnumerable<string>>(), It.Is<CancellationToken>(token => token == cancellationTokenSource.Token)))
				.ThrowsAsync(new OperationCanceledException(cancellationTokenSource.Token));
			SetWorkerContainer(ttsAudioService.Object);

			var task = new TtsStaticPromptRefreshTask(Mock.Of<ILogger>());
			var progress = new Mock<IQuidjiboProgress>(MockBehavior.Loose);

			await FluentActions
				.Awaiting(() => task.ProcessAsync(new TtsStaticPromptRefreshCommand(1), progress.Object, cancellationTokenSource.Token))
				.Should()
				.ThrowAsync<OperationCanceledException>();
		}

		private void SetWorkerContainer(ITtsAudioService ttsAudioService)
		{
			_testWorkerContainer?.Dispose();

			var builder = new ContainerBuilder();
			builder.RegisterInstance(ttsAudioService).As<ITtsAudioService>();
			_testWorkerContainer = builder.Build();

			WorkerBootstrapperContainerField.SetValue(null, _testWorkerContainer);
		}
	}
}
