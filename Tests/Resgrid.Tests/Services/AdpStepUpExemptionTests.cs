using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Per-application step-up exemptions (ADP plan 3.3).
	///
	/// A department can release named apps from the second-factor prompt that guards a protected
	/// reveal, because a dispatcher on a live incident cannot stop to read a code off a phone and a
	/// prompt that lands mid-call is a safety problem rather than a security win.
	///
	/// Everything here is about the direction of the default. Nothing is exempt until someone
	/// deliberately exempts it, an unknown answer means "prompt", and a client that cannot identify
	/// itself never inherits somebody else's exemption.
	/// </summary>
	[TestFixture]
	public class AdpStepUpExemptionTests
	{
		private const int DeptId = 88;
		private const string ManagingUserId = "the-owner";
		private const string OtherAdminUserId = "an-admin";

		private Mock<IDepartmentDataProtectionPolicyRepository> _policyRepo;
		private Mock<IDepartmentsService> _departmentsService;
		private DepartmentDataProtectionPolicy _policy;
		private DepartmentDataProtectionService _service;

		[SetUp]
		public void SetUp()
		{
			_policy = new DepartmentDataProtectionPolicy
			{
				DepartmentDataProtectionPolicyId = 1,
				DepartmentId = DeptId,
				State = (int)DepartmentDataProtectionState.Enabled,
				PolicyEpoch = 4
			};

			_policyRepo = new Mock<IDepartmentDataProtectionPolicyRepository>();
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId)).ReturnsAsync(() => _policy);
			_policyRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<DepartmentDataProtectionPolicy>(),
					It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DepartmentDataProtectionPolicy p, CancellationToken _, bool __) => p);
			_policyRepo.Setup(x => x.IncrementPolicyEpochAsync(DeptId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => ++_policy.PolicyEpoch);

			_departmentsService = new Mock<IDepartmentsService>();
			_departmentsService.Setup(x => x.GetDepartmentByIdAsync(DeptId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DeptId, ManagingUserId = ManagingUserId });

			var cacheProvider = new Mock<ICacheProvider>();
			cacheProvider.Setup(x => x.RetrieveAsync(It.IsAny<string>(),
					It.IsAny<Func<Task<DepartmentDataProtectionPolicy>>>(), It.IsAny<TimeSpan>()))
				.Returns<string, Func<Task<DepartmentDataProtectionPolicy>>, TimeSpan>((_, fallback, __) => fallback());
			cacheProvider.Setup(x => x.RemoveAsync(It.IsAny<string>())).ReturnsAsync(true);

			_service = new DepartmentDataProtectionService(_policyRepo.Object,
				new Mock<IDepartmentProtectedDataEgressPolicyRepository>().Object,
				_departmentsService.Object,
				new Mock<IFeatureToggleService>().Object,
				new Mock<ISubscriptionsService>().Object,
				cacheProvider.Object,
				new ProtectedFieldCatalog(),
				new Mock<IDepartmentDataProtectionMigrationRepository>().Object,
				new Mock<IDepartmentLockService>().Object,
				new Mock<IDepartmentKeyService>().Object);
		}

		[TestCase(UserSessionClientApplication.Web)]
		[TestCase(UserSessionClientApplication.Dispatch)]
		[TestCase(UserSessionClientApplication.Responder)]
		[TestCase(UserSessionClientApplication.Unit)]
		[TestCase(UserSessionClientApplication.Command)]
		[TestCase(UserSessionClientApplication.Api)]
		public async Task Every_app_prompts_until_a_department_says_otherwise(UserSessionClientApplication client)
		{
			(await _service.IsStepUpRequiredForClientAsync(DeptId, client)).Should().BeTrue(
				"a department that has never opened this setting keeps the stronger behaviour");
		}

		[Test]
		public async Task An_exemption_applies_only_to_the_app_it_was_granted_for()
		{
			_policy.StepUpExemptClients = (int)AdpStepUpExemptClients.Dispatch;

			(await _service.IsStepUpRequiredForClientAsync(DeptId, UserSessionClientApplication.Dispatch))
				.Should().BeFalse("the dispatch console is what this exists for");

			(await _service.IsStepUpRequiredForClientAsync(DeptId, UserSessionClientApplication.Web))
				.Should().BeTrue("someone at a desk on the web site is not under the same time pressure");

			(await _service.IsStepUpRequiredForClientAsync(DeptId, UserSessionClientApplication.Responder))
				.Should().BeTrue();
		}

		[TestCase(UserSessionClientApplication.BigBoard)]
		[TestCase(UserSessionClientApplication.Mcp)]
		[TestCase(UserSessionClientApplication.UnknownLegacy)]
		public async Task Some_clients_can_never_be_exempted(UserSessionClientApplication client)
		{
			// Every bit set, including ones that map to nothing.
			_policy.StepUpExemptClients = int.MaxValue;

			(await _service.IsStepUpRequiredForClientAsync(DeptId, client)).Should().BeTrue(
				"BigBoard has nobody to prompt and no business seeing protected values, MCP is automated, " +
				"and a client that cannot identify itself must not inherit somebody else's exemption");
		}

		[TestCase(UserSessionClientApplication.BigBoard)]
		[TestCase(UserSessionClientApplication.Mcp)]
		[TestCase(UserSessionClientApplication.UnknownLegacy)]
		public void The_allow_list_refuses_these_clients_on_its_own(UserSessionClientApplication client)
		{
			// Deliberately WITHOUT Sanitize. Two independent things refuse a non-exemptable client -
			// the stored value is sanitized on the way out, and IsExempt has its own allow-list - and
			// the test above only proves the first. Removing either would be a silent hole, so each
			// is pinned separately.
			((AdpStepUpExemptClients)int.MaxValue).IsExempt(client).Should().BeFalse();
		}

		[Test]
		public async Task A_stored_value_carrying_meaningless_bits_cannot_smuggle_an_exemption()
		{
			_policy.StepUpExemptClients = int.MaxValue;

			var exemptions = await _service.GetStepUpExemptClientsAsync(DeptId);

			exemptions.Should().Be(AdpStepUpExemptClients.Web | AdpStepUpExemptClients.Responder |
				AdpStepUpExemptClients.Unit | AdpStepUpExemptClients.Dispatch |
				AdpStepUpExemptClients.Command | AdpStepUpExemptClients.Api);
		}

		[Test]
		public async Task A_lookup_that_fails_prompts_rather_than_guessing()
		{
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId)).ThrowsAsync(new InvalidOperationException("db"));

			(await _service.IsStepUpRequiredForClientAsync(DeptId, UserSessionClientApplication.Dispatch))
				.Should().BeTrue("the safe answer to 'I am not sure' is the prompt");
		}

		[Test]
		public async Task A_department_with_no_policy_prompts()
		{
			_policy = null;

			(await _service.IsStepUpRequiredForClientAsync(DeptId, UserSessionClientApplication.Dispatch))
				.Should().BeTrue();
		}

		[Test]
		public async Task Only_the_managing_member_can_change_it()
		{
			var result = await _service.SetStepUpExemptClientsAsync(DeptId, AdpStepUpExemptClients.Dispatch,
				OtherAdminUserId);

			result.Should().NotBe(DepartmentDataProtectionEnrollmentResult.Queued);
			_policy.StepUpExemptClients.Should().Be(0,
				"weakening a protection control is not something any administrator can do quietly");
		}

		[Test]
		public async Task Turning_the_prompt_off_bumps_the_policy_epoch()
		{
			var before = _policy.PolicyEpoch;

			await _service.SetStepUpExemptClientsAsync(DeptId, AdpStepUpExemptClients.Dispatch, ManagingUserId);

			_policy.StepUpExemptClients.Should().Be((int)AdpStepUpExemptClients.Dispatch);
			_policy.PolicyEpoch.Should().BeGreaterThan(before);
		}

		[Test]
		public async Task Turning_the_prompt_back_ON_also_bumps_the_epoch()
		{
			// The one that actually matters. Without the bump, grants minted while the app was exempt
			// keep working until they expire - so re-enabling the prompt would not bite for the rest
			// of the window, which is precisely when someone re-enabling it is worried.
			_policy.StepUpExemptClients = (int)AdpStepUpExemptClients.Dispatch;
			var before = _policy.PolicyEpoch;

			await _service.SetStepUpExemptClientsAsync(DeptId, AdpStepUpExemptClients.None, ManagingUserId);

			_policy.StepUpExemptClients.Should().Be(0);
			_policy.PolicyEpoch.Should().BeGreaterThan(before);
		}

		[Test]
		public async Task Saving_the_same_value_changes_nothing()
		{
			_policy.StepUpExemptClients = (int)AdpStepUpExemptClients.Dispatch;
			var before = _policy.PolicyEpoch;

			var result = await _service.SetStepUpExemptClientsAsync(DeptId, AdpStepUpExemptClients.Dispatch, ManagingUserId);

			result.Should().Be(DepartmentDataProtectionEnrollmentResult.Queued);
			_policy.PolicyEpoch.Should().Be(before,
				"a no-op save must not revoke every outstanding grant in the department");
		}

		[Test]
		public async Task Unmappable_bits_are_stripped_before_they_are_stored()
		{
			await _service.SetStepUpExemptClientsAsync(DeptId, (AdpStepUpExemptClients)int.MaxValue, ManagingUserId);

			((AdpStepUpExemptClients)_policy.StepUpExemptClients).Should().Be(
				AdpStepUpExemptClients.Web | AdpStepUpExemptClients.Responder | AdpStepUpExemptClients.Unit |
				AdpStepUpExemptClients.Dispatch | AdpStepUpExemptClients.Command | AdpStepUpExemptClients.Api,
				"a stored value must never carry meaning nothing reads");
		}
	}
}
