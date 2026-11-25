using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Academy
{
	/// <summary>One Resgrid Academy lesson or walkthrough (academy.resgrid.com).</summary>
	public sealed class AcademyLesson
	{
		public AcademyLesson(string id, string topic, double minutes, bool isWalkthrough)
		{
			Id = id;
			Topic = topic;
			Minutes = minutes;
			IsWalkthrough = isWalkthrough;
		}

		/// <summary>The Academy's own id, e.g. "05-calls". Stable: the docs embed it as &lt;AcademyVideo id="..."/&gt;.</summary>
		public string Id { get; }

		/// <summary>English topic name as the Academy lists it.</summary>
		public string Topic { get; }

		public double Minutes { get; }

		/// <summary>A walkthrough has its own page under /deep-dive/lessons/{id}/; a lesson opens on the home page.</summary>
		public bool IsWalkthrough { get; }
	}

	/// <summary>
	/// Academy phase A0 (protocols-training-knowledge-plan §15): a contextual "Learn" link per module page, keyed by Academy
	/// lesson id. No tracking (A1 makes the Academy an assignable course set). The ids mirror the docs repository's
	/// <c>src/components/AcademyVideo/catalog.js</c>; when the Academy adds a lesson, add it in both places.
	/// </summary>
	public static class AcademyCatalog
	{
		public static readonly IReadOnlyList<AcademyLesson> Lessons = new List<AcademyLesson>
		{
			new AcademyLesson("00-start", "Orientation", 3.0, false),
			new AcademyLesson("01-people", "People", 2.6, false),
			new AcademyLesson("02-security", "Security", 3.7, false),
			new AcademyLesson("03-units", "Units", 2.4, false),
			new AcademyLesson("04-apps", "Apps", 3.0, false),
			new AcademyLesson("05-calls", "Calls", 4.3, false),
			new AcademyLesson("06-communication", "Communication", 3.6, false),
			new AcademyLesson("07-mapping", "Mapping", 3.6, false),
			new AcademyLesson("08-shifts", "Shifts", 2.5, false),
			new AcademyLesson("09-training", "Training", 2.7, false),
			new AcademyLesson("10-knowledge", "Knowledge", 4.6, false),
			new AcademyLesson("11-checklists", "Checklists", 2.3, false),
			new AcademyLesson("12-maintenance", "Maintenance", 4.1, false),
			new AcademyLesson("13-inventory", "Inventory", 3.0, false),
			new AcademyLesson("14-records", "Records", 9.4, false),
			new AcademyLesson("15-deployments", "Deployments", 3.2, false),
			new AcademyLesson("16-business", "Business", 8.9, false),
			new AcademyLesson("17-automation", "Automation", 2.6, false),
			new AcademyLesson("18-ptt", "Push-to-talk", 1.9, false),
			new AcademyLesson("19-adp", "Data protection", 2.7, false),
			new AcademyLesson("20-plans", "Plans", 1.7, false),
			new AcademyLesson("21-ai", "AI", 2.4, false),
			new AcademyLesson("22-relay", "Relay", 1.2, false),
			new AcademyLesson("23-self-hosting", "Self-hosting", 1.5, false),
			new AcademyLesson("01-setup-wizard", "Setup wizard", 7.9, true),
			new AcademyLesson("02-department-settings", "Department settings", 6.7, true),
			new AcademyLesson("03-custom-statuses", "Custom statuses", 7.1, true),
			new AcademyLesson("06-dispatch-settings", "Dispatch settings", 7.7, true),
			new AcademyLesson("12-chat", "Chat", 5.9, true),
			new AcademyLesson("13-assistant", "Assistant", 6.0, true),
			new AcademyLesson("14-admin-assist", "Admin Assist", 6.4, true),
		};

		private static readonly Dictionary<string, AcademyLesson> ById =
			Lessons.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

		/// <summary>User-area controller name (without "Controller") to the lesson that teaches that module.</summary>
		public static readonly IReadOnlyDictionary<string, string> ControllerLessons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["Home"] = "00-start",
			["Dashboard"] = "00-start",
			["Personnel"] = "01-people",
			["Groups"] = "01-people",
			["Security"] = "02-security",
			["AccountSecurity"] = "02-security",
			["TwoFactor"] = "02-security",
			["Passkeys"] = "02-security",
			["ProtectedWorkflows"] = "02-security",
			["Department"] = "02-department-settings",
			["CustomStatuses"] = "03-custom-statuses",
			["Units"] = "03-units",
			["UnitTracking"] = "03-units",
			["Dispatch"] = "05-calls",
			["DispatcherDesk"] = "05-calls",
			["RecurringCalls"] = "05-calls",
			["RunCards"] = "05-calls",
			["Templates"] = "05-calls",
			["IntakeChannels"] = "05-calls",
			["IntakeConversations"] = "05-calls",
			["CadUpdates"] = "05-calls",
			["DestinationRules"] = "05-calls",
			["Messages"] = "06-communication",
			["Notifications"] = "06-communication",
			["DistributionLists"] = "06-communication",
			["CommunicationTest"] = "06-communication",
			["MessagingAccounts"] = "06-communication",
			["Mapping"] = "07-mapping",
			["CustomMaps"] = "07-mapping",
			["IndoorMaps"] = "07-mapping",
			["EventLayers"] = "07-mapping",
			["MapVisualizations"] = "07-mapping",
			["Routes"] = "07-mapping",
			["RoadClosures"] = "07-mapping",
			["Shifts"] = "08-shifts",
			["Workshifts"] = "08-shifts",
			["Trainings"] = "09-training",
			["Certifications"] = "09-training",
			["Protocols"] = "10-knowledge",
			["Documents"] = "10-knowledge",
			["Notes"] = "10-knowledge",
			["Files"] = "10-knowledge",
			["Checklists"] = "11-checklists",
			["WorkOrders"] = "12-maintenance",
			["Inventory"] = "13-inventory",
			["Records"] = "14-records",
			["RecordsAnalytics"] = "14-records",
			["RecordsQuality"] = "14-records",
			["RecordDefinitions"] = "14-records",
			["RecordInspections"] = "14-records",
			["RecordInvestigations"] = "14-records",
			["RecordOccupancies"] = "14-records",
			["RecordPermits"] = "14-records",
			["RecordHydrants"] = "14-records",
			["IncidentReports"] = "14-records",
			["IncidentAnalysis"] = "14-records",
			["Logs"] = "14-records",
			["Disclosures"] = "14-records",
			["Deployments"] = "15-deployments",
			["DeploymentOrders"] = "15-deployments",
			["DeploymentWizard"] = "15-deployments",
			["RecordDeployments"] = "15-deployments",
			["IncidentBackOffice"] = "15-deployments",
			["Invoicing"] = "16-business",
			["Bids"] = "16-business",
			["Contracts"] = "16-business",
			["Contacts"] = "16-business",
			["RateSchedules"] = "16-business",
			["Workforce"] = "16-business",
			["CalOesMars"] = "16-business",
			["Workflows"] = "17-automation",
			["Voice"] = "18-ptt",
			["DataProtection"] = "19-adp",
			["Subscription"] = "20-plans",
			["AiDispatch"] = "21-ai",
			["AiDestinations"] = "21-ai",
			["AdminAssist"] = "14-admin-assist",
			["Chat"] = "12-chat",
			["Moderation"] = "12-chat",
		};

		public static AcademyLesson Find(string lessonId)
		{
			if (string.IsNullOrWhiteSpace(lessonId))
				return null;

			return ById.TryGetValue(lessonId.Trim(), out var lesson) ? lesson : null;
		}

		/// <summary>The lesson for a page: an explicit lesson id wins (a page can name a closer walkthrough), then the controller.</summary>
		public static AcademyLesson ForPage(string controller, string explicitLessonId = null)
		{
			var explicitLesson = Find(explicitLessonId);
			if (explicitLesson != null)
				return explicitLesson;

			if (!string.IsNullOrWhiteSpace(controller) && ControllerLessons.TryGetValue(controller, out var id))
				return Find(id);

			return null;
		}

		/// <summary>Absolute link to the lesson on the Academy (self-hosted installs can point AcademyUrl at a mirror).</summary>
		public static string LessonUrl(string academyUrl, AcademyLesson lesson)
		{
			var root = HomeUrl(academyUrl);
			if (lesson == null || root == null)
				return root;

			var id = Uri.EscapeDataString(lesson.Id);
			return lesson.IsWalkthrough ? root + "deep-dive/lessons/" + id + "/" : root + "#lesson=" + id;
		}

		/// <summary>The Academy home with a trailing slash, or null when it is switched off (empty setting).</summary>
		public static string HomeUrl(string academyUrl)
		{
			if (string.IsNullOrWhiteSpace(academyUrl))
				return null;

			var root = academyUrl.Trim();
			return root.EndsWith("/", StringComparison.Ordinal) ? root : root + "/";
		}
	}
}
