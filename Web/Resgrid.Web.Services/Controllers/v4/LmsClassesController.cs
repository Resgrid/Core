using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.Lms;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Training T4 part 1 class sessions (LMS add-on, dark behind Training.Lms), for Omni: my classes, enrol and withdraw, QR sign-in (GPS
	/// only when the session asks) and sign-out; for instructors and class managers the roster, attendance marking, the rotating code to
	/// show as a QR, and the sign-off. 404 unless the Trainings module and the operator flag are on; new enrolments need an active add-on
	/// (lapse rule 3), existing sessions can still be run and signed off.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class LmsClassesController : V4AuthenticatedApiControllerbase
	{
		private static readonly Regex ServiceCode = new Regex("^[a-z][a-z0-9_]{2,80}$", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		private readonly ILmsGateService _gate;
		private readonly ILmsAccessService _access;
		private readonly ILmsClassService _classes;
		private readonly ILmsCompletionRecordingService _recordings;
		private readonly ITrainingCompletionRecordingService _recorded;

		public LmsClassesController(ILmsGateService gate, ILmsAccessService access, ILmsClassService classes, ILmsCompletionRecordingService recordings,
			ITrainingCompletionRecordingService recorded)
		{
			_gate = gate;
			_access = access;
			_classes = classes;
			_recordings = recordings;
			_recorded = recorded;
		}

		private static T Populate<T>(T result, int pageSize, string status = ResponseHelper.Success) where T : StandardApiResponseV4Base
		{
			result.PageSize = pageSize;
			result.Status = status;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>The module and the flag (one gate read); null when classes are dark here.</summary>
		private async Task<LmsActor> ActorAsync()
		{
			if (!await _access.IsTrainingModuleOnAsync(DepartmentId))
				return null;
			var actor = await _gate.GetActorAsync(DepartmentId, UserId);
			return actor?.Gate?.FlagOn == true ? actor : null;
		}

		private static bool MayManage(LmsActor actor) =>
			actor != null && actor.IsCurrentMember && (actor.IsDepartmentAdmin || actor.CanManageCourses || actor.CanAssignTraining);

		public static LmsClassData Map(LmsClassSession session, LmsClassEnrollment mine, bool instructor, int seats, string refusal, DateTime utcNow) => session == null ? null : new LmsClassData
		{
			ClassSessionId = session.LmsClassSessionId,
			Title = session.Title,
			Description = session.Description,
			StartsOn = session.StartsOn,
			EndsOn = session.EndsOn,
			Location = session.Location,
			MeetingUrl = session.MeetingUrl,
			CalendarItemId = session.CalendarItemId,
			Capacity = session.Capacity,
			SeatsTaken = seats,
			WaitlistEnabled = session.WaitlistEnabled,
			QrEnabled = session.QrEnabled,
			GpsRequired = session.GpsRequired,
			Status = ((LmsClassSessionStatuses)session.Status).ToString(),
			IsInstructor = instructor,
			EnrollmentStatus = mine == null ? null : ((LmsClassEnrollmentStatuses)mine.Status).ToString(),
			WaitlistPosition = mine?.WaitlistPosition,
			Attendance = mine == null ? null : ((LmsClassAttendanceStates)mine.Attendance).ToString(),
			CheckInOn = mine?.CheckInOn,
			CheckOutOn = mine?.CheckOutOn,
			Hours = mine?.Hours,
			EnrollRefusal = mine != null && mine.IsOnRoster ? null : refusal,
			CanUnenroll = LmsClassRules.SelfUnenrollRefusal(session, mine, utcNow) == null,
			Tags = session.Tags
		};

		private async Task<ActionResult<T>> ActAsync<T>(T result, Func<Task> work) where T : StandardApiResponseV4Base
		{
			try
			{
				await work();
				return Ok(Populate(result, 1));
			}
			catch (InvalidOperationException ex) when (ex.Message != null && ServiceCode.IsMatch(ex.Message))
			{
				switch (result)
				{
					case LmsClassResult r: r.Error = ex.Message; break;
					case LmsClassRosterResult r: r.Error = ex.Message; break;
					case LmsClassSignOffApiResult r: r.Error = ex.Message; break;
				}
				return Ok(Populate(result, 0, ResponseHelper.Failure));
			}
		}

		private async Task<LmsClassData> MineAsync(int sessionId)
		{
			var now = DateTime.UtcNow;
			var detail = await _classes.GetDetailAsync(DepartmentId, sessionId, now);
			if (detail == null)
				return null;
			var mine = detail.Roster.FirstOrDefault(r => string.Equals(r.Enrollment.UserId, UserId, StringComparison.OrdinalIgnoreCase))?.Enrollment;
			var instructor = detail.Instructors.Any(i => string.Equals(i.UserId, UserId, StringComparison.OrdinalIgnoreCase));
			return Map(detail.Session, mine, instructor, detail.SeatsTaken, null, now);
		}

		/// <summary>The member's classes (enrolled, waitlisted, pending, teaching) and the visible ones they can still enrol in.</summary>
		[HttpGet("GetMyClasses")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsClassesResult>> GetMyClasses()
		{
			if (await ActorAsync() == null)
				return NotFound();
			var now = DateTime.UtcNow;
			var result = new LmsClassesResult
			{
				Data = (await _classes.GetMyClassesAsync(DepartmentId, UserId, now)).Select(c => Map(c.Session, c.Enrollment, c.IsInstructor, c.SeatsTaken, c.EnrollRefusal, now)).ToList()
			};
			return Ok(Populate(result, result.Data.Count));
		}

		[HttpPost("Enroll")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsClassResult>> Enroll([FromBody] LmsClassSessionRef input, CancellationToken cancellationToken)
		{
			var actor = await ActorAsync();
			if (actor == null || input == null)
				return NotFound();
			var result = new LmsClassResult();
			if (!actor.Gate.CanAuthor)
				return Ok(Populate(new LmsClassResult { Error = "lms_class_addon_required" }, 0, ResponseHelper.Failure));
			return await ActAsync(result, async () =>
			{
				await _classes.EnrollSelfAsync(DepartmentId, input.ClassSessionId, UserId, DateTime.UtcNow, cancellationToken);
				result.Data = await MineAsync(input.ClassSessionId);
			});
		}

		[HttpPost("Unenroll")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsClassResult>> Unenroll([FromBody] LmsClassSessionRef input, CancellationToken cancellationToken)
		{
			if (await ActorAsync() == null || input == null)
				return NotFound();
			var result = new LmsClassResult();
			return await ActAsync(result, async () =>
			{
				await _classes.UnenrollSelfAsync(DepartmentId, input.ClassSessionId, UserId, DateTime.UtcNow, cancellationToken);
				result.Data = await MineAsync(input.ClassSessionId);
			});
		}

		/// <summary>QR sign-in: the code scanned from the instructor's screen (an old code is refused); latitude/longitude when the session requires GPS.</summary>
		[HttpPost("SignIn")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsClassResult>> SignIn([FromBody] LmsClassSignInInput input, CancellationToken cancellationToken)
		{
			if (await ActorAsync() == null || input == null)
				return NotFound();
			var result = new LmsClassResult();
			return await ActAsync(result, async () =>
			{
				await _classes.SignInAsync(DepartmentId, input.ClassSessionId, UserId, input.Code, input.Latitude, input.Longitude, DateTime.UtcNow, cancellationToken);
				result.Data = await MineAsync(input.ClassSessionId);
			});
		}

		[HttpPost("SignOut")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsClassResult>> SignOutOfClass([FromBody] LmsClassSessionRef input, CancellationToken cancellationToken)
		{
			if (await ActorAsync() == null || input == null)
				return NotFound();
			var result = new LmsClassResult();
			return await ActAsync(result, async () =>
			{
				await _classes.SignOutAsync(DepartmentId, input.ClassSessionId, UserId, DateTime.UtcNow, cancellationToken);
				result.Data = await MineAsync(input.ClassSessionId);
			});
		}

		/// <summary>The roster of a session, for its instructors and class managers.</summary>
		[HttpGet("GetRoster")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsClassRosterResult>> GetRoster(int sessionId)
		{
			var actor = await ActorAsync();
			if (actor == null)
				return NotFound();
			if (!MayManage(actor) && !await _classes.IsInstructorAsync(DepartmentId, sessionId, UserId))
				return NotFound();
			var now = DateTime.UtcNow;
			var detail = await _classes.GetDetailAsync(DepartmentId, sessionId, now);
			if (detail == null)
				return NotFound();
			var result = new LmsClassRosterResult
			{
				Session = Map(detail.Session, null, true, detail.SeatsTaken, null, now),
				Data = detail.Roster.Select(r => new LmsClassRosterRowData
				{
					EnrollmentId = r.Enrollment.LmsClassEnrollmentId,
					UserId = r.Enrollment.UserId,
					Name = r.Name,
					Status = ((LmsClassEnrollmentStatuses)r.Enrollment.Status).ToString(),
					Attendance = ((LmsClassAttendanceStates)r.Enrollment.Attendance).ToString(),
					IsWalkIn = r.Enrollment.IsWalkIn,
					CheckInMethod = ((LmsClassCheckInMethods)r.Enrollment.CheckInMethod).ToString(),
					CheckInOn = r.Enrollment.CheckInOn,
					CheckOutOn = r.Enrollment.CheckOutOn,
					ProjectedHours = r.ProjectedHours,
					OnShift = r.OnShift,
					WaitlistPosition = r.Enrollment.WaitlistPosition
				}).ToList()
			};
			return Ok(Populate(result, result.Data.Count));
		}

		/// <summary>The current rotating code (and the web sign-in URL) for the instructor's device to show as a QR.</summary>
		[HttpGet("GetSignInCode")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsClassCodeResult>> GetSignInCode(int sessionId)
		{
			var actor = await ActorAsync();
			if (actor == null)
				return NotFound();
			if (!MayManage(actor) && !await _classes.IsInstructorAsync(DepartmentId, sessionId, UserId))
				return NotFound();
			var session = await _classes.GetSessionAsync(DepartmentId, sessionId);
			if (session == null)
				return NotFound();
			var code = _classes.CurrentCode(session, DateTime.UtcNow);
			Response.Headers["Cache-Control"] = "no-store";
			return Ok(Populate(new LmsClassCodeResult
			{
				Open = code != null,
				Code = code,
				SignInUrl = code == null ? null : $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/LmsClasses/SignIn?s={session.LmsClassSessionId}&c={code}",
				SecondsPerCode = LmsCrewCodes.SliceSeconds
			}, 1));
		}

		[HttpPost("MarkAttendance")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsClassRosterResult>> MarkAttendance([FromBody] LmsClassAttendanceInput input, CancellationToken cancellationToken)
		{
			var actor = await ActorAsync();
			if (actor == null || input == null)
				return NotFound();
			if (!MayManage(actor) && !await _classes.IsInstructorAsync(DepartmentId, input.ClassSessionId, UserId))
				return NotFound();
			if (!Enum.TryParse<LmsClassAttendanceStates>(input.Attendance, true, out var state) || !Enum.IsDefined(typeof(LmsClassAttendanceStates), state))
				return Ok(Populate(new LmsClassRosterResult { Error = "lms_class_rules_invalid" }, 0, ResponseHelper.Failure));
			var detail = await _classes.GetDetailAsync(DepartmentId, input.ClassSessionId, DateTime.UtcNow);
			if (detail == null || detail.Roster.All(r => r.Enrollment.LmsClassEnrollmentId != input.EnrollmentId))
				return NotFound();

			var result = new LmsClassRosterResult();
			return await ActAsync(result, () => _classes.MarkAttendanceAsync(DepartmentId, input.EnrollmentId, state, input.Hours, UserId, DateTime.UtcNow, cancellationToken));
		}

		[HttpPost("SignOff")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsClassSignOffApiResult>> SignOff([FromBody] LmsClassSessionRef input, CancellationToken cancellationToken)
		{
			var actor = await ActorAsync();
			if (actor == null || input == null)
				return NotFound();
			if (!MayManage(actor) && !await _classes.IsInstructorAsync(DepartmentId, input.ClassSessionId, UserId))
				return NotFound();
			var result = new LmsClassSignOffApiResult();
			return await ActAsync(result, async () =>
			{
				var outcome = await _classes.SignOffAsync(DepartmentId, input.ClassSessionId, UserId, DateTime.UtcNow, cancellationToken);
				result.Attended = outcome.Attended;
				result.Absent = outcome.Absent;
				result.RecordsWritten = outcome.Written;
				result.RecordsVoided = outcome.Voided;
			});
		}

		/// <summary>
		/// Round 7B (permission 135): the roster's record panel: what the class completes (its LMS course or baseline training) and, per
		/// attendee, whether a completion can be recorded, who is ticked by default (attended) and what was recorded already.
		/// </summary>
		[HttpGet("GetRecordCompletions")]
		[Authorize(Policy = ResgridResources.Training_RecordCompletions)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsClassRecordingResult>> GetRecordCompletions(int sessionId)
		{
			var actor = await ActorAsync();
			if (actor == null)
				return NotFound();
			var view = await _recordings.GetClassRecordingViewAsync(DepartmentId, sessionId, UserId);
			if (view == null || !view.CanRecord)
				return NotFound();
			var result = new LmsClassRecordingResult
			{
				Data = new LmsClassRecordingData
				{
					ClassSessionId = view.Session.LmsClassSessionId,
					TargetKind = view.Target?.ToString(),
					TargetId = view.TargetId,
					TargetName = view.TargetName,
					Refusal = view.Refusal,
					AttestationVersion = view.AttestationVersion,
					Rows = view.Rows.Select(r => new LmsClassRecordingRowData
					{
						UserId = r.UserId,
						Name = r.Name,
						IsExternalLearner = r.IsExternal,
						Attended = r.Attended,
						Selected = r.Selected,
						Refusal = r.Refusal,
						RecordingId = r.Recording?.TrainingCompletionRecordingId,
						RecordedByUserId = r.Recording?.RecordedByUserId,
						RecordedOn = r.Recording?.RecordedOn
					}).ToList()
				}
			};
			return Ok(Populate(result, result.Data.Rows.Count));
		}

		/// <summary>
		/// Round 7B (permission 135): record that the listed roster members completed the class's LMS course or baseline training (the
		/// attestation is required). Each member is checked on their own (group scope, active member, seat, already complete); the result says
		/// who was recorded and why the others were not.
		/// </summary>
		[HttpPost("RecordCompletions")]
		[Authorize(Policy = ResgridResources.Training_RecordCompletions)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsClassRecordCompletionsResult>> RecordCompletions([FromBody] LmsClassRecordCompletionsInput input, CancellationToken cancellationToken)
		{
			var actor = await ActorAsync();
			if (actor == null || input == null)
				return NotFound();
			var outcome = await _recordings.RecordFromClassAsync(DepartmentId, input.ClassSessionId, new Resgrid.Model.TrainingCompletionRecordingRequest
			{
				RecorderUserId = UserId,
				MemberUserIds = input.UserIds ?? new List<string>(),
				Note = input.Note,
				Attested = input.Attested,
				AttestationVersion = string.IsNullOrWhiteSpace(input.AttestationVersion) ? Resgrid.Model.TrainingCompletionAttestation.CurrentVersion : input.AttestationVersion
			}, cancellationToken);
			var result = new LmsClassRecordCompletionsResult
			{
				Error = outcome.Error,
				Recorded = outcome.Recorded,
				Refused = outcome.Refused,
				Results = outcome.Results.Select(r => new LmsRecordedCompletionData
				{
					UserId = r.UserId,
					Recorded = r.Recorded,
					Refusal = r.Refusal,
					RecordingId = r.Recording?.TrainingCompletionRecordingId,
					LearningRecordId = r.Recording?.LearningRecordId
				}).ToList()
			};
			return Ok(Populate(result, result.Results.Count, outcome.Succeeded ? ResponseHelper.Success : ResponseHelper.Failure));
		}

		/// <summary>Round 7B (permission 135): voids a recorded completion with a reason (the learning record and its credits are voided).</summary>
		[HttpPost("VoidRecordedCompletion")]
		[Authorize(Policy = ResgridResources.Training_RecordCompletions)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsClassRecordCompletionsResult>> VoidRecordedCompletion([FromBody] LmsVoidRecordedCompletionInput input, CancellationToken cancellationToken)
		{
			var actor = await ActorAsync();
			if (actor == null || input == null)
				return NotFound();
			var voided = await _recorded.VoidAsync(DepartmentId, input.RecordingId, UserId, input.Reason, cancellationToken);
			if (voided.Refusal == Resgrid.Model.TrainingCompletionRecordingRules.NotFound)
				return NotFound();
			var result = new LmsClassRecordCompletionsResult
			{
				Error = voided.Refusal,
				Results = new List<LmsRecordedCompletionData>
				{
					new LmsRecordedCompletionData { UserId = voided.UserId, Recorded = false, Refusal = voided.Refusal, RecordingId = input.RecordingId }
				}
			};
			return Ok(Populate(result, 1, voided.Refusal == null ? ResponseHelper.Success : ResponseHelper.Failure));
		}
	}
}
