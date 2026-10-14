using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Providers.Messaging;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// Nothing above the Novu provider retries a failed call, so a dropped connection used to lose the push
	/// outright (prod Sentry 2026-10-12: "The response ended prematurely" from resgrid-scheduled) or leave a
	/// device unregistered. These pin what is retried, what is not, and that an outage costs the retry delay
	/// once rather than per recipient.
	/// </summary>
	[TestFixture]
	public class NovuRequestRetryTests
	{
		private DateTime _now;
		private List<TimeSpan> _delays;
		private NovuRequestRetry _retry;

		[SetUp]
		public void SetUp()
		{
			_now = new DateTime(2026, 10, 12, 16, 51, 13, DateTimeKind.Utc);
			_delays = new List<TimeSpan>();
			_retry = new NovuRequestRetry(() => _now, d =>
			{
				_delays.Add(d);
				return Task.CompletedTask;
			});
		}

		private static Func<Task<HttpResponseMessage>> Sequence(params object[] outcomes)
		{
			var index = 0;
			return () =>
			{
				var outcome = outcomes[Math.Min(index++, outcomes.Length - 1)];
				if (outcome is Exception ex)
					return Task.FromException<HttpResponseMessage>(ex);

				return Task.FromResult(new HttpResponseMessage((HttpStatusCode)outcome));
			};
		}

		private static HttpRequestException ResponseEnded()
		{
			return new HttpRequestException("An error occurred while sending the request.",
				new System.IO.IOException("The response ended prematurely. (ResponseEnded)"));
		}

		[Test]
		public async Task SendAsync_should_not_retry_an_accepted_trigger()
		{
			var result = await _retry.SendAsync(Sequence(HttpStatusCode.Created));

			result.Attempts.Should().Be(1);
			NovuRequestRetry.IsAccepted(result).Should().BeTrue();
			_delays.Should().BeEmpty();
		}

		[Test]
		public async Task SendAsync_should_retry_a_dropped_connection_and_report_the_retry()
		{
			var retried = new List<int>();

			var result = await _retry.SendAsync(Sequence(ResponseEnded(), HttpStatusCode.Created), (attempt, _) => retried.Add(attempt));

			result.Attempts.Should().Be(2);
			NovuRequestRetry.IsAccepted(result).Should().BeTrue();
			retried.Should().Equal(1);
			_delays.Should().Equal(NovuRequestRetry.RetryDelays[0]);
		}

		[Test]
		public async Task SendAsync_should_retry_a_gateway_failure()
		{
			var result = await _retry.SendAsync(Sequence(HttpStatusCode.ServiceUnavailable, HttpStatusCode.BadGateway, HttpStatusCode.Created));

			result.Attempts.Should().Be(3);
			NovuRequestRetry.IsAccepted(result).Should().BeTrue();
			_delays.Should().Equal(NovuRequestRetry.RetryDelays);
		}

		[Test]
		public async Task SendAsync_should_rethrow_the_transport_failure_once_attempts_run_out()
		{
			var calls = 0;
			var send = Sequence(ResponseEnded());

			Func<Task> act = () => _retry.SendAsync(() => { calls++; return send(); });

			await act.Should().ThrowAsync<HttpRequestException>();
			calls.Should().Be(NovuRequestRetry.MaxAttempts);
			_retry.RetriesSuppressed.Should().BeTrue();
		}

		[Test]
		public async Task SendAsync_should_return_the_last_gateway_failure_once_attempts_run_out()
		{
			var result = await _retry.SendAsync(Sequence(HttpStatusCode.GatewayTimeout));

			result.Attempts.Should().Be(NovuRequestRetry.MaxAttempts);
			result.Response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
			NovuRequestRetry.IsAccepted(result).Should().BeFalse();
			_retry.RetriesSuppressed.Should().BeTrue();
		}

		[Test]
		public async Task SendAsync_should_not_retry_a_rejection_from_novu_itself()
		{
			var result = await _retry.SendAsync(Sequence(HttpStatusCode.UnprocessableEntity, HttpStatusCode.Created));

			result.Attempts.Should().Be(1);
			NovuRequestRetry.IsAccepted(result).Should().BeFalse();
			_delays.Should().BeEmpty();
		}

		[Test]
		public async Task SendAsync_should_not_retry_a_timeout()
		{
			var calls = 0;
			var send = Sequence(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."));

			Func<Task> act = () => _retry.SendAsync(() => { calls++; return send(); });

			await act.Should().ThrowAsync<TaskCanceledException>();
			calls.Should().Be(1);
			_retry.RetriesSuppressed.Should().BeFalse();
		}

		[Test]
		public async Task SendAsync_should_make_a_single_attempt_while_novu_is_known_to_be_down()
		{
			await FluentActions.Awaiting(() => _retry.SendAsync(Sequence(ResponseEnded()))).Should().ThrowAsync<HttpRequestException>();
			_delays.Clear();

			var calls = 0;
			var send = Sequence(ResponseEnded());
			await FluentActions.Awaiting(() => _retry.SendAsync(() => { calls++; return send(); })).Should().ThrowAsync<HttpRequestException>();

			calls.Should().Be(1);
			_delays.Should().BeEmpty();
		}

		[Test]
		public async Task SendAsync_should_retry_again_once_a_trigger_reaches_novu()
		{
			await FluentActions.Awaiting(() => _retry.SendAsync(Sequence(ResponseEnded()))).Should().ThrowAsync<HttpRequestException>();

			// Novu answering at all, even with a rejection, proves it is reachable again.
			await _retry.SendAsync(Sequence(HttpStatusCode.BadRequest));

			_retry.RetriesSuppressed.Should().BeFalse();
			var result = await _retry.SendAsync(Sequence(ResponseEnded(), HttpStatusCode.Created));
			result.Attempts.Should().Be(2);
		}

		[Test]
		public async Task SendAsync_should_retry_again_once_the_suppression_window_passes()
		{
			await FluentActions.Awaiting(() => _retry.SendAsync(Sequence(ResponseEnded()))).Should().ThrowAsync<HttpRequestException>();

			_now = _now.Add(NovuRequestRetry.SuppressionWindow).AddSeconds(1);

			_retry.RetriesSuppressed.Should().BeFalse();
			var result = await _retry.SendAsync(Sequence(ResponseEnded(), HttpStatusCode.Created));
			result.Attempts.Should().Be(2);
		}

		[Test]
		public async Task IsAccepted_should_treat_a_conflict_on_a_retry_as_the_earlier_attempt_still_processing()
		{
			var result = await _retry.SendAsync(Sequence(ResponseEnded(), HttpStatusCode.Conflict));

			result.Attempts.Should().Be(2);
			NovuRequestRetry.IsAccepted(result).Should().BeTrue();
		}

		[Test]
		public async Task IsAccepted_should_not_treat_a_conflict_on_the_first_attempt_as_accepted()
		{
			var result = await _retry.SendAsync(Sequence(HttpStatusCode.Conflict));

			NovuRequestRetry.IsAccepted(result).Should().BeFalse();
		}
	}
}
