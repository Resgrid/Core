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
			safe.Name.Should().Be("2026-134");
			safe.NatureOfCall.Should().Be(ProtectedProjectionService.GenericDispatchText);
			safe.Address.Should().BeNull();
			safe.GeoLocationData.Should().BeNull();
			safe.ContactName.Should().BeNull();
			safe.ContactNumber.Should().BeNull();
			safe.Notes.Should().BeNull();
			safe.Priority.Should().Be(3, "priority/color routing survives");
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
