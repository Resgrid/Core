using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Lms;

namespace Resgrid.Web.Areas.User.Models.LmsClasses
{
	/// <summary>What every Trainings → Classes page knows (Training T4 part 1, LMS add-on).</summary>
	public abstract class LmsClassPageView
	{
		/// <summary>The add-on is active: new sessions, enrolments, plans and resources may be made (lapse rule 3).</summary>
		public bool CanAuthor { get; set; }
		public bool IsLapsed { get; set; }
		/// <summary>Schedules and runs classes: department admin, Manage Courses (132) or Assign Training (126).</summary>
		public bool CanManage { get; set; }
		/// <summary>Approves and publishes lesson plans: department admin or Manage Courses (132).</summary>
		public bool CanApprove { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }
		public Department Department { get; set; }
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public List<TrainingCategory> Categories { get; set; } = new List<TrainingCategory>();
		public Func<DateTime, DateTime> Local { get; set; } = d => d;

		public string NameOf(string userId) => userId != null && Names.TryGetValue(userId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : userId;

		public string CategoryName(int? categoryId) => categoryId.HasValue ? Categories.Find(c => c.TrainingCategoryId == categoryId.Value)?.Name : null;
	}

	public sealed class LmsClassesIndexView : LmsClassPageView
	{
		public List<LmsClassSession> Upcoming { get; set; } = new List<LmsClassSession>();
		public List<LmsClassSession> Recent { get; set; } = new List<LmsClassSession>();
		public Dictionary<int, List<LmsClassEnrollment>> Rosters { get; set; } = new Dictionary<int, List<LmsClassEnrollment>>();
		public List<string> Tags { get; set; } = new List<string>();
		public string Tag { get; set; }
		public int PendingApprovals { get; set; }
		public int Waitlisted { get; set; }

		public int Seats(int sessionId) => Rosters.TryGetValue(sessionId, out var r) ? LmsClassRules.SeatsTaken(r) : 0;
		public int Count(int sessionId, LmsClassEnrollmentStatuses status) => Rosters.TryGetValue(sessionId, out var r) ? r.Count(e => e.Status == (int)status) : 0;
	}

	public sealed class LmsMyClassesView : LmsClassPageView
	{
		public List<LmsMyClass> Classes { get; set; } = new List<LmsMyClass>();
		public List<LmsClassInstructor> Taught { get; set; } = new List<LmsClassInstructor>();
		public Dictionary<int, LmsClassSession> TaughtSessions { get; set; } = new Dictionary<int, LmsClassSession>();
		public DateTime UtcNow { get; set; }
	}

	public sealed class LmsClassEditView : LmsClassPageView
	{
		public LmsClassSession Session { get; set; }
		public LmsClassSessionInput Input { get; set; } = new LmsClassSessionInput();
		public List<KeyValuePair<int, string>> Courses { get; set; } = new List<KeyValuePair<int, string>>();
		public List<KeyValuePair<int, string>> Trainings { get; set; } = new List<KeyValuePair<int, string>>();
		public List<LmsLessonPlan> LessonPlans { get; set; } = new List<LmsLessonPlan>();
		public List<DepartmentGroup> Groups { get; set; } = new List<DepartmentGroup>();
		public List<KeyValuePair<string, string>> Members { get; set; } = new List<KeyValuePair<string, string>>();
		public bool IsNew => Session == null;
	}

	public sealed class LmsClassSessionView : LmsClassPageView
	{
		public LmsClassSessionDetail Detail { get; set; }
		/// <summary>May run the roster: a class manager or one of the session's instructors.</summary>
		public bool CanRun { get; set; }
		public LmsClassEnrollment Mine { get; set; }
		public string EnrollRefusal { get; set; }
		public List<KeyValuePair<string, string>> Members { get; set; } = new List<KeyValuePair<string, string>>();
		public List<LmsTrainingResource> Resources { get; set; } = new List<LmsTrainingResource>();
		public List<LmsClassSession> Series { get; set; } = new List<LmsClassSession>();
		public LmsAttendanceImportResult Import { get; set; }
		public bool TimecardAvailable { get; set; }
		public DateTime UtcNow { get; set; }
		public LmsClassSession Session => Detail?.Session;
	}

	public sealed class LmsClassKioskView : LmsClassPageView
	{
		public LmsClassSession Session { get; set; }
		public bool Open { get; set; }
		public string QrDataUrl { get; set; }
		public string SignInUrl { get; set; }
		public List<string> Present { get; set; } = new List<string>();
	}

	public sealed class LmsClassSignInView : LmsClassPageView
	{
		public LmsClassSession Session { get; set; }
		public string Code { get; set; }
		public bool Open { get; set; }
		public LmsClassEnrollment Mine { get; set; }
	}

	public sealed class LmsClassSeriesView : LmsClassPageView
	{
		public List<LmsClassSession> Sessions { get; set; } = new List<LmsClassSession>();
		public List<LmsClassEnrollment> Roster { get; set; } = new List<LmsClassEnrollment>();
	}

	public sealed class LmsLessonPlansView : LmsClassPageView
	{
		public List<LmsLessonPlan> Plans { get; set; } = new List<LmsLessonPlan>();
		public IReadOnlyList<LmsLessonPlanStarter> Starters { get; set; } = new List<LmsLessonPlanStarter>();
	}

	public sealed class LmsLessonPlanView : LmsClassPageView
	{
		public LmsLessonPlan Plan { get; set; }
		public List<LmsLessonPlanVersion> Versions { get; set; } = new List<LmsLessonPlanVersion>();
		public LmsLessonPlanVersion Version { get; set; }
		public LmsLessonPlanContent Content { get; set; } = new LmsLessonPlanContent();
		public List<LmsCompetencyFramework> Frameworks { get; set; } = new List<LmsCompetencyFramework>();
		public Dictionary<int, LmsCompetencyItem> CompetencyItems { get; set; } = new Dictionary<int, LmsCompetencyItem>();
		public bool IsDraft => Version != null && Version.Status == (int)ProtocolVersionStatuses.Draft;
	}

	public sealed class LmsTrainingResourcesView : LmsClassPageView
	{
		public List<LmsTrainingResource> Resources { get; set; } = new List<LmsTrainingResource>();
		public Dictionary<int, List<LmsClassResourceBooking>> Bookings { get; set; } = new Dictionary<int, List<LmsClassResourceBooking>>();
		public Dictionary<int, LmsClassSession> Sessions { get; set; } = new Dictionary<int, LmsClassSession>();
		public LmsTrainingResource Editing { get; set; }
	}

	public sealed class LmsClassCostsView : LmsClassPageView
	{
		public int Year { get; set; }
		public LmsClassCostGroupings GroupBy { get; set; }
		public LmsClassCostRollup Rollup { get; set; } = new LmsClassCostRollup();
	}
}
