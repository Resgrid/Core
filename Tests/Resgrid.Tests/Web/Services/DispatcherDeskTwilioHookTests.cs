using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers;
using Resgrid.Web.Services.Models;
using Resgrid.Web.Services.Twilio;
using Twilio.TwiML;
using Twilio.TwiML.Voice;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// The desk hooks in the Twilio and SignalWire webhooks (plan D8, D9): a sender or caller with no member profile is handed to the
	/// desk; members, and anything the desk does not take, keep today's handling. The controllers are built by reflection so the
	/// tests do not depend on their (often growing) constructors.
	/// </summary>
	[TestFixture]
	public class DispatcherDeskTwilioHookTests
	{
		private const int DepartmentId = 42;
		private const string DepartmentNumber = "15559990000";
		private const string Sender = "+15551234567";
		private const string MasterNumber = "+15550001111";

		private Dictionary<Type, Mock> _mocks;
		private Mock<IUserProfileService> _profiles;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<INumbersService> _numbers;
		private Mock<ITwilioVoiceResponseService> _tts;
		private Mock<IDispatcherInboundTextHandler> _textHandler;
		private Mock<IDispatcherInboundVoiceHandler> _voiceHandler;
		private List<InboundMessageEvent> _savedEvents;
		private string _signalWireKey;
		private string _twilioMaster;
		private string _signalWireMaster;

		[SetUp]
		public void SetUp()
		{
			_signalWireKey = global::Resgrid.Config.NumberProviderConfig.SignalWireApiKey;
			global::Resgrid.Config.NumberProviderConfig.SignalWireApiKey = "";
			_twilioMaster = global::Resgrid.Config.NumberProviderConfig.TwilioResgridNumber;
			_signalWireMaster = global::Resgrid.Config.NumberProviderConfig.SignalWireResgridNumber;
			global::Resgrid.Config.NumberProviderConfig.TwilioResgridNumber = MasterNumber;
			global::Resgrid.Config.NumberProviderConfig.SignalWireResgridNumber = MasterNumber;

			_mocks = new Dictionary<Type, Mock>();
			_profiles = Register<IUserProfileService>();
			_settings = Register<IDepartmentSettingsService>();
			_settings.Setup(x => x.GetDepartmentIdByTextToCallNumberAsync(DepartmentNumber)).ReturnsAsync(DepartmentId);
			_numbers = Register<INumbersService>();
			_savedEvents = new List<InboundMessageEvent>();
			_numbers.Setup(x => x.SaveInboundMessageEventAsync(It.IsAny<InboundMessageEvent>(), It.IsAny<CancellationToken>()))
				.Callback<InboundMessageEvent, CancellationToken>((e, _) => _savedEvents.Add(e))
				.ReturnsAsync((InboundMessageEvent e, CancellationToken _) => e);

			var textCommands = Register<ITextCommandService>();
			textCommands.Setup(x => x.DetermineType(It.IsAny<string>())).Returns(new TextCommandPayload { Type = TextCommandTypes.None });

			_tts = Register<ITwilioVoiceResponseService>();
			_tts.Setup(x => x.AppendPromptAsync(It.IsAny<VoiceResponse>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
				.Returns<VoiceResponse, string, CancellationToken, string>((response, text, _, __) =>
				{
					response.Append(new Play { Url = new Uri($"https://tts.example/{Uri.EscapeDataString(text)}.wav") });
					return System.Threading.Tasks.Task.CompletedTask;
				});

			// Constructor-injected (CLAUDE.md): Build hands these to the controllers.
			_textHandler = Register<IDispatcherInboundTextHandler>();
			_voiceHandler = Register<IDispatcherInboundVoiceHandler>();
		}

		[TearDown]
		public void TearDown()
		{
			global::Resgrid.Config.NumberProviderConfig.SignalWireApiKey = _signalWireKey;
			global::Resgrid.Config.NumberProviderConfig.TwilioResgridNumber = _twilioMaster;
			global::Resgrid.Config.NumberProviderConfig.SignalWireResgridNumber = _signalWireMaster;
		}

		private Mock<T> Register<T>() where T : class
		{
			var mock = new Mock<T>();
			_mocks[typeof(T)] = mock;
			return mock;
		}

		/// <summary>Builds the controller with the registered mocks and a loose mock for every other constructor parameter.</summary>
		private T Build<T>(HttpContext context) where T : ControllerBase
		{
			var constructor = typeof(T).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
			{
				if (!_mocks.TryGetValue(p.ParameterType, out var mock))
				{
					mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType));
					_mocks[p.ParameterType] = mock;
				}

				return mock.Object;
			}).ToArray();

			var controller = (T)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = context };
			return controller;
		}

		private static DefaultHttpContext Get(string query)
		{
			var context = new DefaultHttpContext();
			context.Request.Method = "GET";
			context.Request.QueryString = new QueryString(query);
			return context;
		}

		private void Member()
		{
			_profiles.Setup(x => x.GetProfileByMobileNumberAsync("15551234567"))
				.ReturnsAsync(new UserProfile { UserId = "member-1", MobileNumberVerified = true, FirstName = "Pat" });
		}

		#region Twilio SMS

		private TwilioMessage Text(string body = "There is a fire") =>
			new TwilioMessage { From = Sender, To = "+" + DepartmentNumber, Body = body, MessageSid = "SM1" };

		[Test]
		public async System.Threading.Tasks.Task Twilio_text_from_no_profile_goes_to_the_desk()
		{
			_textHandler.Setup(x => x.HandleAsync(DepartmentId, Sender, "+" + DepartmentNumber, "There is a fire", "SM1", It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);

			var result = await Build<TwilioController>(Get("")).IncomingMessage(Text());

			((ContentResult)result).Content.Should().NotContain("<Message");
			_savedEvents.Should().ContainSingle(e => e.Processed == true && e.CustomerId == DepartmentId.ToString());
			// The text commands never ran.
			((Mock<IDepartmentsService>)_mocks[typeof(IDepartmentsService)])
				.Verify(x => x.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Twilio_text_the_desk_does_not_take_keeps_todays_handling()
		{
			_textHandler.Setup(x => x.HandleAsync(It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>())).ReturnsAsync(false);

			await Build<TwilioController>(Get("")).IncomingMessage(Text());

			_textHandler.Verify(x => x.HandleAsync(DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Once);
			// The legacy text-command path ran for the department.
			((Mock<IDepartmentsService>)_mocks[typeof(IDepartmentsService)])
				.Verify(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async System.Threading.Tasks.Task Twilio_text_from_a_member_never_reaches_the_desk()
		{
			Member();

			await Build<TwilioController>(Get("")).IncomingMessage(Text());

			_textHandler.Verify(x => x.HandleAsync(It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Twilio_text_to_the_master_number_asks_the_desk_with_no_department()
		{
			var message = Text("Yes, north side");
			message.To = MasterNumber;
			_textHandler.Setup(x => x.HandleAsync(null, Sender, MasterNumber, "Yes, north side", "SM1", It.IsAny<CancellationToken>())).ReturnsAsync(true);

			await Build<TwilioController>(Get("")).IncomingMessage(message);

			_textHandler.Verify(x => x.HandleAsync(null, Sender, MasterNumber, "Yes, north side", "SM1", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async System.Threading.Tasks.Task Twilio_text_to_an_unmapped_number_never_joins_a_desk_conversation()
		{
			// No department owns the texted number and it is not the master number: "no department" must not reach the desk,
			// which would read it as a master-number reply and join the sender's conversation in any department.
			var message = Text("Yes, north side");
			message.To = "+15550002222";

			await Build<TwilioController>(Get("")).IncomingMessage(message);

			_textHandler.Verify(x => x.HandleAsync(It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Twilio_text_the_desk_does_not_take_is_unchanged()
		{
			// The handler answers false (desk off, or a member's text): today's text handling runs.
			var result = await Build<TwilioController>(Get("")).IncomingMessage(Text());

			result.Should().BeOfType<ContentResult>();
			((Mock<IDepartmentsService>)_mocks[typeof(IDepartmentsService)])
				.Verify(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()), Times.Once);
		}

		#endregion Twilio SMS

		#region Twilio voice

		private static TwilioGatherRequest Voice() => new TwilioGatherRequest { From = Sender, To = "+" + DepartmentNumber };

		[Test]
		public async System.Threading.Tasks.Task Twilio_call_from_no_profile_is_held_by_the_desk()
		{
			_voiceHandler.Setup(x => x.HandleAsync(Sender, "+" + DepartmentNumber, "CA1", It.IsAny<CancellationToken>()))
				.ReturnsAsync("<Response><Enqueue>rgdesk-item-1</Enqueue></Response>");

			var result = await Build<TwilioController>(Get("?CallSid=CA1")).InboundVoice(Voice());

			((ContentResult)result).Content.Should().Be("<Response><Enqueue>rgdesk-item-1</Enqueue></Response>");
			_tts.Verify(x => x.AppendPromptAsync(It.IsAny<VoiceResponse>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Twilio_call_the_desk_does_not_take_keeps_todays_message()
		{
			_voiceHandler.Setup(x => x.HandleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string)null);

			var result = await Build<TwilioController>(Get("?CallSid=CA1")).InboundVoice(Voice());

			((ContentResult)result).Content.Should().Contain(Uri.EscapeDataString(TwilioVoicePromptCatalog.InboundVoiceUnavailable));
			_voiceHandler.Verify(x => x.HandleAsync(Sender, "+" + DepartmentNumber, "CA1", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async System.Threading.Tasks.Task Twilio_call_from_a_member_keeps_the_voice_menu()
		{
			Member();

			await Build<TwilioController>(Get("?CallSid=CA1")).InboundVoice(Voice());

			_voiceHandler.Verify(x => x.HandleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion Twilio voice

		#region SignalWire SMS

		private static string SignalWireQuery(string body = "There is a fire", string to = "+" + DepartmentNumber) =>
			$"?To={Uri.EscapeDataString(to)}&From={Uri.EscapeDataString(Sender)}&Body={Uri.EscapeDataString(body)}&MessageSid=SW1&AccountSid=AC1";

		[Test]
		public async System.Threading.Tasks.Task SignalWire_text_from_no_profile_goes_to_the_desk()
		{
			_textHandler.Setup(x => x.HandleAsync(DepartmentId, "15551234567", DepartmentNumber, "There is a fire", "SW1", It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);

			var result = await Build<SignalWireController>(Get(SignalWireQuery())).Receive(CancellationToken.None);

			((ContentResult)result).Content.Should().BeEmpty();
			_savedEvents.Should().ContainSingle(e => e.Processed == true);
			((Mock<IDepartmentsService>)_mocks[typeof(IDepartmentsService)])
				.Verify(x => x.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task SignalWire_text_the_desk_does_not_take_keeps_todays_handling()
		{
			_textHandler.Setup(x => x.HandleAsync(It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>())).ReturnsAsync(false);

			await Build<SignalWireController>(Get(SignalWireQuery())).Receive(CancellationToken.None);

			((Mock<IDepartmentsService>)_mocks[typeof(IDepartmentsService)])
				.Verify(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async System.Threading.Tasks.Task SignalWire_text_to_the_master_number_asks_the_desk_with_no_department()
		{
			_textHandler.Setup(x => x.HandleAsync(null, "15551234567", "15550001111", "Yes, north side", "SW1", It.IsAny<CancellationToken>())).ReturnsAsync(true);

			await Build<SignalWireController>(Get(SignalWireQuery("Yes, north side", MasterNumber))).Receive(CancellationToken.None);

			_textHandler.Verify(x => x.HandleAsync(null, "15551234567", "15550001111", "Yes, north side", "SW1", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async System.Threading.Tasks.Task SignalWire_text_to_an_unmapped_number_never_joins_a_desk_conversation()
		{
			await Build<SignalWireController>(Get(SignalWireQuery("Yes, north side", "+15550002222"))).Receive(CancellationToken.None);

			_textHandler.Verify(x => x.HandleAsync(It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task SignalWire_text_from_a_member_never_reaches_the_desk()
		{
			Member();

			await Build<SignalWireController>(Get(SignalWireQuery())).Receive(CancellationToken.None);

			_textHandler.Verify(x => x.HandleAsync(It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion SignalWire SMS
	}
}
