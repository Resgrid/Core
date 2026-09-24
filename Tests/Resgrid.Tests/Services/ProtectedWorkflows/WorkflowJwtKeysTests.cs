using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services.ProtectedWorkflows
{
	/// <summary>
	/// private_key_jwt credentials: generated keys, the published JWKS and its overlap window, rotation (a new kid, a
	/// credential_rotated event, no re-approval), and the private key never leaving the encrypted credential. Also the
	/// write side of subject identifiers: a protected department's API write envelopes them through the workload lane.
	/// </summary>
	[TestFixture]
	public class WorkflowJwtKeysTests
	{
		[Test]
		public void the_jwks_publishes_rotated_keys_only_for_the_overlap_window()
		{
			var now = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
			var (_, old) = WorkflowJwtKeys.Generate(WorkflowJwtKeys.Rs384, now.AddDays(-30));
			var (_, current) = WorkflowJwtKeys.Generate(WorkflowJwtKeys.Es384, now);
			old.RetiredOn = now;
			var column = WorkflowJwtKeys.WritePublicKeys(new[] { old, current });

			KeyIds(WorkflowJwtKeys.BuildJwks(column, now.AddDays(6), 7)).Should().BeEquivalentTo(new[] { old.Kid, current.Kid }, "both during the overlap");
			KeyIds(WorkflowJwtKeys.BuildJwks(column, now.AddDays(7).AddMinutes(1), 7)).Should().Equal(current.Kid);

			var jwks = JObject.Parse(WorkflowJwtKeys.BuildJwks(column, now, 7));
			foreach (var key in jwks["keys"])
			{
				key.Value<string>("use").Should().Be("sig");
				((JObject)key).Properties().Select(p => p.Name).Should().NotContain(new[] { "d", "p", "q", "dp", "dq", "qi", "privateKey" });
			}
		}

		private static string[] KeyIds(string jwks) => JObject.Parse(jwks)["keys"].Select(k => k.Value<string>("kid")).ToArray();

		[Test]
		public void the_kid_is_the_rfc7638_thumbprint_and_the_public_half_is_reproducible()
		{
			var (signing, published) = WorkflowJwtKeys.Generate(WorkflowJwtKeys.Rs384, DateTime.UtcNow);

			WorkflowJwtKeys.PublicFor(signing).Jwk.ToString(Formatting.None).Should().Be(published.Jwk.ToString(Formatting.None));
			signing.Kid.Should().Be(published.Kid).And.MatchRegex("^[A-Za-z0-9_-]{43}$");
		}

		[Test]
		public void an_assertion_never_lives_longer_than_five_minutes()
		{
			var (signing, _) = WorkflowJwtKeys.Generate(WorkflowJwtKeys.Es384, DateTime.UtcNow);

			var assertion = WorkflowJwtKeys.CreateAssertion(signing, "client", "https://ehr.example.org/token", DateTime.UtcNow, TimeSpan.FromHours(1));
			var claims = JObject.Parse(System.Text.Encoding.UTF8.GetString(WorkflowJwtKeys.FromBase64Url(assertion.Split('.')[1])));

			(claims.Value<long>("exp") - claims.Value<long>("iat")).Should().Be(300);
			WorkflowJwtKeys.CreateAssertion(signing, "client", "https://x", DateTime.UtcNow).Split('.')[1]
				.Should().NotBe(assertion.Split('.')[1], "every assertion carries a fresh jti");
		}

		// ── Through WorkflowService ─────────────────────────────────────────────────────────────────

		private static WorkflowCredential JwtCredential(string alg = WorkflowJwtKeys.Rs384) => new WorkflowCredential
		{
			WorkflowCredentialId = ProtectedWorkflowHarness.CredentialId,
			DepartmentId = ProtectedWorkflowHarness.DepartmentId,
			Name = "EHR SMART backend",
			CredentialType = (int)WorkflowCredentialType.OAuth2ClientCredentials,
			EncryptedData = JsonConvert.SerializeObject(new
			{
				tokenUrl = "https://org.crm.dynamics.com/oauth2/token",
				clientId = "resgrid-client",
				clientSecret = "should-be-dropped",
				authMethod = WorkflowJwtKeys.PrivateKeyJwt,
				signingAlg = alg
			}),
			UpdatedByUserId = ProtectedWorkflowHarness.AdminA
		};

		[Test]
		public async Task saving_a_private_key_jwt_credential_generates_a_key_and_publishes_only_its_public_half()
		{
			var h = new ProtectedWorkflowHarness();

			await h.WorkflowService.SaveCredentialAsync(JwtCredential(), ProtectedWorkflowHarness.DepartmentCode);

			var stored = h.Credentials.Single(c => c.WorkflowCredentialId == ProtectedWorkflowHarness.CredentialId);
			var secret = JObject.Parse(stored.EncryptedData.Substring(4));
			var keys = secret["signingKeys"].ToObject<WorkflowSigningKey[]>();
			keys.Should().ContainSingle().Which.PrivateKey.Should().NotBeNullOrWhiteSpace();
			secret.Value<string>("clientSecret").Should().BeNull("a private_key_jwt credential keeps no secret");

			var published = WorkflowJwtKeys.ReadPublicKeys(stored.PublicJwks);
			published.Should().ContainSingle().Which.Kid.Should().Be(keys[0].Kid);
			stored.PublicJwks.Should().NotContain(keys[0].PrivateKey);
			stored.PublicJwks.Should().NotContain("\"d\"");
		}

		[Test]
		public async Task editing_a_private_key_jwt_credential_keeps_its_key()
		{
			var h = new ProtectedWorkflowHarness();
			await h.WorkflowService.SaveCredentialAsync(JwtCredential(), ProtectedWorkflowHarness.DepartmentCode);
			var kid = WorkflowJwtKeys.ReadPublicKeys(h.Credentials.Single().PublicJwks).Single().Kid;

			var edit = JwtCredential();
			edit.Name = "Renamed";
			await h.WorkflowService.SaveCredentialAsync(edit, ProtectedWorkflowHarness.DepartmentCode);

			WorkflowJwtKeys.ReadPublicKeys(h.Credentials.Single().PublicJwks).Single().Kid.Should().Be(kid, "the editor never posts keys, and none are lost");
			h.AdminEvents.Should().BeEmpty("nothing about the authentication changed");
		}

		[Test]
		public async Task rotation_adds_a_new_key_keeps_the_old_one_published_records_the_event_and_keeps_the_release_active()
		{
			var h = new ProtectedWorkflowHarness();
			await h.WorkflowService.SaveCredentialAsync(JwtCredential(), ProtectedWorkflowHarness.DepartmentCode);
			var release = h.ActivateRelease();
			h.StoredRelease().TokenHost = "org.crm.dynamics.com";
			h.StoredRelease().AuthMethod = WorkflowJwtKeys.PrivateKeyJwt;
			var firstKid = WorkflowJwtKeys.ReadPublicKeys(h.Credentials.Single().PublicJwks).Single().Kid;

			var rotated = await h.WorkflowService.RotateCredentialSigningKeyAsync(ProtectedWorkflowHarness.CredentialId, ProtectedWorkflowHarness.DepartmentId,
				ProtectedWorkflowHarness.DepartmentCode, ProtectedWorkflowHarness.AdminB);

			rotated.Should().NotBeNull();
			var published = WorkflowJwtKeys.ReadPublicKeys(h.Credentials.Single().PublicJwks);
			published.Should().HaveCount(2);
			var newKid = published.Single(k => !k.RetiredOn.HasValue).Kid;
			newKid.Should().NotBe(firstKid);
			published.Single(k => k.Kid == firstKid).RetiredOn.Should().NotBeNull();
			KeyIds(WorkflowJwtKeys.BuildJwks(h.Credentials.Single().PublicJwks, DateTime.UtcNow, DataProtectionConfig.WorkflowJwksOverlapDays))
				.Should().BeEquivalentTo(new[] { firstKid, newKid });

			var signing = JObject.Parse(h.Credentials.Single().EncryptedData.Substring(4))["signingKeys"].ToObject<WorkflowSigningKey[]>();
			WorkflowJwtKeys.Current(signing).Kid.Should().Be(newKid, "the new key signs from now on");

			h.AdminEvents.Should().ContainSingle(e => e.EventType == ProtectedWorkflowAdminEventTypes.CredentialRotated &&
				e.WorkflowProtectedReleaseId == release.WorkflowProtectedReleaseId && e.Detail.Contains("kid=" + newKid) && e.ActorUserId == ProtectedWorkflowHarness.AdminB);
			h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Active, "the credential id is pinned, the key is not");
		}

		[Test]
		public async Task switching_the_client_authentication_suspends_a_pinned_release()
		{
			var h = new ProtectedWorkflowHarness();
			await h.WorkflowService.SaveCredentialAsync(new WorkflowCredential
			{
				WorkflowCredentialId = ProtectedWorkflowHarness.CredentialId,
				DepartmentId = ProtectedWorkflowHarness.DepartmentId,
				Name = "EHR",
				CredentialType = (int)WorkflowCredentialType.OAuth2ClientCredentials,
				EncryptedData = JsonConvert.SerializeObject(new { tokenUrl = "https://org.crm.dynamics.com/oauth2/token", clientId = "c", clientSecret = "s" })
			}, ProtectedWorkflowHarness.DepartmentCode);
			h.ActivateRelease();

			await h.WorkflowService.SaveCredentialAsync(JwtCredential(), ProtectedWorkflowHarness.DepartmentCode);

			h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Suspended);
			h.StoredRelease().SuspendedReason.Should().Be(ProtectedWorkflowSuspendReasons.CredentialChanged);
		}

		[Test]
		public async Task rotation_is_refused_for_anything_but_a_private_key_jwt_credential_of_the_department()
		{
			var h = new ProtectedWorkflowHarness();

			(await h.WorkflowService.RotateCredentialSigningKeyAsync(ProtectedWorkflowHarness.CredentialId, ProtectedWorkflowHarness.DepartmentId,
				ProtectedWorkflowHarness.DepartmentCode, ProtectedWorkflowHarness.AdminA)).Should().BeNull("the harness credential is a bearer token");

			await h.WorkflowService.SaveCredentialAsync(JwtCredential(), ProtectedWorkflowHarness.DepartmentCode);
			(await h.WorkflowService.RotateCredentialSigningKeyAsync(ProtectedWorkflowHarness.CredentialId, 999,
				ProtectedWorkflowHarness.DepartmentCode, ProtectedWorkflowHarness.AdminA)).Should().BeNull("another department");
		}

		[Test]
		public async Task a_release_pins_the_client_authentication_of_its_credential()
		{
			var h = new ProtectedWorkflowHarness();
			await h.WorkflowService.SaveCredentialAsync(JwtCredential(), ProtectedWorkflowHarness.DepartmentCode);
			h.Credentials.Single().CredentialType.Should().Be((int)WorkflowCredentialType.OAuth2ClientCredentials);

			await h.Service.SaveDraftAsync(ProtectedWorkflowHarness.DepartmentId, h.Workflow.WorkflowId, new ProtectedReleaseDraft
			{
				FieldIds = new[] { "calls.completednotes" },
				RecipientType = (int)ProtectedReleaseRecipientType.CoveredEntity,
				RecipientName = "EHR",
				Purpose = "Encounter documentation"
			}, new ProtectedWorkflowActor { UserId = ProtectedWorkflowHarness.AdminA });
			var result = await h.Service.RequestApprovalAsync(ProtectedWorkflowHarness.DepartmentId, h.Workflow.WorkflowId, true,
				ProtectedWorkflowDefaults.WarningTextVersion, h.StepsFingerprint("org.crm.dynamics.com", WorkflowJwtKeys.PrivateKeyJwt),
				ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminA));

			result.Success.Should().BeTrue(result.ErrorCode);
			h.StoredRelease().AuthMethod.Should().Be(WorkflowJwtKeys.PrivateKeyJwt);
			h.StoredRelease().TokenHost.Should().Be("org.crm.dynamics.com");
		}

		// ── Subject identifiers on the write path ───────────────────────────────────────────────────

		[TestCase(29, true)]
		[TestCase(28, false)]
		public async Task an_api_write_envelopes_subject_identifiers_for_a_department_on_catalog_29(int catalogVersion, bool encrypted)
		{
			var broker = new FakeBroker();
			var dataProtection = new Mock<IDepartmentDataProtectionService>();
			dataProtection.Setup(d => d.ShouldEncryptNewWritesAsync(It.IsAny<int>())).ReturnsAsync(true);
			dataProtection.Setup(d => d.GetPolicyByDepartmentIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = 42, State = (int)DepartmentDataProtectionState.Enabled, CatalogVersion = catalogVersion });
			var writes = new ProtectedReadService(dataProtection.Object, Mock.Of<IProtectedDataGrantService>(), broker, new ProtectedFieldCatalog());
			var call = new Call { CallId = 7, DepartmentId = 42, SubjectIdentifiers = "{\"ehr_client_id\":\"123456\"}" };

			var result = await writes.PrepareCallWriteAsync(42, call, null, null, "system_integration", workloadCaller: true);

			result.Success.Should().BeTrue();
			ProtectedDataEnvelope.HasEnvelopePrefix(call.SubjectIdentifiers).Should().Be(encrypted,
				"a department pinned below 29 keeps the column plaintext until the catalog upgrade reaches it");
			if (encrypted)
			{
				broker.Encrypts.Single().Items.Single().FieldId.Should().Be("calls.subjectidentifiers");
				broker.Plaintext[call.SubjectIdentifiers].Should().Be("{\"ehr_client_id\":\"123456\"}");
			}
		}
	}
}
