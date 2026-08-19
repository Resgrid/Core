using Microsoft.Extensions.Options;
using Resgrid.Web.Tts.Configuration;
using System.Collections.Concurrent;

namespace Resgrid.Web.Tts.Services
{
	/// <summary>
	/// See <see cref="IPiperProcessPool"/>. Per profile the pool holds up to
	/// PiperMaxWorkersPerVoice workers behind a semaphore; excess requests queue on the
	/// semaphore rather than spawning more model-resident processes.
	///
	/// Self-healing: any worker failure (process exit, closed pipe, missing output
	/// file, protocol garbage) disposes that worker and retries the request once on a
	/// brand-new process. The second failure surfaces as InvalidOperationException,
	/// which the TTS request path already treats as a generation failure. A caller
	/// cancellation also disposes the in-use worker — its stdin/stdout protocol state
	/// is unknown mid-request — and the next request simply spawns a replacement.
	/// </summary>
	public sealed class PiperProcessPool : IPiperProcessPool
	{
		private sealed class ProfileState
		{
			public required SemaphoreSlim Slots { get; init; }
			public ConcurrentBag<IPiperWorker> Idle { get; } = new();
		}

		private readonly IPiperWorkerFactory _workerFactory;
		private readonly ILogger<PiperProcessPool> _logger;
		private readonly int _maxWorkersPerProfile;
		private readonly ConcurrentDictionary<string, ProfileState> _profiles = new(StringComparer.Ordinal);
		private volatile bool _disposed;

		public PiperProcessPool(
			IOptions<TtsOptions> options,
			ILogger<PiperProcessPool> logger,
			IPiperWorkerFactory workerFactory)
		{
			_workerFactory = workerFactory;
			_logger = logger;
			_maxWorkersPerProfile = Math.Max(1, options.Value.PiperMaxWorkersPerVoice);
		}

		public async Task SynthesizeAsync(PiperSynthesisProfile profile, string text, string outputFilePath, CancellationToken cancellationToken)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			ArgumentNullException.ThrowIfNull(profile);
			ArgumentException.ThrowIfNullOrWhiteSpace(text);
			ArgumentException.ThrowIfNullOrWhiteSpace(outputFilePath);

			var state = _profiles.GetOrAdd(
				$"{profile.ModelPath}{profile.LengthScale}",
				_ => new ProfileState { Slots = new SemaphoreSlim(_maxWorkersPerProfile, _maxWorkersPerProfile) });

			await state.Slots.WaitAsync(cancellationToken);

			try
			{
				Exception firstFailure = null;

				for (var attempt = 1; attempt <= 2; attempt++)
				{
					cancellationToken.ThrowIfCancellationRequested();

					IPiperWorker worker = null;

					try
					{
						worker = state.Idle.TryTake(out var idleWorker) ? idleWorker : _workerFactory.Create(profile);
						await worker.SynthesizeAsync(text, outputFilePath, cancellationToken);
						state.Idle.Add(worker);
						return;
					}
					catch (OperationCanceledException)
					{
						// The worker may have a response still in flight for the abandoned
						// request; reusing it would desynchronize the protocol.
						worker?.Dispose();
						throw;
					}
					catch (Exception ex)
					{
						worker?.Dispose();
						firstFailure ??= ex;
						_logger.LogWarning(
							ex,
							"Piper worker for model {ModelPath} failed on attempt {Attempt}; respawning.",
							profile.ModelPath,
							attempt);
					}
				}

				throw new InvalidOperationException(
					$"Piper synthesis failed twice (fresh worker included) for model {profile.ModelPath}.",
					firstFailure);
			}
			finally
			{
				state.Slots.Release();
			}
		}

		public ValueTask DisposeAsync()
		{
			_disposed = true;

			// Idle workers are killed here; a worker still serving a request is disposed
			// by that request's cancellation path when the host stops.
			foreach (var state in _profiles.Values)
			{
				while (state.Idle.TryTake(out var worker))
				{
					try
					{
						worker.Dispose();
					}
					catch (Exception ex)
					{
						_logger.LogWarning(ex, "Failed to dispose a pooled Piper worker during shutdown.");
					}
				}
			}

			return ValueTask.CompletedTask;
		}
	}
}
