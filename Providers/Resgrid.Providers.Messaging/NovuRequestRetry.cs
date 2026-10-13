using System.Net;

namespace Resgrid.Providers.Messaging
{
	/// <summary>
	/// Bounded retry for Novu API calls: event triggers, subscriber creates, device credential writes, the
	/// reads behind web push token changes and inbox message deletes (with the inbox session each one opens).
	/// Nothing above the provider retries any of them, so a dropped connection -- Novu closing the socket before
	/// answering ("The response ended prematurely") or an ingress 502/503/504 while its API restarts -- silently
	/// lost the push, or left a device registered nowhere until its app next re-registered.
	///
	/// Every call this wraps is safe to repeat. A trigger or create sends the same idempotency-key on every
	/// attempt, so a Novu with API idempotency enabled replays the first attempt's answer (or answers 409 while
	/// that attempt is still being processed) instead of acting twice; without it the worst case is one extra
	/// push, which beats a missing dispatch, and a repeated create is answered 409 (already exists). Credential
	/// writes are replacing PUTs, a repeated delete finds the message already gone, a repeated inbox session
	/// just opens another, and the rest are GETs.
	///
	/// Callers send per recipient in sequence, with SMS and email waiting behind the push. Once a call has
	/// used up its retries on a transport failure, later calls make a single attempt until one reaches Novu
	/// again or <see cref="SuppressionWindow"/> passes, so an outage costs the retry delay once rather than
	/// once per recipient. All calls go to the same Novu API, so they share that state.
	/// </summary>
	public class NovuRequestRetry
	{
		public const int MaxAttempts = 3;

		/// <summary>Wait before the second and third attempts.</summary>
		public static readonly TimeSpan[] RetryDelays = { TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1) };

		public static readonly TimeSpan SuppressionWindow = TimeSpan.FromSeconds(30);

		/// <summary>Process-wide instance: the suppression state has to outlive a single provider instance.</summary>
		public static readonly NovuRequestRetry Shared = new NovuRequestRetry();

		private readonly Func<DateTime> _utcNow;
		private readonly Func<TimeSpan, Task> _delay;
		private long _suppressedUntilTicks;

		public NovuRequestRetry(Func<DateTime>? utcNow = null, Func<TimeSpan, Task>? delay = null)
		{
			_utcNow = utcNow ?? (() => DateTime.UtcNow);
			_delay = delay ?? (d => Task.Delay(d));
		}

		public bool RetriesSuppressed => Interlocked.Read(ref _suppressedUntilTicks) > _utcNow().Ticks;

		/// <summary>
		/// Runs <paramref name="send"/> until it gets an answer from Novu itself or the attempts run out. The
		/// delegate must build a fresh request (and body) each call -- an HttpRequestMessage cannot be sent
		/// twice. Returns the last response, which the caller owns and reads any non-success from; rethrows the
		/// last transport failure when every attempt failed that way.
		/// </summary>
		/// <param name="onRetry">Told the failed attempt's number and what went wrong, before the wait.</param>
		public async Task<NovuRequestResult> SendAsync(Func<Task<HttpResponseMessage>> send, Action<int, string>? onRetry = null)
		{
			var maxAttempts = RetriesSuppressed ? 1 : MaxAttempts;

			for (var attempt = 1; ; attempt++)
			{
				var isLastAttempt = attempt >= maxAttempts;
				string failure;

				try
				{
					var response = await send();

					if (!IsGatewayFailure(response.StatusCode))
					{
						Interlocked.Exchange(ref _suppressedUntilTicks, 0);
						return new NovuRequestResult(response, attempt);
					}

					if (isLastAttempt)
					{
						Suppress();
						return new NovuRequestResult(response, attempt);
					}

					failure = $"{(int)response.StatusCode} {response.StatusCode}";
					response.Dispose();
				}
				catch (HttpRequestException ex)
				{
					// HttpClient's own timeout surfaces as TaskCanceledException and is deliberately not caught:
					// the attempt already waited the full timeout, and repeating that stalls every recipient
					// queued behind this one.
					if (isLastAttempt)
					{
						Suppress();
						throw;
					}

					failure = ex.InnerException?.Message ?? ex.Message;
				}

				onRetry?.Invoke(attempt, failure);
				await _delay(RetryDelays[Math.Min(attempt - 1, RetryDelays.Length - 1)]);
			}
		}

		/// <summary>
		/// A 409 on a retry is Novu's idempotency guard reporting the earlier attempt with the same key still in
		/// progress: that attempt reached Novu, so the call was accepted. On a first attempt it is a real
		/// conflict.
		/// </summary>
		public static bool IsAccepted(NovuRequestResult result)
		{
			return result.Response.IsSuccessStatusCode
				|| (result.Response.StatusCode == HttpStatusCode.Conflict && result.Attempts > 1);
		}

		/// <summary>The ingress answering for an unreachable or restarting Novu API, not Novu rejecting the call.</summary>
		private static bool IsGatewayFailure(HttpStatusCode statusCode)
		{
			return statusCode == HttpStatusCode.BadGateway
				|| statusCode == HttpStatusCode.ServiceUnavailable
				|| statusCode == HttpStatusCode.GatewayTimeout;
		}

		private void Suppress()
		{
			Interlocked.Exchange(ref _suppressedUntilTicks, _utcNow().Add(SuppressionWindow).Ticks);
		}
	}

	public sealed class NovuRequestResult
	{
		public NovuRequestResult(HttpResponseMessage response, int attempts)
		{
			Response = response;
			Attempts = attempts;
		}

		public HttpResponseMessage Response { get; }

		public int Attempts { get; }
	}
}
