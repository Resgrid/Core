using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Security;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey plan Phase 1, slice 10 (section 7.8): the provider step-up mapping is a validated structure, matching is
	/// exact, and a mapping counts only at the version that passed its test.
	/// </summary>
	[TestFixture]
	public class FederatedMfaMappingTests
	{
		private static FederatedMfaMapping Oidc() => new()
		{
			RequestAcrValues = new List<string> { "urn:okta:loa:2fa:any" },
			RequestClaims = "{\"id_token\":{\"acrs\":{\"essential\":true,\"value\":\"c1\"}}}",
			AcceptAmr = new List<string> { "mfa", "hwk" },
			AcceptAcrs = new List<string> { "c1" }
		};

		private static FederatedMfaMapping Saml() => new()
		{
			RequestAuthnContextClassRefs = new List<string> { "https://refeds.org/profile/mfa" },
			AcceptAuthnContextClassRefs = new List<string> { "https://refeds.org/profile/mfa" }
		};

		private static DepartmentSsoConfig Tested(long version = 3) => new()
		{
			DepartmentSsoConfigId = "cfg", DepartmentId = 42, IsEnabled = true, FederatedMfaMappingJson = Oidc().Serialize(),
			FederatedMfaMappingVersion = version, FederatedMfaTestedVersion = version
		};

		[Test]
		public void Valid_mappings_for_each_protocol_pass_and_round_trip()
		{
			FederatedMfaMapping.Validate(Oidc(), SsoProviderType.Oidc).Should().BeNull();
			FederatedMfaMapping.Validate(Saml(), SsoProviderType.Saml2).Should().BeNull();

			var parsed = FederatedMfaMapping.Parse(Oidc().Serialize());
			parsed.Should().BeEquivalentTo(Oidc());
			Oidc().Serialize().Should().NotContain("requestAuthnContextClassRefs", "empty parts are not stored");
		}

		private static IEnumerable<TestCaseData> Invalid()
		{
			FederatedMfaMapping With(System.Action<FederatedMfaMapping> change)
			{
				var mapping = Oidc();
				change(mapping);
				return mapping;
			}

			yield return new TestCaseData(null, SsoProviderType.Oidc).SetName("Nothing");
			yield return new TestCaseData(With(m => m.AcceptAmr = new List<string> { "pwd" }), SsoProviderType.Oidc).SetName("A password counted as MFA");
			yield return new TestCaseData(With(m => m.AcceptAmr = new List<string> { "PWD" }), SsoProviderType.Oidc).SetName("A password in any case");
			yield return new TestCaseData(With(m => m.AcceptAmr = new List<string> { "mfa", "mfa" }), SsoProviderType.Oidc).SetName("A duplicate");
			yield return new TestCaseData(With(m => m.AcceptAmr = new List<string> { "multi factor" }), SsoProviderType.Oidc).SetName("A space");
			yield return new TestCaseData(With(m => m.AcceptAmr = new List<string> { "" }), SsoProviderType.Oidc).SetName("An empty value");
			yield return new TestCaseData(With(m => m.AcceptAmr = new List<string> { new string('a', 257) }), SsoProviderType.Oidc).SetName("A value too long");
			yield return new TestCaseData(With(m => m.AcceptAmr = Enumerable.Range(0, 11).Select(i => "v" + i).ToList()), SsoProviderType.Oidc)
				.SetName("Too many values");
			yield return new TestCaseData(With(m => { m.AcceptAmr = null; m.AcceptAcrs = null; }), SsoProviderType.Oidc).SetName("Nothing counts as MFA");
			yield return new TestCaseData(With(m => m.RequestClaims = "[1]"), SsoProviderType.Oidc).SetName("A claims request that is not an object");
			yield return new TestCaseData(With(m => m.RequestClaims = "{\"access_token\":{}}"), SsoProviderType.Oidc).SetName("A claims request for another token");
			yield return new TestCaseData(With(m => m.RequestClaims = "{not json"), SsoProviderType.Oidc).SetName("A claims request that is not JSON");
			yield return new TestCaseData(With(m => m.RequestClaims = "{\"id_token\":\"" + new string('x', 2048) + "\"}"), SsoProviderType.Oidc)
				.SetName("A claims request too long");
			yield return new TestCaseData(With(m => m.AcceptAuthnContextClassRefs = new List<string> { "x" }), SsoProviderType.Oidc)
				.SetName("SAML values on an OIDC provider");
			yield return new TestCaseData(Oidc(), SsoProviderType.Saml2).SetName("OIDC values on a SAML provider");
			yield return new TestCaseData(new FederatedMfaMapping { RequestAuthnContextClassRefs = new List<string> { "x" } }, SsoProviderType.Saml2)
				.SetName("A SAML mapping that counts nothing");
		}

		[TestCaseSource(nameof(Invalid))]
		public void Invalid_mappings_are_refused_with_a_reason(FederatedMfaMapping mapping, SsoProviderType provider) =>
			FederatedMfaMapping.Validate(mapping, provider).Should().NotBeNullOrWhiteSpace();

		[Test]
		public void Unreadable_json_is_no_mapping()
		{
			FederatedMfaMapping.Parse(null).Should().BeNull();
			FederatedMfaMapping.Parse("  ").Should().BeNull();
			FederatedMfaMapping.Parse("{broken").Should().BeNull();
		}

		[Test]
		public void Matching_is_exact_and_names_what_matched()
		{
			var mapping = Oidc();
			mapping.AcceptAcr = new List<string> { "urn:okta:loa:2fa:any" };

			mapping.Match(new FederatedMfaSignals { Amr = new[] { "pwd", "hwk" } }).Should().Be("amr:hwk");
			mapping.Match(new FederatedMfaSignals { Amr = new[] { "pwd" }, Acr = new[] { "urn:okta:loa:2fa:any" } }).Should().Be("acr:urn:okta:loa:2fa:any");
			mapping.Match(new FederatedMfaSignals { Acrs = new[] { "c1" } }).Should().Be("acrs:c1");
			mapping.Match(new FederatedMfaSignals { Amr = new[] { "MFA" } }).Should().BeNull("provider identifiers are case-sensitive");
			mapping.Match(new FederatedMfaSignals { Amr = new[] { "pwd" }, Acr = new[] { "urn:okta:loa:1fa:any" } }).Should().BeNull();
			mapping.Match(new FederatedMfaSignals()).Should().BeNull();
			mapping.Match(null).Should().BeNull();

			Saml().Match(new FederatedMfaSignals { AuthnContextClassRefs = new[] { "https://refeds.org/profile/mfa" } })
				.Should().Be("authncontext:https://refeds.org/profile/mfa");
			Saml().Match(new FederatedMfaSignals { Amr = new[] { "https://refeds.org/profile/mfa" } }).Should().BeNull("each kind matches its own list");
		}

		[Test]
		public void A_mapping_counts_only_at_the_version_that_passed_its_test()
		{
			FederatedMfaMapping.IsTested(Tested()).Should().BeTrue();
			FederatedMfaMapping.IsTested(null).Should().BeFalse();

			var changed = Tested();
			changed.FederatedMfaMappingVersion = 4;
			FederatedMfaMapping.IsTested(changed).Should().BeFalse("a changed mapping needs a new test");

			var untested = Tested();
			untested.FederatedMfaTestedVersion = null;
			FederatedMfaMapping.IsTested(untested).Should().BeFalse();

			var disabled = Tested();
			disabled.IsEnabled = false;
			FederatedMfaMapping.IsTested(disabled).Should().BeFalse();

			var removed = Tested();
			removed.FederatedMfaMappingJson = null;
			FederatedMfaMapping.IsTested(removed).Should().BeFalse();

			var never = Tested(0);
			FederatedMfaMapping.IsTested(never).Should().BeFalse("version 0 is the unsaved state");

			FederatedMfaMapping.FactorReferenceFor("cfg", 3).Should().Be("federated:cfg:3");
		}

		[Test]
		public void A_round_trip_satisfies_mfa_only_under_the_tested_mapping_as_it_is_now()
		{
			SsoLoginTransaction Round(System.Action<SsoLoginTransaction> change = null)
			{
				var transaction = new SsoLoginTransaction
				{
					DepartmentId = 42, DepartmentSsoConfigId = "cfg", FederatedMappingVersion = 3, FederatedMfaValue = "amr:mfa"
				};
				change?.Invoke(transaction);
				return transaction;
			}

			FederatedMfaMapping.Satisfies(Round(), Tested()).Should().BeTrue();
			FederatedMfaMapping.Satisfies(Round(t => t.FederatedMfaValue = null), Tested()).Should().BeFalse("nothing was matched");
			FederatedMfaMapping.Satisfies(Round(t => t.FederatedMappingVersion = 2), Tested()).Should().BeFalse("the mapping changed since");
			FederatedMfaMapping.Satisfies(Round(t => t.FederatedMappingVersion = null), Tested()).Should().BeFalse("no mapping was requested");
			FederatedMfaMapping.Satisfies(Round(t => t.DepartmentSsoConfigId = "other"), Tested()).Should().BeFalse();
			FederatedMfaMapping.Satisfies(Round(t => t.DepartmentId = 7), Tested()).Should().BeFalse();
			FederatedMfaMapping.Satisfies(Round(), null).Should().BeFalse();
			FederatedMfaMapping.Satisfies(null, Tested()).Should().BeFalse();

			var retested = Tested();
			retested.FederatedMfaTestedVersion = 2;
			FederatedMfaMapping.Satisfies(Round(), retested).Should().BeFalse("the current version has not passed its test");
		}

		[Test]
		public void Turning_provider_step_up_on_is_recognised_for_either_scope()
		{
			var off = new DepartmentSecurityPolicy();
			DepartmentSecurityPolicyDecisions.EnablesFederatedMfa(off, new DepartmentSecurityPolicy { AllowFederatedMfaForLoginMfa = true }).Should().BeTrue();
			DepartmentSecurityPolicyDecisions.EnablesFederatedMfa(off, new DepartmentSecurityPolicy { AllowFederatedMfaForAdp = true }).Should().BeTrue();
			DepartmentSecurityPolicyDecisions.EnablesFederatedMfa(new DepartmentSecurityPolicy { AllowFederatedMfaForLoginMfa = true },
				new DepartmentSecurityPolicy { AllowFederatedMfaForLoginMfa = true }).Should().BeFalse("keeping it on is not turning it on");
			DepartmentSecurityPolicyDecisions.EnablesFederatedMfa(new DepartmentSecurityPolicy { AllowFederatedMfaForAdp = true }, off)
				.Should().BeFalse("turning it off needs no tested mapping");
		}
	}
}
