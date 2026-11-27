using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Round 10 A5: what a member can use for a step-up (the refusal's usable methods) and the settings warning: the current members who
	/// could not complete an area's step-up as the policy stands, named, with why.
	/// </summary>
	[TestFixture]
	public class StepUpCapabilityServiceTests
	{
		private const int DepartmentId = 42;

		private List<DepartmentMember> _members;
		private Dictionary<string, IdentityUser> _users;
		private HashSet<string> _ssoOnly;
		private HashSet<string> _linked;
		private bool _brokeredSso;
		private Mock<IStepUpPolicyService> _policy;
		private Mock<IMfaPolicyService> _mfaPolicy;
		private Mock<IPasskeyService> _passkeys;
		private Mock<IMfaApprovalService> _approvals;
		private Mock<IDepartmentSsoService> _departmentSso;

		[SetUp]
		public void SetUp()
		{
			_members = new List<DepartmentMember>();
			_users = new Dictionary<string, IdentityUser>();
			_ssoOnly = new HashSet<string>();
			_linked = new HashSet<string>();
			_brokeredSso = false;
			_policy = StepUpTestPolicies.Of(null);
			_mfaPolicy = new Mock<IMfaPolicyService>();
			_passkeys = new Mock<IPasskeyService>();
			_approvals = new Mock<IMfaApprovalService>();
			_departmentSso = new Mock<IDepartmentSsoService>();
			_departmentSso.Setup(s => s.GetSsoConfigsForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => _brokeredSso ? new[] { new DepartmentSsoConfig { DepartmentId = DepartmentId, IsEnabled = true } } : new DepartmentSsoConfig[0]);
		}

		private void Member(string id, string first, bool twoFactor, bool password = true, bool deleted = false, bool disabled = false, bool hidden = false)
		{
			_members.Add(new DepartmentMember { DepartmentId = DepartmentId, UserId = id, IsDeleted = deleted, IsDisabled = disabled, IsHidden = hidden });
			_users[id] = new IdentityUser { Id = id, UserName = id, TwoFactorEnabled = twoFactor, PasswordHash = password ? "hash" : null };
		}

		private StepUpCapabilityService Service()
		{
			var users = new Mock<IUsersService>();
			users.Setup(u => u.GetUserById(It.IsAny<string>(), It.IsAny<bool>())).Returns((string id, bool _) => _users.TryGetValue(id, out var u) ? u : null);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetAllUsersForDepartmentAsync(DepartmentId, true, true))
				.ReturnsAsync(() => _members.Where(m => !m.IsDeleted && m.IsDisabled != true).Select(m => _users[m.UserId]).ToList());
			departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(DepartmentId))
				.ReturnsAsync(() => _members.Select(m => new PersonName { UserId = m.UserId, FirstName = m.UserId == "u-zed" ? "Zed" : "Ann", LastName = m.UserId }).ToList());
			var memberRows = new Mock<IDepartmentMembersRepository>();
			memberRows.Setup(r => r.GetAllDepartmentMembersUnlimitedAsync(DepartmentId)).ReturnsAsync(() => _members);
			memberRows.Setup(r => r.GetDepartmentMemberByDepartmentIdAndUserIdAsync(DepartmentId, It.IsAny<string>()))
				.ReturnsAsync((int _, string id) => _members.FirstOrDefault(m => m.UserId == id));
			var links = new Mock<IExternalIdentityLinkService>();
			links.Setup(l => l.IsLocalLoginAllowedAsync(It.IsAny<string>(), DepartmentId, It.IsAny<CancellationToken>()))
				.ReturnsAsync((string id, int _, CancellationToken _) => !_ssoOnly.Contains(id));
			links.Setup(l => l.GetActiveLinksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string id, CancellationToken _) => (IReadOnlyList<UserExternalIdentityLink>)(_linked.Contains(id)
					? new List<UserExternalIdentityLink> { new() { UserId = id, DepartmentId = DepartmentId, IsActive = true } }
					: new List<UserExternalIdentityLink>()));
			var broker = new Mock<ISsoBrokerService>();
			broker.SetupGet(b => b.IsEnabled).Returns(true);
			broker.Setup(b => b.SupportsBrokered(It.IsAny<DepartmentSsoConfig>())).Returns(true);
			return new StepUpCapabilityService(_policy.Object, _mfaPolicy.Object, users.Object, departments.Object, memberRows.Object, _passkeys.Object,
				_approvals.Object, _departmentSso.Object, links.Object, broker.Object);
		}

		private static StepUpRule Rule(StepUpArea area, StepUpMethodClasses methods = StepUpMethodClasses.Mfa, bool required = true) =>
			StepUpTestPolicies.Rule(area, required, methods);

		[Test]
		public async Task With_a_second_factor_only_every_current_member_without_two_factor_is_listed_by_name()
		{
			Member("u-zed", "Zed", twoFactor: false);
			Member("u-amy", "Ann", twoFactor: false);
			Member("u-ok", "Ann", twoFactor: true);
			Member("u-gone", "Ann", twoFactor: false, deleted: true);
			Member("u-off", "Ann", twoFactor: false, disabled: true);
			Member("u-hid", "Ann", twoFactor: false, hidden: true);

			var readiness = (await Service().GetReadinessAsync(DepartmentId, new[] { Rule(StepUpArea.Signatures) })).Single();

			readiness.ShowWarning.Should().BeTrue();
			readiness.MemberCount.Should().Be(3, "deleted and disabled members are skipped; a hidden member can still be asked to sign");
			readiness.Members.Select(m => m.UserId).Should().BeEquivalentTo("u-zed", "u-amy", "u-hid");
			readiness.Members.Should().OnlyContain(m => m.Reason == StepUpGapReasons.NoSecondFactor);
			readiness.Members.First(m => m.UserId == "u-zed").Name.Should().Be("Zed u-zed");
		}

		[Test]
		public async Task Allowing_the_password_clears_members_who_have_one_and_names_why_the_others_cannot()
		{
			Member("u-pwd", "Ann", twoFactor: false);
			Member("u-sso", "Ann", twoFactor: false, password: false);
			Member("u-ssoonly", "Ann", twoFactor: false);
			_ssoOnly.Add("u-ssoonly");

			var readiness = (await Service().GetReadinessAsync(DepartmentId, new[] { Rule(StepUpArea.Approvals, StepUpMethodClasses.Mfa | StepUpMethodClasses.Password) })).Single();

			readiness.Members.Select(m => m.UserId).Should().BeEquivalentTo("u-sso", "u-ssoonly");
			readiness.Members.Should().OnlyContain(m => m.Reason == StepUpGapReasons.NoPassword);
		}

		[Test]
		public async Task Allowing_sso_clears_linked_members_only_when_the_department_has_a_brokered_provider()
		{
			Member("u-linked", "Ann", twoFactor: false);
			Member("u-unlinked", "Ann", twoFactor: false);
			_linked.Add("u-linked");
			var rule = Rule(StepUpArea.Signatures, StepUpMethodClasses.Mfa | StepUpMethodClasses.Sso);

			(await Service().GetReadinessAsync(DepartmentId, new[] { rule })).Single().MemberCount.Should().Be(2, "no provider, so nobody can use SSO");

			_brokeredSso = true;
			var readiness = (await Service().GetReadinessAsync(DepartmentId, new[] { rule })).Single();
			readiness.Members.Should().ContainSingle().Which.Should().Match<StepUpMemberGap>(g => g.UserId == "u-unlinked" && g.Reason == StepUpGapReasons.NoSsoLink);
		}

		[Test]
		public async Task Provider_mfa_where_the_department_accepts_it_counts_as_a_second_factor()
		{
			Member("u-fed", "Ann", twoFactor: false);
			_mfaPolicy.Setup(p => p.IsMethodAcceptedAsync(DepartmentId, MfaMethodScope.Login, MfaEvidenceMethod.Federated, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_departmentSso.Setup(s => s.IsFederatedMfaAvailableAsync(DepartmentId, "u-fed", It.IsAny<CancellationToken>())).ReturnsAsync(true);

			(await Service().GetReadinessAsync(DepartmentId, new[] { Rule(StepUpArea.Signatures) })).Single().MemberCount.Should().Be(0);
			(await Service().GetReadinessAsync(DepartmentId, new[] { Rule(StepUpArea.ProtectedData) })).Single().MemberCount.Should().Be(1,
				"protected data follows its own provider switch, off here");
		}

		[Test]
		public async Task An_optional_area_has_no_warning_and_the_count_stays_exact_past_the_listed_names()
		{
			for (var i = 0; i < 30; i++)
				Member("u-" + i, "Ann", twoFactor: false);

			var readiness = await Service().GetReadinessAsync(DepartmentId, new[] { Rule(StepUpArea.Signatures), Rule(StepUpArea.Approvals, required: false) }, maxListed: 10);

			readiness.Single(r => r.Rule.Area == StepUpArea.Signatures).Should().Match<StepUpReadiness>(r => r.MemberCount == 30 && r.Members.Count == 10);
			readiness.Single(r => r.Rule.Area == StepUpArea.Approvals).ShowWarning.Should().BeFalse();
		}

		[Test]
		public async Task Describe_lists_the_allowed_kinds_the_member_can_use_and_their_second_factors_for_this_app()
		{
			Member("u-1", "Ann", twoFactor: true);
			_policy = StepUpTestPolicies.Of(a => a == StepUpArea.Approvals ? Rule(a, StepUpMethodClasses.Mfa | StepUpMethodClasses.Password | StepUpMethodClasses.Sso) : null);
			_mfaPolicy.Setup(p => p.IsMethodAcceptedAsync(DepartmentId, MfaMethodScope.Login, MfaEvidenceMethod.Passkey, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_passkeys.Setup(p => p.HasActiveForClientAsync("u-1", UserSessionClientApplication.Unit, It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var requirement = await Service().DescribeAsync(DepartmentId, "u-1", UserSessionClientApplication.Unit, StepUpArea.Approvals);

			requirement.Required.Should().BeTrue();
			requirement.AllowedMethods.Should().Equal("mfa", "sso", "password");
			requirement.UsableMethods.Should().Equal(new[] { "mfa", "password" }, "no provider, so not sso");
			requirement.MfaMethods.Should().Equal("totp", "passkey");
			requirement.CanComplete.Should().BeTrue();

			var other = await Service().DescribeAsync(DepartmentId, "u-1", UserSessionClientApplication.Dispatch, StepUpArea.Approvals);
			other.MfaMethods.Should().Equal(new[] { "totp" }, "a passkey counts only for the app it is bound to");
		}

		[Test]
		public async Task A_member_with_nothing_allowed_usable_cannot_complete()
		{
			Member("u-1", "Ann", twoFactor: false);
			var requirement = await Service().DescribeAsync(DepartmentId, "u-1", UserSessionClientApplication.Responder, StepUpArea.Signatures);
			requirement.UsableMethods.Should().BeEmpty();
			requirement.CanComplete.Should().BeFalse();
		}
	}
}
