using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	// Training T4 part 1 (round 5B): the class reports registered in the round-4C report catalog (ILmsReportSource), so they can be saved,
	// put in folders and scheduled on worker 94. Names come from the Trainings resources (the catalog's), headings from LmsClasses.

	internal static class LmsClassReportData
	{
		/// <summary>The sessions that started in the report's period (and category).</summary>
		public static async Task<List<LmsClassSession>> SessionsAsync(ILmsClassService classes, LmsReportRequest request) =>
			(await classes.GetSessionsAsync(request.DepartmentId, request.FromUtc, request.ToUtc) ?? new List<LmsClassSession>())
				.Where(s => s.StartsOn >= request.FromUtc && s.StartsOn < request.ToUtc &&
					(!request.Parameters.CategoryId.HasValue || s.CategoryId == request.Parameters.CategoryId.Value))
				.OrderBy(s => s.StartsOn).ThenBy(s => s.LmsClassSessionId).ToList();

		public static string Local(DateTime utc, LmsReportRequest request) =>
			(request.Department == null ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc).TimeConverter(request.Department)).ToString("g", request.Culture);

		public static string H(string key, LmsReportRequest request) => LmsClassText.Get(key, request.Culture);

		public static async Task<Dictionary<string, string>> NamesAsync(IDepartmentsService departments, int departmentId) =>
			LmsReportCells.Names(await departments.GetAllPersonnelNamesForDepartmentAsync(departmentId));
	}

	/// <summary>The OSHA-grade class roster (§13 T4 item 1): dates, content summary, trainers and qualifications, attendees and job titles.</summary>
	public class LmsClassRosterReportSource : ILmsReportSource
	{
		private readonly ILmsClassService _classes;

		public LmsClassRosterReportSource(ILmsClassService classes) { _classes = classes; }

		public LmsReportDescriptor Descriptor { get; } = new LmsReportDescriptor
		{
			Key = "class-roster", NameKey = "Cls.Report.Roster", DescriptionKey = "Cls.Report.RosterHelp", Tier = LmsReportTiers.Lms, UsesPeriod = true, UsesCategory = true
		};

		public async Task<LmsReportTable> BuildAsync(LmsReportRequest request, Func<string, string> text)
		{
			var sessions = (await LmsClassReportData.SessionsAsync(_classes, request)).Where(s => s.Status == (int)LmsClassSessionStatuses.SignedOff).ToList();
			var ids = sessions.Select(s => s.LmsClassSessionId).ToList();
			var roster = (await _classes.GetEnrollmentsAsync(request.DepartmentId, ids)).GroupBy(e => e.LmsClassSessionId).ToDictionary(g => g.Key, g => g.ToList());
			var instructors = (await _classes.GetInstructorsAsync(request.DepartmentId, ids)).GroupBy(i => i.LmsClassSessionId).ToDictionary(g => g.Key, g => g.ToList());
			var table = new LmsReportTable
			{
				Title = text(Descriptor.NameKey),
				Columns = { LmsClassReportData.H("Col.Date", request), LmsClassReportData.H("Col.Class", request), LmsClassReportData.H("Col.ContentSummary", request),
					LmsClassReportData.H("Col.Instructors", request), LmsClassReportData.H("Col.Attendee", request), LmsClassReportData.H("Col.JobTitle", request),
					LmsClassReportData.H("Col.Attendance", request), LmsClassReportData.H("Col.Hours", request) }
			};

			foreach (var session in sessions)
			{
				var trainers = string.Join("; ", (instructors.TryGetValue(session.LmsClassSessionId, out var list) ? list : new List<LmsClassInstructor>())
					.Select(i => string.IsNullOrWhiteSpace(i.Qualifications) ? i.Name : i.Name + " (" + i.Qualifications + ")"));
				var when = LmsClassReportData.Local(session.StartsOn, request) + " – " + LmsClassReportData.Local(session.EndsOn, request);
				foreach (var e in (roster.TryGetValue(session.LmsClassSessionId, out var rows) ? rows : new List<LmsClassEnrollment>())
					.Where(e => e.Status == (int)LmsClassEnrollmentStatuses.Enrolled && (e.Attendance == (int)LmsClassAttendanceStates.Attended || e.Attendance == (int)LmsClassAttendanceStates.Absent))
					.OrderBy(e => e.MemberName ?? e.UserId, StringComparer.CurrentCultureIgnoreCase))
					table.Rows.Add(new List<string>
					{
						when, session.Title, session.ContentSummary ?? string.Empty, trainers, e.MemberName ?? e.UserId, e.JobTitle ?? string.Empty,
						LmsClassReportData.H("Attendance." + (LmsClassAttendanceStates)e.Attendance, request),
						e.Attendance == (int)LmsClassAttendanceStates.Attended ? LmsReportCells.Hours(e.Hours ?? 0m, request.Culture) : string.Empty
					});
			}

			table.Footer.Add(string.Format(request.Culture, LmsClassReportData.H("Footer.Sessions", request), sessions.Count, table.Rows.Count));
			return table;
		}
	}

	/// <summary>Enrolment per session (§13a T4): capacity, enrolled, waitlisted, pending, attended, absent and walk-ins.</summary>
	public class LmsClassEnrollmentReportSource : ILmsReportSource
	{
		private readonly ILmsClassService _classes;

		public LmsClassEnrollmentReportSource(ILmsClassService classes) { _classes = classes; }

		public LmsReportDescriptor Descriptor { get; } = new LmsReportDescriptor
		{
			Key = "class-enrollment", NameKey = "Cls.Report.Enrollment", DescriptionKey = "Cls.Report.EnrollmentHelp", Tier = LmsReportTiers.Lms, UsesPeriod = true, UsesCategory = true
		};

		public async Task<LmsReportTable> BuildAsync(LmsReportRequest request, Func<string, string> text)
		{
			var sessions = await LmsClassReportData.SessionsAsync(_classes, request);
			var roster = (await _classes.GetEnrollmentsAsync(request.DepartmentId, sessions.Select(s => s.LmsClassSessionId))).GroupBy(e => e.LmsClassSessionId)
				.ToDictionary(g => g.Key, g => g.ToList());
			var table = new LmsReportTable
			{
				Title = text(Descriptor.NameKey),
				Columns = { LmsClassReportData.H("Col.Date", request), LmsClassReportData.H("Col.Class", request), LmsClassReportData.H("Col.Status", request),
					LmsClassReportData.H("Col.Capacity", request), LmsClassReportData.H("Col.Enrolled", request), LmsClassReportData.H("Col.Waitlisted", request),
					LmsClassReportData.H("Col.Pending", request), LmsClassReportData.H("Col.Attended", request), LmsClassReportData.H("Col.Absent", request),
					LmsClassReportData.H("Col.WalkIns", request) }
			};

			foreach (var session in sessions)
			{
				var rows = roster.TryGetValue(session.LmsClassSessionId, out var list) ? list : new List<LmsClassEnrollment>();
				var enrolled = rows.Where(e => e.Status == (int)LmsClassEnrollmentStatuses.Enrolled).ToList();
				table.Rows.Add(new List<string>
				{
					LmsClassReportData.Local(session.StartsOn, request), session.Title, LmsClassReportData.H("Status." + (LmsClassSessionStatuses)session.Status, request),
					session.Capacity > 0 ? session.Capacity.ToString(request.Culture) : "∞", enrolled.Count.ToString(request.Culture),
					rows.Count(e => e.Status == (int)LmsClassEnrollmentStatuses.Waitlisted).ToString(request.Culture),
					rows.Count(e => e.Status == (int)LmsClassEnrollmentStatuses.PendingApproval).ToString(request.Culture),
					enrolled.Count(e => e.Attendance == (int)LmsClassAttendanceStates.Attended).ToString(request.Culture),
					enrolled.Count(e => e.Attendance == (int)LmsClassAttendanceStates.Absent).ToString(request.Culture),
					enrolled.Count(e => e.IsWalkIn).ToString(request.Culture)
				});
			}

			table.Footer.Add(string.Format(request.Culture, LmsClassReportData.H("Footer.Sessions", request), sessions.Count, roster.Values.Sum(r => r.Count(e => e.IsOnRoster))));
			return table;
		}
	}

	/// <summary>Enrolment exceptions (§13a T4): no-shows, members still waitlisted or awaiting approval, inactive members on a roster, and enrolled members on shift.</summary>
	public class LmsClassExceptionReportSource : ILmsReportSource
	{
		private readonly ILmsClassService _classes;
		private readonly IDepartmentsService _departments;

		public LmsClassExceptionReportSource(ILmsClassService classes, IDepartmentsService departments)
		{
			_classes = classes;
			_departments = departments;
		}

		public LmsReportDescriptor Descriptor { get; } = new LmsReportDescriptor
		{
			Key = "class-exceptions", NameKey = "Cls.Report.Exceptions", DescriptionKey = "Cls.Report.ExceptionsHelp", Tier = LmsReportTiers.Lms, UsesPeriod = true, UsesCategory = true
		};

		public async Task<LmsReportTable> BuildAsync(LmsReportRequest request, Func<string, string> text)
		{
			var sessions = (await LmsClassReportData.SessionsAsync(_classes, request)).Where(s => s.Status != (int)LmsClassSessionStatuses.Cancelled).ToList();
			var roster = (await _classes.GetEnrollmentsAsync(request.DepartmentId, sessions.Select(s => s.LmsClassSessionId))).GroupBy(e => e.LmsClassSessionId)
				.ToDictionary(g => g.Key, g => g.ToList());
			var names = await LmsClassReportData.NamesAsync(_departments, request.DepartmentId);
			var active = await _departments.GetActiveMemberUserIdsAsync(request.DepartmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var table = new LmsReportTable
			{
				Title = text(Descriptor.NameKey),
				Columns = { LmsClassReportData.H("Col.Date", request), LmsClassReportData.H("Col.Class", request), LmsClassReportData.H("Col.Member", request),
					LmsClassReportData.H("Col.Exception", request) }
			};

			string Name(string id) => names.TryGetValue(id ?? string.Empty, out var n) ? n : id;
			void Add(LmsClassSession s, string userId, string key) =>
				table.Rows.Add(new List<string> { LmsClassReportData.Local(s.StartsOn, request), s.Title, Name(userId), LmsClassReportData.H(key, request) });

			foreach (var session in sessions)
			{
				var rows = roster.TryGetValue(session.LmsClassSessionId, out var list) ? list : new List<LmsClassEnrollment>();
				var past = session.StartsOn <= request.UtcNow;
				HashSet<string> onShift = null;
				if (!past)
				{
					try
					{
						onShift = await _classes.GetShiftConflictsAsync(request.DepartmentId, session, rows.Where(e => e.Status == (int)LmsClassEnrollmentStatuses.Enrolled).Select(e => e.UserId));
					}
					catch (Exception ex)
					{
						Framework.Logging.LogException(ex, "Class shift conflicts could not be read for the exception report.");
					}
				}

				foreach (var e in rows.OrderBy(r => Name(r.UserId), StringComparer.CurrentCultureIgnoreCase))
				{
					if (e.IsOnRoster && !active.Contains(e.UserId ?? string.Empty))
						Add(session, e.UserId, "Exception.Inactive");
					if (e.Status == (int)LmsClassEnrollmentStatuses.Enrolled && e.Attendance == (int)LmsClassAttendanceStates.Absent)
						Add(session, e.UserId, "Exception.NoShow");
					else if (e.Status == (int)LmsClassEnrollmentStatuses.Enrolled && e.Attendance == (int)LmsClassAttendanceStates.Excused)
						Add(session, e.UserId, "Exception.Excused");
					else if (e.Status == (int)LmsClassEnrollmentStatuses.Waitlisted)
						Add(session, e.UserId, past ? "Exception.NeverPromoted" : "Exception.Waitlisted");
					else if (e.Status == (int)LmsClassEnrollmentStatuses.PendingApproval)
						Add(session, e.UserId, "Exception.Pending");
					if (onShift != null && e.Status == (int)LmsClassEnrollmentStatuses.Enrolled && onShift.Contains(e.UserId ?? string.Empty))
						Add(session, e.UserId, "Exception.OnShift");
				}
			}

			table.Footer.Add(string.Format(request.Culture, LmsClassReportData.H("Footer.Exceptions", request), table.Rows.Count));
			return table;
		}
	}

	/// <summary>Series reporting (§13a T4): each member's attendance across the sessions of a recurring class.</summary>
	public class LmsClassSeriesReportSource : ILmsReportSource
	{
		private readonly ILmsClassService _classes;
		private readonly IDepartmentsService _departments;

		public LmsClassSeriesReportSource(ILmsClassService classes, IDepartmentsService departments)
		{
			_classes = classes;
			_departments = departments;
		}

		public LmsReportDescriptor Descriptor { get; } = new LmsReportDescriptor
		{
			Key = "class-series", NameKey = "Cls.Report.Series", DescriptionKey = "Cls.Report.SeriesHelp", Tier = LmsReportTiers.Lms, UsesPeriod = true, UsesCategory = true
		};

		public async Task<LmsReportTable> BuildAsync(LmsReportRequest request, Func<string, string> text)
		{
			var inPeriod = (await LmsClassReportData.SessionsAsync(_classes, request)).Where(s => !string.IsNullOrWhiteSpace(s.SeriesKey)).ToList();
			var names = await LmsClassReportData.NamesAsync(_departments, request.DepartmentId);
			var table = new LmsReportTable
			{
				Title = text(Descriptor.NameKey),
				Columns = { LmsClassReportData.H("Col.Series", request), LmsClassReportData.H("Col.Member", request), LmsClassReportData.H("Col.Sessions", request),
					LmsClassReportData.H("Col.Attended", request), LmsClassReportData.H("Col.AttendanceRate", request), LmsClassReportData.H("Col.Hours", request) }
			};

			foreach (var series in inPeriod.GroupBy(s => s.SeriesKey, StringComparer.OrdinalIgnoreCase))
			{
				var sessions = (await _classes.GetSeriesAsync(request.DepartmentId, series.Key)).Where(s => s.Status != (int)LmsClassSessionStatuses.Cancelled).ToList();
				if (sessions.Count == 0)
					continue;
				var title = sessions.OrderBy(s => s.SeriesIndex).First().Title;
				var held = sessions.Count(s => s.StartsOn <= request.UtcNow);
				var roster = await _classes.GetEnrollmentsAsync(request.DepartmentId, sessions.Select(s => s.LmsClassSessionId));
				foreach (var member in roster.Where(e => e.Status == (int)LmsClassEnrollmentStatuses.Enrolled).GroupBy(e => e.UserId, StringComparer.OrdinalIgnoreCase)
					.OrderBy(g => names.TryGetValue(g.Key, out var n) ? n : g.Key, StringComparer.CurrentCultureIgnoreCase))
				{
					var attended = member.Count(e => e.Attendance == (int)LmsClassAttendanceStates.Attended);
					table.Rows.Add(new List<string>
					{
						title, names.TryGetValue(member.Key, out var name) ? name : member.Key, held.ToString(request.Culture), attended.ToString(request.Culture),
						held == 0 ? string.Empty : ((decimal)attended / held).ToString("P0", request.Culture), LmsReportCells.Hours(member.Sum(e => e.Hours ?? 0m), request.Culture)
					});
				}
			}

			return table;
		}
	}

	/// <summary>Class costs (R-C10) per course, member or year, beside the training-request costs of the same grouping (round 4C).</summary>
	public class LmsClassCostReportSource : ILmsReportSource
	{
		private readonly ILmsClassService _classes;
		private readonly IDepartmentsService _departments;

		public LmsClassCostReportSource(ILmsClassService classes, IDepartmentsService departments)
		{
			_classes = classes;
			_departments = departments;
		}

		public LmsReportDescriptor Descriptor { get; } = new LmsReportDescriptor
		{
			Key = "class-costs", NameKey = "Cls.Report.Costs", DescriptionKey = "Cls.Report.CostsHelp", Tier = LmsReportTiers.Lms, UsesPeriod = true,
			Groupings = new Dictionary<int, string> { { 0, "Prg.GroupBy.Course" }, { 1, "Prg.GroupBy.Member" }, { 2, "Prg.GroupBy.Year" } }
		};

		public async Task<LmsReportTable> BuildAsync(LmsReportRequest request, Func<string, string> text)
		{
			var groupBy = Enum.IsDefined(typeof(LmsClassCostGroupings), request.Parameters.GroupBy) ? (LmsClassCostGroupings)request.Parameters.GroupBy : LmsClassCostGroupings.Course;
			var rollup = await _classes.GetCostRollupAsync(request.DepartmentId, request.FromUtc, request.ToUtc, groupBy);
			var names = groupBy == LmsClassCostGroupings.Member ? await LmsClassReportData.NamesAsync(_departments, request.DepartmentId) : new Dictionary<string, string>();
			var table = new LmsReportTable
			{
				Title = text(Descriptor.NameKey),
				Columns = { LmsClassReportData.H(groupBy == LmsClassCostGroupings.Member ? "Col.Member" : groupBy == LmsClassCostGroupings.Year ? "Col.Year" : "Col.Course", request),
					LmsClassReportData.H("Col.Sessions", request), LmsClassReportData.H("Col.Attendees", request), LmsClassReportData.H("Col.Hours", request),
					LmsClassReportData.H("Col.ClassCost", request), LmsClassReportData.H("Col.RequestCost", request) }
			};
			foreach (var row in rollup.Rows)
				table.Rows.Add(new List<string>
				{
					groupBy == LmsClassCostGroupings.Member && names.TryGetValue(row.Key, out var name) ? name : row.Key, row.Sessions.ToString(request.Culture),
					row.Attendees.ToString(request.Culture), LmsReportCells.Hours(row.Hours, request.Culture), LmsReportCells.Money(row.ClassCost, request.Culture),
					LmsReportCells.Money(row.RequestCost, request.Culture)
				});
			table.Footer.Add(string.Format(request.Culture, LmsClassReportData.H("Footer.Costs", request), LmsReportCells.Money(rollup.ClassTotal, request.Culture),
				LmsReportCells.Money(rollup.RequestTotal, request.Culture), rollup.Currency));
			return table;
		}
	}

	/// <summary>Instructor records (§13 T4 item 4): who taught what and the hours taught in the period.</summary>
	public class LmsInstructorHoursReportSource : ILmsReportSource
	{
		private readonly ILmsClassService _classes;

		public LmsInstructorHoursReportSource(ILmsClassService classes) { _classes = classes; }

		public LmsReportDescriptor Descriptor { get; } = new LmsReportDescriptor
		{
			Key = "instructor-hours", NameKey = "Cls.Report.Instructors", DescriptionKey = "Cls.Report.InstructorsHelp", Tier = LmsReportTiers.Lms, UsesPeriod = true, UsesCategory = true
		};

		public async Task<LmsReportTable> BuildAsync(LmsReportRequest request, Func<string, string> text)
		{
			var sessions = (await LmsClassReportData.SessionsAsync(_classes, request)).Where(s => s.Status != (int)LmsClassSessionStatuses.Cancelled).ToDictionary(s => s.LmsClassSessionId);
			var instructors = await _classes.GetInstructorsAsync(request.DepartmentId, sessions.Keys);
			var table = new LmsReportTable
			{
				Title = text(Descriptor.NameKey),
				Columns = { LmsClassReportData.H("Col.Instructor", request), LmsClassReportData.H("Col.Qualifications", request), LmsClassReportData.H("Col.Sessions", request),
					LmsClassReportData.H("Col.HoursTaught", request), LmsClassReportData.H("Col.Classes", request) }
			};

			decimal Taught(LmsClassInstructor i) => i.HoursTaught ?? (sessions.TryGetValue(i.LmsClassSessionId, out var s) && s.EndsOn > s.StartsOn
				? Math.Round((decimal)(s.EndsOn - s.StartsOn).TotalHours, 2) : 0m);

			foreach (var person in instructors.GroupBy(i => string.IsNullOrWhiteSpace(i.UserId) ? "name:" + (i.Name ?? string.Empty).Trim().ToUpperInvariant() : i.UserId.Trim().ToUpperInvariant())
				.OrderBy(g => g.First().Name, StringComparer.CurrentCultureIgnoreCase))
				table.Rows.Add(new List<string>
				{
					person.First().Name ?? person.First().UserId, person.Select(i => i.Qualifications).FirstOrDefault(q => !string.IsNullOrWhiteSpace(q)) ?? string.Empty,
					person.Select(i => i.LmsClassSessionId).Distinct().Count().ToString(request.Culture), LmsReportCells.Hours(person.Sum(Taught), request.Culture),
					string.Join("; ", person.Select(i => sessions.TryGetValue(i.LmsClassSessionId, out var s) ? s.Title : null).Where(t => t != null).Distinct().Take(10))
				});
			return table;
		}
	}
}
