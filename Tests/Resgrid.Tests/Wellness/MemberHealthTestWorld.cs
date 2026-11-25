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
	/// An in-memory department for the WL1 tests: an admin with no wellness role, a wellness officer, a designated officer, a group admin
	/// supervising the member's group, plain members and a hidden member; the case, injury, duty period and clearance rows the repository
	/// mocks serve; and recordings of what the service wrote, sent and audited.
	/// </summary>
	internal sealed class MemberHealthTestWorld
	{
		public const int DepartmentId = 7;
		public const string Admin = "admin-user";
		public const string WellnessOfficer = "wellness-user";
		public const string Designated = "designated-user";
		public const string GroupAdmin = "group-admin";
		public const string Member = "member-user";
		public const string Other = "other-user";
		public const string OutsideGroup = "outside-user";
		public const string Hidden = "hidden-user";
		public const int WellnessRoleId = 41;
		public const int DesignatedRoleId = 42;
		public const int BbpTypeId = 70;
		public const int ClearanceTypeId = 71;
		public const int GroupId = 5;

		public readonly List<RmsExposureIncident> Incidents = new List<RmsExposureIncident>();
		public readonly List<RmsMemberInjury> Injuries = new List<RmsMemberInjury>();
		public readonly List<RmsInjuryDutyPeriod> Periods = new List<RmsInjuryDutyPeriod>();
		public readonly List<RmsMedicalClearance> Clearances = new List<RmsMedicalClearance>();
		public readonly List<RmsResponderExposure> Exposures = new List<RmsResponderExposure>();
		public readonly List<RmsRecordWorkAssignment> Assignments = new List<RmsRecordWorkAssignment>();
		public readonly List<RmsRecordLegalHold> Holds = new List<RmsRecordLegalHold>();
		public readonly List<PersonnelCertification> Certifications = new List<PersonnelCertification>();
		public readonly List<AuditEvent> Audits = new List<AuditEvent>();
		public readonly List<(string UserId, string Title, string Message, string EventCode)> Sent = new List<(string, string, string, string)>();
		public readonly Dictionary<int, Call> Calls = new Dictionary<int, Call>();
		public WellnessSettings Settings = new WellnessSettings
		{
			WellnessOfficerRoleId = WellnessRoleId,
			DesignatedOfficerRoleId = DesignatedRoleId,
			BbpTrainingCertificationTypeId = BbpTypeId,
			MedicalClearanceCertificationTypeId = ClearanceTypeId
		};

		public readonly Mock<IRmsExposureIncidentsRepository> IncidentsRepository = new Mock<IRmsExposureIncidentsRepository>();
		public readonly Mock<IRmsMemberInjuriesRepository> InjuriesRepository = new Mock<IRmsMemberInjuriesRepository>();
		public readonly Mock<IRmsInjuryDutyPeriodsRepository> PeriodsRepository = new Mock<IRmsInjuryDutyPeriodsRepository>();
		public readonly Mock<IRmsMedicalClearancesRepository> ClearancesRepository = new Mock<IRmsMedicalClearancesRepository>();
		public readonly Mock<IRmsResponderExposuresRepository> ExposuresRepository = new Mock<IRmsResponderExposuresRepository>();
		public readonly Mock<IRmsRecordWorkAssignmentsRepository> AssignmentsRepository = new Mock<IRmsRecordWorkAssignmentsRepository>();
		public readonly Mock<IRmsRecordLegalHoldsRepository> HoldsRepository = new Mock<IRmsRecordLegalHoldsRepository>();
		public readonly Mock<IMemberWellnessService> Wellness = new Mock<IMemberWellnessService>();
		public readonly Mock<IDepartmentsService> Departments = new Mock<IDepartmentsService>();
		public readonly Mock<IDepartmentSettingsService> SettingsService = new Mock<IDepartmentSettingsService>();
		public readonly Mock<IPersonnelRolesService> Roles = new Mock<IPersonnelRolesService>();
		public readonly Mock<IDepartmentGroupsService> Groups = new Mock<IDepartmentGroupsService>();
		public readonly Mock<ICertificationService> CertificationService = new Mock<ICertificationService>();
		public readonly Mock<ICallsService> CallsService = new Mock<ICallsService>();
		public readonly Mock<ICommunicationService> Communication = new Mock<ICommunicationService>();
		public readonly Mock<IProtectedWriteService> ProtectedWrite = new Mock<IProtectedWriteService>();
		public readonly Mock<IProtectedReadService> ProtectedRead = new Mock<IProtectedReadService>();
		public readonly Mock<IProtectedGrantContext> Grant = new Mock<IProtectedGrantContext>();
		public readonly Mock<IEventAggregator> Events = new Mock<IEventAggregator>();

		private static readonly Dictionary<string, int[]> RoleMembership = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
		{
			[WellnessOfficer] = new[] { WellnessRoleId },
			[Designated] = new[] { DesignatedRoleId }
		};

		public MemberHealthTestWorld()
		{
			var members = new Dictionary<string, DepartmentMember>(StringComparer.OrdinalIgnoreCase)
			{
				[Admin] = new DepartmentMember { DepartmentId = DepartmentId, UserId = Admin, IsAdmin = true },
				[WellnessOfficer] = new DepartmentMember { DepartmentId = DepartmentId, UserId = WellnessOfficer },
				[Designated] = new DepartmentMember { DepartmentId = DepartmentId, UserId = Designated },
				[GroupAdmin] = new DepartmentMember { DepartmentId = DepartmentId, UserId = GroupAdmin },
				[Member] = new DepartmentMember { DepartmentId = DepartmentId, UserId = Member },
				[Other] = new DepartmentMember { DepartmentId = DepartmentId, UserId = Other },
				[OutsideGroup] = new DepartmentMember { DepartmentId = DepartmentId, UserId = OutsideGroup },
				[Hidden] = new DepartmentMember { DepartmentId = DepartmentId, UserId = Hidden, IsHidden = true }
			};
			Departments.Setup(d => d.GetDepartmentMemberAsync(It.IsAny<string>(), DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync((string userId, int _, bool _) => userId != null && members.TryGetValue(userId, out var m) ? m : null);
			Departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(() => new Department { DepartmentId = DepartmentId, Name = "Station 9", TimeZone = "Etc/UTC", ManagingUserId = "owner" });
			Departments.Setup(d => d.GetActiveMemberUserIdsAsync(DepartmentId))
				.ReturnsAsync(() => new HashSet<string>(members.Values.Where(m => m.IsHidden != true).Select(m => m.UserId), StringComparer.OrdinalIgnoreCase));
			Departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(DepartmentId))
				.ReturnsAsync(() => new List<PersonName>
				{
					new PersonName { UserId = Member, FirstName = "Mia", LastName = "Member" },
					new PersonName { UserId = Hidden, FirstName = "Hal", LastName = "Hidden" }
				});

			Wellness.Setup(w => w.GetSettingsAsync(DepartmentId)).ReturnsAsync(() => Settings.Normalized());
			Wellness.Setup(w => w.IsDepartmentAdminAsync(DepartmentId, It.IsAny<string>()))
				.ReturnsAsync((int _, string userId) => string.Equals(userId, Admin, StringComparison.OrdinalIgnoreCase));
			SettingsService.Setup(s => s.GetTextToCallNumberForDepartmentAsync(It.IsAny<int>())).ReturnsAsync((string)null);

			Roles.Setup(r => r.GetRolesForUserAsync(It.IsAny<string>(), DepartmentId))
				.ReturnsAsync((string userId, int _) => (RoleMembership.TryGetValue(userId ?? string.Empty, out var ids) ? ids : Array.Empty<int>())
					.Select(id => new PersonnelRole { PersonnelRoleId = id, DepartmentId = DepartmentId }).ToList());
			Roles.Setup(r => r.GetAllMembersOfRoleAsync(It.IsAny<int>()))
				.ReturnsAsync((int roleId) => RoleMembership.Where(p => p.Value.Contains(roleId)).Select(p => new PersonnelRoleUser { UserId = p.Key, PersonnelRoleId = roleId }).ToList());

			Groups.Setup(g => g.GetGroupMemberForUserAsync(It.IsAny<string>(), DepartmentId))
				.ReturnsAsync((string userId, int _) => string.Equals(userId, GroupAdmin, StringComparison.OrdinalIgnoreCase)
					? new DepartmentGroupMember { DepartmentGroupId = GroupId, UserId = GroupAdmin, IsAdmin = true }
					: new DepartmentGroupMember { DepartmentGroupId = string.Equals(userId, OutsideGroup, StringComparison.OrdinalIgnoreCase) ? 6 : GroupId, UserId = userId, IsAdmin = false });
			Groups.Setup(g => g.GetGroupIdsForAllUsersInDepartmentAsync(DepartmentId))
				.ReturnsAsync(() => members.Keys.ToDictionary(k => k, k => string.Equals(k, OutsideGroup, StringComparison.OrdinalIgnoreCase) ? 6 : GroupId, StringComparer.OrdinalIgnoreCase));

			CertificationService.Setup(c => c.GetCertificationTypeByIdAsync(BbpTypeId))
				.ReturnsAsync(new DepartmentCertificationType { DepartmentCertificationTypeId = BbpTypeId, DepartmentId = DepartmentId, Code = "BBP", Type = "Bloodborne Pathogens", DefaultValidityMonths = 12 });
			CertificationService.Setup(c => c.GetCertificationTypeByIdAsync(ClearanceTypeId))
				.ReturnsAsync(new DepartmentCertificationType { DepartmentCertificationTypeId = ClearanceTypeId, DepartmentId = DepartmentId, Code = "NFPA-1582", Type = "Medical Clearance (NFPA 1582)", DefaultValidityMonths = 12 });
			CertificationService.Setup(c => c.GetCertificationsByUserIdAsync(It.IsAny<string>()))
				.ReturnsAsync((string userId) => Certifications.Where(c => string.Equals(c.UserId, userId, StringComparison.OrdinalIgnoreCase)).ToList());
			CertificationService.Setup(c => c.SaveCertificationAsync(It.IsAny<PersonnelCertification>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((PersonnelCertification c, CancellationToken _) => { c.PersonnelCertificationId = 1000 + Certifications.Count; Certifications.Add(c); return c; });

			CallsService.Setup(c => c.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync((int callId, bool _) => Calls.TryGetValue(callId, out var call) ? call : null);

			Communication.Setup(c => c.SendNotificationAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(),
					It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>(), It.IsAny<string>()))
				.Callback((string userId, int _, string message, string _, Department _, string title, UserProfile _, bool _, string code) => Sent.Add((userId, title, message, code)))
				.ReturnsAsync(true);

			// Department not enrolled in Advanced Data Protection: values pass through as plaintext.
			SetupPassThrough<RmsExposureIncident>();
			SetupPassThrough<RmsMemberInjury>();
			SetupPassThrough<RmsMedicalClearance>();
			SetupPassThrough<RmsResponderExposure>();

			Events.Setup(e => e.SendMessage(It.IsAny<AuditEvent>())).Callback((AuditEvent e) => Audits.Add(e));

			IncidentsRepository.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Incidents.FirstOrDefault(i => i.DepartmentId == d && i.RmsExposureIncidentId == id));
			IncidentsRepository.Setup(r => r.GetForUserAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string u) => Incidents.Where(i => i.DepartmentId == d && string.Equals(i.UserId, u, StringComparison.OrdinalIgnoreCase)).ToList());
			IncidentsRepository.Setup(r => r.GetForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync((int d, bool all) => Incidents.Where(i => i.DepartmentId == d && (all || i.IsOpen)).Select(Copy).ToList());
			IncidentsRepository.Setup(r => r.GetForExposureAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string e) => Incidents.Where(i => i.DepartmentId == d && i.ResponderExposureId == e).ToList());
			IncidentsRepository.Setup(r => r.InsertAsync(It.IsAny<RmsExposureIncident>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsExposureIncident i, CancellationToken _, bool _) => { Incidents.Add(i); return i; });
			IncidentsRepository.Setup(r => r.UpdateAsync(It.IsAny<RmsExposureIncident>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsExposureIncident i, CancellationToken _, bool _) => i);
			IncidentsRepository.Setup(r => r.GetOverdueNotificationsAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync((DateTime now, int _) => Incidents.Where(i => i.Stage == (int)ExposureIncidentStages.NotificationRequested && i.NotificationDueOn <= now && !i.NotificationOverdueNoticeOn.HasValue).ToList());
			IncidentsRepository.Setup(r => r.ClaimOverdueNoticeAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, DateTime on, CancellationToken _) =>
				{
					var row = Incidents.FirstOrDefault(i => i.DepartmentId == d && i.RmsExposureIncidentId == id && !i.NotificationOverdueNoticeOn.HasValue);
					if (row == null) return false;
					row.NotificationOverdueNoticeOn = on;
					return true;
				});
			IncidentsRepository.Setup(r => r.GetDepartmentsWithCasesBeforeAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync((DateTime cutoff, int _) => Incidents.Where(i => i.OccurredOn < cutoff).Select(i => i.DepartmentId).Distinct().ToList());
			IncidentsRepository.Setup(r => r.GetBeforeAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync((int d, DateTime cutoff, int _) => Incidents.Where(i => i.DepartmentId == d && i.OccurredOn < cutoff).ToList());
			IncidentsRepository.Setup(r => r.DeleteForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, CancellationToken _) => Incidents.RemoveAll(i => i.DepartmentId == d && i.RmsExposureIncidentId == id));

			InjuriesRepository.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Injuries.FirstOrDefault(i => i.DepartmentId == d && i.RmsMemberInjuryId == id));
			InjuriesRepository.Setup(r => r.GetForUserAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string u) => Injuries.Where(i => i.DepartmentId == d && string.Equals(i.UserId, u, StringComparison.OrdinalIgnoreCase)).ToList());
			InjuriesRepository.Setup(r => r.GetOpenForDepartmentAsync(It.IsAny<int>()))
				.ReturnsAsync((int d) => Injuries.Where(i => i.DepartmentId == d && i.IsOpen).Select(Copy).ToList());
			InjuriesRepository.Setup(r => r.GetForYearAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int y) => Injuries.Where(i => i.DepartmentId == d && i.OshaYear == y).ToList());
			InjuriesRepository.Setup(r => r.GetMaxCaseNumberAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int y) => Injuries.Where(i => i.DepartmentId == d && i.OshaYear == y).Select(i => i.OshaCaseNumber).DefaultIfEmpty(0).Max());
			InjuriesRepository.Setup(r => r.InsertAsync(It.IsAny<RmsMemberInjury>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsMemberInjury i, CancellationToken _, bool _) => { Injuries.Add(i); return i; });
			InjuriesRepository.Setup(r => r.UpdateAsync(It.IsAny<RmsMemberInjury>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsMemberInjury i, CancellationToken _, bool _) => i);
			InjuriesRepository.Setup(r => r.GetDueReturnReviewsAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync((DateTime now, int _) => Injuries.Where(i => i.IsOpen && i.ExpectedReturnOn <= now && (!i.ReturnReviewNoticeOn.HasValue || i.ReturnReviewNoticeOn < i.ExpectedReturnOn)).ToList());
			InjuriesRepository.Setup(r => r.ClaimReturnReviewAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, DateTime? expected, DateTime on, CancellationToken _) =>
				{
					var row = Injuries.FirstOrDefault(i => i.DepartmentId == d && i.RmsMemberInjuryId == id && i.ReturnReviewNoticeOn == expected);
					if (row == null) return false;
					row.ReturnReviewNoticeOn = on;
					return true;
				});
			InjuriesRepository.Setup(r => r.GetDepartmentsWithInjuriesBeforeYearAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int year, int _) => Injuries.Where(i => i.OshaYear < year).Select(i => i.DepartmentId).Distinct().ToList());
			InjuriesRepository.Setup(r => r.GetBeforeYearAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int year, int _) => Injuries.Where(i => i.DepartmentId == d && i.OshaYear < year).ToList());
			InjuriesRepository.Setup(r => r.DeleteForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, CancellationToken _) => Injuries.RemoveAll(i => i.DepartmentId == d && i.RmsMemberInjuryId == id));

			PeriodsRepository.Setup(r => r.GetForInjuryAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Periods.Where(p => p.DepartmentId == d && p.InjuryId == id).OrderBy(p => p.StartsOn).ToList());
			PeriodsRepository.Setup(r => r.GetForInjuriesAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>()))
				.ReturnsAsync((int d, IEnumerable<string> ids) => Periods.Where(p => p.DepartmentId == d && ids.Contains(p.InjuryId)).ToList());
			PeriodsRepository.Setup(r => r.InsertAsync(It.IsAny<RmsInjuryDutyPeriod>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsInjuryDutyPeriod p, CancellationToken _, bool _) => { Periods.Add(p); return p; });
			PeriodsRepository.Setup(r => r.UpdateAsync(It.IsAny<RmsInjuryDutyPeriod>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsInjuryDutyPeriod p, CancellationToken _, bool _) => p);
			PeriodsRepository.Setup(r => r.DeleteForInjuryAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, CancellationToken _) => Periods.RemoveAll(p => p.DepartmentId == d && p.InjuryId == id));

			ClearancesRepository.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Clearances.FirstOrDefault(c => c.DepartmentId == d && c.RmsMedicalClearanceId == id));
			ClearancesRepository.Setup(r => r.GetForUserAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string u) => Clearances.Where(c => c.DepartmentId == d && string.Equals(c.UserId, u, StringComparison.OrdinalIgnoreCase)).ToList());
			ClearancesRepository.Setup(r => r.GetActiveForDepartmentAsync(It.IsAny<int>()))
				.ReturnsAsync((int d) => Clearances.Where(c => c.DepartmentId == d && !c.IsVoided).Select(Copy).ToList());
			ClearancesRepository.Setup(r => r.InsertAsync(It.IsAny<RmsMedicalClearance>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsMedicalClearance c, CancellationToken _, bool _) => { Clearances.Add(c); return c; });
			ClearancesRepository.Setup(r => r.UpdateAsync(It.IsAny<RmsMedicalClearance>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsMedicalClearance c, CancellationToken _, bool _) => c);
			ClearancesRepository.Setup(r => r.GetDepartmentsWithClearancesBeforeAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync((DateTime cutoff, int _) => Clearances.Where(c => c.EvaluatedOn < cutoff).Select(c => c.DepartmentId).Distinct().ToList());
			ClearancesRepository.Setup(r => r.GetBeforeAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync((int d, DateTime cutoff, int _) => Clearances.Where(c => c.DepartmentId == d && c.EvaluatedOn < cutoff).ToList());
			ClearancesRepository.Setup(r => r.DeleteForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, CancellationToken _) => Clearances.RemoveAll(c => c.DepartmentId == d && c.RmsMedicalClearanceId == id));

			ExposuresRepository.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Exposures.FirstOrDefault(e => e.DepartmentId == d && e.RmsResponderExposureId == id));
			ExposuresRepository.Setup(r => r.InsertAsync(It.IsAny<RmsResponderExposure>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsResponderExposure e, CancellationToken _, bool _) => { Exposures.Add(e); return e; });

			AssignmentsRepository.Setup(r => r.GetForRecordAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string recordId) => Assignments.Where(a => a.DepartmentId == d && a.RecordId == recordId).ToList());
			AssignmentsRepository.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Assignments.FirstOrDefault(a => a.DepartmentId == d && a.RmsRecordWorkAssignmentId == id));
			AssignmentsRepository.Setup(r => r.InsertAsync(It.IsAny<RmsRecordWorkAssignment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordWorkAssignment a, CancellationToken _, bool _) => { Assignments.Add(a); return a; });
			AssignmentsRepository.Setup(r => r.UpdateAsync(It.IsAny<RmsRecordWorkAssignment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordWorkAssignment a, CancellationToken _, bool _) => a);
			AssignmentsRepository.Setup(r => r.DeleteAsync(It.IsAny<RmsRecordWorkAssignment>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((RmsRecordWorkAssignment a, CancellationToken _) => Assignments.Remove(a));

			HoldsRepository.Setup(r => r.GetActiveForDepartmentAsync(It.IsAny<int>()))
				.ReturnsAsync((int d) => Holds.Where(h => h.DepartmentId == d && h.IsActive).ToList());
		}

		/// <summary>A list query returns fresh rows, as the database does: the service may blank them without touching the stored ones.</summary>
		private static T Copy<T>(T row) where T : class =>
			(T)typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(row, null);

		private void SetupPassThrough<T>() where T : class
		{
			ProtectedWrite.Setup(w => w.PrepareRecordsEntityWriteAsync(It.IsAny<int>(), It.IsAny<T>(), It.IsAny<T>(), It.IsAny<string>(),
					It.IsAny<IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)>>(), It.IsAny<Action>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedWriteResult { Success = true });
			ProtectedRead.Setup(r => r.ResolveRecordsEntitiesForReadAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<(T Entity, string RowKey)>>(),
					It.IsAny<IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)>>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedReadResult());
		}

		public MemberHealthService Service() => new MemberHealthService(IncidentsRepository.Object, InjuriesRepository.Object, PeriodsRepository.Object,
			ClearancesRepository.Object, ExposuresRepository.Object, AssignmentsRepository.Object, HoldsRepository.Object, Wellness.Object, Departments.Object,
			SettingsService.Object, Roles.Object, Groups.Object, new Lazy<ICertificationService>(() => CertificationService.Object), CallsService.Object,
			new Lazy<ICommunicationService>(() => Communication.Object), new Lazy<IProtectedWriteService>(() => ProtectedWrite.Object),
			new Lazy<IProtectedReadService>(() => ProtectedRead.Object), Grant.Object, Events.Object);

		public RmsResponderExposure AddExposure(string userId, ResponderExposureCategories category, ResponderExposureStatuses status, DateTime occurredOn, int? callId = null)
		{
			var row = new RmsResponderExposure
			{
				RmsResponderExposureId = Guid.NewGuid().ToString(),
				DepartmentId = DepartmentId,
				UserId = userId,
				Category = (int)category,
				Status = (int)status,
				OccurredOn = occurredOn,
				CallId = callId,
				RowVersion = 1
			};
			Exposures.Add(row);
			return row;
		}

		public RmsExposureIncident AddIncident(string userId, ExposureIncidentStages stage, DateTime occurredOn)
		{
			var row = new RmsExposureIncident
			{
				RmsExposureIncidentId = Guid.NewGuid().ToString(),
				DepartmentId = DepartmentId,
				UserId = userId,
				Route = (int)ExposureIncidentRoutes.Percutaneous,
				OccurredOn = occurredOn,
				ReportedOn = occurredOn,
				Stage = (int)stage,
				RowVersion = 1
			};
			Incidents.Add(row);
			return row;
		}

		public RmsMemberInjury AddInjury(string userId, DateTime occurredOn, MemberDutyStatuses duty = MemberDutyStatuses.FullDuty, MemberInjuryStates state = MemberInjuryStates.Open,
			string restrictions = null)
		{
			var row = new RmsMemberInjury
			{
				RmsMemberInjuryId = Guid.NewGuid().ToString(),
				DepartmentId = DepartmentId,
				UserId = userId,
				OccurredOn = occurredOn,
				ReportedOn = occurredOn,
				OshaYear = occurredOn.Year,
				OshaCaseNumber = Injuries.Count(i => i.OshaYear == occurredOn.Year) + 1,
				State = (int)state,
				DutyStatus = (int)duty,
				DutyStatusSince = duty == MemberDutyStatuses.FullDuty ? (DateTime?)null : occurredOn.Date,
				Restrictions = restrictions,
				Description = "Fell from the tailboard",
				InjuryDetails = "Sprained left ankle",
				RowVersion = 1
			};
			Injuries.Add(row);
			return row;
		}
	}
}
