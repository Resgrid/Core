using System;
using System.IO;
using File = System.IO.File;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Providers.Bus;
using Resgrid.Providers.Bus.Rabbit;
using Resgrid.Services.Intake;

namespace Resgrid.Tests.Intake
{
	/// <summary>
	/// EventingTypes 21 end to end (plan §8): the service publishes <see cref="IntakeConversationUpdatedEvent"/> with ids, state and handler
	/// only; the outbound provider relays it to the eventing topic; the inbound provider hands type 21 to the eventing host, which calls
	/// the hub method <c>intakeConversationUpdated(callId, payload)</c>; the web client raises a DOM event for the call panels.
	/// </summary>
	[TestFixture]
	public class IntakeConversationEventingTests
	{
		private static string Root()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;
			directory.Should().NotBeNull();
			return directory!.FullName;
		}

		[Test]
		public void the_payload_carries_ids_state_and_handler_but_never_text_or_the_number()
		{
			var payload = IntakeConversationService.Payload(new IntakeConversation
			{
				IntakeConversationId = "conv-1", CallId = 900, State = 1, Handler = 3, ParticipantAddress = "+15305550199", CloseReason = "secret", AssignedUserId = "disp-1"
			});

			var json = JObject.Parse(payload);
			json.Properties().Should().HaveCount(3);
			((string)json["conversationId"]).Should().Be("conv-1");
			((int)json["state"]).Should().Be(1);
			((int)json["handler"]).Should().Be(3);
			payload.Should().NotContain("5305550199").And.NotContain("disp-1");
		}

		[Test]
		public void the_eventing_type_is_21()
		{
			((int)EventingTypes.IntakeConversationUpdated).Should().Be(21);
		}

		[Test]
		public void the_outbound_provider_relays_the_event_to_the_topic()
		{
			var field = typeof(OutboundEventProvider).GetField("intakeConversationUpdatedTopicHandler");
			field.Should().NotBeNull();
			field!.FieldType.Should().Be(typeof(Func<IntakeConversationUpdatedEvent, Task>));
			typeof(RabbitTopicProvider).GetMethod("IntakeConversationUpdated", new[] { typeof(int), typeof(int), typeof(string) }).Should().NotBeNull();
		}

		[Test]
		public void the_inbound_provider_hands_type_21_to_the_eventing_host()
		{
			var provider = new RabbitInboundEventProvider();
			Func<int, string, string, Task> handler = (d, id, p) => Task.CompletedTask;

			provider.RegisterForIntakeConversationEvents(handler);

			provider.ProcessIntakeConversationEvent.Should().BeSameAs(handler);
		}

		[Test]
		public void the_hub_method_and_the_web_dom_event_are_wired()
		{
			var root = Root();
			var worker = File.ReadAllText(Path.Combine(root, "Web", "Resgrid.Web.Eventing", "Worker.cs"));
			worker.Should().Contain("RegisterForIntakeConversationEvents((departmentId, id, payload) => _eventingHub.Clients.Group(departmentId.ToString()).SendAsync(\"intakeConversationUpdated\", id, payload))");

			var signalr = File.ReadAllText(Path.Combine(root, "Web", "Resgrid.Web", "wwwroot", "js", "app", "common", "signalr", "resgrid.common.signalr.js"));
			signalr.Should().Contain("eventHub.on(\"intakeConversationUpdated\", function (id, payload)");
			signalr.Should().Contain("resgrid:intake-conversation-updated");

			var panel = File.ReadAllText(Path.Combine(root, "Web", "Resgrid.Web", "wwwroot", "js", "app", "internal", "dispatch", "resgrid.dispatch.intakeconversation.js"));
			panel.Should().Contain("resgrid:intake-conversation-updated");
		}
	}
}
