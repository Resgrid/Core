using System;
using System.Net.Mail;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// EmailService is the one place the department-scoped operational emails are composed, so it is the one
	/// place the department masthead (RMS plan section 4.10.1) is looked up and handed to the provider. A
	/// branding lookup that fails must degrade to Resgrid chrome, never to an unsent dispatch email.
	/// </summary>
	[TestFixture]
	public class EmailServiceBrandingTests
	{
		private const int Dept = 4;
		private Mock<IEmailProvider> _provider;
		private Mock<IEmailSender> _sender;
		private Mock<IDepartmentProfileMediaService> _branding;
		private EmailService _service;
		private bool _departmentWasAlreadyBypassed;

		[SetUp]
		public void SetUp()
		{
			// The test configuration runs with DoNotBroadcast on; the bypass list is how a department's
			// outbound path is exercised without touching the global switch.
			_departmentWasAlreadyBypassed = Resgrid.Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(Dept);
			if (!_departmentWasAlreadyBypassed)
				Resgrid.Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Add(Dept);

			_provider = new Mock<IEmailProvider>();
			_sender = new Mock<IEmailSender>();
			_branding = new Mock<IDepartmentProfileMediaService>();

			_service = new EmailService(Mock.Of<IUserProfileService>(), Mock.Of<IUsersService>(), Mock.Of<IGeoLocationProvider>(), _provider.Object,
				Mock.Of<IDepartmentsService>(), Mock.Of<ICallEmailProvider>(), _sender.Object, Mock.Of<IAmazonEmailSender>(), _branding.Object);
		}

		[TearDown]
		public void TearDown()
		{
			if (!_departmentWasAlreadyBypassed)
				Resgrid.Config.SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Remove(Dept);
		}

		private static DepartmentEmailBranding Branded()
		{
			return new DepartmentEmailBranding { DepartmentId = Dept, Enabled = true, DisplayName = "Springfield Fire", LogoUrl = "https://app.example/User/Department/PublicMasthead?key=abc" };
		}

		private static Call SampleCall()
		{
			return new Call { CallId = 7, DepartmentId = Dept, Name = "Structure Fire", NatureOfCall = "Smoke showing", Priority = 3, LoggedOn = DateTime.UtcNow, Address = "100 Main St" };
		}

		[Test]
		public async Task Call_email_is_composed_with_the_department_branding()
		{
			_branding.Setup(b => b.GetEmailBrandingAsync(Dept)).ReturnsAsync(Branded());
			DepartmentEmailBranding passed = null;
			_provider.Setup(p => p.SendCallMail(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DepartmentEmailBranding>()))
				.Callback(new InvocationAction(i => passed = (DepartmentEmailBranding)i.Arguments[12]))
				.ReturnsAsync(true);

			var dispatch = new CallDispatch { UserId = "u1", User = new IdentityUser { Email = "member@example.com" } };
			await _service.SendCallAsync(SampleCall(), dispatch, new UserProfile { UserId = "u1", SendEmail = true });

			passed.Should().NotBeNull();
			passed.Enabled.Should().BeTrue();
			passed.LogoUrl.Should().Be("https://app.example/User/Department/PublicMasthead?key=abc");
		}

		[Test]
		public async Task Message_email_is_composed_with_the_department_branding()
		{
			_branding.Setup(b => b.GetEmailBrandingAsync(Dept)).ReturnsAsync(Branded());
			DepartmentEmailBranding passed = null;
			_provider.Setup(p => p.SendMessageMail(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DepartmentEmailBranding>()))
				.Callback(new InvocationAction(i => passed = (DepartmentEmailBranding)i.Arguments[8]))
				.ReturnsAsync(true);

			var message = new Message { MessageId = 5, Subject = "Drill", Body = "Tonight", SentOn = DateTime.UtcNow, ReceivingUserId = "u1" };
			await _service.SendMessageAsync(message, "Chief", Dept, new UserProfile { UserId = "u1", SendMessageEmail = true }, new IdentityUser { Email = "member@example.com" });

			passed.Should().NotBeNull();
			passed.Enabled.Should().BeTrue();
		}

		[Test]
		public async Task A_failed_branding_lookup_still_sends_the_call_email_with_resgrid_chrome()
		{
			_branding.Setup(b => b.GetEmailBrandingAsync(Dept)).ThrowsAsync(new InvalidOperationException("redis down"));
			DepartmentEmailBranding passed = null;
			_provider.Setup(p => p.SendCallMail(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DepartmentEmailBranding>()))
				.Callback(new InvocationAction(i => passed = (DepartmentEmailBranding)i.Arguments[12]))
				.ReturnsAsync(true);

			var dispatch = new CallDispatch { UserId = "u1", User = new IdentityUser { Email = "member@example.com" } };
			var result = await _service.SendCallAsync(SampleCall(), dispatch, new UserProfile { UserId = "u1", SendEmail = true });

			result.Should().BeTrue();
			passed.Should().NotBeNull("the provider still gets a branding object, just a disabled one");
			passed.Enabled.Should().BeFalse();
		}

		[Test]
		public async Task Notification_email_from_display_name_carries_the_department_identity_when_branding_is_on()
		{
			_branding.Setup(b => b.GetEmailBrandingAsync(Dept)).ReturnsAsync(Branded());
			string fromDisplayName = null;
			_sender.Setup(s => s.SendEmail(It.IsAny<MailMessage>())).Callback<MailMessage>(m => fromDisplayName = m.From.DisplayName).ReturnsAsync(true);

			await _service.SendNotificationAsync("u1", "A record was returned for correction.", Dept,
				new UserProfile { UserId = "u1", SendNotificationEmail = true, MembershipEmail = "member@example.com" });

			fromDisplayName.Should().Be("Springfield Fire via Resgrid");
		}

		[Test]
		public async Task Notification_email_from_display_name_stays_resgrid_without_branding()
		{
			_branding.Setup(b => b.GetEmailBrandingAsync(Dept)).ReturnsAsync(DepartmentEmailBranding.Disabled(Dept, "Springfield Fire"));
			string fromDisplayName = null;
			_sender.Setup(s => s.SendEmail(It.IsAny<MailMessage>())).Callback<MailMessage>(m => fromDisplayName = m.From.DisplayName).ReturnsAsync(true);

			await _service.SendNotificationAsync("u1", "A record was returned for correction.", Dept,
				new UserProfile { UserId = "u1", SendNotificationEmail = true, MembershipEmail = "member@example.com" });

			fromDisplayName.Should().Be("Resgrid");
		}

		[Test]
		public void From_display_name_needs_both_the_opt_in_and_a_name()
		{
			EmailService.NotificationFromDisplayName(null).Should().Be("Resgrid");
			EmailService.NotificationFromDisplayName(new DepartmentEmailBranding { Enabled = true, DisplayName = "  " }).Should().Be("Resgrid");
			EmailService.NotificationFromDisplayName(new DepartmentEmailBranding { Enabled = false, DisplayName = "Springfield Fire" }).Should().Be("Resgrid");
			EmailService.NotificationFromDisplayName(new DepartmentEmailBranding { Enabled = true, DisplayName = " Springfield Fire " }).Should().Be("Springfield Fire via Resgrid");
		}
	}
}
