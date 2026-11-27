using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Crew viewing (R-C4): a station watches one lesson on a shared screen showing a rotating QR (<see cref="LmsCrewCodes"/>); each
	/// member scans it on their own phone, is marked present for that viewing, and then takes the quiz on that phone. Nobody completes
	/// a course because a colleague pressed play: presence completes only the lesson shown, per member who scanned.
	/// </summary>
	public class LmsCrewViewingService : ILmsCrewViewingService
	{
		/// <summary>How long a viewing accepts scans when nobody ends it.</summary>
		public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(4);

		private readonly ILmsCrewViewingsRepository _viewings;
		private readonly ILmsCrewViewingAttendeesRepository _attendees;
		private readonly ILmsCoursesRepository _courses;
		private readonly ILmsCourseOwnersRepository _owners;
		private readonly ILmsLessonsRepository _lessons;
		private readonly ILmsEntitlementService _entitlements;
		private readonly ILmsLearnerService _learner;
		private readonly IDepartmentsService _departmentsService;
		private readonly IEventAggregator _eventAggregator;

		public LmsCrewViewingService(ILmsCrewViewingsRepository viewings, ILmsCrewViewingAttendeesRepository attendees, ILmsCoursesRepository courses,
			ILmsCourseOwnersRepository owners, ILmsLessonsRepository lessons, ILmsEntitlementService entitlements, ILmsLearnerService learner,
			IDepartmentsService departmentsService, IEventAggregator eventAggregator)
		{
			_viewings = viewings;
			_attendees = attendees;
			_courses = courses;
			_owners = owners;
			_lessons = lessons;
			_entitlements = entitlements;
			_learner = learner;
			_departmentsService = departmentsService;
			_eventAggregator = eventAggregator;
		}

		public async Task<LmsResult> StartAsync(LmsActor actor, int lmsCourseId, int lmsLessonId, CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.IsCurrentMember)
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			var course = await _courses.GetVisibleByIdAsync(actor.DepartmentId, lmsCourseId);
			if (course == null || course.IsDeleted || !course.CurrentVersionId.HasValue)
				return LmsResult.Fail(LmsErrors.NotFound);

			if (course.IsSystem)
			{
				if (!actor.MayAssignAcademy || !actor.Gate.ModuleOn)
					return LmsResult.Fail(LmsErrors.NotAuthorized);
				if (!await _entitlements.IsVisibleAsync(actor.DepartmentId, course))
					return LmsResult.Fail(LmsErrors.NotEntitled);
			}
			else
			{
				// Crew viewing is an LMS feature (§17.2) and it enrolls members: it needs the add-on like any new assignment.
				if (!actor.RightsOn(course, await _owners.GetForCourseAsync(actor.DepartmentId, course.LmsCourseId)).Assign)
					return LmsResult.Fail(LmsErrors.NotAuthorized);
				if (!LmsLapseRules.CanAssignNew(actor.Gate, false))
					return LmsResult.Fail(LmsErrors.AddonRequired);
			}

			var lesson = await _lessons.GetVisibleByIdAsync(actor.DepartmentId, lmsLessonId);
			if (lesson == null || lesson.LmsCourseVersionId != course.CurrentVersionId.Value)
				return LmsResult.Fail(LmsErrors.NotFound);

			var now = DateTime.UtcNow;
			var viewing = new LmsCrewViewing
			{
				DepartmentId = actor.DepartmentId,
				LmsCourseId = course.LmsCourseId,
				LmsCourseVersionId = course.CurrentVersionId.Value,
				LmsLessonId = lesson.LmsLessonId,
				StartedByUserId = actor.UserId,
				StartedOn = now,
				ExpiresOn = now.Add(MaxDuration),
				Secret = LmsCrewCodes.NewSecret()
			};
			await _viewings.InsertAsync(viewing, cancellationToken, true);

			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = actor.DepartmentId,
				UserId = actor.UserId,
				Type = AuditLogTypes.LmsCrewViewingStarted,
				After = JsonConvert.SerializeObject(new { viewing.LmsCrewViewingId, course.Title, lesson = lesson.Title }),
				Successful = true,
				ServerName = Environment.MachineName
			});
			return new LmsResult { Course = course, Value = viewing };
		}

		public Task<LmsCrewViewing> GetAsync(int departmentId, int lmsCrewViewingId) => _viewings.GetByIdForDepartmentAsync(departmentId, lmsCrewViewingId);

		public async Task<List<LmsCrewViewingAttendee>> GetAttendeesAsync(int departmentId, int lmsCrewViewingId) =>
			await _attendees.GetForViewingAsync(departmentId, lmsCrewViewingId) ?? new List<LmsCrewViewingAttendee>();

		public string CurrentCode(LmsCrewViewing viewing, DateTime utcNow) =>
			IsOpen(viewing, utcNow) ? LmsCrewCodes.CurrentCode(viewing.Secret, utcNow) : null;

		private static bool IsOpen(LmsCrewViewing viewing, DateTime utcNow) => viewing != null && !viewing.EndedOn.HasValue && viewing.ExpiresOn > utcNow;

		public async Task<LmsResult> ScanAsync(int departmentId, string userId, int lmsCrewViewingId, string code, string ipAddress, CancellationToken cancellationToken = default)
		{
			var viewing = await _viewings.GetByIdForDepartmentAsync(departmentId, lmsCrewViewingId);
			var now = DateTime.UtcNow;
			if (viewing == null)
				return LmsResult.Fail(LmsErrors.NotFound);
			if (!IsOpen(viewing, now) || !LmsCrewCodes.IsValid(viewing.Secret, code, now))
				return LmsResult.Fail(LmsErrors.CodeExpired);

			// The member scanning must be a current member of the department (gates use IsCurrentMember).
			var member = await _departmentsService.GetDepartmentMemberAsync(userId, departmentId, false);
			if (!DepartmentMemberStateHelper.IsCurrentMember(member, departmentId))
				return LmsResult.Fail(LmsErrors.NotAuthorized);

			var presence = await _learner.RecordCrewPresenceAsync(viewing, userId, cancellationToken);
			if (presence.Error != LmsErrors.None && presence.Error != LmsErrors.RequirementNotMet)
				return presence;

			await _attendees.TryInsertAsync(new LmsCrewViewingAttendee
			{
				DepartmentId = departmentId,
				LmsCrewViewingId = viewing.LmsCrewViewingId,
				UserId = userId,
				MarkedOn = now,
				IpAddress = LmsCourseService.Truncate(ipAddress, 64)
			}, cancellationToken);
			return presence;
		}

		public async Task<LmsResult> EndAsync(LmsActor actor, int lmsCrewViewingId, CancellationToken cancellationToken = default)
		{
			var viewing = await _viewings.GetByIdForDepartmentAsync(actor?.DepartmentId ?? 0, lmsCrewViewingId);
			if (viewing == null)
				return LmsResult.Fail(LmsErrors.NotFound);
			if (!string.Equals(viewing.StartedByUserId, actor.UserId, StringComparison.OrdinalIgnoreCase) && !actor.MayManage && !actor.MayAssignAcademy)
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			if (!viewing.EndedOn.HasValue)
			{
				viewing.EndedOn = DateTime.UtcNow;
				await _viewings.UpdateAsync(viewing, cancellationToken, true);
			}
			return new LmsResult { Value = viewing };
		}
	}

	/// <summary>
	/// Academy visibility (R-S2): the department's module switches, the feature flags and the add-ons a lesson names. Add-ons are one
	/// billing call per request and only when a key needs one; any error hides the add-on lessons (fail closed).
	/// </summary>
	public class LmsEntitlementService : ILmsEntitlementService
	{
		private readonly IDepartmentSettingsService _settings;
		private readonly IFeatureToggleService _flags;
		private readonly ISubscriptionsService _subscriptions;

		public LmsEntitlementService(IDepartmentSettingsService settings, IFeatureToggleService flags, ISubscriptionsService subscriptions)
		{
			_settings = settings;
			_flags = flags;
			_subscriptions = subscriptions;
		}

		public async Task<AcademyEntitlements> GetEntitlementsAsync(int departmentId, IEnumerable<string> visibilityKeys)
		{
			var entitlements = new AcademyEntitlements();
			if (departmentId <= 0)
				return entitlements;

			try
			{
				var modules = await _settings.GetDepartmentModuleSettingsAsync(departmentId);
				if (modules != null)
					foreach (var property in modules.GetType().GetProperties().Where(p => p.PropertyType == typeof(bool) && p.Name.EndsWith("Disabled", StringComparison.Ordinal)))
						if (!(bool)property.GetValue(modules))
							entitlements.Modules.Add(property.Name.Substring(0, property.Name.Length - "Disabled".Length));
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, "Academy module entitlements could not be read.");
			}

			var (flags, addons) = AcademyEntitlements.Mentioned(visibilityKeys);
			foreach (var flag in flags)
			{
				try
				{
					if (await _flags.IsEnabledAsync(flag, departmentId))
						entitlements.Flags.Add(flag);
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, "An Academy feature flag could not be evaluated.");
				}
			}

			if (addons.Count > 0)
			{
				try
				{
					var plans = await _subscriptions.GetAllAddonPlansAsync() ?? new List<PlanAddon>();
					var wanted = plans.Where(p => p != null && !string.IsNullOrWhiteSpace(p.PlanAddonId) && Enum.IsDefined(typeof(PlanAddonTypes), p.AddonType) &&
						addons.Contains(((PlanAddonTypes)p.AddonType).ToString())).ToList();
					if (wanted.Count > 0)
					{
						var now = DateTime.UtcNow;
						var payments = await _subscriptions.GetCurrentPaymentAddonsForDepartmentAsync(departmentId, wanted.Select(p => p.PlanAddonId).Distinct().ToList()) ?? new List<PaymentAddon>();
						foreach (var plan in wanted)
							if (payments.Any(x => x != null && x.DepartmentId == departmentId && x.PlanAddonId == plan.PlanAddonId && x.EffectiveOn <= now && x.EndingOn > now))
								entitlements.Addons.Add(((PlanAddonTypes)plan.AddonType).ToString());
					}
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, "Academy add-on entitlements could not be read.");
				}
			}

			return entitlements;
		}

		public async Task<bool> IsVisibleAsync(int departmentId, LmsCourse course)
		{
			if (course == null)
				return false;
			if (string.IsNullOrWhiteSpace(course.VisibilityKeys))
				return true;
			return (await GetEntitlementsAsync(departmentId, new[] { course.VisibilityKeys })).Allows(course.VisibilityKeys);
		}
	}
}
