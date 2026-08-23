using System.Net.Mail;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.NumberProvider;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Outbound SMS leaves by two transports that need the number in different shapes, and
	/// UserProfile.GetPhoneNumber() is the right shape for neither once a number is stored in E.164:
	/// it strips the leading "+" but keeps the country code.
	///
	/// A direct provider send needs full E.164 - Twilio takes the value verbatim, and a non-US number
	/// without its "+" is meaningless. A carrier gateway address ("{0}@vtext.com") needs the bare
	/// national number - 11 digits addresses a mailbox that does not exist.
	/// </summary>
	[TestFixture]
	public class SmsServiceNumberFormatTests
	{
		private Mock<ITextMessageProvider> _textMessageProvider;
		private Mock<IEmailSender> _emailSender;
		private SmsService _service;

		[SetUp]
		public void SetUp()
		{
			_textMessageProvider = new Mock<ITextMessageProvider>();
			_emailSender = new Mock<IEmailSender>();

			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = false;
			Resgrid.Config.SystemBehaviorConfig.DepartmentsToForceSmsGateway.Clear();

			_service = new SmsService(
				new Mock<IUserProfileService>().Object,
				new Mock<IGeoLocationProvider>().Object,
				_textMessageProvider.Object,
				new Mock<IDepartmentSettingsService>().Object,
				_emailSender.Object,
				new Mock<ISubscriptionsService>().Object,
				new Mock<ICacheProvider>().Object,
				// The real processor: deterministic, no I/O, and the point of the test is that the
				// service asks it for the correct form.
				new PhoneNumberProcesserProvider());
		}

		private static UserProfile Profile(string mobileNumber, MobileCarriers carrier) => new UserProfile
		{
			UserId = "user-1",
			MobileNumber = mobileNumber,
			MobileCarrier = (int)carrier,
			SendMessageSms = true
		};

		private Task SendAsync(UserProfile profile) =>
			_service.SendMessageAsync(new Message { Subject = "Subject", Body = "Body" }, "+15555550100", 1, profile);

		[TestCase("+12705550101")]
		[TestCase("(270) 555-0101")]
		[TestCase("2705550101")]
		public async Task Direct_send_receives_the_number_in_e164(string stored)
		{
			// Verizon is a direct-send carrier, so this goes to the provider rather than a gateway.
			await SendAsync(Profile(stored, MobileCarriers.Verizon));

			_textMessageProvider.Verify(x => x.SendTextMessage(
				"+12705550101", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MobileCarriers>(),
				It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>()), Times.Once);
		}

		[Test]
		public async Task Direct_send_keeps_the_plus_on_a_non_us_number()
		{
			// Without the "+" this number has no meaning to the provider at all.
			await SendAsync(Profile("+61255501234", MobileCarriers.Telstra));

			_textMessageProvider.Verify(x => x.SendTextMessage(
				"+61255501234", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MobileCarriers>(),
				It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>()), Times.Once);
		}

		[TestCase("+12705550101")]
		[TestCase("(270) 555-0101")]
		[TestCase("2705550101")]
		public async Task Carrier_gateway_receives_the_bare_national_number(string stored)
		{
			// MetroPCS has no direct-send route, so this addresses the carrier's SMS gateway.
			MailMessage sent = null;
			_emailSender.Setup(x => x.SendEmail(It.IsAny<MailMessage>()))
				.Callback<MailMessage>(m => sent = m)
				.ReturnsAsync(true);

			await SendAsync(Profile(stored, MobileCarriers.MetroPcs));

			sent.Should().NotBeNull();
			sent.To.Should().ContainSingle().Which.Address.Should().Be("2705550101@mymetropcs.com");
		}

		[Test]
		public async Task An_unparseable_number_falls_back_to_the_previous_behaviour()
		{
			// Nothing valid to send to. Rather than dropping the message on a new code path, it goes
			// out exactly as it did before this change and the provider rejects it as it did before.
			await SendAsync(Profile("12345", MobileCarriers.Verizon));

			_textMessageProvider.Verify(x => x.SendTextMessage(
				"12345", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MobileCarriers>(),
				It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>()), Times.Once);
		}
	}
}
