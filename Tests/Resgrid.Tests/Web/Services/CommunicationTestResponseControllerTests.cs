using System;
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
