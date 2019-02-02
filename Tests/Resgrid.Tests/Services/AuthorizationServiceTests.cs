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
			public void should_be_able_to_delete_user_in_department_for_managingUser()
			{
				var valid = _authorizationService.CanUserDeleteUser(1, TestData.Users.TestUser1Id, TestData.Users.TestUser2Id);

				valid.Should().BeTrue();
			}

			[Test]
			public void should_be_able_to_delete_user_in_department_for_adminUser()
			{
				var valid = _authorizationService.CanUserDeleteUser(1, TestData.Users.TestUser2Id, TestData.Users.TestUser3Id);

				valid.Should().BeTrue();
			}

			[Test]
			public void should_not_be_able_to_delete_user_in_same_department()
			{
				var valid = _authorizationService.CanUserDeleteUser(1, TestData.Users.TestUser3Id, TestData.Users.TestUser4Id);

				valid.Should().BeFalse();
			}

			[Test]
			public void should_not_be_able_to_delete_user_in_different_department()
			{
				var valid = _authorizationService.CanUserDeleteUser(1, TestData.Users.TestUser3Id, TestData.Users.TestUser6Id);

				valid.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_authroizing_a_delete_pushUri : with_the_authorization_service
		{
			[Test]
			public void should_be_able_to_delete_own_pushuri()
			{
				var valid = _authorizationService.CanUserDeletePushUri(TestData.Users.TestUser8Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			public void should_not_be_able_to_delete_other_pushuri()
			{
				var valid = _authorizationService.CanUserDeletePushUri(TestData.Users.TestUser6Id, 1);

				valid.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_authroizing_managing_invites : with_the_authorization_service
		{
			[Test]
			public void should_be_able_to_manage_an_invite()
			{
				var valid = _authorizationService.CanUserManageInvite(TestData.Users.TestUser1Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			public void should_not_be_able_to_manage_an_invite()
			{
				var valid = _authorizationService.CanUserManageInvite(TestData.Users.TestUser1Id, 3);

				valid.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_authroizing_a_call : with_the_authorization_service
		{
			[Test]
			public void should_be_able_to_view_call()
			{
				var valid = _authorizationService.CanUserViewCall(TestData.Users.TestUser1Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			public void should_not_be_able_to_view_call()
			{
				var valid = _authorizationService.CanUserViewCall(TestData.Users.TestUser5Id, 1);

				valid.Should().BeFalse();
			}

			[Test]
			public void should_be_able_to_edit_call()
			{
				var valid = _authorizationService.CanUserEditCall(TestData.Users.TestUser1Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			public void should_not_be_able_to_edit_call()
			{
				var valid = _authorizationService.CanUserEditCall(TestData.Users.TestUser3Id, 1);

				valid.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_authroizing_a_message : with_the_authorization_service
		{
			[Test]
			public void should_be_able_to_view_message_as_sender()
			{
				var valid = _authorizationService.CanUserViewMessage(TestData.Users.TestUser1Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			[Ignore("")]
			public void should_be_able_to_view_message_as_recipient()
			{
				var valid = _authorizationService.CanUserViewMessage(TestData.Users.TestUser2Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			public void should_not_be_able_to_view_message()
			{
				var valid = _authorizationService.CanUserViewMessage(TestData.Users.TestUser4Id, 1);

				valid.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_authroizing_a_log : with_the_authorization_service
		{
			[Test]
			public void should_be_able_to_view_log_with_department_admin()
			{
				var valid = _authorizationService.CanUserViewAndEditWorkLog(TestData.Users.TestUser1Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			public void should_be_able_to_view_log_with_creator()
			{
				var valid = _authorizationService.CanUserViewMessage(TestData.Users.TestUser1Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			public void should_not_be_able_to_view_not_sent_to()
			{
				var valid = _authorizationService.CanUserViewMessage(TestData.Users.TestUser4Id, 1);

				valid.Should().BeFalse();
			}
		}
	}
}