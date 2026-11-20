using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Intake;

namespace Resgrid.Tests.Intake
{
	/// <summary>Intake channel addresses are normalized per transport and unique per transport + address across departments.</summary>
	[TestFixture]
	public class IntakeChannelsServiceTests
	{
		private const int DepartmentId = 55;

		private Mock<IIntakeChannelsRepository> _repository;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IEncryptionService> _encryption;
		private IntakeChannelsService _service;
		private IntakeChannel _saved;

		[SetUp]
		public void SetUp()
		{
			_saved = null;
			_repository = new Mock<IIntakeChannelsRepository>();
			_repository.Setup(r => r.SaveOrUpdateAsync(It.IsAny<IntakeChannel>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IntakeChannel c, CancellationToken _, bool __) =>
				{
					c.IntakeChannelId ??= "new-id";
					_saved = c;
					return c;
				});
			_settings = new Mock<IDepartmentSettingsService>();
			_encryption = new Mock<IEncryptionService>();
			_encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns((string s) => "E(" + s + ")");
			_encryption.Setup(e => e.Decrypt(It.IsAny<string>())).Returns((string s) => s.Substring(2, s.Length - 3));
			_service = new IntakeChannelsService(_repository.Object, _settings.Object, _encryption.Object, TimeProvider.System);
		}

		[TestCase(IntakeChannelTypes.Sms, "(775) 555-0100", "+17755550100")]
		[TestCase(IntakeChannelTypes.Sms, "17755550100", "+17755550100")]
		[TestCase(IntakeChannelTypes.Voice, "+44 20 7946 0958", "+442079460958")]
		[TestCase(IntakeChannelTypes.WhatsApp, "whatsapp:+14155238886", "+14155238886")]
		[TestCase(IntakeChannelTypes.Email, "Hospital.Line@dispatch.resgrid.com", "hospital.line")]
		[TestCase(IntakeChannelTypes.Email, " MutualAid ", "mutualaid")]
		[TestCase(IntakeChannelTypes.Slack, "t012abcde/c034fghij", "T012ABCDE/C034FGHIJ")]
		[TestCase(IntakeChannelTypes.Teams, "72F988BF-86F1-41AF-91AB-2D7CD011DB47/19:abc123@thread.tacv2;messageid=1700000000000", "72f988bf-86f1-41af-91ab-2d7cd011db47/19:abc123@thread.tacv2")]
		[TestCase(IntakeChannelTypes.Telegram, "-1001234567890", "-1001234567890")]
		[TestCase(IntakeChannelTypes.Discord, "1234567890123456789", "1234567890123456789")]
		[TestCase(IntakeChannelTypes.GoogleChat, "AAAAbCdEfGh", "spaces/AAAAbCdEfGh")]
		[TestCase(IntakeChannelTypes.GoogleChat, "spaces/AAAAbCdEfGh", "spaces/AAAAbCdEfGh")]
		public void addresses_normalize_per_transport(IntakeChannelTypes type, string input, string expected)
		{
			_service.NormalizeAddress(type, input).Should().Be(expected);
		}

		[TestCase(IntakeChannelTypes.Sms, "call me")]
		[TestCase(IntakeChannelTypes.Sms, "12")]
		[TestCase(IntakeChannelTypes.Email, "bad..name")]
		[TestCase(IntakeChannelTypes.Email, "has space")]
		[TestCase(IntakeChannelTypes.Slack, "C034FGHIJ")]
		[TestCase(IntakeChannelTypes.Slack, "T012ABCDE/D034FGHIJ")]
		[TestCase(IntakeChannelTypes.Teams, "not-a-guid/19:abc@thread.tacv2")]
		[TestCase(IntakeChannelTypes.Telegram, "@mygroup")]
		[TestCase(IntakeChannelTypes.Discord, "general")]
		[TestCase(IntakeChannelTypes.Webhook, "short")]
		[TestCase(IntakeChannelTypes.Sms, "")]
		public void invalid_addresses_are_rejected(IntakeChannelTypes type, string input)
		{
			_service.NormalizeAddress(type, input).Should().BeNull();
		}

		[Test]
		public async Task save_stores_the_normalized_address()
		{
			var saved = await _service.SaveChannelAsync(new IntakeChannel
			{
				DepartmentId = DepartmentId, Name = "  Mutual aid  ", ChannelType = (int)IntakeChannelTypes.Slack, Address = "t012abcde/c034fghij",
				ProcessingMode = (int)IntakeProcessingModes.Template, AllowedSenders = "U1, U2;;U1\nU3", IsEnabled = true
			}, "admin");

			saved.Address.Should().Be("T012ABCDE/C034FGHIJ");
			saved.Name.Should().Be("Mutual aid");
			saved.AllowedSenders.Should().Be("U1,U2,U3");
			saved.CreatedByUserId.Should().Be("admin");
		}

		[Test]
		public async Task an_address_another_channel_listens_on_is_refused()
		{
			_repository.Setup(r => r.GetLiveByAddressAsync((int)IntakeChannelTypes.Email, "transfers"))
				.ReturnsAsync(new IntakeChannel { IntakeChannelId = "other", DepartmentId = 99 });

			Func<Task> save = () => _service.SaveChannelAsync(new IntakeChannel
			{
				DepartmentId = DepartmentId, Name = "Transfers", ChannelType = (int)IntakeChannelTypes.Email, Address = "Transfers",
				ProcessingMode = (int)IntakeProcessingModes.Template
			}, "admin");

			(await save.Should().ThrowAsync<IntakeChannelValidationException>()).Which.Code.Should().Be("AddressInUse");
		}

		[Test]
		public async Task an_email_intake_address_cannot_be_a_departments_dispatch_address()
		{
			_settings.Setup(s => s.GetDepartmentIdForDispatchEmailAsync("springfieldfire")).ReturnsAsync(12);

			Func<Task> save = () => _service.SaveChannelAsync(new IntakeChannel
			{
				DepartmentId = DepartmentId, Name = "Email", ChannelType = (int)IntakeChannelTypes.Email, Address = "springfieldfire",
				ProcessingMode = (int)IntakeProcessingModes.Template
			}, "admin");

			(await save.Should().ThrowAsync<IntakeChannelValidationException>()).Which.Code.Should().Be("AddressInUse");
		}

		[Test]
		public async Task an_invalid_address_is_refused()
		{
			Func<Task> save = () => _service.SaveChannelAsync(new IntakeChannel
			{
				DepartmentId = DepartmentId, Name = "Text", ChannelType = (int)IntakeChannelTypes.Sms, Address = "not a number",
				ProcessingMode = (int)IntakeProcessingModes.Template
			}, "admin");

			(await save.Should().ThrowAsync<IntakeChannelValidationException>()).Which.Code.Should().Be("InvalidAddress");
		}

		[Test]
		public async Task a_webhook_gets_a_generated_token_whatever_was_typed_and_a_secret_only_on_rotate()
		{
			var saved = await _service.SaveChannelAsync(new IntakeChannel
			{
				DepartmentId = DepartmentId, Name = "CAD bridge", ChannelType = (int)IntakeChannelTypes.Webhook, Address = "chosen-by-admin",
				ProcessingMode = (int)IntakeProcessingModes.EnhancedAi, IsEnabled = true
			}, "admin");

			saved.Address.Should().NotBe("chosen-by-admin");
			IntakeChannelAddresses.Normalize(IntakeChannelTypes.Webhook, saved.Address).Should().Be(saved.Address);
			saved.WebhookSecretProtected.Should().BeNull();

			_repository.Setup(r => r.GetByIdAsync("new-id")).ReturnsAsync(saved);
			var secret = await _service.RotateWebhookSecretAsync(DepartmentId, "new-id", "admin");

			secret.Should().StartWith("whsec_");
			saved.WebhookSecretProtected.Should().Be("enc1:E(" + secret + ")");
			_service.GetWebhookSecret(saved).Should().Be(secret);
		}

		[Test]
		public async Task another_departments_channel_is_invisible()
		{
			_repository.Setup(r => r.GetByIdAsync("x")).ReturnsAsync(new IntakeChannel { IntakeChannelId = "x", DepartmentId = 99 });

			(await _service.GetChannelByIdAsync(DepartmentId, "x")).Should().BeNull();
			(await _service.RotateWebhookSecretAsync(DepartmentId, "x", "admin")).Should().BeNull();
			(await _service.DeleteChannelAsync(DepartmentId, "x", "admin")).Should().BeFalse();
		}

		[Test]
		public async Task ensure_creates_once_and_re_enables_a_disabled_channel()
		{
			var created = await _service.EnsureChannelAsync(DepartmentId, IntakeChannelTypes.Sms, "7755550100", "Dedicated number", "admin");
			created.Address.Should().Be("+17755550100");
			created.IsEnabled.Should().BeTrue();
			created.ProcessingMode.Should().Be((int)IntakeProcessingModes.Template);

			created.IsEnabled = false;
			_repository.Setup(r => r.GetLiveByAddressAsync((int)IntakeChannelTypes.Sms, "+17755550100")).ReturnsAsync(created);
			var again = await _service.EnsureChannelAsync(DepartmentId, IntakeChannelTypes.Sms, "+1 775 555 0100", "Dedicated number", "admin");

			again.Should().BeSameAs(created);
			again.IsEnabled.Should().BeTrue();
		}

		[Test]
		public async Task ensure_refuses_a_number_another_department_holds()
		{
			_repository.Setup(r => r.GetLiveByAddressAsync((int)IntakeChannelTypes.Sms, "+17755550100"))
				.ReturnsAsync(new IntakeChannel { IntakeChannelId = "o", DepartmentId = 99, IsEnabled = true });

			Func<Task> ensure = () => _service.EnsureChannelAsync(DepartmentId, IntakeChannelTypes.Sms, "+17755550100", "x", "admin");

			(await ensure.Should().ThrowAsync<IntakeChannelValidationException>()).Which.Code.Should().Be("AddressInUse");
		}

		[Test]
		public async Task disable_for_address_only_touches_the_departments_channel()
		{
			var channel = new IntakeChannel { IntakeChannelId = "c", DepartmentId = DepartmentId, IsEnabled = true, ChannelType = (int)IntakeChannelTypes.Voice };
			_repository.Setup(r => r.GetLiveByAddressAsync((int)IntakeChannelTypes.Voice, "+17755550100")).ReturnsAsync(channel);

			await _service.DisableChannelsForAddressAsync(99, IntakeChannelTypes.Voice, "+17755550100");
			channel.IsEnabled.Should().BeTrue();

			await _service.DisableChannelsForAddressAsync(DepartmentId, IntakeChannelTypes.Voice, "+17755550100");
			channel.IsEnabled.Should().BeFalse();
		}

		[Test]
		public async Task resolve_returns_only_enabled_channels()
		{
			var channel = new IntakeChannel { IntakeChannelId = "c", DepartmentId = DepartmentId, IsEnabled = false };
			_repository.Setup(r => r.GetLiveByAddressAsync((int)IntakeChannelTypes.Telegram, "-100123")).ReturnsAsync(channel);

			(await _service.ResolveChannelAsync(IntakeChannelTypes.Telegram, "-100123")).Should().BeNull();
			channel.IsEnabled = true;
			(await _service.ResolveChannelAsync(IntakeChannelTypes.Telegram, "-100123")).Should().BeSameAs(channel);
		}
	}
}
