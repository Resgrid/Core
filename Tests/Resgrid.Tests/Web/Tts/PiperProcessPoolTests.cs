using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Resgrid.Web.Tts.Configuration;
using Resgrid.Web.Tts.Services;

namespace Resgrid.Tests.Web.Tts
{
	[TestFixture]
	public class PiperProcessPoolTests
	{
		private static readonly PiperSynthesisProfile Profile = new("/voices/en_US-ryan-medium.onnx", "1.17");

		private sealed class FakeWorker : IPiperWorker
		{
			private readonly Queue<Func<Task>> _behaviors;

			public FakeWorker(params Func<Task>[] behaviors)
			{
				_behaviors = new Queue<Func<Task>>(behaviors);
			}

			public int SynthesisCount { get; private set; }
			public bool Disposed { get; private set; }

			public Task SynthesizeAsync(string text, string outputFilePath, CancellationToken cancellationToken)
			{
				cancellationToken.ThrowIfCancellationRequested();
				SynthesisCount++;
				return _behaviors.Count > 0 ? _behaviors.Dequeue()() : Task.CompletedTask;
			}

			public void Dispose()
			{
				Disposed = true;
			}
		}

		private sealed class FakeWorkerFactory : IPiperWorkerFactory
		{
			private readonly Queue<FakeWorker> _workers;

			public FakeWorkerFactory(params FakeWorker[] workers)
			{
				_workers = new Queue<FakeWorker>(workers);
			}

			public List<FakeWorker> Created { get; } = new();

			public IPiperWorker Create(PiperSynthesisProfile profile)
			{
				var worker = _workers.Count > 0 ? _workers.Dequeue() : new FakeWorker();
				Created.Add(worker);
				return worker;
			}
		}

		private static PiperProcessPool CreatePool(FakeWorkerFactory factory, int maxWorkersPerVoice = 2)
		{
			return new PiperProcessPool(
				Options.Create(new TtsOptions { PiperMaxWorkersPerVoice = maxWorkersPerVoice }),
				NullLogger<PiperProcessPool>.Instance,
				factory);
		}

		[Test]
		public async Task should_reuse_a_healthy_worker_across_requests()
		{
			var factory = new FakeWorkerFactory();
			await using var pool = CreatePool(factory);

			await pool.SynthesizeAsync(Profile, "one", "/tmp/one.wav", CancellationToken.None);
			await pool.SynthesizeAsync(Profile, "two", "/tmp/two.wav", CancellationToken.None);

			factory.Created.Should().HaveCount(1);
			factory.Created[0].SynthesisCount.Should().Be(2);
		}

		[Test]
		public async Task should_dispose_a_failed_worker_and_retry_on_a_fresh_one()
		{
			var failing = new FakeWorker(() => throw new InvalidOperationException("piper died"));
			var factory = new FakeWorkerFactory(failing);
			await using var pool = CreatePool(factory);

			await pool.SynthesizeAsync(Profile, "text", "/tmp/out.wav", CancellationToken.None);

			failing.Disposed.Should().BeTrue();
			factory.Created.Should().HaveCount(2);
			factory.Created[1].SynthesisCount.Should().Be(1);
			factory.Created[1].Disposed.Should().BeFalse();
		}

		[Test]
		public async Task should_surface_the_first_failure_when_the_respawned_worker_also_fails()
		{
			var factory = new FakeWorkerFactory(
				new FakeWorker(() => throw new InvalidOperationException("first failure")),
				new FakeWorker(() => throw new InvalidOperationException("second failure")));
			await using var pool = CreatePool(factory);

			var act = () => pool.SynthesizeAsync(Profile, "text", "/tmp/out.wav", CancellationToken.None);

			(await act.Should().ThrowAsync<InvalidOperationException>())
				.WithInnerException<InvalidOperationException>()
				.WithMessage("first failure");
			factory.Created.Should().OnlyContain(worker => worker.Disposed);
		}

		[Test]
		public async Task should_dispose_the_worker_and_propagate_on_cancellation()
		{
			using var cts = new CancellationTokenSource();
			var worker = new FakeWorker(() =>
			{
				cts.Cancel();
				return Task.FromCanceled(cts.Token);
			});
			var factory = new FakeWorkerFactory(worker);
			await using var pool = CreatePool(factory);

			var act = () => pool.SynthesizeAsync(Profile, "text", "/tmp/out.wav", cts.Token);

			await act.Should().ThrowAsync<OperationCanceledException>();
			worker.Disposed.Should().BeTrue();
			factory.Created.Should().HaveCount(1);
		}

		[Test]
		public async Task should_dispose_idle_workers_when_the_pool_is_disposed()
		{
			var factory = new FakeWorkerFactory();
			var pool = CreatePool(factory);

			await pool.SynthesizeAsync(Profile, "text", "/tmp/out.wav", CancellationToken.None);
			await pool.DisposeAsync();

			factory.Created.Should().OnlyContain(worker => worker.Disposed);

			var act = () => pool.SynthesizeAsync(Profile, "text", "/tmp/out.wav", CancellationToken.None);
			await act.Should().ThrowAsync<ObjectDisposedException>();
		}

		[Test]
		public async Task should_dispose_a_worker_whose_synthesis_finishes_during_shutdown()
		{
			// The pool drained its idle bag before this synthesis completed; returning
			// the worker to the bag unconditionally would leak its Piper process.
			PiperProcessPool pool = null;
			var worker = new FakeWorker(async () =>
			{
				await pool.DisposeAsync();
			});
			var factory = new FakeWorkerFactory(worker);
			pool = CreatePool(factory);

			await pool.SynthesizeAsync(Profile, "text", "/tmp/out.wav", CancellationToken.None);

			worker.Disposed.Should().BeTrue();
		}

		[Test]
		public async Task should_keep_separate_workers_per_synthesis_profile()
		{
			var factory = new FakeWorkerFactory();
			await using var pool = CreatePool(factory);

			await pool.SynthesizeAsync(Profile, "text", "/tmp/one.wav", CancellationToken.None);
			await pool.SynthesizeAsync(Profile with { LengthScale = "0.80" }, "text", "/tmp/two.wav", CancellationToken.None);

			factory.Created.Should().HaveCount(2);
		}
	}
}
