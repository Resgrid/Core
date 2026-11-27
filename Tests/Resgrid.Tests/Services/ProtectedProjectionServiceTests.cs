using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ProtectedProjectionServiceTests
	{
		private const int DeptId = 42;

		private Mock<IDepartmentDataProtectionService> _protection;
		private ProtectedProjectionService _service;

		[SetUp]
		public void SetUp()
		{
			_protection = new Mock<IDepartmentDataProtectionService>();
			_service = new ProtectedProjectionService(_protection.Object, new ProtectedFieldCatalog());
		}

		private sealed class FakeCallAddedEvent
		{
			public int DepartmentId { get; set; } = DeptId;
			public int CallId { get; set; } = 1001;
			public string Name { get; set; } = "Structure Fire";
			public string NatureOfCall { get; set; } = "Smoke showing, occupant trapped";
			public string Priority { get; set; } = "High";
			public FakeNote[] Notes2 { get; set; } = { new FakeNote() };
			public byte[] Data { get; set; } = { 1, 2, 3 };
		}

		private sealed class FakeNote
		{
			public string Note { get; set; } = "Patient is diabetic";
			public string AddedByUserId { get; set; } = "user-1";
		}

		[Test]
		public async Task Unprotected_department_serializes_plainly()
		{
			_protection.Setup(x => x.IsProtectionEnforcedAsync(DeptId)).ReturnsAsync(false);

			var json = await _service.BuildSafeWorkflowPayloadAsync(DeptId, new FakeCallAddedEvent());
			var parsed = JObject.Parse(json);

			parsed["Name"].Value<string>().Should().Be("Structure Fire");
			parsed["is_redacted"].Should().BeNull();
		}

		[Test]
		public async Task Enforced_department_gets_redacted_scalars_omitted_binaries_and_metadata()
		{
			_protection.Setup(x => x.IsProtectionEnforcedAsync(DeptId)).ReturnsAsync(true);

			var json = await _service.BuildSafeWorkflowPayloadAsync(DeptId, new FakeCallAddedEvent());
			var parsed = JObject.Parse(json);

			parsed["Name"].Value<string>().Should().Be("REDACTED", "cataloged scalars become the exact placeholder");
			parsed["NatureOfCall"].Value<string>().Should().Be("REDACTED");
			parsed["Data"].Should().BeNull("cataloged binaries are omitted, never inlined");

			// Structural and non-cataloged values survive so routing still works.
			parsed["DepartmentId"].Value<int>().Should().Be(DeptId);
			parsed["CallId"].Value<int>().Should().Be(1001);
			parsed["Priority"].Value<string>().Should().Be("High");

			// Nested user-authored content is redacted wherever it appears in the graph.
			parsed["Notes2"][0]["Note"].Value<string>().Should().Be("REDACTED");
			parsed["Notes2"][0]["AddedByUserId"].Value<string>().Should().Be("user-1");

			parsed["is_redacted"].Value<bool>().Should().BeTrue();
			parsed["catalog_version"].Value<int>().Should().Be(new ProtectedFieldCatalog().Version);
			parsed["redacted_fields"].Values<string>().Should().Contain(new[] { "Name", "NatureOfCall", "Note", "Data" });
		}

		[Test]
		public async Task Unknown_protection_state_redacts_defensively()
		{
			_protection.Setup(x => x.IsProtectionEnforcedAsync(DeptId))
				.ThrowsAsync(new System.InvalidOperationException("state store down"));

			var json = await _service.BuildSafeWorkflowPayloadAsync(DeptId, new FakeCallAddedEvent());
			var parsed = JObject.Parse(json);

			parsed["Name"].Value<string>().Should().Be("REDACTED",
				"an unknown protection state must never leak plaintext");
			parsed["is_redacted"].Value<bool>().Should().BeTrue();
		}

		[Test]
		public async Task Null_payload_returns_null()
		{
			(await _service.BuildSafeWorkflowPayloadAsync(DeptId, null)).Should().BeNull();
		}

		private static Resgrid.Model.Call CallWithCase() => new Resgrid.Model.Call
		{
			CallId = 1001, DepartmentId = DeptId, Name = "Sick person", Number = "26-1001",
			Protocols = new System.Collections.Generic.List<Resgrid.Model.CallProtocol>
			{
				new Resgrid.Model.CallProtocol
				{
					CallProtocolId = 70, CallId = 1001, DispatchProtocolId = 42, ProtocolVersionId = 118, Data = "EMD-26", Determinant = "26-C-1", ContentHash = "abc",
					CaseJson = "{\"answers\":{\"breathing\":\"CASE-ANSWER-NOT-BREATHING\"}}", AnswersJson = "{\"KEY-ANSWER-DIABETIC\":1}",
					OverrideReason = "OVERRIDE-CALLER-SAID-CHEST-PAIN", StepLogJson = "{\"note\":\"STEP-NOTE-PATIENT-NAME\"}", RemovedReason = "REMOVED-FREE-TEXT"
				}
			}
		};

		private static readonly string[] CallerContent =
			{ "CASE-ANSWER-NOT-BREATHING", "KEY-ANSWER-DIABETIC", "OVERRIDE-CALLER-SAID-CHEST-PAIN", "STEP-NOTE-PATIENT-NAME", "REMOVED-FREE-TEXT" };

		[TestCase(false)]
		[TestCase(true)]
		public async Task Call_events_never_carry_a_protocol_case_answers_step_log_or_reasons_into_a_workflow_payload(bool enforced)
		{
			// Round 7C: CallAdded / CallUpdated / CallClosed serialize call.Protocols; the caller content goes, the ids and determinant stay.
			_protection.Setup(x => x.IsProtectionEnforcedAsync(DeptId)).ReturnsAsync(enforced);

			foreach (var payload in new object[]
			{
				new Resgrid.Model.Events.CallAddedEvent { DepartmentId = DeptId, Call = CallWithCase() },
				new Resgrid.Model.Events.CallUpdatedEvent { DepartmentId = DeptId, Call = CallWithCase() },
				new Resgrid.Model.Events.CallClosedEvent { DepartmentId = DeptId, Call = CallWithCase() }
			})
			{
				var json = await _service.BuildSafeWorkflowPayloadAsync(DeptId, payload);

				foreach (var content in CallerContent)
					json.Should().NotContain(content, payload.GetType().Name);
				var row = (JObject)JObject.Parse(json)["Call"]["Protocols"][0];
				foreach (var field in Resgrid.Model.Events.CallWorkflowPayload.StrippedProtocolFields)
					row.Property(field).Should().BeNull(field);
				row["Determinant"].Value<string>().Should().Be("26-C-1");
				row["DispatchProtocolId"].Value<int>().Should().Be(42);
				if (!enforced)
					row["Data"].Value<string>().Should().Be("EMD-26", "the protocol's code stays (a protected department's projection treats Data by the catalog)");
			}
		}

		[Test]
		public void Every_text_field_of_a_call_protocol_is_either_stripped_from_workflow_payloads_or_known_safe()
		{
			// A new free-text field on CallProtocol fails here until it is classified (stripped, or kept because it is an id or a code).
			var classified = new System.Collections.Generic.HashSet<string>(Resgrid.Model.Events.CallWorkflowPayload.StrippedProtocolFields);
			classified.UnionWith(Resgrid.Model.Events.CallWorkflowPayload.KeptProtocolTextFields);

			foreach (var property in typeof(Resgrid.Model.CallProtocol).GetProperties())
				if (property.PropertyType == typeof(string) && property.CanWrite) // stored text (TableName/IdName are constants)
					classified.Should().Contain(property.Name, $"CallProtocol.{property.Name} must be classified for workflow payloads");
		}

		#region Notification-safe call

		private static Resgrid.Model.Call ProtectedCall() => new Resgrid.Model.Call
		{
			CallId = 1001,
			DepartmentId = DeptId,
			Number = "2026-134",
			Priority = 3,
			Name = "Cardiac Arrest - Smith Residence",
			NatureOfCall = "62yo male, CPR in progress",
			Address = "123 Main St",
			GeoLocationData = "39.1,-84.5",
			ContactName = "Jane Smith",
			ContactNumber = "555-0100",
			Notes = "History of heart disease"
		};

		private void SetupEgress(Resgrid.Model.ProtectedDataEgressMode push = Resgrid.Model.ProtectedDataEgressMode.GenericOnly,
			Resgrid.Model.ProtectedDataEgressMode sms = Resgrid.Model.ProtectedDataEgressMode.GenericOnly)
		{
			_protection.Setup(x => x.GetEgressPolicyByDepartmentIdAsync(DeptId, It.IsAny<bool>()))
				.ReturnsAsync(new Resgrid.Model.DepartmentProtectedDataEgressPolicy
				{
					DepartmentId = DeptId,
					PushMode = (int)push,
					SmsMode = (int)sms,
					EmailMode = (int)Resgrid.Model.ProtectedDataEgressMode.GenericOnly,
					VoiceMode = (int)Resgrid.Model.ProtectedDataEgressMode.GenericOnly
				});
		}

		[Test]
		public async Task Unprotected_department_gets_the_original_call_by_reference()
		{
			_protection.Setup(x => x.IsProtectionEnforcedAsync(DeptId)).ReturnsAsync(false);
			var call = ProtectedCall();

			var safe = await _service.BuildNotificationSafeCallAsync(DeptId, call, Resgrid.Model.ProtectedDataEgressChannel.Sms);

			safe.Should().BeSameAs(call);
		}

		[Test]
		public async Task Generic_only_channel_gets_the_sanitized_clone()
		{
			_protection.Setup(x => x.IsProtectionEnforcedAsync(DeptId)).ReturnsAsync(true);
			SetupEgress();
			var call = ProtectedCall();

			var safe = await _service.BuildNotificationSafeCallAsync(DeptId, call, Resgrid.Model.ProtectedDataEgressChannel.Sms);

			safe.Should().NotBeSameAs(call);
			safe.CallId.Should().Be(1001);
			safe.Number.Should().Be("2026-134", "the system-generated call number is allowlisted");
			// The call's name, address and location are operational (2026-10-17) and survive the sanitized clone.
			safe.Name.Should().Be("Cardiac Arrest - Smith Residence");
			safe.Address.Should().Be("123 Main St");
			safe.GeoLocationData.Should().Be("39.1,-84.5");
			// Every catalogued field is withheld.
			safe.NatureOfCall.Should().Be(ProtectedProjectionService.GenericDispatchText);
			safe.ContactName.Should().BeNull();
			safe.ContactNumber.Should().BeNull();
			safe.Notes.Should().BeNull();
			safe.Priority.Should().Be(3, "priority/color routing survives");

			// An envelope-shaped operational value is never passed on; the name falls back to the call number.
			var enveloped = ProtectedCall(); enveloped.Name = "rgdp:1:1:name=="; enveloped.Address = "rgdp:1:1:address==";
			var fallback = await _service.BuildNotificationSafeCallAsync(DeptId, enveloped, Resgrid.Model.ProtectedDataEgressChannel.Sms);
			fallback.Name.Should().Be("2026-134");
			fallback.Address.Should().BeNull();
		}

		[Test]
		public async Task Allow_protected_content_mode_passes_the_original_for_that_channel_only()
		{
			_protection.Setup(x => x.IsProtectionEnforcedAsync(DeptId)).ReturnsAsync(true);
			SetupEgress(push: Resgrid.Model.ProtectedDataEgressMode.AllowProtectedContent);
			var call = ProtectedCall();

			(await _service.BuildNotificationSafeCallAsync(DeptId, call, Resgrid.Model.ProtectedDataEgressChannel.Push))
				.Should().BeSameAs(call, "the department explicitly acknowledged protected push content");
			(await _service.BuildNotificationSafeCallAsync(DeptId, call, Resgrid.Model.ProtectedDataEgressChannel.Sms))
				.Should().NotBeSameAs(call, "each channel is an independent choice");
		}

		[Test]
		public async Task Chat_platforms_are_always_generic_for_protected_departments()
		{
			_protection.Setup(x => x.IsProtectionEnforcedAsync(DeptId)).ReturnsAsync(true);
			SetupEgress(push: Resgrid.Model.ProtectedDataEgressMode.AllowProtectedContent,
				sms: Resgrid.Model.ProtectedDataEgressMode.AllowProtectedContent);

			(await _service.BuildNotificationSafeCallAsync(DeptId, ProtectedCall(), Resgrid.Model.ProtectedDataEgressChannel.ChatPlatform))
				.NatureOfCall.Should().Be(ProtectedProjectionService.GenericDispatchText,
					"third-party chat egress has no allow mode");
		}

		[Test]
		public async Task Allow_protected_content_degrades_when_any_cataloged_field_is_enveloped()
		{
			_protection.Setup(x => x.IsProtectionEnforcedAsync(DeptId)).ReturnsAsync(true);
			SetupEgress(push: Resgrid.Model.ProtectedDataEgressMode.AllowProtectedContent);

			// Name/NatureOfCall/Address are plaintext, so a fixed three-field check would wave this
			// through — but Notes carries an envelope, and templates and TTS prompts read it. Every
			// cataloged field must be checked or ciphertext reaches the carrier.
			var call = new Resgrid.Model.Call
			{
				CallId = 1001,
				DepartmentId = DeptId,
				Number = "26-100",
				Name = "Structure Fire",
				NatureOfCall = "Smoke showing",
				Notes = "rgdp:1:1:notes=="
			};

			var safe = await _service.BuildNotificationSafeCallAsync(DeptId, call, Resgrid.Model.ProtectedDataEgressChannel.Push);

			safe.Should().NotBeSameAs(call, "an enveloped cataloged field forces the sanitized clone");
			safe.NatureOfCall.Should().Be(ProtectedProjectionService.GenericDispatchText);
			safe.Notes.Should().BeNull("the sanitized clone carries no user-authored content");
		}

		[Test]
		public async Task Allow_protected_content_degrades_on_an_enveloped_contact_number()
		{
			_protection.Setup(x => x.IsProtectionEnforcedAsync(DeptId)).ReturnsAsync(true);
			SetupEgress(push: Resgrid.Model.ProtectedDataEgressMode.AllowProtectedContent);

			var call = new Resgrid.Model.Call
			{
				CallId = 1001,
				DepartmentId = DeptId,
				Number = "26-100",
				Name = "Structure Fire",
				ContactNumber = "rgdp:1:1:contactnumber=="
			};

			(await _service.BuildNotificationSafeCallAsync(DeptId, call, Resgrid.Model.ProtectedDataEgressChannel.Push))
				.Should().NotBeSameAs(call);
		}

		[Test]
		public async Task Unknown_protection_or_egress_state_sanitizes_defensively()
		{
			_protection.Setup(x => x.IsProtectionEnforcedAsync(DeptId))
				.ThrowsAsync(new System.InvalidOperationException("state store down"));

			(await _service.BuildNotificationSafeCallAsync(DeptId, ProtectedCall(), Resgrid.Model.ProtectedDataEgressChannel.Sms))
				.NatureOfCall.Should().Be(ProtectedProjectionService.GenericDispatchText);

			_protection.Reset();
			_protection.Setup(x => x.IsProtectionEnforcedAsync(DeptId)).ReturnsAsync(true);
			_protection.Setup(x => x.GetEgressPolicyByDepartmentIdAsync(DeptId, It.IsAny<bool>()))
				.ThrowsAsync(new System.InvalidOperationException("egress store down"));

			(await _service.BuildNotificationSafeCallAsync(DeptId, ProtectedCall(), Resgrid.Model.ProtectedDataEgressChannel.Push))
				.NatureOfCall.Should().Be(ProtectedProjectionService.GenericDispatchText);
		}

		#endregion
	}
}
