using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Fido2NetLib;
using Fido2NetLib.Objects;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Providers.Authentication;
using Resgrid.Services;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey plan Phase 1, slice 7: the Fido2 4.1.0 adapter against a software authenticator. Each client is checked
	/// against its own RP ID and exact origins, user verification is required, and an assertion verifies only with the key
	/// and user handle it is given (the ownership lookup is the service's job).
	/// </summary>
	[TestFixture]
	public class PasskeyProviderTests
	{
		internal const string Layout = "web=web.resgrid.test|https://web.resgrid.test;unit=unit.resgrid.test|https://unit.resgrid.test;" +
			"responder=responder.resgrid.test|https://responder.resgrid.test";

		internal static string Origin(UserSessionClientApplication client) => client switch
		{
			UserSessionClientApplication.Web => "https://web.resgrid.test",
			UserSessionClientApplication.Unit => "https://unit.resgrid.test",
			UserSessionClientApplication.Responder => "https://responder.resgrid.test",
			_ => "https://other.resgrid.test"
		};

		private static Fido2PasskeyProvider Provider(string layout = Layout) => new(new RelyingPartyRegistry(layout));

		private static readonly byte[] Handle = RandomNumberGenerator.GetBytes(32);

		private static async Task<(PasskeyRegistrationVerification Registered, SoftPasskeyAuthenticator Authenticator)> Register(
			Fido2PasskeyProvider provider, UserSessionClientApplication client = UserSessionClientApplication.Unit, SoftPasskeyAuthenticator authenticator = null)
		{
			authenticator ??= new SoftPasskeyAuthenticator();
			var options = provider.CreateRegistrationOptions(client, Handle, "user1", "User One", Array.Empty<byte[]>(), preferRoaming: false);
			var registered = await provider.VerifyRegistrationAsync(client, options, authenticator.Register(options, Origin(client)));
			registered.Succeeded.Should().BeTrue();
			return (registered, authenticator);
		}

		private static PasskeyAssertionCredential Stored(PasskeyRegistrationVerification registered, long signCount = 0, byte[] userHandle = null) => new()
		{
			CredentialId = registered.CredentialId, PublicKey = registered.PublicKey, UserHandle = userHandle ?? registered.UserHandle, SignCount = signCount
		};

		[Test]
		public void Registration_options_require_user_verification_a_discoverable_credential_and_no_attestation()
		{
			var existing = RandomNumberGenerator.GetBytes(32);
			var options = CredentialCreateOptions.FromJson(Provider().CreateRegistrationOptions(UserSessionClientApplication.Unit, Handle, "user1", "User One",
				new[] { existing }, preferRoaming: false));

			options.Rp.Id.Should().Be("unit.resgrid.test");
			options.User.Id.Should().Equal(Handle);
			options.AuthenticatorSelection.UserVerification.Should().Be(UserVerificationRequirement.Required);
			options.AuthenticatorSelection.ResidentKey.Should().Be(ResidentKeyRequirement.Required);
			options.AuthenticatorSelection.AuthenticatorAttachment.Should().BeNull();
			options.Attestation.Should().Be(AttestationConveyancePreference.None);
			options.ExcludeCredentials.Select(c => c.Id).Should().ContainSingle().Which.Should().Equal(existing);

			var shared = CredentialCreateOptions.FromJson(Provider().CreateRegistrationOptions(UserSessionClientApplication.Unit, Handle, "user1", "User One",
				Array.Empty<byte[]>(), preferRoaming: true));
			shared.AuthenticatorSelection.AuthenticatorAttachment.Should().Be(AuthenticatorAttachment.CrossPlatform,
				"a shared installation asks for a security key or phone");
			shared.Hints.Should().Contain(PublicKeyCredentialHint.SecurityKey);
		}

		[Test]
		public async Task A_registration_verifies_and_reports_only_the_public_credential()
		{
			var (registered, authenticator) = await Register(Provider());

			registered.CredentialId.Should().Equal(authenticator.CredentialId);
			registered.UserHandle.Should().Equal(Handle);
			registered.Algorithm.Should().Be(-7);
			registered.PublicKey.Should().NotBeEmpty();
			registered.IsBackupEligible.Should().BeTrue();
			registered.IsBackedUp.Should().BeTrue();
			registered.Transports.Should().Contain(new[] { "internal", "hybrid" });
			registered.Attachment.Should().Be("platform");
			registered.AttestationFormat.Should().Be("none");
		}

		[TestCase("https://responder.resgrid.test", null, TestName = "Another app's origin")]
		[TestCase("https://unit.resgrid.test.evil.test", null, TestName = "A look-alike origin")]
		[TestCase("https://unit.resgrid.test", "responder.resgrid.test", TestName = "Another app's RP ID")]
		[TestCase("https://unit.resgrid.test", "resgrid.test", TestName = "The parent domain as RP ID")]
		public async Task A_registration_for_another_origin_or_rp_is_refused(string origin, string rpId)
		{
			var provider = Provider();
			var options = provider.CreateRegistrationOptions(UserSessionClientApplication.Unit, Handle, "user1", "User One", Array.Empty<byte[]>(), false);

			var registered = await provider.VerifyRegistrationAsync(UserSessionClientApplication.Unit, options,
				new SoftPasskeyAuthenticator().Register(options, origin, rpIdOverride: rpId));

			registered.Succeeded.Should().BeFalse();
			registered.FailureCode.Should().Be("passkey_verification_failed");
		}

		[Test]
		public async Task A_registration_without_user_verification_is_refused()
		{
			var provider = Provider();
			var options = provider.CreateRegistrationOptions(UserSessionClientApplication.Unit, Handle, "user1", "User One", Array.Empty<byte[]>(), false);

			(await provider.VerifyRegistrationAsync(UserSessionClientApplication.Unit, options,
				new SoftPasskeyAuthenticator().Register(options, Origin(UserSessionClientApplication.Unit), userVerified: false))).Succeeded.Should().BeFalse();
		}

		[Test]
		public async Task A_registration_is_checked_against_the_exact_options_issued()
		{
			var provider = Provider();
			var issued = provider.CreateRegistrationOptions(UserSessionClientApplication.Unit, Handle, "user1", "User One", Array.Empty<byte[]>(), false);
			var other = provider.CreateRegistrationOptions(UserSessionClientApplication.Unit, Handle, "user1", "User One", Array.Empty<byte[]>(), false);

			(await provider.VerifyRegistrationAsync(UserSessionClientApplication.Unit, issued,
				new SoftPasskeyAuthenticator().Register(other, Origin(UserSessionClientApplication.Unit)))).Succeeded.Should().BeFalse("the challenge differs");
		}

		[Test]
		public async Task An_assertion_verifies_only_with_the_bound_key_and_user_handle()
		{
			var provider = Provider();
			var (registered, authenticator) = await Register(provider);
			var (otherRegistered, _) = await Register(provider);

			var options = provider.CreateAssertionOptions(UserSessionClientApplication.Unit, new[] { registered.CredentialId });
			AssertionOptions.FromJson(options).AllowCredentials.Select(c => c.Id).Should().ContainSingle().Which.Should().Equal(registered.CredentialId);
			AssertionOptions.FromJson(options).UserVerification.Should().Be(UserVerificationRequirement.Required);

			var assertion = authenticator.Assert(options, Origin(UserSessionClientApplication.Unit), Handle);
			provider.ReadCredentialId(assertion).Should().Equal(registered.CredentialId);

			var verified = await provider.VerifyAssertionAsync(UserSessionClientApplication.Unit, options, assertion, Stored(registered));
			verified.Succeeded.Should().BeTrue();
			verified.SignCount.Should().Be(1);

			(await provider.VerifyAssertionAsync(UserSessionClientApplication.Unit, options, authenticator.Assert(options, Origin(UserSessionClientApplication.Unit), Handle),
				new PasskeyAssertionCredential { CredentialId = registered.CredentialId, PublicKey = otherRegistered.PublicKey, UserHandle = Handle, SignCount = 1 }))
				.Succeeded.Should().BeFalse("another credential's key must not verify it");

			(await provider.VerifyAssertionAsync(UserSessionClientApplication.Unit, options, authenticator.Assert(options, Origin(UserSessionClientApplication.Unit), Handle),
				Stored(registered, 2, userHandle: RandomNumberGenerator.GetBytes(32)))).Succeeded.Should().BeFalse("the user handle must be the credential's");

			(await provider.VerifyAssertionAsync(UserSessionClientApplication.Unit, options, authenticator.Assert(options, Origin(UserSessionClientApplication.Unit), Handle),
				Stored(otherRegistered, 2))).Succeeded.Should().BeFalse("the response must name the credential it is checked against");
		}

		[Test]
		public async Task An_assertion_for_another_apps_rp_or_origin_is_refused()
		{
			var provider = Provider();
			var (registered, authenticator) = await Register(provider);
			var options = provider.CreateAssertionOptions(UserSessionClientApplication.Unit, new[] { registered.CredentialId });

			(await provider.VerifyAssertionAsync(UserSessionClientApplication.Unit, options,
				authenticator.Assert(options, Origin(UserSessionClientApplication.Responder), Handle), Stored(registered))).Succeeded.Should().BeFalse();
			(await provider.VerifyAssertionAsync(UserSessionClientApplication.Unit, options,
				authenticator.Assert(options, Origin(UserSessionClientApplication.Unit), Handle, rpIdOverride: "responder.resgrid.test"), Stored(registered)))
				.Succeeded.Should().BeFalse();
			(await provider.VerifyAssertionAsync(UserSessionClientApplication.Responder, options,
				authenticator.Assert(options, Origin(UserSessionClientApplication.Unit), Handle), Stored(registered)))
				.Succeeded.Should().BeFalse("the Responder relying party never accepts a Unit ceremony");
		}

		[Test]
		public async Task An_assertion_without_user_verification_or_with_a_regressed_counter_is_refused()
		{
			var provider = Provider();
			var (registered, authenticator) = await Register(provider);
			var options = provider.CreateAssertionOptions(UserSessionClientApplication.Unit, new[] { registered.CredentialId });

			(await provider.VerifyAssertionAsync(UserSessionClientApplication.Unit, options,
				authenticator.Assert(options, Origin(UserSessionClientApplication.Unit), Handle, userVerified: false), Stored(registered))).Succeeded.Should().BeFalse();
			(await provider.VerifyAssertionAsync(UserSessionClientApplication.Unit, options,
				authenticator.Assert(options, Origin(UserSessionClientApplication.Unit), Handle, counterOverride: 5), Stored(registered, signCount: 10)))
				.Succeeded.Should().BeFalse("a counter that went backwards suggests a cloned authenticator");
		}

		[Test]
		public async Task A_synced_passkey_that_never_counts_still_verifies()
		{
			var provider = Provider();
			var (registered, authenticator) = await Register(provider, authenticator: new SoftPasskeyAuthenticator(countSignatures: false));
			var options = provider.CreateAssertionOptions(UserSessionClientApplication.Unit, new[] { registered.CredentialId });

			var verified = await provider.VerifyAssertionAsync(UserSessionClientApplication.Unit, options,
				authenticator.Assert(options, Origin(UserSessionClientApplication.Unit), Handle), Stored(registered));
			verified.Succeeded.Should().BeTrue();
			verified.SignCount.Should().Be(0);
		}

		[Test]
		public async Task Unreadable_responses_and_clients_without_a_relying_party_fail_closed()
		{
			var provider = Provider();
			provider.ReadCredentialId("not json").Should().BeNull();
			provider.ReadCredentialId("{}").Should().BeNull();
			(await provider.VerifyRegistrationAsync(UserSessionClientApplication.Unit, "{}", "{\"id\":1}")).Succeeded.Should().BeFalse();

			provider.IsAvailableFor(UserSessionClientApplication.Dispatch).Should().BeFalse("Dispatch has no relying party in this layout");
			provider.Invoking(p => p.CreateAssertionOptions(UserSessionClientApplication.Dispatch, Array.Empty<byte[]>()))
				.Should().Throw<InvalidOperationException>();
			(await provider.VerifyRegistrationAsync(UserSessionClientApplication.Dispatch, "{}", "{}")).Succeeded.Should().BeFalse();

			Provider("web=resgrid.test|https://resgrid.test;unit=unit.resgrid.test|https://unit.resgrid.test")
				.IsAvailableFor(UserSessionClientApplication.Unit).Should().BeFalse("a layout that is not ready offers no relying party at all");
		}

		[Test]
		public void The_cose_algorithm_is_read_from_the_public_key()
		{
			Fido2PasskeyProvider.ReadCoseAlgorithm(null).Should().BeNull();
			Fido2PasskeyProvider.ReadCoseAlgorithm(new byte[] { 0xff }).Should().BeNull();

			var writer = new System.Formats.Cbor.CborWriter();
			writer.WriteStartMap(2);
			writer.WriteInt32(1); writer.WriteInt32(3);
			writer.WriteInt32(3); writer.WriteInt32(-257);
			writer.WriteEndMap();
			Fido2PasskeyProvider.ReadCoseAlgorithm(writer.Encode()).Should().Be(-257);
		}
	}
}
