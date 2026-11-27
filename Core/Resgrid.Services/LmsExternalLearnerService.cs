using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// External learners (§13 T5 item 2, extended enterprise; §17.4 decision 10; §13a branded learner portal). A learner-only identity per
	/// department, kept apart from members by construction: a row of its own (ASP.NET Core Identity's password hasher, hashed single-use
	/// e-mailed tokens for invitation, verification and reset), a synthetic "xl-" user id the LMS runtime carries, and its own portal cookie
	/// the member app never reads. Fair use: included up to 25% of purchased seats (minimum 25), beyond that counted in packs like members
	/// and refused with the packs to add when no seat is left; suspended while the add-on is not working (lapse rule 7). The account e-mails here
	/// (invitation, verification, reset, "already registered"; <see cref="LmsLearnerAccountMail"/>) go in the learner's language, else the
	/// department's default, else English (round 9C), like round 7B's class e-mails (LmsLearnerClassMailer). Round 7B also raises workflow
	/// triggers 245-246 (registered, enrolled) and keeps the learner's e-mail language and reminder unsubscribe. Nothing here runs from a sweep.
	/// </summary>
	public class LmsExternalLearnerService : ILmsExternalLearnerService
	{
		private static readonly PasswordHasher<LmsExternalLearner> Hasher = new PasswordHasher<LmsExternalLearner>();

		private readonly ILmsLearnerPortalsRepository _portals;
		private readonly ILmsExternalLearnersRepository _learners;
		private readonly ILmsExternalLearnerTokensRepository _tokens;
		private readonly ILmsExternalCourseAccessRepository _courseAccess;
		private readonly ILmsPublicClassesRepository _publicClasses;
		private readonly ILmsClassFeesRepository _fees;
		private readonly ILmsClassEnrollmentsRepository _classEnrollments;
		private readonly ILmsCoursesRepository _courses;
		private readonly ILmsCourseVersionsRepository _versions;
		private readonly ILmsEnrollmentsRepository _courseEnrollments;
		private readonly ILearningRecordsRepository _records;
		private readonly ILmsClassService _classes;
		private readonly ILmsLearnerService _runtime;
		private readonly ILmsPublicClassService _publicClassService;
		private readonly ILmsGateService _gate;
		private readonly ILmsSeatService _seats;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentProfileMediaService _profiles;
		private readonly IEmailSender _email;
		private readonly IEventAggregator _events;
		private readonly ILmsLearnerNoticesRepository _notices;
		private readonly IUserProfileService _userProfiles;

		public LmsExternalLearnerService(ILmsLearnerPortalsRepository portals, ILmsExternalLearnersRepository learners, ILmsExternalLearnerTokensRepository tokens,
			ILmsExternalCourseAccessRepository courseAccess, ILmsPublicClassesRepository publicClasses, ILmsClassFeesRepository fees,
			ILmsClassEnrollmentsRepository classEnrollments, ILmsCoursesRepository courses, ILmsCourseVersionsRepository versions, ILmsEnrollmentsRepository courseEnrollments,
			ILearningRecordsRepository records, ILmsClassService classes, ILmsLearnerService runtime, ILmsPublicClassService publicClassService, ILmsGateService gate,
			ILmsSeatService seats, IDepartmentsService departments, IDepartmentProfileMediaService profiles, IEmailSender email, IEventAggregator events,
			ILmsLearnerNoticesRepository notices, IUserProfileService userProfiles)
		{
			_userProfiles = userProfiles;
			_portals = portals;
			_learners = learners;
			_tokens = tokens;
			_courseAccess = courseAccess;
			_publicClasses = publicClasses;
			_fees = fees;
			_classEnrollments = classEnrollments;
			_courses = courses;
			_versions = versions;
			_courseEnrollments = courseEnrollments;
			_records = records;
			_classes = classes;
			_runtime = runtime;
			_publicClassService = publicClassService;
			_gate = gate;
			_seats = seats;
			_departments = departments;
			_profiles = profiles;
			_email = email;
			_events = events;
			_notices = notices;
		}

		#region Portal

		public async Task<LmsLearnerPortal> GetPortalAsync(int departmentId)
		{
			var portal = departmentId <= 0 ? null : await _portals.GetByDepartmentAsync(departmentId);
			return portal != null && portal.DepartmentId == departmentId ? portal : null;
		}

		public async Task<LmsLearnerPortal> GetPortalByKeyAsync(string portalKey) =>
			LmsExternalLearnerRules.IsPortalKeyShape(portalKey) ? await _portals.GetByKeyAsync(portalKey) : null;

		public async Task<LmsLearnerPortal> SavePortalAsync(int departmentId, LmsLearnerPortalInput input, string actingUserId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || input == null)
				throw new InvalidOperationException("lms_xl_not_found");
			var accent = string.IsNullOrWhiteSpace(input.AccentColor) ? null : LmsExternalLearnerRules.NormalizeAccent(input.AccentColor) ?? throw new InvalidOperationException("lms_xl_accent_invalid");
			var now = DateTime.UtcNow;
			var existing = await GetPortalAsync(departmentId);
			var before = existing == null ? null : PortalSnapshot(existing);
			var portal = existing ?? new LmsLearnerPortal { DepartmentId = departmentId, PortalKey = LmsExternalLearnerRules.NewPortalKey(), CreatedOn = now };
			portal.IsEnabled = input.IsEnabled;
			portal.SelfRegistration = input.SelfRegistration;
			portal.PublicListing = input.PublicListing;
			portal.AccentColor = accent;
			portal.WelcomeText = LmsExternalLearnerRules.Trim(input.WelcomeText, LmsLearnerPortal.WelcomeMaxLength);
			portal.HideLogo = input.HideLogo;
			portal.UpdatedOn = now;
			portal.UpdatedByUserId = actingUserId;
			var stored = portal.LmsLearnerPortalId > 0 ? await _portals.SaveOrUpdateAsync(portal, cancellationToken, true) : await _portals.InsertAsync(portal, cancellationToken, true);
			Audit(departmentId, actingUserId, AuditLogTypes.LmsLearnerPortalChanged, before, PortalSnapshot(stored));
			return stored;
		}

		public async Task<LmsLearnerPortal> RegeneratePortalKeyAsync(int departmentId, string actingUserId, CancellationToken cancellationToken = default)
		{
			var portal = await GetPortalAsync(departmentId) ?? throw new InvalidOperationException("lms_xl_portal_off");
			portal.PortalKey = LmsExternalLearnerRules.NewPortalKey();
			portal.UpdatedOn = DateTime.UtcNow;
			portal.UpdatedByUserId = actingUserId;
			var stored = await _portals.SaveOrUpdateAsync(portal, cancellationToken, true);
			Audit(departmentId, actingUserId, AuditLogTypes.LmsLearnerPortalChanged, null, JsonConvert.SerializeObject(new { keyRegenerated = true }));
			return stored;
		}

		private static string PortalSnapshot(LmsLearnerPortal p) =>
			JsonConvert.SerializeObject(new { p.IsEnabled, p.SelfRegistration, p.PublicListing, p.AccentColor, p.HideLogo, Welcome = p.WelcomeText != null });

		public async Task<LmsLearnerPortalBranding> GetBrandingAsync(LmsLearnerPortal portal)
		{
			string name = null, logo = null, timeZone = null;
			if (portal != null)
			{
				try
				{
					var branding = await _profiles.GetBrandingAsync(portal.DepartmentId);
					name = branding?.DisplayName;
					var masthead = branding?.Rendition(DepartmentProfileMediaKind.EmailMasthead);
					if (masthead != null && !string.IsNullOrWhiteSpace(masthead.MediaKey))
						logo = DepartmentProfileMediaService.PublicMastheadUrl(masthead.MediaKey);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "Learner portal branding could not be read; showing the Resgrid defaults.");
				}
				try
				{
					var department = await _departments.GetDepartmentByIdAsync(portal.DepartmentId, false);
					timeZone = department?.TimeZone;
					if (string.IsNullOrWhiteSpace(name))
						name = department?.Name;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}
			var result = LmsExternalLearnerRules.Branding(portal, name, logo);
			result.TimeZone = timeZone;
			return result;
		}

		public async Task<bool> IsPortalOpenAsync(LmsLearnerPortal portal)
		{
			if (portal == null || !portal.IsEnabled)
				return false;
			try
			{
				// One gate read per request; lapse rule 7: external learners are suspended while the add-on is not working.
				var gate = await _gate.GetGateAsync(portal.DepartmentId);
				return gate != null && gate.FlagOn && gate.AddonActive == true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The LMS gate could not be read for the learner portal.");
				return false;
			}
		}

		public string PortalUrl(LmsLearnerPortal portal, string path = null)
		{
			var root = (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/Learn/" + portal?.PortalKey;
			return string.IsNullOrEmpty(path) ? root : root + "/" + path.TrimStart('/');
		}

		#endregion Portal

		#region Fair use

		public async Task<LmsFairUse> GetFairUseAsync(int departmentId)
		{
			var summary = await _seats.GetSummaryAsync(departmentId);
			var counted = await _learners.CountCountedAsync(departmentId);
			return new LmsFairUse
			{
				AddonActive = summary != null && (summary.State == LmsSubscriptionStates.Active || summary.State == LmsSubscriptionStates.DunningGrace),
				PurchasedSeats = summary?.PurchasedSeats ?? 0,
				EnabledMemberSeats = summary?.EnabledSeats ?? 0,
				CountedLearners = Math.Max(0, counted),
				Allowance = LmsExternalLearnerRules.Allowance(summary?.PurchasedSeats ?? 0)
			};
		}

		private async Task<LmsFairUseDecision> DecideAsync(int departmentId) =>
			LmsExternalLearnerRules.Decide(await GetFairUseAsync(departmentId), 1, Math.Max(1, Config.LmsAddonConfig.PackSize));

		#endregion Fair use

		#region Learners (department side)

		public async Task<List<LmsExternalLearner>> GetLearnersAsync(int departmentId) =>
			departmentId <= 0 ? new List<LmsExternalLearner>() :
			(await _learners.GetForDepartmentAsync(departmentId) ?? new List<LmsExternalLearner>()).Where(l => l.DepartmentId == departmentId).ToList();

		public async Task<LmsExternalLearner> GetLearnerAsync(int departmentId, int lmsExternalLearnerId)
		{
			var learner = departmentId <= 0 ? null : await _learners.GetByIdForDepartmentAsync(departmentId, lmsExternalLearnerId);
			return learner != null && learner.DepartmentId == departmentId ? learner : null;
		}

		public async Task<LmsExternalLearnerResult> InviteAsync(int departmentId, LmsExternalLearnerInvite invite, string actingUserId, CancellationToken cancellationToken = default)
		{
			if (invite == null || !LmsExternalLearnerRules.IsValidEmail(invite.Email))
				return LmsExternalLearnerResult.Fail("lms_xl_email_invalid");
			var first = LmsExternalLearnerRules.Trim(invite.FirstName, LmsExternalLearner.NameMaxLength);
			var last = LmsExternalLearnerRules.Trim(invite.LastName, LmsExternalLearner.NameMaxLength);
			if (first == null || last == null)
				return LmsExternalLearnerResult.Fail("lms_xl_name_required");
			var portal = await GetPortalAsync(departmentId);
			if (portal == null || !portal.IsEnabled)
				return LmsExternalLearnerResult.Fail("lms_xl_portal_off");
			if (await _learners.GetByEmailAsync(departmentId, invite.Email) != null)
				return LmsExternalLearnerResult.Fail("lms_xl_exists");

			var decision = await DecideAsync(departmentId);
			if (!decision.Allowed)
				return LmsExternalLearnerResult.Fail(decision.Refusal, decision.PacksNeeded);

			var now = DateTime.UtcNow;
			LmsExternalLearner learner;
			try
			{
				learner = await _learners.InsertAsync(new LmsExternalLearner
				{
					DepartmentId = departmentId,
					LearnerUserId = LmsExternalLearnerIds.New(),
					Email = LmsExternalLearnerRules.NormalizeEmail(invite.Email),
					FirstName = first,
					LastName = last,
					Organization = LmsExternalLearnerRules.Trim(invite.Organization, LmsExternalLearner.OrganizationMaxLength),
					SponsorContactId = LmsExternalLearnerRules.Trim(invite.SponsorContactId, 128),
					Status = (int)LmsExternalLearnerStatuses.Invited,
					Source = (int)LmsExternalLearnerSources.Invited,
					SecurityStamp = LmsExternalLearnerRules.NewToken(),
					UnsubscribeKey = LmsLearnerNoticeRules.NewUnsubscribeKey(),
					InvitedByUserId = actingUserId,
					CreatedOn = now
				}, cancellationToken, true);
			}
			catch (Exception ex) when (IsDuplicate(ex))
			{
				return LmsExternalLearnerResult.Fail("lms_xl_exists");
			}

			var notes = new List<string>();
			foreach (var courseId in (invite.CourseIds ?? new List<int>()).Distinct())
			{
				var enrolled = await EnrollInCourseAsync(departmentId, learner.LmsExternalLearnerId, courseId, actingUserId, cancellationToken);
				if (!enrolled.Succeeded)
					notes.Add(enrolled.Error);
			}
			foreach (var sessionId in (invite.ClassSessionIds ?? new List<int>()).Distinct())
			{
				var placed = await EnrollInClassAsync(departmentId, learner.LmsExternalLearnerId, sessionId, actingUserId, cancellationToken);
				if (!placed.Succeeded)
					notes.Add(placed.Error);
			}

			await SendInviteAsync(portal, learner, cancellationToken);
			Audit(departmentId, actingUserId, AuditLogTypes.LmsExternalLearnerInvited, null,
				JsonConvert.SerializeObject(new { learner.LmsExternalLearnerId, learner.LearnerUserId, decision.CountsInPacks, courses = invite.CourseIds, classes = invite.ClassSessionIds }));
			return new LmsExternalLearnerResult { Learner = learner, Value = notes };
		}

		public async Task<LmsExternalLearnerResult> ResendInviteAsync(int departmentId, int lmsExternalLearnerId, string actingUserId, CancellationToken cancellationToken = default)
		{
			var learner = await GetLearnerAsync(departmentId, lmsExternalLearnerId);
			if (learner == null || learner.Status != (int)LmsExternalLearnerStatuses.Invited)
				return LmsExternalLearnerResult.Fail("lms_xl_not_invited");
			var portal = await GetPortalAsync(departmentId);
			if (portal == null || !portal.IsEnabled)
				return LmsExternalLearnerResult.Fail("lms_xl_portal_off");
			await SendInviteAsync(portal, learner, cancellationToken);
			return new LmsExternalLearnerResult { Learner = learner };
		}

		public async Task<LmsExternalLearnerResult> UpdateAsync(int departmentId, int lmsExternalLearnerId, string firstName, string lastName, string organization,
			string sponsorContactId, string billingContactId, string actingUserId, CancellationToken cancellationToken = default)
		{
			var learner = await GetLearnerAsync(departmentId, lmsExternalLearnerId);
			if (learner == null || learner.Status == (int)LmsExternalLearnerStatuses.Erased)
				return LmsExternalLearnerResult.Fail("lms_xl_not_found");
			var first = LmsExternalLearnerRules.Trim(firstName, LmsExternalLearner.NameMaxLength);
			var last = LmsExternalLearnerRules.Trim(lastName, LmsExternalLearner.NameMaxLength);
			if (first == null || last == null)
				return LmsExternalLearnerResult.Fail("lms_xl_name_required");
			var before = JsonConvert.SerializeObject(new { learner.Organization, learner.SponsorContactId, learner.BillingContactId });
			var now = DateTime.UtcNow;
			var stored = await ChangeLearnerAsync(learner, l =>
			{
				if (l.Status == (int)LmsExternalLearnerStatuses.Erased)
					return false;
				l.FirstName = first;
				l.LastName = last;
				l.Organization = LmsExternalLearnerRules.Trim(organization, LmsExternalLearner.OrganizationMaxLength);
				l.SponsorContactId = LmsExternalLearnerRules.Trim(sponsorContactId, 128);
				l.BillingContactId = LmsExternalLearnerRules.Trim(billingContactId, 128);
				l.UpdatedOn = now;
				return true;
			}, cancellationToken);
			if (stored == null)
				return LmsExternalLearnerResult.Fail("lms_xl_busy");
			Audit(departmentId, actingUserId, AuditLogTypes.LmsExternalLearnerChanged, before,
				JsonConvert.SerializeObject(new { learner.LmsExternalLearnerId, stored.Organization, stored.SponsorContactId, stored.BillingContactId }));
			return new LmsExternalLearnerResult { Learner = stored };
		}

		public async Task<LmsExternalLearnerResult> SetStatusAsync(int departmentId, int lmsExternalLearnerId, LmsExternalLearnerStatuses status, string actingUserId,
			CancellationToken cancellationToken = default)
		{
			var learner = await GetLearnerAsync(departmentId, lmsExternalLearnerId);
			if (learner == null || learner.Status == (int)LmsExternalLearnerStatuses.Erased)
				return LmsExternalLearnerResult.Fail("lms_xl_not_found");
			var before = ((LmsExternalLearnerStatuses)learner.Status).ToString();

			switch (status)
			{
				case LmsExternalLearnerStatuses.Active:
					if (learner.IsCounted)
						return new LmsExternalLearnerResult { Learner = learner };
					if (learner.Status == (int)LmsExternalLearnerStatuses.Pending)
						return LmsExternalLearnerResult.Fail("lms_xl_not_verified");
					var decision = await DecideAsync(departmentId);
					if (!decision.Allowed)
						return LmsExternalLearnerResult.Fail(decision.Refusal, decision.PacksNeeded);
					break;
				case LmsExternalLearnerStatuses.Suspended:
				case LmsExternalLearnerStatuses.Removed:
					break;
				default:
					return LmsExternalLearnerResult.Fail("lms_xl_status_invalid");
			}

			// Applied to the stored row (security review follow-up, M0412): a racing save cannot undo the new status or stamp, nor this one undo it.
			var now = DateTime.UtcNow;
			var stored = await ChangeLearnerAsync(learner, l =>
			{
				if (l.Status == (int)LmsExternalLearnerStatuses.Erased)
					return false;
				if (status == LmsExternalLearnerStatuses.Active)
				{
					if (l.IsCounted || l.Status == (int)LmsExternalLearnerStatuses.Pending)
						return false;
					l.Status = (int)(string.IsNullOrEmpty(l.PasswordHash) ? LmsExternalLearnerStatuses.Invited : LmsExternalLearnerStatuses.Active);
				}
				else
				{
					l.Status = (int)status;
					l.SecurityStamp = LmsExternalLearnerRules.NewToken();
				}
				l.UpdatedOn = now;
				return true;
			}, cancellationToken);
			if (stored == null)
			{
				var fresh = await GetLearnerAsync(departmentId, lmsExternalLearnerId);
				return status == LmsExternalLearnerStatuses.Active && fresh?.IsCounted == true
					? new LmsExternalLearnerResult { Learner = fresh }
					: LmsExternalLearnerResult.Fail("lms_xl_busy");
			}
			learner = stored;
			if (status == LmsExternalLearnerStatuses.Removed)
				await WithdrawOpenPlacesAsync(departmentId, learner, actingUserId, cancellationToken);
			Audit(departmentId, actingUserId, AuditLogTypes.LmsExternalLearnerChanged, JsonConvert.SerializeObject(new { status = before }),
				JsonConvert.SerializeObject(new { learner.LmsExternalLearnerId, status = ((LmsExternalLearnerStatuses)stored.Status).ToString() }));
			return new LmsExternalLearnerResult { Learner = stored };
		}

		public async Task<LmsExternalLearnerResult> EnrollInCourseAsync(int departmentId, int lmsExternalLearnerId, int lmsCourseId, string actingUserId,
			CancellationToken cancellationToken = default)
		{
			var learner = await GetLearnerAsync(departmentId, lmsExternalLearnerId);
			if (learner == null || !learner.IsCounted)
				return LmsExternalLearnerResult.Fail("lms_xl_not_found");
			var access = await _courseAccess.GetByCourseAsync(departmentId, lmsCourseId);
			if (access == null || access.Access == (int)LmsExternalCourseAccessKinds.None)
				return LmsExternalLearnerResult.Fail("lms_xl_course_closed");
			var protocolRefusal = await ProtocolRefusalAsync(departmentId, await _courses.GetVisibleByIdAsync(departmentId, lmsCourseId));
			if (protocolRefusal != null)
				return LmsExternalLearnerResult.Fail(protocolRefusal);
			return AnnounceCourse(MapCourse(await _runtime.EnrollExternalAsync(departmentId, lmsCourseId, learner.LearnerUserId, actingUserId, false, cancellationToken), learner));
		}

		public async Task<LmsExternalLearnerResult> EnrollInClassAsync(int departmentId, int lmsExternalLearnerId, int lmsClassSessionId, string actingUserId,
			CancellationToken cancellationToken = default)
		{
			var learner = await GetLearnerAsync(departmentId, lmsExternalLearnerId);
			if (learner == null || !learner.IsCounted)
				return LmsExternalLearnerResult.Fail("lms_xl_not_found");
			return await PlaceAsync(departmentId, learner, lmsClassSessionId, actingUserId, true, cancellationToken);
		}

		/// <summary>
		/// Security review M3 (2026-11-25): why the course's version in force cannot go to external learners (a protocol lesson that fails the
		/// public-page rules: licensed content, an audience, a document-backed version, or disabled / not in force), or null when it can.
		/// </summary>
		private async Task<string> ProtocolRefusalAsync(int departmentId, LmsCourse course)
		{
			if (course == null || course.DepartmentId != departmentId || !course.CurrentVersionId.HasValue)
				return null;
			return (await _runtime.GetExternalProtocolEligibilityAsync(departmentId, course.CurrentVersionId.Value)) switch
			{
				ProtocolPublicEligibility.Eligible => null,
				ProtocolPublicEligibility.LicensedContent => "lms_xl_course_protocol_licensed",
				ProtocolPublicEligibility.AudienceRestricted => "lms_xl_course_protocol_audience",
				ProtocolPublicEligibility.DocumentBacked => "lms_xl_course_protocol_document",
				_ => "lms_xl_course_protocol_unavailable"
			};
		}

		public async Task<List<LmsExternalCourseAccess>> GetCourseAccessAsync(int departmentId) =>
			departmentId <= 0 ? new List<LmsExternalCourseAccess>() :
			(await _courseAccess.GetForDepartmentAsync(departmentId) ?? new List<LmsExternalCourseAccess>()).Where(a => a.DepartmentId == departmentId).ToList();

		public async Task<LmsExternalCourseAccess> SetCourseAccessAsync(int departmentId, int lmsCourseId, LmsExternalCourseAccessKinds access, string actingUserId,
			CancellationToken cancellationToken = default)
		{
			var course = await _courses.GetVisibleByIdAsync(departmentId, lmsCourseId);
			if (course == null || course.IsSystem || course.DepartmentId != departmentId || course.IsDeleted)
				throw new InvalidOperationException("lms_xl_course_not_found");
			if (!Enum.IsDefined(typeof(LmsExternalCourseAccessKinds), access))
				throw new InvalidOperationException("lms_xl_status_invalid");
			if (access != LmsExternalCourseAccessKinds.None)
			{
				var protocolRefusal = await ProtocolRefusalAsync(departmentId, course);
				if (protocolRefusal != null)
					throw new InvalidOperationException(protocolRefusal);
			}
			var row = await _courseAccess.GetByCourseAsync(departmentId, lmsCourseId) ?? new LmsExternalCourseAccess { DepartmentId = departmentId, LmsCourseId = lmsCourseId };
			var before = row.LmsExternalCourseAccessId > 0 ? ((LmsExternalCourseAccessKinds)row.Access).ToString() : null;
			row.Access = (int)access;
			row.UpdatedOn = DateTime.UtcNow;
			row.UpdatedByUserId = actingUserId;
			var stored = row.LmsExternalCourseAccessId > 0 ? await _courseAccess.SaveOrUpdateAsync(row, cancellationToken, true) : await _courseAccess.InsertAsync(row, cancellationToken, true);
			Audit(departmentId, actingUserId, AuditLogTypes.LmsLearnerPortalChanged, before, JsonConvert.SerializeObject(new { lmsCourseId, access = access.ToString() }));
			return stored;
		}

		#endregion Learners (department side)

		#region Identity (portal side)

		public async Task<LmsExternalLearnerResult> RegisterAsync(int departmentId, LmsLearnerRegistration registration, CancellationToken cancellationToken = default)
		{
			var portal = await GetPortalAsync(departmentId);
			if (registration == null || portal == null || !portal.SelfRegistration || !await IsPortalOpenAsync(portal))
				return LmsExternalLearnerResult.Fail("lms_xl_registration_closed");
			if (!LmsExternalLearnerRules.IsValidEmail(registration.Email))
				return LmsExternalLearnerResult.Fail("lms_xl_email_invalid");
			var first = LmsExternalLearnerRules.Trim(registration.FirstName, LmsExternalLearner.NameMaxLength);
			var last = LmsExternalLearnerRules.Trim(registration.LastName, LmsExternalLearner.NameMaxLength);
			if (first == null || last == null)
				return LmsExternalLearnerResult.Fail("lms_xl_name_required");
			var passwordRefusal = LmsExternalLearnerRules.PasswordRefusal(registration.Password);
			if (passwordRefusal != null)
				return LmsExternalLearnerResult.Fail(passwordRefusal);
			if (registration.LmsClassSessionId.HasValue)
			{
				var refusal = await ClassRefusalAsync(departmentId, registration.LmsClassSessionId.Value, DateTime.UtcNow);
				if (refusal != null)
					return LmsExternalLearnerResult.Fail(refusal);
			}

			var now = DateTime.UtcNow;
			var existing = await _learners.GetByEmailAsync(departmentId, registration.Email);
			if (existing != null && existing.Status != (int)LmsExternalLearnerStatuses.Pending)
			{
				// No account enumeration: the page says the same thing; the mailbox owner is told to sign in or reset the password.
				await SendAccountMailAsync(portal, existing.Email, existing.Language ?? registration.Language, LmsLearnerAccountMailKinds.AlreadyRegistered, null,
					PortalUrl(portal, "SignIn"), TimeSpan.Zero, cancellationToken);
				return new LmsExternalLearnerResult();
			}

			var decision = await DecideAsync(departmentId);
			if (!decision.Allowed)
				return LmsExternalLearnerResult.Fail("lms_xl_portal_full");

			var learner = existing ?? new LmsExternalLearner
			{
				DepartmentId = departmentId,
				LearnerUserId = LmsExternalLearnerIds.New(),
				Email = LmsExternalLearnerRules.NormalizeEmail(registration.Email),
				Status = (int)LmsExternalLearnerStatuses.Pending,
				Source = (int)LmsExternalLearnerSources.SelfRegistered,
				CreatedOn = now
			};
			// Security review M1 (2026-11-25): a registration still waiting for its address to be confirmed is never changed by another one (a new
			// password, names or stamp under the first registrant's still-valid link was an account pre-hijack). The address only gets a
			// verification link again, under the stamp it already has, and the page answers exactly as for a new address.
			if (existing == null)
			{
				learner.FirstName = first;
				learner.LastName = last;
				learner.Organization = LmsExternalLearnerRules.Trim(registration.Organization, LmsExternalLearner.OrganizationMaxLength);
				learner.Language = LmsLearnerMailText.SupportedCulture(registration.Language)?.TwoLetterISOLanguageName ?? learner.Language;
				learner.UnsubscribeKey ??= LmsLearnerNoticeRules.NewUnsubscribeKey();
				learner.PasswordHash = Hasher.HashPassword(learner, registration.Password);
				learner.SecurityStamp = LmsExternalLearnerRules.NewToken();
				learner.UpdatedOn = now;
				try
				{
					learner = await _learners.InsertAsync(learner, cancellationToken, true);
				}
				catch (Exception ex) when (IsDuplicate(ex))
				{
					// The same address registered twice at once: the first registration stands and its e-mail is on the way.
					return new LmsExternalLearnerResult();
				}
			}

			var token = await IssueTokenAsync(learner, LmsExternalLearnerTokenPurposes.VerifyEmail, LmsExternalLearnerRules.VerifyLifetime, registration.LmsClassSessionId, cancellationToken);
			await SendAccountMailAsync(portal, learner.Email, learner.Language, LmsLearnerAccountMailKinds.VerifyEmail, learner.FirstName,
				PortalUrl(portal, "Verify?token=" + Uri.EscapeDataString(token)), LmsExternalLearnerRules.VerifyLifetime, cancellationToken);
			Audit(departmentId, null, AuditLogTypes.LmsExternalLearnerRegistered, null,
				JsonConvert.SerializeObject(new { learner.LmsExternalLearnerId, learner.LearnerUserId, registration.LmsClassSessionId }));
			return new LmsExternalLearnerResult { Learner = learner };
		}

		public async Task<LmsExternalLearnerResult> VerifyEmailAsync(int departmentId, string token, string password, string firstName, string lastName,
			CancellationToken cancellationToken = default)
		{
			// Security review follow-up (M1, attacker-first order): whoever opened the e-mailed link chooses the password (and confirms the name)
			// here. The password typed at registration is discarded, so a password someone else registered the address with never works.
			var refusal = LmsExternalLearnerRules.PasswordRefusal(password);
			if (refusal != null)
				return LmsExternalLearnerResult.Fail(refusal);
			var first = LmsExternalLearnerRules.Trim(firstName, LmsExternalLearner.NameMaxLength);
			var last = LmsExternalLearnerRules.Trim(lastName, LmsExternalLearner.NameMaxLength);
			if (first == null || last == null)
				return LmsExternalLearnerResult.Fail("lms_xl_name_required");
			var (row, learner) = await ReadTokenAsync(departmentId, token, LmsExternalLearnerTokenPurposes.VerifyEmail);
			if (row == null || learner == null || learner.Status != (int)LmsExternalLearnerStatuses.Pending)
				return LmsExternalLearnerResult.Fail("lms_xl_token_invalid");

			// Fair use decides at activation (a pending learner holds no place); the token stays usable for a later try.
			var decision = await DecideAsync(departmentId);
			if (!decision.Allowed)
				return LmsExternalLearnerResult.Fail("lms_xl_portal_full");
			if (!await _tokens.ClaimAsync(row.LmsExternalLearnerTokenId, DateTime.UtcNow, cancellationToken))
				return LmsExternalLearnerResult.Fail("lms_xl_token_invalid");

			var now = DateTime.UtcNow;
			learner = await ChangeLearnerAsync(learner, l =>
			{
				if (l.Status != (int)LmsExternalLearnerStatuses.Pending)
					return false;
				l.FirstName = first;
				l.LastName = last;
				l.PasswordHash = Hasher.HashPassword(l, password);
				l.SecurityStamp = LmsExternalLearnerRules.NewToken();
				l.AccessFailedCount = 0;
				l.LockoutEndsOn = null;
				l.Status = (int)LmsExternalLearnerStatuses.Active;
				l.EmailConfirmedOn = now;
				l.UpdatedOn = now;
				return true;
			}, cancellationToken);
			if (learner == null)
				return LmsExternalLearnerResult.Fail("lms_xl_token_invalid");
			AnnounceLearner(learner, ExternalLearnerWorkflowChanges.Registered);

			var result = new LmsExternalLearnerResult { Learner = learner };
			if (row.LmsClassSessionId.HasValue)
			{
				var placed = await RegisterForClassAsync(departmentId, learner, row.LmsClassSessionId.Value, cancellationToken);
				result.Value = placed.Succeeded ? placed.Value : placed.Error;
			}
			return result;
		}

		public async Task<LmsExternalLearnerResult> GetVerificationAsync(int departmentId, string token)
		{
			// Security review M2: the link's page only reads the token (a mail scanner opening it changes nothing); the POST activates.
			var (row, learner) = await ReadTokenAsync(departmentId, token, LmsExternalLearnerTokenPurposes.VerifyEmail);
			return row == null || learner == null || learner.Status != (int)LmsExternalLearnerStatuses.Pending
				? LmsExternalLearnerResult.Fail("lms_xl_token_invalid")
				: new LmsExternalLearnerResult { Learner = learner };
		}

		public async Task<LmsExternalLearnerResult> GetInviteAsync(int departmentId, string token)
		{
			var (row, learner) = await ReadTokenAsync(departmentId, token, LmsExternalLearnerTokenPurposes.Invite);
			return row == null || learner == null || learner.Status != (int)LmsExternalLearnerStatuses.Invited
				? LmsExternalLearnerResult.Fail("lms_xl_token_invalid")
				: new LmsExternalLearnerResult { Learner = learner };
		}

		public async Task<LmsExternalLearnerResult> AcceptInviteAsync(int departmentId, string token, string password, CancellationToken cancellationToken = default)
		{
			var refusal = LmsExternalLearnerRules.PasswordRefusal(password);
			if (refusal != null)
				return LmsExternalLearnerResult.Fail(refusal);
			var (row, learner) = await ReadTokenAsync(departmentId, token, LmsExternalLearnerTokenPurposes.Invite);
			if (row == null || learner == null || learner.Status != (int)LmsExternalLearnerStatuses.Invited)
				return LmsExternalLearnerResult.Fail("lms_xl_token_invalid");
			if (!await _tokens.ClaimAsync(row.LmsExternalLearnerTokenId, DateTime.UtcNow, cancellationToken))
				return LmsExternalLearnerResult.Fail("lms_xl_token_invalid");

			// An invitation never carries a password of its own: the one chosen here, by whoever opened the e-mailed link, is the only one.
			var now = DateTime.UtcNow;
			learner = await ChangeLearnerAsync(learner, l =>
			{
				if (l.Status != (int)LmsExternalLearnerStatuses.Invited)
					return false;
				l.PasswordHash = Hasher.HashPassword(l, password);
				l.SecurityStamp = LmsExternalLearnerRules.NewToken();
				l.EmailConfirmedOn = now;
				l.Status = (int)LmsExternalLearnerStatuses.Active;
				l.UpdatedOn = now;
				return true;
			}, cancellationToken);
			if (learner == null)
				return LmsExternalLearnerResult.Fail("lms_xl_token_invalid");
			AnnounceLearner(learner, ExternalLearnerWorkflowChanges.Registered);
			return new LmsExternalLearnerResult { Learner = learner };
		}

		public async Task<LmsExternalLearnerResult> SignInAsync(int departmentId, string email, string password, CancellationToken cancellationToken = default)
		{
			var now = DateTime.UtcNow;
			var learner = LmsExternalLearnerRules.IsValidEmail(email) ? await _learners.GetByEmailAsync(departmentId, email) : null;
			if (learner == null || string.IsNullOrEmpty(learner.PasswordHash) || learner.DepartmentId != departmentId)
			{
				// The same work as a real check, so an unknown address takes as long as a wrong password.
				Hasher.VerifyHashedPassword(new LmsExternalLearner(), DummyHash, password ?? string.Empty);
				return LmsExternalLearnerResult.Fail("lms_xl_signin_failed");
			}
			if (learner.LockoutEndsOn.HasValue && learner.LockoutEndsOn.Value > now)
			{
				// Security review L1 (2026-11-25): a locked account answers like a wrong password or an unknown address (the same work and the
				// same message), so the answer never reveals that the account exists; the lockout stands, even for the right password.
				Hasher.VerifyHashedPassword(learner, learner.PasswordHash, password ?? string.Empty);
				return LmsExternalLearnerResult.Fail("lms_xl_signin_failed");
			}

			var verified = Hasher.VerifyHashedPassword(learner, learner.PasswordHash, password ?? string.Empty);
			if (verified == PasswordVerificationResult.Failed)
			{
				// Counted on the stored row (a compare-and-set): parallel wrong passwords each count, and no count writes back an old stamp.
				await ChangeLearnerAsync(learner, l =>
				{
					l.AccessFailedCount++;
					if (l.AccessFailedCount >= LmsExternalLearner.MaxFailedSignIns)
					{
						l.LockoutEndsOn = now.Add(LmsExternalLearner.LockoutLength);
						l.AccessFailedCount = 0;
					}
					l.UpdatedOn = now;
					return true;
				}, cancellationToken);
				return LmsExternalLearnerResult.Fail("lms_xl_signin_failed");
			}

			switch ((LmsExternalLearnerStatuses)learner.Status)
			{
				case LmsExternalLearnerStatuses.Active:
					break;
				case LmsExternalLearnerStatuses.Pending:
					return LmsExternalLearnerResult.Fail("lms_xl_not_verified");
				case LmsExternalLearnerStatuses.Suspended:
					return LmsExternalLearnerResult.Fail("lms_xl_suspended");
				default:
					return LmsExternalLearnerResult.Fail("lms_xl_signin_failed");
			}

			// Only while the row still holds the password just checked and is still active (a reset or a suspension racing this sign-in wins).
			var checkedHash = learner.PasswordHash;
			learner = await ChangeLearnerAsync(learner, l =>
			{
				if (l.Status != (int)LmsExternalLearnerStatuses.Active || !string.Equals(l.PasswordHash, checkedHash, StringComparison.Ordinal) ||
					(l.LockoutEndsOn.HasValue && l.LockoutEndsOn.Value > now))
					return false;
				if (verified == PasswordVerificationResult.SuccessRehashNeeded)
					l.PasswordHash = Hasher.HashPassword(l, password);
				l.AccessFailedCount = 0;
				l.LockoutEndsOn = null;
				l.LastSignInOn = now;
				l.UpdatedOn = now;
				return true;
			}, cancellationToken);
			return learner == null ? LmsExternalLearnerResult.Fail("lms_xl_signin_failed") : new LmsExternalLearnerResult { Learner = learner };
		}

		private static readonly string DummyHash = Hasher.HashPassword(new LmsExternalLearner(), Guid.NewGuid().ToString("N"));

		public async Task BeginPasswordResetAsync(int departmentId, string email, CancellationToken cancellationToken = default)
		{
			var portal = await GetPortalAsync(departmentId);
			var learner = portal == null || !LmsExternalLearnerRules.IsValidEmail(email) ? null : await _learners.GetByEmailAsync(departmentId, email);
			if (learner == null || learner.Status != (int)LmsExternalLearnerStatuses.Active)
				return;
			var token = await IssueTokenAsync(learner, LmsExternalLearnerTokenPurposes.ResetPassword, LmsExternalLearnerRules.ResetLifetime, null, cancellationToken);
			await SendAccountMailAsync(portal, learner.Email, learner.Language, LmsLearnerAccountMailKinds.ResetPassword, learner.FirstName,
				PortalUrl(portal, "Reset?token=" + Uri.EscapeDataString(token)), LmsExternalLearnerRules.ResetLifetime, cancellationToken);
		}

		public async Task<LmsExternalLearnerResult> ResetPasswordAsync(int departmentId, string token, string password, CancellationToken cancellationToken = default)
		{
			var refusal = LmsExternalLearnerRules.PasswordRefusal(password);
			if (refusal != null)
				return LmsExternalLearnerResult.Fail(refusal);
			var (row, learner) = await ReadTokenAsync(departmentId, token, LmsExternalLearnerTokenPurposes.ResetPassword);
			if (row == null || learner == null || learner.Status != (int)LmsExternalLearnerStatuses.Active)
				return LmsExternalLearnerResult.Fail("lms_xl_token_invalid");
			if (!await _tokens.ClaimAsync(row.LmsExternalLearnerTokenId, DateTime.UtcNow, cancellationToken))
				return LmsExternalLearnerResult.Fail("lms_xl_token_invalid");
			var now = DateTime.UtcNow;
			learner = await ChangeLearnerAsync(learner, l =>
			{
				if (l.Status != (int)LmsExternalLearnerStatuses.Active)
					return false;
				l.PasswordHash = Hasher.HashPassword(l, password);
				l.SecurityStamp = LmsExternalLearnerRules.NewToken();
				l.AccessFailedCount = 0;
				l.LockoutEndsOn = null;
				l.UpdatedOn = now;
				return true;
			}, cancellationToken);
			return learner == null ? LmsExternalLearnerResult.Fail("lms_xl_token_invalid") : new LmsExternalLearnerResult { Learner = learner };
		}

		public async Task<LmsExternalLearner> ValidateSessionAsync(int departmentId, int lmsExternalLearnerId, string securityStamp)
		{
			if (departmentId <= 0 || lmsExternalLearnerId <= 0 || string.IsNullOrEmpty(securityStamp))
				return null;
			var learner = await GetLearnerAsync(departmentId, lmsExternalLearnerId);
			return learner != null && learner.Status == (int)LmsExternalLearnerStatuses.Active && string.Equals(learner.SecurityStamp, securityStamp, StringComparison.Ordinal)
				? learner : null;
		}

		public async Task SignOutAsync(int departmentId, int lmsExternalLearnerId, CancellationToken cancellationToken = default)
		{
			// Security review L2 (2026-11-25): a new stamp, re-checked on every request, so a copied portal cookie dies with the sign-out.
			var learner = await GetLearnerAsync(departmentId, lmsExternalLearnerId);
			var now = DateTime.UtcNow;
			await ChangeLearnerAsync(learner, l =>
			{
				if (l.Status == (int)LmsExternalLearnerStatuses.Erased)
					return false;
				l.SecurityStamp = LmsExternalLearnerRules.NewToken();
				l.UpdatedOn = now;
				return true;
			}, cancellationToken);
		}

		/// <summary>
		/// Security review follow-up (M0412): applies <paramref name="change"/> to the learner row as a compare-and-set on RowVersion. When another
		/// save got there first, the row is read again and the change applied to the fresh copy (it returns false when it no longer applies),
		/// so no stale copy ever writes back an old stamp, password or status. The stored learner, or null when the change did not apply or
		/// kept losing the race.
		/// </summary>
		private async Task<LmsExternalLearner> ChangeLearnerAsync(LmsExternalLearner learner, Func<LmsExternalLearner, bool> change, CancellationToken cancellationToken)
		{
			if (learner == null)
				return null;
			var (departmentId, learnerId) = (learner.DepartmentId, learner.LmsExternalLearnerId);
			for (var attempt = 0; attempt < 3; attempt++)
			{
				if (attempt > 0)
					learner = await GetLearnerAsync(departmentId, learnerId);
				if (learner == null || !change(learner))
					return null;
				if (await _learners.TryUpdateAsync(learner, learner.RowVersion, cancellationToken))
					return learner;
			}
			return null;
		}

		private async Task<(LmsExternalLearnerToken Row, LmsExternalLearner Learner)> ReadTokenAsync(int departmentId, string token, LmsExternalLearnerTokenPurposes purpose)
		{
			var hash = LmsExternalLearnerRules.HashToken(token);
			var row = hash == null ? null : await _tokens.GetByHashAsync(hash);
			if (row == null || row.DepartmentId != departmentId || row.Purpose != (int)purpose || row.UsedOn.HasValue || row.ExpiresOn <= DateTime.UtcNow)
				return (null, null);
			var learner = await GetLearnerAsync(departmentId, row.LmsExternalLearnerId);
			// Security review M1 (2026-11-25): a token is good only under the security stamp it was issued with.
			if (learner == null || string.IsNullOrEmpty(row.StampHash) ||
				!string.Equals(row.StampHash, LmsExternalLearnerRules.StampHash(learner.SecurityStamp), StringComparison.Ordinal))
				return (null, null);
			return (row, learner);
		}

		private async Task<string> IssueTokenAsync(LmsExternalLearner learner, LmsExternalLearnerTokenPurposes purpose, TimeSpan lifetime, int? classSessionId,
			CancellationToken cancellationToken)
		{
			var token = LmsExternalLearnerRules.NewToken();
			var now = DateTime.UtcNow;
			await _tokens.InsertAsync(new LmsExternalLearnerToken
			{
				DepartmentId = learner.DepartmentId,
				LmsExternalLearnerId = learner.LmsExternalLearnerId,
				Purpose = (int)purpose,
				TokenHash = LmsExternalLearnerRules.HashToken(token),
				StampHash = LmsExternalLearnerRules.StampHash(learner.SecurityStamp),
				LmsClassSessionId = classSessionId,
				ExpiresOn = now.Add(lifetime),
				CreatedOn = now
			}, cancellationToken, true);
			return token;
		}

		private async Task SendInviteAsync(LmsLearnerPortal portal, LmsExternalLearner learner, CancellationToken cancellationToken)
		{
			var token = await IssueTokenAsync(learner, LmsExternalLearnerTokenPurposes.Invite, LmsExternalLearnerRules.InviteLifetime, null, cancellationToken);
			await SendAccountMailAsync(portal, learner.Email, learner.Language, LmsLearnerAccountMailKinds.Invite, learner.FirstName,
				PortalUrl(portal, "Accept?token=" + Uri.EscapeDataString(token)), LmsExternalLearnerRules.InviteLifetime, cancellationToken);
		}

		/// <summary>
		/// An account e-mail in the learner's language (their saved language, else the department's default: its managing member's language,
		/// else English), from the department's branded name. Round 9C; before, these were English.
		/// </summary>
		private async Task SendAccountMailAsync(LmsLearnerPortal portal, string to, string learnerLanguage, LmsLearnerAccountMailKinds kind, string firstName, string url,
			TimeSpan lifetime, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(to))
				return;
			try
			{
				var branding = await GetBrandingAsync(portal);
				var department = portal == null ? null : await _departments.GetDepartmentByIdAsync(portal.DepartmentId, false);
				var culture = await new ContentNoticeCultures(_userProfiles, department).ForLanguageAsync(learnerLanguage);
				var composed = LmsLearnerAccountMail.Compose(kind, firstName, branding.DisplayName, url, lifetime, culture);
				using var mail = new MailMessage();
				mail.To.Add(to);
				mail.From = new MailAddress("DO-NOT-REPLY@resgrid.com", branding.DisplayName);
				mail.Subject = composed.Subject;
				mail.Body = composed.Body;
				mail.IsBodyHtml = false;
				mail.SubjectEncoding = System.Text.Encoding.UTF8;
				mail.BodyEncoding = System.Text.Encoding.UTF8;
				await _email.SendEmail(mail);
			}
			catch (Exception ex)
			{
				// A failed e-mail never fails the account change; the department can resend an invitation.
				Logging.LogException(ex, "A learner portal e-mail could not be sent.");
			}
		}

		#endregion Identity (portal side)

		#region Portal reads and actions

		public async Task<LmsLearnerCatalog> GetCatalogAsync(int departmentId, LmsExternalLearner learner, DateTime utcNow)
		{
			var catalog = new LmsLearnerCatalog();
			if (departmentId <= 0)
				return catalog;

			var publicClasses = (await _publicClasses.GetForDepartmentAsync(departmentId) ?? new List<LmsPublicClass>()).Where(p => p.DepartmentId == departmentId && p.IsPublic)
				.GroupBy(p => p.LmsClassSessionId).ToDictionary(g => g.Key, g => g.First());
			if (publicClasses.Count > 0)
			{
				var sessions = (await _classes.GetSessionsAsync(departmentId, utcNow, null) ?? new List<LmsClassSession>())
					.Where(s => publicClasses.ContainsKey(s.LmsClassSessionId) && !s.IsDeleted).ToList();
				var roster = (await _classes.GetEnrollmentsAsync(departmentId, sessions.Select(s => s.LmsClassSessionId)) ?? new List<LmsClassEnrollment>()).ToLookup(e => e.LmsClassSessionId);
				foreach (var session in sessions.OrderBy(s => s.StartsOn))
				{
					var rows = roster[session.LmsClassSessionId].ToList();
					var mine = learner == null ? null : rows.FirstOrDefault(e => string.Equals(e.UserId, learner.LearnerUserId, StringComparison.OrdinalIgnoreCase) && e.IsOnRoster);
					var refusal = LmsExternalLearnerRules.ClassRegistrationRefusal(session, publicClasses[session.LmsClassSessionId], utcNow);
					if (refusal != null && mine == null)
						continue;
					catalog.Classes.Add(new LmsLearnerClassItem
					{
						Session = session,
						PublicClass = publicClasses[session.LmsClassSessionId],
						SeatsTaken = LmsClassRules.SeatsTaken(rows),
						Mine = mine,
						Refusal = refusal,
						WaitlistPosition = LmsLearnerNoticeRules.WaitlistPosition(rows, mine)
					});
				}
			}

			var open = (await GetCourseAccessAsync(departmentId)).Where(a => a.Access == (int)LmsExternalCourseAccessKinds.Catalog).Select(a => a.LmsCourseId).ToList();
			if (open.Count > 0)
			{
				var courses = (await _courses.GetVisibleByIdsAsync(departmentId, open) ?? new List<LmsCourse>())
					.Where(c => c.DepartmentId == departmentId && !c.IsSystem && !c.IsDeleted && c.Status == (int)ProtocolStatuses.Published && c.CurrentVersionId.HasValue).ToList();
				var versions = (await _versions.GetVisibleByIdsAsync(departmentId, courses.Select(c => c.CurrentVersionId.Value)) ?? new List<LmsCourseVersion>())
					.ToDictionary(v => v.LmsCourseVersionId);
				var mine = learner == null ? new List<LmsEnrollment>() : await _courseEnrollments.GetForUserAsync(departmentId, learner.LearnerUserId) ?? new List<LmsEnrollment>();
				foreach (var course in courses.OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase))
				{
					versions.TryGetValue(course.CurrentVersionId.Value, out var version);
					catalog.Courses.Add(new LmsLearnerCourseItem
					{
						Course = course,
						Version = version,
						Mine = mine.Where(e => e.LmsCourseId == course.LmsCourseId && e.Status != (int)LmsEnrollmentStatuses.Withdrawn)
							.OrderByDescending(e => e.IsOpen).ThenByDescending(e => e.AssignedOn).FirstOrDefault()
					});
				}
			}

			return catalog;
		}

		public async Task<LmsLearnerMyLearning> GetMyLearningAsync(int departmentId, LmsExternalLearner learner, DateTime utcNow)
		{
			var mine = new LmsLearnerMyLearning();
			if (learner == null || learner.DepartmentId != departmentId)
				return mine;

			mine.Courses = await _runtime.GetMyCoursesAsync(departmentId, learner.LearnerUserId) ?? new List<LmsMyCourse>();
			foreach (var enrollment in (await _classEnrollments.GetForUserAsync(departmentId, learner.LearnerUserId) ?? new List<LmsClassEnrollment>())
				.Where(e => e.IsOnRoster || e.Attendance != (int)LmsClassAttendanceStates.Unmarked))
			{
				var session = await _classes.GetSessionAsync(departmentId, enrollment.LmsClassSessionId);
				if (session == null)
					continue;
				// Round 7B: a waitlisted learner sees their place in the queue (1 is next).
				var position = enrollment.Status == (int)LmsClassEnrollmentStatuses.Waitlisted
					? LmsLearnerNoticeRules.WaitlistPosition(await _classes.GetEnrollmentsAsync(departmentId, new[] { session.LmsClassSessionId }), enrollment) : null;
				mine.Classes.Add(new LmsLearnerClassItem { Session = session, Mine = enrollment, WaitlistPosition = position });
			}
			mine.Classes = mine.Classes.OrderByDescending(c => c.Session.StartsOn >= utcNow).ThenBy(c => c.Session.StartsOn).ToList();
			mine.Records = (await _records.QueryAsync(new LearningRecordQuery
			{
				DepartmentId = departmentId,
				UserIds = new List<string> { learner.LearnerUserId },
				Statuses = new List<int> { (int)LearningRecordStatuses.Verified },
				Take = 500
			}) ?? new List<LearningRecord>()).Where(r => string.Equals(r.UserId, learner.LearnerUserId, StringComparison.OrdinalIgnoreCase))
				.OrderByDescending(r => r.CompletedOn).ToList();
			return mine;
		}

		public async Task<LmsExternalLearnerResult> RegisterForClassAsync(int departmentId, LmsExternalLearner learner, int lmsClassSessionId, CancellationToken cancellationToken = default)
		{
			if (learner == null || learner.DepartmentId != departmentId || learner.Status != (int)LmsExternalLearnerStatuses.Active)
				return LmsExternalLearnerResult.Fail("lms_xl_not_found");
			var refusal = await ClassRefusalAsync(departmentId, lmsClassSessionId, DateTime.UtcNow);
			if (refusal != null)
				return LmsExternalLearnerResult.Fail(refusal);
			return await PlaceAsync(departmentId, learner, lmsClassSessionId, learner.LearnerUserId, false, cancellationToken);
		}

		public async Task<LmsExternalLearnerResult> WithdrawFromClassAsync(int departmentId, LmsExternalLearner learner, int lmsClassSessionId, CancellationToken cancellationToken = default)
		{
			if (learner == null || learner.DepartmentId != departmentId)
				return LmsExternalLearnerResult.Fail("lms_xl_not_found");
			try
			{
				var row = await _classes.UnenrollSelfAsync(departmentId, lmsClassSessionId, learner.LearnerUserId, DateTime.UtcNow, cancellationToken);
				return new LmsExternalLearnerResult { Learner = learner, Value = row };
			}
			catch (InvalidOperationException ex)
			{
				return LmsExternalLearnerResult.Fail(ex.Message);
			}
		}

		public async Task<LmsExternalLearnerResult> EnrollSelfInCourseAsync(int departmentId, LmsExternalLearner learner, int lmsCourseId, CancellationToken cancellationToken = default)
		{
			if (learner == null || learner.DepartmentId != departmentId || learner.Status != (int)LmsExternalLearnerStatuses.Active)
				return LmsExternalLearnerResult.Fail("lms_xl_not_found");
			var access = await _courseAccess.GetByCourseAsync(departmentId, lmsCourseId);
			if (access == null || access.Access != (int)LmsExternalCourseAccessKinds.Catalog)
				return LmsExternalLearnerResult.Fail("lms_xl_course_closed");
			// Security review M3: the learner is told only that the course is closed (the reason is for the officer).
			if (await ProtocolRefusalAsync(departmentId, await _courses.GetVisibleByIdAsync(departmentId, lmsCourseId)) != null)
				return LmsExternalLearnerResult.Fail("lms_xl_course_closed");
			return AnnounceCourse(MapCourse(await _runtime.EnrollExternalAsync(departmentId, lmsCourseId, learner.LearnerUserId, learner.LearnerUserId, true, cancellationToken), learner));
		}

		public async Task<CertificateDocument> GetCertificateAsync(int departmentId, LmsExternalLearner learner, int learningRecordId)
		{
			if (learner == null || learner.DepartmentId != departmentId)
				return null;
			var record = await _records.GetByIdForDepartmentAsync(departmentId, learningRecordId);
			if (record == null || record.DepartmentId != departmentId || record.Status != (int)LearningRecordStatuses.Verified ||
				!string.Equals(record.UserId, learner.LearnerUserId, StringComparison.OrdinalIgnoreCase))
				return null;
			var branding = await GetBrandingAsync(await GetPortalAsync(departmentId));
			return new CertificateDocument
			{
				Context = new TrainingDocumentContext { DepartmentName = branding.DisplayName, GeneratedOnUtc = DateTime.UtcNow },
				MemberName = learner.Name,
				Course = record.Title,
				CourseCode = record.CourseCode,
				CompletedOn = record.CompletedOn,
				Hours = record.Hours,
				CeHours = record.CeHours,
				CeProvider = record.CeProvider,
				CeCourseNumber = record.CeCourseNumber,
				IssuedBy = branding.DisplayName,
				VerificationCode = record.VerificationCode
			};
		}

		private async Task<string> ClassRefusalAsync(int departmentId, int lmsClassSessionId, DateTime utcNow)
		{
			var session = await _classes.GetSessionAsync(departmentId, lmsClassSessionId);
			var publicClass = session == null ? null : await _publicClasses.GetBySessionAsync(departmentId, lmsClassSessionId);
			return LmsExternalLearnerRules.ClassRegistrationRefusal(session, publicClass, utcNow);
		}

		private async Task<LmsExternalLearnerResult> PlaceAsync(int departmentId, LmsExternalLearner learner, int lmsClassSessionId, string actingUserId, bool byManager,
			CancellationToken cancellationToken)
		{
			LmsClassEnrollment placed;
			try
			{
				placed = await _classes.PlaceExternalAsync(departmentId, lmsClassSessionId, learner.LearnerUserId, learner.Name, learner.Organization, actingUserId, byManager,
					DateTime.UtcNow, cancellationToken);
			}
			catch (InvalidOperationException ex)
			{
				return LmsExternalLearnerResult.Fail(ex.Message);
			}
			LmsClassFee fee = null;
			try
			{
				fee = await _publicClassService.EnsureFeeAsync(placed, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "The fee of an external learner's class place could not be recorded.");
			}
			var session = await _classes.GetSessionAsync(departmentId, lmsClassSessionId);
			AnnounceLearner(learner, ExternalLearnerWorkflowChanges.Enrolled, e =>
			{
				e.Subject = "Class";
				e.SessionId = lmsClassSessionId;
				e.Title = session?.Title;
				e.StartsOn = session?.StartsOn;
				e.Status = ((LmsClassEnrollmentStatuses)placed.Status).ToString();
				e.Fee = fee == null || fee.Waived ? 0m : fee.Amount;
				e.Currency = fee?.Currency;
			});
			return new LmsExternalLearnerResult { Learner = learner, Value = placed };
		}

		private static LmsExternalLearnerResult MapCourse(LmsResult result, LmsExternalLearner learner)
		{
			if (result != null && result.Succeeded)
				return new LmsExternalLearnerResult { Learner = learner, Value = result.Enrollment };
			return LmsExternalLearnerResult.Fail(result?.Error switch
			{
				LmsErrors.NotFound => "lms_xl_course_not_found",
				LmsErrors.AddonRequired => "lms_xl_addon_inactive",
				_ => "lms_xl_course_refused"
			});
		}

		private async Task WithdrawOpenPlacesAsync(int departmentId, LmsExternalLearner learner, string actingUserId, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			foreach (var row in (await _classEnrollments.GetForUserAsync(departmentId, learner.LearnerUserId) ?? new List<LmsClassEnrollment>()).Where(e => e.IsOnRoster))
			{
				try
				{
					var session = await _classes.GetSessionAsync(departmentId, row.LmsClassSessionId);
					if (session != null && session.IsScheduled && session.StartsOn > now)
						await _classes.RemoveMemberAsync(departmentId, row.LmsClassEnrollmentId, actingUserId ?? learner.LearnerUserId, now, cancellationToken);
				}
				catch (InvalidOperationException)
				{
					// A signed-off or closed session keeps its roster (the record of who attended).
				}
			}
		}

		#endregion Portal reads and actions

		#region E-mail preferences and resend (round 7B)

		public async Task<LmsExternalLearnerResult> SetPreferencesAsync(int departmentId, LmsExternalLearner learner, string language, bool remindersOptOut,
			CancellationToken cancellationToken = default)
		{
			var stored = learner == null ? null : await GetLearnerAsync(departmentId, learner.LmsExternalLearnerId);
			if (stored == null || stored.Status != (int)LmsExternalLearnerStatuses.Active)
				return LmsExternalLearnerResult.Fail("lms_xl_not_found");
			var before = JsonConvert.SerializeObject(new { stored.Language, stored.RemindersOptOut });
			var now = DateTime.UtcNow;
			stored = await ChangeLearnerAsync(stored, l =>
			{
				if (l.Status != (int)LmsExternalLearnerStatuses.Active)
					return false;
				l.Language = string.IsNullOrWhiteSpace(language) ? null : LmsLearnerMailText.SupportedCulture(language)?.TwoLetterISOLanguageName ?? l.Language;
				l.RemindersOptOut = remindersOptOut;
				l.UnsubscribeKey ??= LmsLearnerNoticeRules.NewUnsubscribeKey();
				l.UpdatedOn = now;
				return true;
			}, cancellationToken);
			if (stored == null)
				return LmsExternalLearnerResult.Fail("lms_xl_busy");
			Audit(departmentId, null, AuditLogTypes.LmsLearnerPreferencesChanged, before,
				JsonConvert.SerializeObject(new { stored.LmsExternalLearnerId, stored.Language, stored.RemindersOptOut }));
			return new LmsExternalLearnerResult { Learner = stored };
		}

		public async Task<LmsExternalLearner> GetByUnsubscribeKeyAsync(int departmentId, string unsubscribeKey)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(unsubscribeKey))
				return null;
			var learner = await _learners.GetByUnsubscribeKeyAsync(departmentId, unsubscribeKey.Trim());
			return learner != null && learner.DepartmentId == departmentId && learner.Status != (int)LmsExternalLearnerStatuses.Erased &&
				string.Equals(learner.UnsubscribeKey, unsubscribeKey.Trim(), StringComparison.Ordinal) ? learner : null;
		}

		public async Task<LmsExternalLearnerResult> UnsubscribeAsync(int departmentId, string unsubscribeKey, CancellationToken cancellationToken = default)
		{
			var learner = await GetByUnsubscribeKeyAsync(departmentId, unsubscribeKey);
			if (learner == null)
				return LmsExternalLearnerResult.Fail("lms_xl_token_invalid");
			if (learner.RemindersOptOut)
				return new LmsExternalLearnerResult { Learner = learner };
			var now = DateTime.UtcNow;
			learner = await ChangeLearnerAsync(learner, l =>
			{
				if (l.Status == (int)LmsExternalLearnerStatuses.Erased)
					return false;
				l.RemindersOptOut = true;
				l.UpdatedOn = now;
				return true;
			}, cancellationToken);
			if (learner == null)
				return LmsExternalLearnerResult.Fail("lms_xl_busy");
			Audit(departmentId, null, AuditLogTypes.LmsLearnerPreferencesChanged, null,
				JsonConvert.SerializeObject(new { learner.LmsExternalLearnerId, learner.RemindersOptOut, via = "unsubscribe_link" }));
			return new LmsExternalLearnerResult { Learner = learner };
		}

		public async Task ResendAccessEmailAsync(int departmentId, string email, CancellationToken cancellationToken = default)
		{
			var portal = await GetPortalAsync(departmentId);
			if (portal == null || !portal.IsEnabled || !LmsExternalLearnerRules.IsValidEmail(email))
				return;
			var learner = await _learners.GetByEmailAsync(departmentId, email);
			if (learner == null || learner.DepartmentId != departmentId)
				return;
			switch ((LmsExternalLearnerStatuses)learner.Status)
			{
				case LmsExternalLearnerStatuses.Pending:
					var token = await IssueTokenAsync(learner, LmsExternalLearnerTokenPurposes.VerifyEmail, LmsExternalLearnerRules.VerifyLifetime, null, cancellationToken);
					await SendAccountMailAsync(portal, learner.Email, learner.Language, LmsLearnerAccountMailKinds.VerifyEmail, learner.FirstName,
						PortalUrl(portal, "Verify?token=" + Uri.EscapeDataString(token)), LmsExternalLearnerRules.VerifyLifetime, cancellationToken);
					break;
				case LmsExternalLearnerStatuses.Invited:
					await SendInviteAsync(portal, learner, cancellationToken);
					break;
				case LmsExternalLearnerStatuses.Active:
					await SendAccountMailAsync(portal, learner.Email, learner.Language, LmsLearnerAccountMailKinds.SignInReminder, learner.FirstName,
						PortalUrl(portal, "SignIn"), TimeSpan.Zero, cancellationToken);
					break;
			}
		}

		/// <summary>Workflow triggers 245-247: the learner by ids only (never the name, e-mail address or organization).</summary>
		private void AnnounceLearner(LmsExternalLearner learner, ExternalLearnerWorkflowChanges change, Action<ExternalLearnerWorkflowEvent> fill = null)
		{
			if (learner == null)
				return;
			try
			{
				var e = new ExternalLearnerWorkflowEvent
				{
					DepartmentId = learner.DepartmentId,
					Change = change,
					LearnerId = learner.LmsExternalLearnerId,
					LearnerUserId = learner.LearnerUserId,
					Source = ((LmsExternalLearnerSources)learner.Source).ToString(),
					OccurredOn = DateTime.UtcNow
				};
				fill?.Invoke(e);
				_events?.SendMessage(e);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "An external learner workflow event could not be raised.");
			}
		}

		private LmsExternalLearnerResult AnnounceCourse(LmsExternalLearnerResult result)
		{
			if (result != null && result.Succeeded && result.Value is LmsEnrollment enrollment)
				AnnounceLearner(result.Learner, ExternalLearnerWorkflowChanges.Enrolled, e =>
				{
					e.Subject = "Course";
					e.CourseId = enrollment.LmsCourseId;
					e.Status = ((LmsEnrollmentStatuses)enrollment.Status).ToString();
				});
			return result;
		}

		#endregion E-mail preferences and resend (round 7B)

		#region GDPR

		public async Task<string> ExportAsync(int departmentId, int lmsExternalLearnerId)
		{
			var learner = await GetLearnerAsync(departmentId, lmsExternalLearnerId);
			if (learner == null)
				return null;
			var classes = await _classEnrollments.GetForUserAsync(departmentId, learner.LearnerUserId) ?? new List<LmsClassEnrollment>();
			var courses = await _courseEnrollments.GetForUserAsync(departmentId, learner.LearnerUserId) ?? new List<LmsEnrollment>();
			var fees = await _fees.GetForLearnerAsync(departmentId, learner.LearnerUserId) ?? new List<LmsClassFee>();
			var records = await _records.QueryAsync(new LearningRecordQuery
			{
				DepartmentId = departmentId,
				UserIds = new List<string> { learner.LearnerUserId },
				IncludeVoided = true,
				Take = 5000
			}) ?? new List<LearningRecord>();

			// Never the password hash, the security stamp or a token.
			return JsonConvert.SerializeObject(new
			{
				exportedOnUtc = DateTime.UtcNow,
				account = new
				{
					learner.Email,
					learner.FirstName,
					learner.LastName,
					learner.Organization,
					status = ((LmsExternalLearnerStatuses)learner.Status).ToString(),
					source = ((LmsExternalLearnerSources)learner.Source).ToString(),
					learner.EmailConfirmedOn,
					learner.LastSignInOn,
					learner.CreatedOn,
					learner.Language,
					learner.RemindersOptOut
				},
				classes = classes.Select(c => new
				{
					c.LmsClassSessionId,
					status = ((LmsClassEnrollmentStatuses)c.Status).ToString(),
					attendance = ((LmsClassAttendanceStates)c.Attendance).ToString(),
					c.EnrolledOn,
					c.CheckInOn,
					c.CheckOutOn,
					c.Hours
				}),
				fees = fees.Select(f => new { f.LmsClassSessionId, f.Amount, f.Currency, f.Waived, invoiced = f.IsInvoiced, f.InvoicedOn }),
				courses = courses.Select(e => new
				{
					e.LmsCourseId,
					status = ((LmsEnrollmentStatuses)e.Status).ToString(),
					e.AssignedOn,
					e.StartedOn,
					e.CompletedOn,
					e.ProgressPercent
				}),
				learningRecords = records.Select(r => new { r.Title, r.CourseCode, r.CompletedOn, r.Hours, r.CeHours, r.CeProvider, r.Status, r.VerificationCode })
			}, Formatting.Indented);
		}

		public async Task<LmsExternalLearnerResult> EraseAsync(int departmentId, int lmsExternalLearnerId, string actingUserId, CancellationToken cancellationToken = default)
		{
			var learner = await GetLearnerAsync(departmentId, lmsExternalLearnerId);
			if (learner == null)
				return LmsExternalLearnerResult.Fail("lms_xl_not_found");
			if (learner.Status == (int)LmsExternalLearnerStatuses.Erased)
				return new LmsExternalLearnerResult { Learner = learner };

			// Open places go first (the waitlist moves on), then the learner's own data; the roster rows that stay hold no personal field.
			await WithdrawOpenPlacesAsync(departmentId, learner, actingUserId, cancellationToken);
			var purged = await _learners.PurgeLearnerDataAsync(departmentId, learner.LearnerUserId, cancellationToken);
			await _tokens.DeleteForLearnerAsync(departmentId, learner.LmsExternalLearnerId, cancellationToken);
			await _notices.DeleteForLearnerAsync(departmentId, learner.LmsExternalLearnerId, cancellationToken);

			var now = DateTime.UtcNow;
			var stored = await ChangeLearnerAsync(learner, l =>
			{
				l.Email = null;
				l.FirstName = null;
				l.LastName = null;
				l.Organization = null;
				l.SponsorContactId = null;
				l.BillingContactId = null;
				l.PasswordHash = null;
				l.SecurityStamp = LmsExternalLearnerRules.NewToken();
				l.Status = (int)LmsExternalLearnerStatuses.Erased;
				l.InvitedByUserId = null;
				l.LastSignInOn = null;
				l.EmailConfirmedOn = null;
				l.LockoutEndsOn = null;
				l.AccessFailedCount = 0;
				l.ErasedOn = now;
				l.UpdatedOn = now;
				l.Language = null;
				l.UnsubscribeKey = null;
				return true;
			}, cancellationToken);
			if (stored == null)
				return LmsExternalLearnerResult.Fail("lms_xl_busy");
			Audit(departmentId, actingUserId, AuditLogTypes.LmsExternalLearnerErased, null,
				JsonConvert.SerializeObject(new { learner.LmsExternalLearnerId, bySelf = actingUserId == null, rows = purged }));
			return new LmsExternalLearnerResult { Learner = stored };
		}

		#endregion GDPR

		private static bool IsDuplicate(Exception ex)
		{
			for (var e = ex; e != null; e = e.InnerException)
			{
				if (e is Npgsql.PostgresException postgres && postgres.SqlState == "23505")
					return true;
				if (e is Microsoft.Data.SqlClient.SqlException sql && (sql.Number == 2601 || sql.Number == 2627))
					return true;
			}
			return false;
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, string before, string after)
		{
			_events?.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				Before = before,
				After = after,
				Successful = true,
				ServerName = Environment.MachineName
			});
		}
	}
}
