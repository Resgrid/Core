namespace Resgrid.Web.Tts.Services
{
	/// <summary>
	/// A synthesis profile a persistent Piper worker is started with. Length scale is
	/// carried as its formatted command-line value so profiles compare exactly.
	/// </summary>
	public sealed record PiperSynthesisProfile(string ModelPath, string LengthScale);

	/// <summary>
	/// Pool of long-lived Piper processes, keyed by synthesis profile. A worker keeps
	/// its ONNX model resident, so synthesis skips the per-request model load that
	/// dominates cold generation time. Failed or wedged workers are disposed and the
	/// request retries once on a freshly spawned worker.
	/// </summary>
	public interface IPiperProcessPool : IAsyncDisposable
	{
		Task SynthesizeAsync(PiperSynthesisProfile profile, string text, string outputFilePath, CancellationToken cancellationToken);
	}

	/// <summary>
	/// One persistent Piper process. Callers must serialize access: a worker handles a
	/// single synthesis at a time (the pool guarantees this). A worker that throws is
	/// in an unknown protocol state and must be disposed, never reused.
	/// </summary>
	public interface IPiperWorker : IDisposable
	{
		Task SynthesizeAsync(string text, string outputFilePath, CancellationToken cancellationToken);
	}

	public interface IPiperWorkerFactory
	{
		IPiperWorker Create(PiperSynthesisProfile profile);
	}
}
