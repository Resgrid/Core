using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Moq;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Department step-up policies for tests of the services that consult one. <see cref="Strict"/> is today's behaviour (every area
	/// required, a second factor only), which leaves every older signing test meaning what it did.
	/// </summary>
	public static class StepUpTestPolicies
	{
		public const int WindowMinutes = 15;

		public static IStepUpPolicyService Strict() => Of(null).Object;

		/// <summary>A policy whose rules come from <paramref name="rule"/> (null = strict for that area).</summary>
		public static Mock<IStepUpPolicyService> Of(Func<StepUpArea, StepUpRule> rule)
		{
			StepUpRule For(StepUpArea area) => rule?.Invoke(area) ?? StepUpRule.Default(area, WindowMinutes);
			var mock = new Mock<IStepUpPolicyService>();
			mock.Setup(p => p.GetRuleAsync(It.IsAny<int>(), It.IsAny<StepUpArea>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, StepUpArea area, CancellationToken _) => For(area));
			mock.Setup(p => p.GetRulesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, CancellationToken _) => (IReadOnlyList<StepUpRule>)StepUpAreas.All.Select(For).ToList());
			return mock;
		}

		public static StepUpRule Rule(StepUpArea area, bool required = true, StepUpMethodClasses methods = StepUpMethodClasses.Mfa) => new()
		{
			Area = area, Required = required, Methods = methods | StepUpMethodClasses.Mfa, WindowMinutes = WindowMinutes
		};

		/// <summary>A capability service describing the strict rule for a member with an authenticator.</summary>
		public static IStepUpCapabilityService Capabilities(StepUpCapabilities capabilities = null, IStepUpPolicyService policy = null)
		{
			policy ??= Strict();
			var mock = new Mock<IStepUpCapabilityService>();
			mock.Setup(c => c.GetCapabilitiesAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<UserSessionClientApplication>(), It.IsAny<StepUpArea>(),
				It.IsAny<CancellationToken>())).ReturnsAsync(capabilities ?? new StepUpCapabilities { TotpEnrolled = true });
			mock.Setup(c => c.DescribeAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<UserSessionClientApplication>(), It.IsAny<StepUpArea>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync((int department, string _, UserSessionClientApplication _, StepUpArea area, CancellationToken token) =>
					StepUpRequirement.For(policy.GetRuleAsync(department, area, token).GetAwaiter().GetResult(), capabilities ?? new StepUpCapabilities { TotpEnrolled = true }));
			return mock.Object;
		}
	}
}
