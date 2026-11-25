using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Web.Attributes;
using Resgrid.Web.Services.Models.v4.Certifications;
using ApiCertifications = Resgrid.Web.Services.Controllers.v4.CertificationsController;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using MvcClaims = Resgrid.Web.Helpers.ClaimsAuthorizationHelper;
using WebCertifications = Resgrid.Web.Areas.User.Controllers.CertificationsController;

namespace Resgrid.Tests.Web
{
	/// <summary>
	/// Verifying a certification record writes an L2 signature (S0): the web Verify action sits behind a step-up required for
	/// the operation (POST, antiforgery) and signs with the step-up time the filter left in HttpContext.Items; a REAL
	/// SignatureService then records Level 2 / Method StepUp / SubjectKind CertificationVerification bound to the record's
	/// artifact hash. The v4 VerifyCertification refuses with 403 (X-Resgrid-Reason certifications_step_up_required) and verifies
	/// nothing without fresh, accepted step-up evidence for the session, and with it verifies and records the same L2 signature.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class CertificationVerificationSignatureTests
	{
		private const int DepartmentId = 10;
		private const int CertificationId = 55;
		private const int TypeId = 12;
		private const string Officer = "officer";
		private const string Holder = "holder";
		private const string SessionId = "S-1";
		private const long Generation = 3;

		private Mock<ICertificationService> _certifications;
		private Mock<ISignatureRecordsRepository> _signatureRows;
		private List<SignatureRecord> _inserted;
		private SignatureService _signatureService;
		private DateTime _verifiedOn;

		[SetUp]
		public void SetUp()
		{
			_verifiedOn = new DateTime(2026, 11, 24, 15, 0, 0, DateTimeKind.Utc);
			_certifications = new Mock<ICertificationService>();
			_certifications.Setup(x => x.VerifyCertificationAsync(CertificationId, DepartmentId, Officer, It.IsAny<CancellationToken>())).ReturnsAsync(() => Saved());
			_certifications.Setup(x => x.GetCertificationTypeByIdAsync(TypeId)).ReturnsAsync(new DepartmentCertificationType
			{
				DepartmentCertificationTypeId = TypeId, DepartmentId = DepartmentId, Type = "EMT"
			});
			_certifications.Setup(x => x.GetCertificationByIdAsync(CertificationId)).ReturnsAsync(() => Saved());
			_certifications.Setup(x => x.GetAllCertificationTypesByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentCertificationType>
			{
				new DepartmentCertificationType { DepartmentCertificationTypeId = TypeId, DepartmentId = DepartmentId, Type = "EMT" }
			});
			_certifications.Setup(x => x.GetCertificationCreditTotalsAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync(new Dictionary<int, decimal>());

			_inserted = new List<SignatureRecord>();
			_signatureRows = new Mock<ISignatureRecordsRepository>();
			_signatureRows.Setup(x => x.InsertAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((SignatureRecord r, CancellationToken _, bool __) => { r.SignatureRecordId = 900 + _inserted.Count; _inserted.Add(r); })
				.ReturnsAsync((SignatureRecord r, CancellationToken _, bool __) => r);
			_signatureService = new SignatureService(_signatureRows.Object, Mock.Of<IProtocolVersionsRepository>(), Mock.Of<IPersonnelCertificationRepository>(),
				Mock.Of<IUserProfileService>(), Mock.Of<IPersonnelRolesService>(), Mock.Of<IEventAggregator>(), Mock.Of<IProtocolVersionFilesRepository>());
		}

		[TearDown]
		public void TearDown()
		{
			ApiClaims._httpContextAccessor = null;
			MvcClaims._httpContextAccessor = null;
		}

		private PersonnelCertification Saved() => new PersonnelCertification
		{
			PersonnelCertificationId = CertificationId, DepartmentId = DepartmentId, UserId = Holder, DepartmentCertificationTypeId = TypeId, Name = "EMT",
			RecievedOn = new DateTime(2025, 3, 1), ExpiresOn = new DateTime(2027, 3, 1), VerifiedOn = _verifiedOn, VerifiedByUserId = Officer
		};

		private static DefaultHttpContext SignIn()
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, Officer),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
					new Claim(ResgridClaimTypes.Resources.Certifications, ResgridClaimTypes.Actions.View),
					new Claim(ResgridClaimTypes.Resources.Certifications, ResgridClaimTypes.Actions.Update)
				}, "test"))
			};
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			ApiClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			MvcClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			return http;
		}

		private void AssertOneL2Record()
		{
			var record = _inserted.Should().ContainSingle().Subject;
			record.Level.Should().Be((int)SignatureLevels.StepUp);
			record.Method.Should().Be((int)SignatureMethods.StepUp);
			record.SubjectKind.Should().Be((int)SignatureSubjectKinds.CertificationVerification);
			record.SubjectId.Should().Be(CertificationId.ToString(CultureInfo.InvariantCulture));
			record.Intent.Should().Be((int)SignatureIntents.Verify);
			record.SignerUserId.Should().Be(Officer);
			record.DepartmentId.Should().Be(DepartmentId);
			record.ArtifactSha256.Should().Be(_signatureService.CertificationArtifactSha256(Saved()));
			record.RevisionId.Should().Be(_verifiedOn.ToString("o", CultureInfo.InvariantCulture));
			record.StatementText.Should().Contain("EMT");
		}

		#region Web Verify

		private WebCertifications Web(ISignatureService signatures, DateTime? stepUpAtUtc)
		{
			var http = SignIn();
			if (stepUpAtUtc.HasValue)
				http.Items["mfa_verified_at"] = stepUpAtUtc.Value;

			var strings = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Certifications.Certifications>>();
			strings.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			return new WebCertifications(_certifications.Object, Mock.Of<IPersonnelRolesService>(), Mock.Of<IUnitsService>(), Mock.Of<IDepartmentsService>(),
				Mock.Of<IDepartmentGroupsService>(), Mock.Of<IUserProfileService>(), Mock.Of<IProtectedReadService>(), strings.Object,
				Mock.Of<Resgrid.Model.Services.IAuthorizationService>(), Mock.Of<ILearningRecordService>(), signatures,
				Mock.Of<IStringLocalizer<Resgrid.Localization.Areas.User.Signatures.Signatures>>())
			{
				ControllerContext = new ControllerContext { HttpContext = http },
				TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
			};
		}

		[Test]
		public void Web_verify_requires_a_recent_step_up_for_the_operation_and_is_an_antiforgery_post()
		{
			var verify = typeof(WebCertifications).GetMethods().Single(m => m.Name == "Verify");

			verify.GetCustomAttribute<RequiresRecentTwoFactorAttribute>().Should().NotBeNull()
				.And.Subject.As<RequiresRecentTwoFactorAttribute>().RequireForOperation.Should().BeTrue();
			verify.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull();
			verify.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull();
		}

		[Test]
		public async Task Web_verify_with_a_fresh_step_up_signs_the_certification_artifact_with_the_step_up_time()
		{
			var stepUpAt = DateTime.UtcNow.AddMinutes(-1);
			SignatureRequest request = null;
			SignatureProof proof = null;
			var signatures = new Mock<ISignatureService>();
			signatures.Setup(x => x.CertificationArtifactSha256(It.IsAny<PersonnelCertification>()))
				.Returns((PersonnelCertification c) => _signatureService.CertificationArtifactSha256(c));
			signatures.Setup(x => x.SignAsync(It.IsAny<SignatureRequest>(), It.IsAny<SignatureProof>(), It.IsAny<CancellationToken>()))
				.Callback((SignatureRequest r, SignatureProof p, CancellationToken _) => { request = r; proof = p; })
				.ReturnsAsync(new SignatureResult { Outcome = SignatureOutcomes.Signed, Record = new SignatureRecord() });

			var result = await Web(signatures.Object, stepUpAt).Verify(CertificationId, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Record");
			_certifications.Verify(x => x.VerifyCertificationAsync(CertificationId, DepartmentId, Officer, It.IsAny<CancellationToken>()), Times.Once);
			signatures.Verify(x => x.SignAsync(It.IsAny<SignatureRequest>(), It.IsAny<SignatureProof>(), It.IsAny<CancellationToken>()), Times.Once);
			request.Use.Should().BeSameAs(SignatureUse.CertificationVerification);
			request.DepartmentId.Should().Be(DepartmentId);
			request.SubjectId.Should().Be(CertificationId.ToString(CultureInfo.InvariantCulture));
			request.ArtifactSha256.Should().Be(_signatureService.CertificationArtifactSha256(Saved()));
			proof.UserId.Should().Be(Officer);
			proof.StepUpVerifiedOnUtc.Should().Be(stepUpAt);
			proof.PasskeyId.Should().BeNull();
		}

		[Test]
		public async Task Web_verify_with_a_fresh_step_up_records_an_L2_step_up_signature_through_the_real_signing_layer()
		{
			var result = await Web(_signatureService, DateTime.UtcNow.AddMinutes(-1)).Verify(CertificationId, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			AssertOneL2Record();
			_inserted.Single().StepUpEvidenceReference.Should().StartWith("mfa:");
		}

		[Test]
		public async Task Web_verify_reached_without_step_up_evidence_records_no_signature()
		{
			// The filter is the gate; should the action ever run without its evidence, the signing layer still refuses L2.
			await Web(_signatureService, null).Verify(CertificationId, CancellationToken.None);

			_inserted.Should().BeEmpty();
			_signatureRows.Verify(x => x.InsertAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		#endregion Web Verify

		#region v4 VerifyCertification

		private (ApiCertifications Controller, DefaultHttpContext Http) Api(MfaEvidence evidence, bool accepted, bool withSession = true)
		{
			var http = SignIn();
			if (withSession)
				http.Items[ProtectedGrantSessionContext.HttpItemKey] = new ProtectedGrantSessionContext { SessionId = SessionId, AuthenticationGeneration = Generation };

			var evidenceService = new Mock<IMfaEvidenceService>();
			evidenceService.Setup(x => x.GetLatestSecondFactorAsync(Officer, MfaEvidence.TrackedSessionKey(SessionId), Generation, It.IsAny<CancellationToken>()))
				.ReturnsAsync(evidence);
			var policy = new Mock<IMfaPolicyService>();
			policy.Setup(x => x.IsEvidenceAcceptedAsync(DepartmentId, MfaMethodScope.Login, It.IsAny<MfaEvidence>(), It.IsAny<CancellationToken>())).ReturnsAsync(accepted);

			var controller = new ApiCertifications(_certifications.Object, Mock.Of<IProtectedReadService>(), Mock.Of<IDepartmentsService>(), _signatureService,
				evidenceService.Object, policy.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
			return (controller, http);
		}

		private static MfaEvidence Evidence(DateTime verifiedOnUtc) => new MfaEvidence
		{
			UserId = Officer, SessionKey = MfaEvidence.TrackedSessionKey(SessionId), Method = 1, VerifiedOnUtc = verifiedOnUtc
		};

		[Test]
		public void V4_verify_certification_is_a_post_behind_certifications_update()
		{
			var verify = typeof(ApiCertifications).GetMethod("VerifyCertification");

			verify.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull();
			verify.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).Should().Contain(ResgridResources.Certifications_Update);
		}

		[TestCase("no-session")]
		[TestCase("no-evidence")]
		[TestCase("stale")]
		[TestCase("rejected-by-policy")]
		public async Task V4_verify_without_fresh_accepted_step_up_is_403_and_verifies_nothing(string scenario)
		{
			var window = Math.Max(1, Resgrid.Config.TwoFactorConfig.StepUpVerificationWindowMinutes);
			var (controller, http) = scenario switch
			{
				"no-session" => Api(Evidence(DateTime.UtcNow.AddMinutes(-1)), true, withSession: false),
				"no-evidence" => Api(null, true),
				"stale" => Api(Evidence(DateTime.UtcNow.AddMinutes(-(window + 5))), true),
				_ => Api(Evidence(DateTime.UtcNow.AddMinutes(-1)), false)
			};

			var result = await controller.VerifyCertification(CertificationId, CancellationToken.None);

			result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
			http.Response.Headers["X-Resgrid-Reason"].ToString().Should().Be("certifications_step_up_required");
			_certifications.Verify(x => x.VerifyCertificationAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_inserted.Should().BeEmpty();
		}

		[Test]
		public async Task V4_verify_with_fresh_accepted_step_up_verifies_and_records_an_L2_signature()
		{
			var (controller, _) = Api(Evidence(DateTime.UtcNow.AddMinutes(-1)), true);

			var result = await controller.VerifyCertification(CertificationId, CancellationToken.None);

			result.Value.Should().NotBeNull();
			result.Value.Data.Id.Should().Be(CertificationId);
			_certifications.Verify(x => x.VerifyCertificationAsync(CertificationId, DepartmentId, Officer, It.IsAny<CancellationToken>()), Times.Once);
			AssertOneL2Record();
			_inserted.Single().StepUpEvidenceReference.Should().StartWith("mfa:1:");
		}

		#endregion v4 VerifyCertification
	}
}
