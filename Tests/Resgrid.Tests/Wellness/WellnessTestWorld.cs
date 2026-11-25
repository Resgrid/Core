using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Wellness;

namespace Resgrid.Tests.Wellness
{
	/// <summary>
	/// An in-memory department for the member wellness tests: members (an admin without the wellness role, an officer holding it, two
	/// plain members), the exposure rows the repository mock serves, and recordings of what the service wrote, sent and audited.
	/// </summary>
	internal sealed class WellnessTestWorld
	{
		public const int DepartmentId = 7;
		public const int OtherDepartmentId = 8;
		public const string Admin = "admin-user";
		public const string Officer = "officer-user";
		public const string Member = "member-user";
		public const string Peer = "peer-user";
		public const string Hidden = "hidden-user";
		public const int OfficerRoleId = 41;
		public const int PeerCertificationTypeId = 77;

		public readonly List<RmsResponderExposure> Exposures = new List<RmsResponderExposure>();
		public readonly List<RmsCallExposureFlag> Flags = new List<RmsCallExposureFlag>();
		public readonly List<RmsResponderScreeningFlag> ScreeningFlags = new List<RmsResponderScreeningFlag>();
		public readonly List<RmsRecordWorkAssignment> Assignments = new List<RmsRecordWorkAssignment>();
		public readonly List<RmsRecordLegalHold> Holds = new List<RmsRecordLegalHold>();
		public readonly List<AuditEvent> Audits = new List<AuditEvent>();
		public readonly List<(string UserId, string Title, string Message)> Sent = new List<(string, string, string)>();
		public readonly Dictionary<int, Call> Calls = new Dictionary<int, Call>();
		public readonly List<ActionLog> Statuses = new List<ActionLog>();
		public readonly List<UnitState> UnitStates = new List<UnitState>();
		public readonly List<CheckInRecord> CheckIns = new List<CheckInRecord>();
		public readonly List<string> PeerTeam = new List<string> { Peer };
		public WellnessSettings Settings = new WellnessSettings { WellnessOfficerRoleId = OfficerRoleId, PeerTeamCertificationTypeId = PeerCertificationTypeId };
		public string TimeZone = "Etc/UTC";
		public bool ClaimSucceeds = true;
		public Func<string, int, bool> CanEditCall = (userId, callId) => false;

		public readonly Mock<IRmsResponderExposuresRepository> ExposuresRepository = new Mock<IRmsResponderExposuresRepository>();
		public readonly Mock<IRmsCallExposureFlagsRepository> FlagsRepository = new Mock<IRmsCallExposureFlagsRepository>();
		public readonly Mock<IRmsResponderScreeningFlagsRepository> ScreeningRepository = new Mock<IRmsResponderScreeningFlagsRepository>();
		public readonly Mock<IRmsRecordWorkAssignmentsRepository> AssignmentsRepository = new Mock<IRmsRecordWorkAssignmentsRepository>();
		public readonly Mock<IRmsRecordLegalHoldsRepository> HoldsRepository = new Mock<IRmsRecordLegalHoldsRepository>();
		public readonly Mock<IDepartmentsService> Departments = new Mock<IDepartmentsService>();
		public readonly Mock<IDepartmentSettingsService> SettingsService = new Mock<IDepartmentSettingsService>();
		public readonly Mock<IPersonnelRolesService> Roles = new Mock<IPersonnelRolesService>();
		public readonly Mock<ICertificationService> Certifications = new Mock<ICertificationService>();
		public readonly Mock<ICallsService> CallsService = new Mock<ICallsService>();
		public readonly Mock<IAuthorizationService> Authorization = new Mock<IAuthorizationService>();
		public readonly Mock<IActionLogsService> ActionLogs = new Mock<IActionLogsService>();
		public readonly Mock<IUnitsService> Units = new Mock<IUnitsService>();
		public readonly Mock<ICheckInTimerService> CheckInService = new Mock<ICheckInTimerService>();
		public readonly Mock<ICommunicationService> Communication = new Mock<ICommunicationService>();
		public readonly Mock<IProtectedWriteService> ProtectedWrite = new Mock<IProtectedWriteService>();
		public readonly Mock<IProtectedReadService> ProtectedRead = new Mock<IProtectedReadService>();
		public readonly Mock<IProtectedGrantContext> Grant = new Mock<IProtectedGrantContext>();
		public readonly Mock<IEventAggregator> Events = new Mock<IEventAggregator>();

		public WellnessTestWorld()
		{
			var members = new Dictionary<string, DepartmentMember>(StringComparer.OrdinalIgnoreCase)
			{
				[Admin] = new DepartmentMember { DepartmentId = DepartmentId, UserId = Admin, IsAdmin = true },
				[Officer] = new DepartmentMember { DepartmentId = DepartmentId, UserId = Officer },
				[Member] = new DepartmentMember { DepartmentId = DepartmentId, UserId = Member },
				[Peer] = new DepartmentMember { DepartmentId = DepartmentId, UserId = Peer },
				[Hidden] = new DepartmentMember { DepartmentId = DepartmentId, UserId = Hidden, IsHidden = true }
			};
			Departments.Setup(d => d.GetDepartmentMemberAsync(It.IsAny<string>(), DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync((string userId, int _, bool _) => userId != null && members.TryGetValue(userId, out var m) ? m : null);
			Departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(() => new Department { DepartmentId = DepartmentId, Name = "Station", TimeZone = TimeZone, ManagingUserId = "owner" });
			Departments.Setup(d => d.GetActiveMemberUserIdsAsync(DepartmentId))
				.ReturnsAsync(() => new HashSet<string>(new[] { Admin, Officer, Member, Peer }, StringComparer.OrdinalIgnoreCase));
			Departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(DepartmentId))
				.ReturnsAsync(() => new List<PersonName> { new PersonName { UserId = Member, FirstName = "Mia", LastName = "Member" } });

			SettingsService.Setup(s => s.GetWellnessSettingsAsync(DepartmentId)).ReturnsAsync(() => Settings.Normalized());
			SettingsService.Setup(s => s.GetTextToCallNumberForDepartmentAsync(It.IsAny<int>())).ReturnsAsync((string)null);

			Roles.Setup(r => r.GetRolesForUserAsync(It.IsAny<string>(), DepartmentId))
				.ReturnsAsync((string userId, int _) => string.Equals(userId, Officer, StringComparison.OrdinalIgnoreCase)
					? new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = OfficerRoleId, DepartmentId = DepartmentId, Name = "Wellness" } }
					: new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 5, DepartmentId = DepartmentId, Name = "Engine" } });
			Roles.Setup(r => r.GetRolesForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = OfficerRoleId, DepartmentId = DepartmentId }, new PersonnelRole { PersonnelRoleId = 5, DepartmentId = DepartmentId } });

			Certifications.Setup(c => c.GetCertificationTypeByIdAsync(PeerCertificationTypeId))
				.ReturnsAsync(new DepartmentCertificationType { DepartmentCertificationTypeId = PeerCertificationTypeId, DepartmentId = DepartmentId, Code = "PEER", Type = "Peer support" });
			Certifications.Setup(c => c.GetUsersWithValidCertificationAsync(DepartmentId, It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
				.ReturnsAsync(() => PeerTeam.ToList());

			CallsService.Setup(c => c.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync((int callId, bool _) => Calls.TryGetValue(callId, out var call) ? call : null);
			Authorization.Setup(a => a.CanUserEditCallAsync(It.IsAny<string>(), It.IsAny<int>()))
				.ReturnsAsync((string userId, int callId) => CanEditCall(userId, callId));

			ActionLogs.Setup(a => a.GetActionLogsForCallAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(() => Statuses.ToList());
			Units.Setup(u => u.GetUnitStatesForCallAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(() => UnitStates.ToList());
			Units.Setup(u => u.GetRolesForUnitStateAsync(It.IsAny<int>())).ReturnsAsync(new List<UnitStateRole>());
			CheckInService.Setup(c => c.GetCheckInsForCallAsync(It.IsAny<int>())).ReturnsAsync(() => CheckIns.ToList());

			Communication.Setup(c => c.SendNotificationAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(),
					It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>()))
				.Callback((string userId, int _, string message, string _, Department _, string title, UserProfile _, bool _) => Sent.Add((userId, title, message)))
				.ReturnsAsync(true);

			// Department not enrolled in Advanced Data Protection: values pass through as plaintext.
			ProtectedWrite.Setup(w => w.PrepareRecordsEntityWriteAsync(It.IsAny<int>(), It.IsAny<RmsResponderExposure>(), It.IsAny<RmsResponderExposure>(), It.IsAny<string>(),
					It.IsAny<IReadOnlyDictionary<string, (Func<RmsResponderExposure, string> Get, Action<RmsResponderExposure, string> Set)>>(), It.IsAny<Action>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedWriteResult { Success = true });
			ProtectedWrite.Setup(w => w.PrepareRecordsEntityWriteAsync(It.IsAny<int>(), It.IsAny<RmsResponderScreeningFlag>(), It.IsAny<RmsResponderScreeningFlag>(), It.IsAny<string>(),
					It.IsAny<IReadOnlyDictionary<string, (Func<RmsResponderScreeningFlag, string> Get, Action<RmsResponderScreeningFlag, string> Set)>>(), It.IsAny<Action>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedWriteResult { Success = true });
			ProtectedRead.Setup(r => r.ResolveRecordsEntitiesForReadAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<(RmsResponderExposure Entity, string RowKey)>>(),
					It.IsAny<IReadOnlyDictionary<string, (Func<RmsResponderExposure, string> Get, Action<RmsResponderExposure, string> Set)>>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedReadResult());
			ProtectedRead.Setup(r => r.ResolveRecordsEntitiesForReadAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<(RmsResponderScreeningFlag Entity, string RowKey)>>(),
					It.IsAny<IReadOnlyDictionary<string, (Func<RmsResponderScreeningFlag, string> Get, Action<RmsResponderScreeningFlag, string> Set)>>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedReadResult());

			Events.Setup(e => e.SendMessage(It.IsAny<AuditEvent>())).Callback((AuditEvent e) => Audits.Add(e));

			// Repositories: rows live in the lists; each query filters the way its SQL does.
			ExposuresRepository.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Exposures.FirstOrDefault(e => e.DepartmentId == d && e.RmsResponderExposureId == id));
			ExposuresRepository.Setup(r => r.GetForUserAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string u) => Exposures.Where(e => e.DepartmentId == d && string.Equals(e.UserId, u, StringComparison.OrdinalIgnoreCase)).OrderByDescending(e => e.OccurredOn).ToList());
			ExposuresRepository.Setup(r => r.GetForDepartmentAsync(It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
				.ReturnsAsync((int d, DateTime? from, DateTime? to) => Exposures.Where(e => e.DepartmentId == d && (!from.HasValue || e.OccurredOn >= from) && (!to.HasValue || e.OccurredOn < to)).ToList());
			ExposuresRepository.Setup(r => r.GetForCallAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int c) => Exposures.Where(e => e.DepartmentId == d && e.CallId == c).ToList());
			ExposuresRepository.Setup(r => r.InsertAsync(It.IsAny<RmsResponderExposure>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsResponderExposure e, CancellationToken _, bool _) => { Exposures.Add(e); return e; });
			ExposuresRepository.Setup(r => r.UpdateAsync(It.IsAny<RmsResponderExposure>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsResponderExposure e, CancellationToken _, bool _) => e);
			ExposuresRepository.Setup(r => r.DeleteSuggestionsFromFlagAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string f, CancellationToken _) => Exposures.RemoveAll(e => e.DepartmentId == d && e.CallExposureFlagId == f && e.Status == (int)ResponderExposureStatuses.Suggested));
			ExposuresRepository.Setup(r => r.GetDepartmentsWithDueAnniversariesAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync((DateTime dueBy, int _) => Exposures.Where(e => IsDue(e, dueBy)).Select(e => e.DepartmentId).Distinct().ToList());
			ExposuresRepository.Setup(r => r.GetDueAnniversariesAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync((int d, DateTime dueBy, int _) => Exposures.Where(e => e.DepartmentId == d && IsDue(e, dueBy)).ToList());
			ExposuresRepository.Setup(r => r.ClaimAnniversaryAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, DateTime expected, DateTime next, DateTime sent, CancellationToken _) =>
				{
					var row = Exposures.FirstOrDefault(e => e.DepartmentId == d && e.RmsResponderExposureId == id && e.NextAnniversaryOn == expected);
					if (!ClaimSucceeds || row == null) return false;
					row.NextAnniversaryOn = next;
					row.LastReminderSentOn = sent;
					return true;
				});
			ExposuresRepository.Setup(r => r.GetDepartmentsWithEntriesBeforeAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync((DateTime cutoff, int _) => Exposures.Where(e => e.OccurredOn < cutoff).Select(e => e.DepartmentId).Distinct().ToList());
			ExposuresRepository.Setup(r => r.GetBeforeAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync((int d, DateTime cutoff, int _) => Exposures.Where(e => e.DepartmentId == d && e.OccurredOn < cutoff).ToList());
			ExposuresRepository.Setup(r => r.DeleteForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, CancellationToken _) => Exposures.RemoveAll(e => e.DepartmentId == d && e.RmsResponderExposureId == id));

			FlagsRepository.Setup(r => r.GetForCallAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int c) => Flags.Where(f => f.DepartmentId == d && f.CallId == c).ToList());
			FlagsRepository.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Flags.FirstOrDefault(f => f.DepartmentId == d && f.RmsCallExposureFlagId == id));
			FlagsRepository.Setup(r => r.InsertAsync(It.IsAny<RmsCallExposureFlag>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsCallExposureFlag f, CancellationToken _, bool _) => { Flags.Add(f); return f; });
			FlagsRepository.Setup(r => r.UpdateAsync(It.IsAny<RmsCallExposureFlag>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsCallExposureFlag f, CancellationToken _, bool _) => f);
			FlagsRepository.Setup(r => r.GetPendingAsync(It.IsAny<int>()))
				.ReturnsAsync(() => Flags.Where(f => !f.SuggestionsGeneratedOn.HasValue && !f.ClearedOn.HasValue).ToList());
			FlagsRepository.Setup(r => r.ClaimGenerationAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, DateTime on, CancellationToken _) =>
				{
					var flag = Flags.FirstOrDefault(f => f.DepartmentId == d && f.RmsCallExposureFlagId == id && !f.SuggestionsGeneratedOn.HasValue && !f.ClearedOn.HasValue);
					if (flag == null) return false;
					flag.SuggestionsGeneratedOn = on;
					return true;
				});
			FlagsRepository.Setup(r => r.SetSuggestionCountAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, int count, CancellationToken _) => { Flags.First(f => f.RmsCallExposureFlagId == id).SuggestionCount = count; return 1; });

			ScreeningRepository.Setup(r => r.GetActiveForUserAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string u) => ScreeningFlags.FirstOrDefault(f => f.DepartmentId == d && string.Equals(f.UserId, u, StringComparison.OrdinalIgnoreCase) && !f.ClearedOn.HasValue));
			ScreeningRepository.Setup(r => r.GetActiveForDepartmentAsync(It.IsAny<int>()))
				.ReturnsAsync((int d) => ScreeningFlags.Where(f => f.DepartmentId == d && !f.ClearedOn.HasValue).ToList());
			ScreeningRepository.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => ScreeningFlags.FirstOrDefault(f => f.DepartmentId == d && f.RmsResponderScreeningFlagId == id));
			ScreeningRepository.Setup(r => r.InsertAsync(It.IsAny<RmsResponderScreeningFlag>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsResponderScreeningFlag f, CancellationToken _, bool _) => { ScreeningFlags.Add(f); return f; });
			ScreeningRepository.Setup(r => r.UpdateAsync(It.IsAny<RmsResponderScreeningFlag>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsResponderScreeningFlag f, CancellationToken _, bool _) => f);

			AssignmentsRepository.Setup(r => r.GetForRecordAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string recordId) => Assignments.Where(a => a.DepartmentId == d && a.RecordId == recordId).ToList());
			AssignmentsRepository.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Assignments.FirstOrDefault(a => a.DepartmentId == d && a.RmsRecordWorkAssignmentId == id));
			AssignmentsRepository.Setup(r => r.InsertAsync(It.IsAny<RmsRecordWorkAssignment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordWorkAssignment a, CancellationToken _, bool _) => { Assignments.Add(a); return a; });
			AssignmentsRepository.Setup(r => r.UpdateAsync(It.IsAny<RmsRecordWorkAssignment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordWorkAssignment a, CancellationToken _, bool _) => a);

			HoldsRepository.Setup(r => r.GetActiveForDepartmentAsync(It.IsAny<int>()))
				.ReturnsAsync((int d) => Holds.Where(h => h.DepartmentId == d && h.IsActive).ToList());
		}

		private static bool IsDue(RmsResponderExposure e, DateTime dueBy) =>
			e.Status == (int)ResponderExposureStatuses.Confirmed && (e.RemindMember || e.RemindPeerTeam) && e.NextAnniversaryOn.HasValue && e.NextAnniversaryOn.Value <= dueBy;

		public MemberWellnessService Service() => new MemberWellnessService(ExposuresRepository.Object, FlagsRepository.Object, ScreeningRepository.Object,
			AssignmentsRepository.Object, HoldsRepository.Object, Departments.Object, SettingsService.Object, Roles.Object,
			new Lazy<ICertificationService>(() => Certifications.Object), CallsService.Object, new Lazy<IAuthorizationService>(() => Authorization.Object),
			new Lazy<IActionLogsService>(() => ActionLogs.Object), new Lazy<IUnitsService>(() => Units.Object), new Lazy<ICheckInTimerService>(() => CheckInService.Object),
			new Lazy<ICommunicationService>(() => Communication.Object), new Lazy<IProtectedWriteService>(() => ProtectedWrite.Object),
			new Lazy<IProtectedReadService>(() => ProtectedRead.Object), Grant.Object, Events.Object);

		public RmsResponderExposure AddExposure(string userId, ResponderExposureCategories category, ResponderExposureStatuses status, DateTime occurredOn, int departmentId = DepartmentId,
			string substance = "Diesel exhaust", int? callId = null)
		{
			var row = new RmsResponderExposure
			{
				RmsResponderExposureId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				UserId = userId,
				Category = (int)category,
				Status = (int)status,
				OccurredOn = occurredOn,
				CallId = callId,
				Substance = substance,
				HealthDetails = "Cough for two days",
				RowVersion = 1
			};
			Exposures.Add(row);
			return row;
		}

		public Call AddCall(int callId, CallStates state, DateTime loggedOn, int departmentId = DepartmentId)
		{
			var call = new Call { CallId = callId, DepartmentId = departmentId, State = (int)state, LoggedOn = loggedOn, Name = "Structure fire" };
			Calls[callId] = call;
			return call;
		}
	}
}
