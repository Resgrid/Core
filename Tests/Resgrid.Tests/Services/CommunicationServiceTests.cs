using System;
using System.Collections.ObjectModel;
using System.ServiceModel.Configuration;
using Moq;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Framework.Testing;
using Resgrid.Model;
using Resgrid.Model.Messages;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Tests.Services
{
	namespace CommunicationServiceTests
	{
		public class with_the_communication_service : TestBase
		{
			protected Mock<ISmsService> _smsServiceMock;
			protected Mock<IEmailService> _emailServiceMock;
			protected Mock<IPushService> _pushServiceMock;
			protected Mock<IGeoLocationProvider> _geoLocationProviderMock;
			protected Mock<IOutboundVoiceProvider> _outboundVoiceProviderMock;
			protected Mock<IUserProfileService> _userProfileServiceMock;
			protected Mock<IDepartmentSettingsService> _departmentSettingsServiceMock;

			protected ICommunicationService _communicationService;

			protected with_the_communication_service()
			{
				_smsServiceMock = new Mock<ISmsService>();
				_emailServiceMock = new Mock<IEmailService>();
				_pushServiceMock = new Mock<IPushService>();
				_geoLocationProviderMock = new Mock<IGeoLocationProvider>();
				_outboundVoiceProviderMock = new Mock<IOutboundVoiceProvider>();
				_userProfileServiceMock = new Mock<IUserProfileService>();
				_departmentSettingsServiceMock = new Mock<IDepartmentSettingsService>();

				_communicationService = new CommunicationService(_smsServiceMock.Object, _emailServiceMock.Object, _pushServiceMock.Object,
					_geoLocationProviderMock.Object, _outboundVoiceProviderMock.Object, _userProfileServiceMock.Object, _departmentSettingsServiceMock.Object);
			}
		}

		[TestFixture]
		public class when_sending_a_communication : with_the_communication_service
		{
			//[Test]
			public void should_be_able_to_send_message()
			{
				Message message = new Message();
				message.Subject = "Test";
				message.Body = "Test Body";
				message.IsBroadcast = true;
				message.ReceivingUserId = TestData.Users.TestUser1Id;
				message.ReceivingUser = new IdentityUser()
				{
					UserId = TestData.Users.TestUser1Id,
					Email = "test@resgrid.com"
				};

				UserProfile profile = new UserProfile();
				profile.UserId = TestData.Users.TestUser1Id;
				profile.MobileNumber = "555-555-5555";
				profile.MobileCarrier = (int)MobileCarriers.Att;
				profile.SendMessageEmail = true;
				profile.SendMessagePush = true;
				profile.SendMessageSms = true;

				_communicationService.SendMessage(message, "Test Sender", "0000000", 1, profile);
				_smsServiceMock.Verify(m => m.SendMessage(message, "0000000", 1, profile));
				_emailServiceMock.Verify(m => m.SendMessage(message, "Test Sender", profile, message.ReceivingUser));
				_pushServiceMock.Verify(m => m.PushMessage(It.IsAny<StandardPushMessage>(), TestData.Users.TestUser1Id, profile));
			}

			//[Test]
			public void should_be_able_to_send_call()
			{
				Call call = new Call();
				call.DepartmentId = 1;
				call.Name = "Priority 1E Cardiac Arrest D12";
				call.NatureOfCall = "RP reports a person lying on the street not breathing.";
				call.Notes = "RP doesn't know how to do CPR, can't roll over patient";
				call.MapPage = "22T";
				call.GeoLocationData = "39.27710789298309,-119.77201511943328";
				call.Dispatches = new Collection<CallDispatch>();
				call.LoggedOn = DateTime.Now;
				call.ReportingUserId = TestData.Users.TestUser1Id;

				CallDispatch cd = new CallDispatch();
				cd.UserId = TestData.Users.TestUser1Id;
				call.Dispatches.Add(cd);

				CallDispatch cd1 = new CallDispatch();
				cd1.UserId = TestData.Users.TestUser2Id;
				call.Dispatches.Add(cd1);

				_communicationService.SendCall(call, cd, null, 1, null);
				_smsServiceMock.Verify(m => m.SendCall(call, cd, null, 1, null, null));
				_emailServiceMock.Verify(m => m.SendCall(call, cd, null));
				//_pushServiceMock.Verify(m => m.PushCall(It.IsAny<StandardPushCall>(), Users.TestUser1Id));
			}
		}
	}
}
