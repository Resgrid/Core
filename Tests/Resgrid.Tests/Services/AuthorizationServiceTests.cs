using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework.Testing;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Services
{
	namespace AuthorizationServiceTests
	{
		public class with_the_authorization_service : TestBase
		{
			protected IAuthorizationService _authorizationService;

			protected with_the_authorization_service()
			{
				_authorizationService = Resolve<IAuthorizationService>();
			}
		}

		[TestFixture]
		public class when_authroizing_a_delete_action : with_the_authorization_service
		{
			[Test]
			public async Task should_be_able_to_delete_user_in_department_for_managingUser()
			{
				var valid = await _authorizationService.CanUserDeleteUserAsync(1, TestData.Users.TestUser1Id, TestData.Users.TestUser2Id);

				valid.Should().BeTrue();
			}

			[Test]
			public async Task should_be_able_to_delete_user_in_department_for_adminUser()
			{
				var valid = await _authorizationService.CanUserDeleteUserAsync(1, TestData.Users.TestUser2Id, TestData.Users.TestUser3Id);

				valid.Should().BeTrue();
			}

			[Test]
			public async Task should_not_be_able_to_delete_user_in_same_department()
			{
				var valid = await _authorizationService.CanUserDeleteUserAsync(1, TestData.Users.TestUser3Id, TestData.Users.TestUser4Id);

				valid.Should().BeFalse();
			}

			[Test]
			public async Task should_not_be_able_to_delete_user_in_different_department()
			{
				var valid = await _authorizationService.CanUserDeleteUserAsync(1, TestData.Users.TestUser3Id, TestData.Users.TestUser6Id);

				valid.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_authroizing_managing_invites : with_the_authorization_service
		{
			[Test]
			public async Task should_be_able_to_manage_an_invite()
			{
				var valid = await _authorizationService.CanUserManageInviteAsync(TestData.Users.TestUser1Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			public async Task should_not_be_able_to_manage_an_invite()
			{
				var valid = await _authorizationService.CanUserManageInviteAsync(TestData.Users.TestUser1Id, 3);

				valid.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_authroizing_a_call : with_the_authorization_service
		{
			[Test]
			public async Task should_be_able_to_view_call()
			{
				var valid = await _authorizationService.CanUserViewCallAsync(TestData.Users.TestUser1Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			public async Task should_not_be_able_to_view_call()
			{
				var valid = await _authorizationService.CanUserViewCallAsync(TestData.Users.TestUser5Id, 1);

				valid.Should().BeFalse();
			}

			[Test]
			public async Task should_be_able_to_edit_call()
			{
				var valid = await _authorizationService.CanUserEditCallAsync(TestData.Users.TestUser1Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			public async Task should_not_be_able_to_edit_call()
			{
				var valid = await _authorizationService.CanUserEditCallAsync(TestData.Users.TestUser3Id, 1);

				valid.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_authroizing_a_message : with_the_authorization_service
		{
			[Test]
			public async Task should_be_able_to_view_message_as_sender()
			{
				var valid = await _authorizationService.CanUserViewMessageAsync(TestData.Users.TestUser1Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			[Ignore("")]
			public async Task should_be_able_to_view_message_as_recipient()
			{
				var valid = await _authorizationService.CanUserViewMessageAsync(TestData.Users.TestUser2Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			public async Task should_not_be_able_to_view_message()
			{
				var valid = await _authorizationService.CanUserViewMessageAsync(TestData.Users.TestUser4Id, 1);

				valid.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_authroizing_a_log : with_the_authorization_service
		{
			[Test]
			public async Task should_be_able_to_view_log_with_department_admin()
			{
				var valid = await _authorizationService.CanUserViewAndEditWorkLogAsync(TestData.Users.TestUser1Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			public async Task should_be_able_to_view_log_with_creator()
			{
				var valid = await _authorizationService.CanUserViewMessageAsync(TestData.Users.TestUser1Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			public async Task should_not_be_able_to_view_not_sent_to()
			{
				var valid = await _authorizationService.CanUserViewMessageAsync(TestData.Users.TestUser4Id, 1);

				valid.Should().BeFalse();
			}
		}
	}
}
