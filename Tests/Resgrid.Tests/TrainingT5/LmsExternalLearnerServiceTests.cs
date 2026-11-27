using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>
	/// Training T5 external learners: invitation and self-registration with e-mail verification, the fair-use refusal, password sign-in with
	/// lockout, the session stamp, the add-on lapse closing the portal (rule 7), branding fallback, and GDPR export and erasure.
	/// </summary>
	[TestFixture]
	public class LmsExternalLearnerServiceTests
	{
		private const int DepartmentId = 71;

		private List<LmsExternalLearner> _learners;
		private List<LmsExternalLearnerToken> _tokens;
		private List<MailMessage> _mail;
		private LmsLearnerPortal _portal;
		private LmsSeatSummary _seats;
		private LmsDepartmentGate _gate;
		private Mock<ILmsExternalLearnersRepository> _learnerRepo;
		private Mock<ILmsExternalLearnerTokensRepository> _tokenRepo;
		private Mock<ILmsClassService> _classes;
		private Mock<ILmsLearnerService> _runtime;
		private Mock<ILmsExternalCourseAccessRepository> _access;
		private Mock<IDepartmentProfileMediaService> _profiles;
		private Mock<ILmsCoursesRepository> _courses;
		private Mock<IUserProfileService> _userProfiles;
		private string _managerLanguage;
		private LmsExternalLearnerService _service;

		[SetUp]
		public void SetUp()
		{
			_learners = new List<LmsExternalLearner>();
			_tokens = new List<LmsExternalLearnerToken>();
			_mail = new List<MailMessage>();
			_portal = new LmsLearnerPortal { LmsLearnerPortalId = 1, DepartmentId = DepartmentId, PortalKey = "abcdefghjkmnpqrs", IsEnabled = true, SelfRegistration = true };
			_seats = new LmsSeatSummary { State = LmsSubscriptionStates.Active, PurchasedPacks = 1, PackSize = 25, EnabledSeats = 10 };
			_gate = new LmsDepartmentGate { DepartmentId = DepartmentId, ModuleOn = true, FlagOn = true, AddonActive = true };

			var portals = new Mock<ILmsLearnerPortalsRepository>();
			portals.Setup(p => p.GetByDepartmentAsync(DepartmentId)).ReturnsAsync(() => _portal);

			_learnerRepo = new Mock<ILmsExternalLearnersRepository>();
			_learnerRepo.Setup(r => r.GetByEmailAsync(DepartmentId, It.IsAny<string>()))
				.ReturnsAsync((int _, string email) => _learners.FirstOrDefault(l => string.Equals(l.Email, LmsExternalLearnerRules.NormalizeEmail(email), StringComparison.OrdinalIgnoreCase)));
			_learnerRepo.Setup(r => r.GetByIdForDepartmentAsync(DepartmentId, It.IsAny<int>())).ReturnsAsync((int _, int id) => _learners.FirstOrDefault(l => l.LmsExternalLearnerId == id));
			_learnerRepo.Setup(r => r.CountCountedAsync(DepartmentId)).ReturnsAsync(() => _learners.Count(l => l.IsCounted));
			_learnerRepo.Setup(r => r.InsertAsync(It.IsAny<LmsExternalLearner>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((LmsExternalLearner l, CancellationToken _, bool _) => { l.LmsExternalLearnerId = _learners.Count + 1; _learners.Add(l); return l; });
			_learnerRepo.Setup(r => r.SaveOrUpdateAsync(It.IsAny<LmsExternalLearner>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((LmsExternalLearner l, CancellationToken _, bool _) => l);
			_learnerRepo.Setup(r => r.PurgeLearnerDataAsync(DepartmentId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(7);
			// The compare-and-set on RowVersion (M0412); the real SQL and a racing stale save are proven in LmsExternalLearnersDatabaseTests.
			_learnerRepo.Setup(r => r.TryUpdateAsync(It.IsAny<LmsExternalLearner>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((LmsExternalLearner l, long expected, CancellationToken _) =>
				{
					var stored = _learners.FirstOrDefault(x => x.LmsExternalLearnerId == l.LmsExternalLearnerId);
					if (stored == null || stored.RowVersion != expected) return false;
					l.RowVersion = expected + 1;
					return true;
				});

			_tokenRepo = new Mock<ILmsExternalLearnerTokensRepository>();
			_tokenRepo.Setup(r => r.InsertAsync(It.IsAny<LmsExternalLearnerToken>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((LmsExternalLearnerToken t, CancellationToken _, bool _) => { t.LmsExternalLearnerTokenId = _tokens.Count + 1; _tokens.Add(t); return t; });
			_tokenRepo.Setup(r => r.GetByHashAsync(It.IsAny<string>())).ReturnsAsync((string hash) => _tokens.FirstOrDefault(t => t.TokenHash == hash));
			_tokenRepo.Setup(r => r.ClaimAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, DateTime now, CancellationToken _) =>
				{
					var token = _tokens.FirstOrDefault(t => t.LmsExternalLearnerTokenId == id && t.UsedOn == null && t.ExpiresOn > now);
					if (token == null) return false;
					token.UsedOn = now;
					return true;
				});

			_classes = new Mock<ILmsClassService>();
			_runtime = new Mock<ILmsLearnerService>();
			_access = new Mock<ILmsExternalCourseAccessRepository>();
			var gate = new Mock<ILmsGateService>();
			gate.Setup(g => g.GetGateAsync(DepartmentId)).ReturnsAsync(() => _gate);
			var seats = new Mock<ILmsSeatService>();
			seats.Setup(s => s.GetSummaryAsync(DepartmentId)).ReturnsAsync(() => _seats);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, Name = "Station 9", ManagingUserId = "manager" });
			_managerLanguage = null;
			_userProfiles = new Mock<IUserProfileService>();
			_userProfiles.Setup(p => p.GetProfileByUserIdAsync("manager", It.IsAny<bool>())).ReturnsAsync(() => new UserProfile { UserId = "manager", Language = _managerLanguage });
			_profiles = new Mock<IDepartmentProfileMediaService>();
			_profiles.Setup(p => p.GetBrandingAsync(DepartmentId)).ReturnsAsync(new DepartmentBranding { DepartmentId = DepartmentId, DisplayName = "Station 9 Fire" });
			var email = new Mock<IEmailSender>();
			email.Setup(e => e.SendEmail(It.IsAny<MailMessage>())).Callback<MailMessage>(m => _mail.Add(m)).ReturnsAsync(true);
			var records = new Mock<ILearningRecordsRepository>();
			records.Setup(r => r.QueryAsync(It.IsAny<LearningRecordQuery>())).ReturnsAsync(new List<LearningRecord>());
			var publicClasses = new Mock<ILmsPublicClassesRepository>();
			publicClasses.Setup(p => p.GetBySessionAsync(DepartmentId, 5)).ReturnsAsync(new LmsPublicClass { DepartmentId = DepartmentId, LmsClassSessionId = 5, IsPublic = true });
			_classes.Setup(c => c.GetSessionAsync(DepartmentId, 5)).ReturnsAsync(new LmsClassSession
			{
				LmsClassSessionId = 5, DepartmentId = DepartmentId, Title = "CPR", StartsOn = DateTime.UtcNow.AddDays(5), EndsOn = DateTime.UtcNow.AddDays(5).AddHours(3),
				EnrollmentOpen = true, Status = (int)LmsClassSessionStatuses.Scheduled
			});
			_classes.Setup(c => c.PlaceExternalAsync(DepartmentId, 5, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, int s, string userId, string _, string _, string _, bool _, DateTime _, CancellationToken _) =>
					new LmsClassEnrollment { DepartmentId = d, LmsClassSessionId = s, UserId = userId, LmsClassEnrollmentId = 40, Status = (int)LmsClassEnrollmentStatuses.Enrolled });
			var classEnrollments = new Mock<ILmsClassEnrollmentsRepository>();
			classEnrollments.Setup(c => c.GetForUserAsync(DepartmentId, It.IsAny<string>())).ReturnsAsync(new List<LmsClassEnrollment>());
			var courseEnrollments = new Mock<ILmsEnrollmentsRepository>();
			courseEnrollments.Setup(c => c.GetForUserAsync(DepartmentId, It.IsAny<string>())).ReturnsAsync(new List<LmsEnrollment>());
			var fees = new Mock<ILmsClassFeesRepository>();
			fees.Setup(f => f.GetForLearnerAsync(DepartmentId, It.IsAny<string>())).ReturnsAsync(new List<LmsClassFee>());

			_service = new LmsExternalLearnerService(portals.Object, _learnerRepo.Object, _tokenRepo.Object, _access.Object, publicClasses.Object, fees.Object,
				classEnrollments.Object, (_courses = new Mock<ILmsCoursesRepository>()).Object, Mock.Of<ILmsCourseVersionsRepository>(), courseEnrollments.Object, records.Object, _classes.Object,
				_runtime.Object, Mock.Of<ILmsPublicClassService>(), gate.Object, seats.Object, departments.Object, _profiles.Object, email.Object, Mock.Of<IEventAggregator>(),
				Mock.Of<ILmsLearnerNoticesRepository>(), _userProfiles.Object);
		}

		/// <summary>The token an e-mail carried (the link's query value).</summary>
		private string TokenFrom(MailMessage mail)
		{
			var start = mail.Body.IndexOf("token=", StringComparison.Ordinal);
			start.Should().BeGreaterThan(0, "the e-mail carries a link");
			var token = new string(mail.Body.Substring(start + 6).TakeWhile(c => !char.IsWhiteSpace(c)).ToArray());
			return Uri.UnescapeDataString(token);
		}

		private LmsExternalLearnerInvite Invite(string email = "casey@example.org") =>
			new LmsExternalLearnerInvite { Email = email, FirstName = "Casey", LastName = "Rivera", Organization = "County CERT", ClassSessionIds = new List<int> { 5 } };

		[Test]
		public async Task An_invitation_makes_a_learner_only_identity_and_emails_a_single_use_link()
		{
			var result = await _service.InviteAsync(DepartmentId, Invite(), "officer");

			result.Succeeded.Should().BeTrue();
			var learner = result.Learner;
			learner.Status.Should().Be((int)LmsExternalLearnerStatuses.Invited);
			LmsExternalLearnerIds.IsExternal(learner.LearnerUserId).Should().BeTrue("never a member id");
			learner.PasswordHash.Should().BeNull();
			_classes.Verify(c => c.PlaceExternalAsync(DepartmentId, 5, learner.LearnerUserId, "Casey Rivera", "County CERT", "officer", true, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()));
			_mail.Should().ContainSingle();
			_mail[0].To.Single().Address.Should().Be("casey@example.org");
			_mail[0].Body.Should().Contain("/Learn/abcdefghjkmnpqrs/Accept?token=").And.NotContain("casey@example.org", "no personal data in the link");
			_tokens.Single().TokenHash.Should().Be(LmsExternalLearnerRules.HashToken(TokenFrom(_mail[0])), "only the hash is stored");

			(await _service.InviteAsync(DepartmentId, Invite("CASEY@example.org"), "officer")).Error.Should().Be("lms_xl_exists");
		}

		[Test]
		public async Task Accepting_the_invitation_sets_the_password_once()
		{
			var invited = await _service.InviteAsync(DepartmentId, Invite(), "officer");
			var token = TokenFrom(_mail[0]);

			(await _service.AcceptInviteAsync(DepartmentId, token, "weak")).Error.Should().Be("lms_xl_password_length");
			var accepted = await _service.AcceptInviteAsync(DepartmentId, token, "Str0ngPassword");
			accepted.Succeeded.Should().BeTrue();
			accepted.Learner.Status.Should().Be((int)LmsExternalLearnerStatuses.Active);
			accepted.Learner.EmailConfirmedOn.Should().NotBeNull();
			accepted.Learner.PasswordHash.Should().NotBeNullOrEmpty().And.NotContain("Str0ngPassword");
			(await _service.AcceptInviteAsync(DepartmentId, token, "Str0ngPassword")).Error.Should().Be("lms_xl_token_invalid", "single use");
			(await _service.AcceptInviteAsync(DepartmentId + 1, token, "Str0ngPassword")).Error.Should().Be("lms_xl_token_invalid", "another department's portal");
			invited.Learner.LmsExternalLearnerId.Should().Be(accepted.Learner.LmsExternalLearnerId);
		}

		[Test]
		public async Task Beyond_fair_use_with_no_seat_left_the_invitation_is_refused_with_the_packs_to_add()
		{
			_seats = new LmsSeatSummary { State = LmsSubscriptionStates.Active, PurchasedPacks = 1, PackSize = 25, EnabledSeats = 25 };
			for (var i = 0; i < 25; i++)
				_learners.Add(new LmsExternalLearner { LmsExternalLearnerId = 100 + i, DepartmentId = DepartmentId, Email = "l" + i + "@x.org", Status = (int)LmsExternalLearnerStatuses.Active });

			var result = await _service.InviteAsync(DepartmentId, Invite(), "officer");

			result.Error.Should().Be("lms_xl_seats_full");
			result.PacksNeeded.Should().Be(1);
			_mail.Should().BeEmpty();
			(await _service.GetFairUseAsync(DepartmentId)).Allowance.Should().Be(25);
		}

		[Test]
		public async Task The_add_on_lapse_closes_the_portal_and_stops_invitations()
		{
			_gate.AddonActive = false;
			_seats = new LmsSeatSummary { State = LmsSubscriptionStates.Lapsed, PurchasedPacks = 0, PackSize = 25 };

			(await _service.IsPortalOpenAsync(_portal)).Should().BeFalse("lapse rule 7: external learners are suspended");
			(await _service.InviteAsync(DepartmentId, Invite(), "officer")).Error.Should().Be("lms_xl_addon_inactive");
			_gate.AddonActive = true;
			_gate.FlagOn = false;
			(await _service.IsPortalOpenAsync(_portal)).Should().BeFalse("dark behind Training.Lms");
			_gate.FlagOn = true;
			_portal.IsEnabled = false;
			(await _service.IsPortalOpenAsync(_portal)).Should().BeFalse();
		}

		[Test]
		public async Task Self_registration_needs_the_setting_and_verification_before_the_learner_counts()
		{
			_portal.SelfRegistration = false;
			(await _service.RegisterAsync(DepartmentId, new LmsLearnerRegistration { Email = "pat@example.org", FirstName = "Pat", LastName = "Lee", Password = "Str0ngPassword" }))
				.Error.Should().Be("lms_xl_registration_closed");
			_portal.SelfRegistration = true;

			var registered = await _service.RegisterAsync(DepartmentId, new LmsLearnerRegistration
			{
				Email = "pat@example.org", FirstName = "Pat", LastName = "Lee", Password = "Str0ngPassword", LmsClassSessionId = 5
			});
			registered.Succeeded.Should().BeTrue();
			registered.Learner.Status.Should().Be((int)LmsExternalLearnerStatuses.Pending);
			(await _service.GetFairUseAsync(DepartmentId)).CountedLearners.Should().Be(0, "an unverified registration holds no place");
			(await _service.SignInAsync(DepartmentId, "pat@example.org", "Str0ngPassword")).Error.Should().Be("lms_xl_not_verified");

			var verified = await _service.VerifyEmailAsync(DepartmentId, TokenFrom(_mail.Single()), "Str0ngPassword", "Pat", "Lee");
			verified.Succeeded.Should().BeTrue();
			verified.Learner.Status.Should().Be((int)LmsExternalLearnerStatuses.Active);
			_classes.Verify(c => c.PlaceExternalAsync(DepartmentId, 5, registered.Learner.LearnerUserId, "Pat Lee", null, registered.Learner.LearnerUserId, false, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
				"the class chosen at registration is booked once the address is confirmed");
			(await _service.SignInAsync(DepartmentId, "PAT@example.org", "Str0ngPassword")).Succeeded.Should().BeTrue();
		}

		[Test]
		public async Task Registering_an_address_that_has_an_account_reveals_nothing_and_creates_nothing()
		{
			_learners.Add(new LmsExternalLearner { LmsExternalLearnerId = 9, DepartmentId = DepartmentId, Email = "pat@example.org", Status = (int)LmsExternalLearnerStatuses.Active });

			var result = await _service.RegisterAsync(DepartmentId, new LmsLearnerRegistration { Email = "pat@example.org", FirstName = "Mallory", LastName = "X", Password = "Str0ngPassword" });

			result.Succeeded.Should().BeTrue("the page says the same as for a new address");
			result.Learner.Should().BeNull();
			_learners.Should().ContainSingle();
			_learners[0].FirstName.Should().BeNull("an existing account is never changed by a registration");
			_mail.Single().Body.Should().Contain("already has one");
		}

		[Test]
		public async Task Verification_waits_while_the_portal_is_full_and_the_link_stays_usable()
		{
			await _service.RegisterAsync(DepartmentId, new LmsLearnerRegistration { Email = "pat@example.org", FirstName = "Pat", LastName = "Lee", Password = "Str0ngPassword" });
			var token = TokenFrom(_mail.Single());
			_seats = new LmsSeatSummary { State = LmsSubscriptionStates.Active, PurchasedPacks = 1, PackSize = 25, EnabledSeats = 25 };
			for (var i = 0; i < 25; i++)
				_learners.Add(new LmsExternalLearner { LmsExternalLearnerId = 100 + i, DepartmentId = DepartmentId, Email = "l" + i + "@x.org", Status = (int)LmsExternalLearnerStatuses.Invited });

			(await _service.VerifyEmailAsync(DepartmentId, token, "Str0ngPassword", "Pat", "Lee")).Error.Should().Be("lms_xl_portal_full");
			_tokens.Single().UsedOn.Should().BeNull();

			_seats = new LmsSeatSummary { State = LmsSubscriptionStates.Active, PurchasedPacks = 2, PackSize = 25, EnabledSeats = 25 };
			(await _service.VerifyEmailAsync(DepartmentId, token, "Str0ngPassword", "Pat", "Lee")).Succeeded.Should().BeTrue("a pack was added");
		}

		[Test]
		public async Task Five_wrong_passwords_lock_the_learner_and_a_stamp_change_ends_sessions()
		{
			await _service.InviteAsync(DepartmentId, Invite(), "officer");
			var learner = (await _service.AcceptInviteAsync(DepartmentId, TokenFrom(_mail[0]), "Str0ngPassword")).Learner;
			var stamp = learner.SecurityStamp;

			for (var i = 0; i < 4; i++)
				(await _service.SignInAsync(DepartmentId, "casey@example.org", "Wrong1Password")).Error.Should().Be("lms_xl_signin_failed");
			// Security review L1: the locking failure, the locked account and an unknown address all give the same answer.
			(await _service.SignInAsync(DepartmentId, "casey@example.org", "Wrong1Password")).Error.Should().Be("lms_xl_signin_failed");
			(await _service.SignInAsync(DepartmentId, "casey@example.org", "Str0ngPassword")).Error.Should().Be("lms_xl_signin_failed", "even the right password waits out the lockout");
			_learners.Single().LockoutEndsOn.Should().BeAfter(DateTime.UtcNow, "the lockout itself stays");
			(await _service.SignInAsync(DepartmentId, "nobody@example.org", "Str0ngPassword")).Error.Should().Be("lms_xl_signin_failed");

			(await _service.ValidateSessionAsync(DepartmentId, learner.LmsExternalLearnerId, stamp)).Should().NotBeNull();
			await _service.SetStatusAsync(DepartmentId, learner.LmsExternalLearnerId, LmsExternalLearnerStatuses.Suspended, "officer");
			(await _service.ValidateSessionAsync(DepartmentId, learner.LmsExternalLearnerId, stamp)).Should().BeNull("suspension signs the learner out");
		}

		[Test]
		public async Task Branding_falls_back_when_the_profile_cannot_be_read()
		{
			_profiles.Setup(p => p.GetBrandingAsync(DepartmentId)).ThrowsAsync(new InvalidOperationException("down"));

			var branding = await _service.GetBrandingAsync(_portal);

			branding.DisplayName.Should().Be("Station 9", "the department name stands in");
			branding.LogoUrl.Should().BeNull();
			branding.Accent.Should().Be(LmsExternalLearnerRules.DefaultAccent);
			(await _service.GetBrandingAsync(null)).DisplayName.Should().Be("Resgrid");
		}

		[Test]
		public async Task The_gdpr_export_never_carries_credentials()
		{
			await _service.InviteAsync(DepartmentId, Invite(), "officer");
			var learner = (await _service.AcceptInviteAsync(DepartmentId, TokenFrom(_mail[0]), "Str0ngPassword")).Learner;

			var json = await _service.ExportAsync(DepartmentId, learner.LmsExternalLearnerId);
			var parsed = JObject.Parse(json);

			parsed["account"]["Email"].Value<string>().Should().Be("casey@example.org");
			json.Should().NotContain(learner.PasswordHash).And.NotContain(learner.SecurityStamp).And.NotContain("PasswordHash");
			parsed["learningRecords"].Should().NotBeNull();
		}

		[Test]
		public async Task Erasure_clears_every_personal_field_ends_sessions_and_purges_the_learner_rows()
		{
			await _service.InviteAsync(DepartmentId, Invite(), "officer");
			var learner = (await _service.AcceptInviteAsync(DepartmentId, TokenFrom(_mail[0]), "Str0ngPassword")).Learner;
			var stamp = learner.SecurityStamp;

			var erased = await _service.EraseAsync(DepartmentId, learner.LmsExternalLearnerId, null);

			erased.Succeeded.Should().BeTrue();
			var row = erased.Learner;
			row.Status.Should().Be((int)LmsExternalLearnerStatuses.Erased);
			new[] { row.Email, row.FirstName, row.LastName, row.Organization, row.PasswordHash, row.SponsorContactId, row.BillingContactId }.Should().OnlyContain(v => v == null);
			row.ErasedOn.Should().NotBeNull();
			_learnerRepo.Verify(r => r.PurgeLearnerDataAsync(DepartmentId, learner.LearnerUserId, It.IsAny<CancellationToken>()), Times.Once);
			_tokenRepo.Verify(r => r.DeleteForLearnerAsync(DepartmentId, learner.LmsExternalLearnerId, It.IsAny<CancellationToken>()), Times.Once);
			(await _service.ValidateSessionAsync(DepartmentId, learner.LmsExternalLearnerId, stamp)).Should().BeNull();
			(await _service.SignInAsync(DepartmentId, "casey@example.org", "Str0ngPassword")).Succeeded.Should().BeFalse();
			(await _service.GetFairUseAsync(DepartmentId)).CountedLearners.Should().Be(0, "an erased learner frees the place");
		}

		[Test]
		public async Task Courses_need_external_access_and_catalog_listing_for_self_enrolment()
		{
			await _service.InviteAsync(DepartmentId, Invite(), "officer");
			var learner = (await _service.AcceptInviteAsync(DepartmentId, TokenFrom(_mail[0]), "Str0ngPassword")).Learner;

			(await _service.EnrollSelfInCourseAsync(DepartmentId, learner, 12)).Error.Should().Be("lms_xl_course_closed");
			_access.Setup(a => a.GetByCourseAsync(DepartmentId, 12)).ReturnsAsync(new LmsExternalCourseAccess { LmsCourseId = 12, Access = (int)LmsExternalCourseAccessKinds.Assigned });
			(await _service.EnrollSelfInCourseAsync(DepartmentId, learner, 12)).Error.Should().Be("lms_xl_course_closed", "assigned-only courses are not self-enrolled");

			_runtime.Setup(r => r.EnrollExternalAsync(DepartmentId, 12, learner.LearnerUserId, "officer", false, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new LmsResult { Enrollment = new LmsEnrollment { LmsEnrollmentId = 3 } });
			(await _service.EnrollInCourseAsync(DepartmentId, learner.LmsExternalLearnerId, 12, "officer")).Succeeded.Should().BeTrue();
		}

		#region Security review 2026-11-25 (round 9D)

		private Task<LmsExternalLearnerResult> RegisterAsync(string email, string first, string password) =>
			_service.RegisterAsync(DepartmentId, new LmsLearnerRegistration { Email = email, FirstName = first, LastName = "Lee", Password = password });

		[Test]
		public async Task Security_review_M1_registering_a_pending_address_again_never_replaces_the_password_names_or_stamp()
		{
			// The address owner registers; before confirming, someone else registers the same address with a password of their own.
			var first = await RegisterAsync("pat@example.org", "Pat", "Str0ngPassword");
			var ownerLink = TokenFrom(_mail.Single());
			var row = _learners.Single();
			var (hash, stamp) = (row.PasswordHash, row.SecurityStamp);

			var again = await RegisterAsync("PAT@example.org", "Mallory", "Attack3rPassword");

			again.Succeeded.Should().BeTrue();
			again.Learner.Should().NotBeNull("the page answers exactly as for a new address (no enumeration)");
			_learners.Should().ContainSingle();
			row.PasswordHash.Should().Be(hash, "a pending registration is never re-keyed");
			row.FirstName.Should().Be("Pat");
			row.SecurityStamp.Should().Be(stamp);
			_learnerRepo.Verify(r => r.TryUpdateAsync(It.IsAny<LmsExternalLearner>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
			_mail.Should().HaveCount(2);
			TokenFrom(_mail[1]).Should().NotBe(ownerLink, "the address gets a verification link again");

			(await _service.VerifyEmailAsync(DepartmentId, ownerLink, "Str0ngPassword", "Pat", "Lee")).Succeeded.Should().BeTrue();
			(await _service.SignInAsync(DepartmentId, "pat@example.org", "Attack3rPassword")).Succeeded.Should().BeFalse("the second registrant's password never took");
			(await _service.SignInAsync(DepartmentId, "pat@example.org", "Str0ngPassword")).Learner.LmsExternalLearnerId.Should().Be(first.Learner.LmsExternalLearnerId);
		}

		[Test]
		public async Task Security_review_M1_attacker_first_the_confirmation_sets_the_password_so_the_attackers_never_works()
		{
			// Someone registers the owner's address first, with a password of their own; the verification link goes to the owner's inbox.
			await RegisterAsync("pat@example.org", "Mallory", "Attack3rPassword");
			var link = TokenFrom(_mail.Single());
			var registrationStamp = _learners.Single().SecurityStamp;
			(await _service.GetVerificationAsync(DepartmentId, link)).Learner.Name.Should().Be("Mallory Lee", "the Confirm page shows who registered, with the date");

			(await _service.VerifyEmailAsync(DepartmentId, link, "weak", "Pat", "Lee")).Error.Should().Be("lms_xl_password_length");
			(await _service.VerifyEmailAsync(DepartmentId, link, "Own3rPassword", " ", "Lee")).Error.Should().Be("lms_xl_name_required");
			_tokens.Single().UsedOn.Should().BeNull("a refused confirmation leaves the link usable");

			var confirmed = await _service.VerifyEmailAsync(DepartmentId, link, "Own3rPassword", "Pat", "Lee");

			confirmed.Succeeded.Should().BeTrue();
			confirmed.Learner.Name.Should().Be("Pat Lee", "the owner confirms the name");
			confirmed.Learner.SecurityStamp.Should().NotBe(registrationStamp, "activation rotates the stamp");
			(await _service.SignInAsync(DepartmentId, "pat@example.org", "Attack3rPassword")).Error.Should().Be("lms_xl_signin_failed", "the registration's password was discarded");
			(await _service.SignInAsync(DepartmentId, "pat@example.org", "Own3rPassword")).Succeeded.Should().BeTrue("whoever controls the inbox owns the account");
		}

		[Test]
		public async Task Security_review_M1_an_emailed_token_is_bound_to_the_stamp_it_was_issued_under()
		{
			await RegisterAsync("pat@example.org", "Pat", "Str0ngPassword");
			var link = TokenFrom(_mail.Single());
			var learner = _learners.Single();
			_tokens.Single().StampHash.Should().Be(LmsExternalLearnerRules.StampHash(learner.SecurityStamp)).And.NotContain(learner.SecurityStamp);
			(await _service.GetVerificationAsync(DepartmentId, link)).Learner.Should().BeSameAs(learner);

			learner.SecurityStamp = LmsExternalLearnerRules.NewToken();

			(await _service.GetVerificationAsync(DepartmentId, link)).Error.Should().Be("lms_xl_token_invalid");
			(await _service.VerifyEmailAsync(DepartmentId, link, "Str0ngPassword", "Pat", "Lee")).Error.Should().Be("lms_xl_token_invalid", "the stamp changed after the link was sent");
			learner.Status.Should().Be((int)LmsExternalLearnerStatuses.Pending);
			_tokens.Single().UsedOn.Should().BeNull();

			_tokens.Single().StampHash = null;
			(await _service.VerifyEmailAsync(DepartmentId, link, "Str0ngPassword", "Pat", "Lee")).Error.Should().Be("lms_xl_token_invalid", "a token with no stamp is refused");
		}

		[Test]
		public async Task Security_review_L1_a_locked_account_answers_exactly_like_a_wrong_password_or_an_unknown_address()
		{
			await _service.InviteAsync(DepartmentId, Invite(), "officer");
			await _service.AcceptInviteAsync(DepartmentId, TokenFrom(_mail[0]), "Str0ngPassword");
			var unknown = await _service.SignInAsync(DepartmentId, "nobody@example.org", "Wrong1Password");
			var wrong = await _service.SignInAsync(DepartmentId, "casey@example.org", "Wrong1Password");
			for (var i = 1; i < LmsExternalLearner.MaxFailedSignIns; i++)
				await _service.SignInAsync(DepartmentId, "casey@example.org", "Wrong1Password");
			var locked = await _service.SignInAsync(DepartmentId, "casey@example.org", "Str0ngPassword");

			new[] { unknown.Error, wrong.Error, locked.Error }.Should().AllBe("lms_xl_signin_failed");
			locked.Learner.Should().BeNull();
			_learners.Single().LockoutEndsOn.Should().BeAfter(DateTime.UtcNow, "the lockout stays");
		}

		[Test]
		public async Task Security_review_L2_signing_out_gives_a_new_stamp_so_a_copied_cookie_stops_working()
		{
			await _service.InviteAsync(DepartmentId, Invite(), "officer");
			var learner = (await _service.AcceptInviteAsync(DepartmentId, TokenFrom(_mail[0]), "Str0ngPassword")).Learner;
			var copied = learner.SecurityStamp;
			(await _service.ValidateSessionAsync(DepartmentId, learner.LmsExternalLearnerId, copied)).Should().NotBeNull();

			await _service.SignOutAsync(DepartmentId, learner.LmsExternalLearnerId);

			(await _service.ValidateSessionAsync(DepartmentId, learner.LmsExternalLearnerId, copied)).Should().BeNull("the copied cookie carries the old stamp");
			var again = await _service.SignInAsync(DepartmentId, "casey@example.org", "Str0ngPassword");
			(await _service.ValidateSessionAsync(DepartmentId, learner.LmsExternalLearnerId, again.Learner.SecurityStamp)).Should().NotBeNull("a new sign-in works");
		}

		[TestCase(ProtocolPublicEligibility.LicensedContent, "lms_xl_course_protocol_licensed")]
		[TestCase(ProtocolPublicEligibility.AudienceRestricted, "lms_xl_course_protocol_audience")]
		[TestCase(ProtocolPublicEligibility.DocumentBacked, "lms_xl_course_protocol_document")]
		[TestCase(ProtocolPublicEligibility.Disabled, "lms_xl_course_protocol_unavailable")]
		[TestCase(ProtocolPublicEligibility.NotPublished, "lms_xl_course_protocol_unavailable")]
		public async Task Security_review_M3_a_course_whose_protocol_lesson_fails_the_public_rules_is_refused_to_external_learners(ProtocolPublicEligibility eligibility, string reason)
		{
			_courses.Setup(c => c.GetVisibleByIdAsync(DepartmentId, 12))
				.ReturnsAsync(new LmsCourse { LmsCourseId = 12, DepartmentId = DepartmentId, CurrentVersionId = 40, Status = (int)ProtocolStatuses.Published });
			_runtime.Setup(r => r.GetExternalProtocolEligibilityAsync(DepartmentId, 40)).ReturnsAsync(eligibility);

			var refused = () => _service.SetCourseAccessAsync(DepartmentId, 12, LmsExternalCourseAccessKinds.Catalog, "officer");
			(await refused.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be(reason, "the officer is told why");
			_access.Verify(a => a.InsertAsync(It.IsAny<LmsExternalCourseAccess>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			await _service.SetCourseAccessAsync(DepartmentId, 12, LmsExternalCourseAccessKinds.None, "officer");

			// Access granted before the protocol changed: every enrolment checks again.
			_access.Setup(a => a.GetByCourseAsync(DepartmentId, 12)).ReturnsAsync(new LmsExternalCourseAccess { LmsCourseId = 12, Access = (int)LmsExternalCourseAccessKinds.Catalog });
			await _service.InviteAsync(DepartmentId, Invite(), "officer");
			var learner = (await _service.AcceptInviteAsync(DepartmentId, TokenFrom(_mail[0]), "Str0ngPassword")).Learner;
			(await _service.EnrollInCourseAsync(DepartmentId, learner.LmsExternalLearnerId, 12, "officer")).Error.Should().Be(reason);
			(await _service.EnrollSelfInCourseAsync(DepartmentId, learner, 12)).Error.Should().Be("lms_xl_course_closed", "the learner is not told the officer's reason");
			_runtime.Verify(r => r.EnrollExternalAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion

		// Round 9C: the account e-mails go in the learner's language, else the department default, else English.

		[Test]
		public async Task An_invitation_goes_in_the_department_default_language()
		{
			_managerLanguage = "de-DE";

			await _service.InviteAsync(DepartmentId, Invite(), "officer");

			var mail = _mail.Single();
			mail.Subject.Should().Be("Station 9 Fire hat Sie zu einer Schulung eingeladen");
			mail.Body.Should().StartWith("Hallo Casey,").And.Contain("/Learn/abcdefghjkmnpqrs/Accept?token=").And.Contain("14 Tage");
			mail.SubjectEncoding.Should().Be(System.Text.Encoding.UTF8);
			TokenFrom(mail).Should().NotBeNullOrEmpty("the link stands on its own line");
		}

		[Test]
		public async Task Registration_verification_and_reset_go_in_the_learner_language()
		{
			_managerLanguage = "de";
			var registered = await _service.RegisterAsync(DepartmentId, new LmsLearnerRegistration
			{
				Email = "pat@example.org", FirstName = "Pat", LastName = "Lee", Password = "Str0ngPassword", Language = "fr"
			});
			registered.Succeeded.Should().BeTrue();
			_mail.Single().Subject.Should().Be("Confirmez votre adresse e-mail", "the learner chose French over the department's German");
			_mail.Single().Body.Should().Contain("/Learn/abcdefghjkmnpqrs/Verify?token=");
			(await _service.VerifyEmailAsync(DepartmentId, TokenFrom(_mail.Single()), "Str0ngPassword", "Pat", "Lee")).Succeeded.Should().BeTrue();

			_mail.Clear();
			await _service.BeginPasswordResetAsync(DepartmentId, "pat@example.org");
			_mail.Single().Subject.Should().Be("Réinitialisez votre mot de passe de formation");
			_mail.Single().Body.Should().StartWith("Bonjour Pat,").And.Contain("/Learn/abcdefghjkmnpqrs/Reset?token=");
		}

		[Test]
		public async Task Without_a_supported_language_the_account_emails_are_english()
		{
			_managerLanguage = "pt-BR";
			await _service.RegisterAsync(DepartmentId, new LmsLearnerRegistration
			{
				Email = "pat@example.org", FirstName = "Pat", LastName = "Lee", Password = "Str0ngPassword", Language = "xx"
			});

			_mail.Single().Subject.Should().Be("Confirm your e-mail address");
			_mail.Single().Body.Should().StartWith("Hello Pat,");
		}
	}
}
