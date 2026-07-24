using System;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Providers.Bus;

namespace Resgrid.Tests.Providers
{
	[TestFixture]
	public class EventAggregatorTests
	{
		[Test]
		public async Task SendMessageAsync_should_await_async_listener()
		{
			var aggregator = new EventAggregator();
			var listenerCompleted = false;
			aggregator.AddAsyncListener<string>(async message =>
			{
				await Task.Yield();
				listenerCompleted = message == "location-updated";
			});

			await aggregator.SendMessageAsync("location-updated");

			listenerCompleted.Should().BeTrue();
		}

		[Test]
		public async Task SendMessageAsync_should_propagate_listener_failure()
		{
			var aggregator = new EventAggregator();
			aggregator.AddAsyncListener<string>(_ =>
				Task.FromException(new InvalidOperationException("Realtime publish failed.")));
			Func<Task> act = async () => await aggregator.SendMessageAsync("location-updated");

			await act.Should().ThrowAsync<InvalidOperationException>()
				.WithMessage("Realtime publish failed.");
		}
	}
}
