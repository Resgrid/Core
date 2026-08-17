using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Twilio;
using Twilio.TwiML;
using Twilio.TwiML.Voice;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	public class CommunicationTestResponseControllerTests
	{
		private Mock<ICommunicationTestService> _communicationTestServiceMock;
		private Mock<IDepartmentSettingsService> _departmentSettingsServiceMock;
		private Mock<ITwilioVoiceResponseService> _twilioVoiceResponseServiceMock;
		private CommunicationTestResponseController _controller;

		[SetUp]
		public void SetUp()
		{
			_communicationTestServiceMock = new Mock<ICommunicationTestService>(MockBehavior.Strict);
			_departmentSettingsServiceMock = new Mock<IDepartmentSettingsService>(MockBehavior.Strict);
			_twilioVoiceResponseServiceMock = new Mock<ITwilioVoiceResponseService>(MockBehavior.Strict);
			_twilioVoiceResponseServiceMock
				.Setup(x => x.AppendPromptAsync(It.IsAny<VoiceResponse>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
				.Returns<VoiceResponse, string, CancellationToken, string>((response, text, _, __) =>
				{
					response.Append(new Play
					{
						Url = new Uri($"https://tts.example/{Uri.EscapeDataString(text)}.wav")
					});
					return System.Threading.Tasks.Task.CompletedTask;
				});

			_twilioVoiceResponseServiceMock
				.Setup(x => x.AppendPromptsAsync(It.IsAny<Gather>(), It.IsAny<System.Collections.Generic.IEnumerable<string>>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
				.Returns<Gather, System.Collections.Generic.IEnumerable<string>, CancellationToken, string>((gather, prompts, _, __) =>
				{
					foreach (var text in prompts)
					{
						gather.Append(new Play
						{
							Url = new Uri($"https://tts.example/{Uri.EscapeDataString(text)}.wav")
						});
					}
					return System.Threading.Tasks.Task.CompletedTask;
				});

			_controller = new CommunicationTestResponseController(
				_communicationTestServiceMock.Object,
				_departmentSettingsServiceMock.Object,
				_twilioVoiceResponseServiceMock.Object)
			{
				ControllerContext = new ControllerContext
				{
					HttpContext = new DefaultHttpContext()
				}
			};
		}

		[Test]
		public async System.Threading.Tasks.Task voice_webhook_should_skip_department_lookup_when_token_missing()
		{
			var result = await _controller.VoiceWebhook(null, "1");

			var content = result.Content;
			content.Should().Contain("<Play>");
			_twilioVoiceResponseServiceMock.Verify(x => x.AppendPromptAsync(It.IsAny<VoiceResponse>(), TwilioVoicePromptCatalog.CommunicationTestRecorded, It.IsAny<CancellationToken>(), null), Times.Once);
			_communicationTestServiceMock.Verify(x => x.GetDepartmentIdByResponseTokenAsync(It.IsAny<string>()), Times.Never);
			_communicationTestServiceMock.Verify(x => x.RecordVoiceResponseAsync(It.IsAny<string>()), Times.Never);
			_departmentSettingsServiceMock.Verify(x => x.GetTtsLanguageForDepartmentAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task voice_call_should_gather_a_keypress_back_to_the_webhook_with_the_token()
		{
			_communicationTestServiceMock.Setup(x => x.GetDepartmentIdByResponseTokenAsync("abc123")).ReturnsAsync(4);
			_departmentSettingsServiceMock.Setup(x => x.GetTtsLanguageForDepartmentAsync(4)).ReturnsAsync("en");
			_communicationTestServiceMock.Setup(x => x.GetRecipientLanguageByResponseTokenAsync("abc123")).ReturnsAsync((string)null);

			var result = await _controller.VoiceCall("abc123");

			var content = result.Content;
			content.Should().Contain("<Gather");
			content.Should().Contain("CommunicationTestResponse/VoiceWebhook?token=abc123");
			content.Should().Contain("<Hangup>");
			_communicationTestServiceMock.Verify(x => x.RecordVoiceResponseAsync(It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task voice_call_should_speak_the_recipients_language_not_the_departments()
		{
			_communicationTestServiceMock.Setup(x => x.GetDepartmentIdByResponseTokenAsync("abc123")).ReturnsAsync(4);
			_departmentSettingsServiceMock.Setup(x => x.GetTtsLanguageForDepartmentAsync(4)).ReturnsAsync("en");
			_communicationTestServiceMock.Setup(x => x.GetRecipientLanguageByResponseTokenAsync("abc123")).ReturnsAsync("de");

			await _controller.VoiceCall("abc123");

			// A member who set their profile to German is not reliably reached by an English recording,
			// so the call must switch both the words and the voice reading them.
			var german = Resgrid.Localization.Areas.User.CommunicationTest.CommunicationTestMessageCatalog.GetVoicePrompts("de")[0];
			_twilioVoiceResponseServiceMock.Verify(x => x.AppendPromptsAsync(
				It.IsAny<Gather>(),
				It.Is<System.Collections.Generic.IEnumerable<string>>(p => p.Contains(german)),
				It.IsAny<CancellationToken>(),
				"de"), Times.AtLeastOnce);
		}

		[Test]
		public async System.Threading.Tasks.Task voice_call_should_fall_back_to_the_department_voice_when_the_recipient_has_no_language()
		{
			_communicationTestServiceMock.Setup(x => x.GetDepartmentIdByResponseTokenAsync("abc123")).ReturnsAsync(4);
			_departmentSettingsServiceMock.Setup(x => x.GetTtsLanguageForDepartmentAsync(4)).ReturnsAsync("sv");
			_communicationTestServiceMock.Setup(x => x.GetRecipientLanguageByResponseTokenAsync("abc123")).ReturnsAsync((string)null);

			await _controller.VoiceCall("abc123");

			_twilioVoiceResponseServiceMock.Verify(x => x.AppendPromptsAsync(
				It.IsAny<Gather>(),
				It.IsAny<System.Collections.Generic.IEnumerable<string>>(),
				It.IsAny<CancellationToken>(),
				"sv"), Times.AtLeastOnce);
		}

		[Test]
		public async System.Threading.Tasks.Task voice_call_should_not_gather_without_a_token()
		{
			var result = await _controller.VoiceCall(null);

			result.Content.Should().NotContain("<Gather");
			_communicationTestServiceMock.Verify(x => x.GetDepartmentIdByResponseTokenAsync(It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task department_tts_language_helper_should_return_null_without_lookup_for_blank_token()
		{
			var method = typeof(CommunicationTestResponseController).GetMethod("GetDepartmentTtsLanguageAsync", BindingFlags.Instance | BindingFlags.NonPublic);

			var task = (System.Threading.Tasks.Task<string>)method!.Invoke(_controller, new object[] { " " });
			var result = await task;

			result.Should().BeNull();
			_communicationTestServiceMock.Verify(x => x.GetDepartmentIdByResponseTokenAsync(It.IsAny<string>()), Times.Never);
			_departmentSettingsServiceMock.Verify(x => x.GetTtsLanguageForDepartmentAsync(It.IsAny<int>()), Times.Never);
		}
	}
}
