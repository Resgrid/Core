using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.TrainingT6
{
	/// <summary>
	/// Round 9A test world: the real issuance engine and requirement-set service over in-memory certifications, ledger, sets, items, alert
	/// claims and completion facts, with the certification service, notifier and event bus mocked (their calls are recorded).
	/// </summary>
	public sealed class CertificationWorld
	{
		public const int Dept = 41;
		public const string Member = "cert-member";
		public const string Member2 = "cert-member-2";
		public const string Officer = "cert-officer";
		public const string Gone = "cert-gone";

		public readonly Department Department = new Department { DepartmentId = Dept, Name = "Harbor Works", TimeZone = "Eastern Standard Time" };
		public readonly Dictionary<int, DepartmentCertificationType> Types = new Dictionary<int, DepartmentCertificationType>();
		public readonly List<PersonnelCertification> Records = new List<PersonnelCertification>();
		public readonly List<LmsCertificationIssuance> Ledger = new List<LmsCertificationIssuance>();
		public readonly List<LmsCertificationRequirementSet> Sets = new List<LmsCertificationRequirementSet>();
		public readonly List<LmsCertificationRequirementItem> Items = new List<LmsCertificationRequirementItem>();
		public readonly List<LmsCertificationAlert> Alerts = new List<LmsCertificationAlert>();
		public readonly List<LmsRequirementFact> CourseFacts = new List<LmsRequirementFact>();
		public readonly List<LmsRequirementFact> TrainingFacts = new List<LmsRequirementFact>();
		public readonly List<LmsRequirementFact> HourFacts = new List<LmsRequirementFact>();
		public readonly List<LmsRequirementFact> EvaluationFacts = new List<LmsRequirementFact>();
		public readonly Dictionary<int, LmsEnrollment> Enrollments = new Dictionary<int, LmsEnrollment>();
		public readonly Dictionary<int, decimal> CreditHours = new Dictionary<int, decimal>();
		public readonly HashSet<string> Active = new HashSet<string>(new[] { Member, Member2, Officer }, StringComparer.OrdinalIgnoreCase);
		public readonly List<(List<string> To, string Title, string Message, object[] Args, string Code)> Notices = new List<(List<string>, string, string, object[], string)>();
		public readonly List<CertificationTrainingWorkflowEvent> WorkflowEvents = new List<CertificationTrainingWorkflowEvent>();
		public readonly List<AuditEvent> Audits = new List<AuditEvent>();

		public readonly Mock<ICertificationService> Certifications = new Mock<ICertificationService>();
		public readonly Mock<IDepartmentsService> Departments = new Mock<IDepartmentsService>();
		public readonly Mock<ILmsProgramNotifier> Notifier = new Mock<ILmsProgramNotifier>();
		public readonly Mock<IEventAggregator> Bus = new Mock<IEventAggregator>();
		public readonly Mock<ILmsAccessService> Access = new Mock<ILmsAccessService>();
		public readonly Mock<ITrainingService> Trainings = new Mock<ITrainingService>();
		public readonly Mock<ILmsCoursesRepository> Courses = new Mock<ILmsCoursesRepository>();
		public readonly Mock<ILearningRecordService> LearningRecords = new Mock<ILearningRecordService>();
		public readonly LmsCertificationIssuanceService Issuance;
		public readonly LmsCertificationRequirementService Requirements;
		public DateTime? ClaimedDay;

		public CertificationWorld()
		{
			Departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(Department);
			Departments.Setup(d => d.GetActiveMemberUserIdsAsync(Dept)).ReturnsAsync(() => new HashSet<string>(Active, StringComparer.OrdinalIgnoreCase));
			Departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(Dept)).ReturnsAsync(new List<PersonName>
			{
				new PersonName { UserId = Member, FirstName = "Morgan", LastName = "Member" }, new PersonName { UserId = Member2, FirstName = "Riley", LastName = "Second" }, new PersonName { UserId = Gone, FirstName = "Gale", LastName = "Gone" }
			});

			Certifications.Setup(c => c.GetCertificationTypeByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => Types.TryGetValue(id, out var type) ? type : null);
			Certifications.Setup(c => c.GetAllCertificationTypesByDepartmentAsync(Dept)).ReturnsAsync(() => Types.Values.ToList());
			Certifications.Setup(c => c.GetActiveCertificationTypesAsync(Dept, It.IsAny<CertificationAppliesTo?>())).ReturnsAsync(() => Types.Values.Where(t => t.IsActive && !t.IsDeleted).ToList());
			Certifications.Setup(c => c.GetCertificationsForDepartmentAsync(Dept, It.IsAny<IEnumerable<string>>()))
				.ReturnsAsync((int d, IEnumerable<string> users) => Records.Where(r => !r.IsDeleted && (users == null || users.Contains(r.UserId, StringComparer.OrdinalIgnoreCase))).ToList());
			Certifications.Setup(c => c.SaveCertificationAsync(It.IsAny<PersonnelCertification>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((PersonnelCertification c, CancellationToken _) => { c.PersonnelCertificationId = 500 + Records.Count; Records.Add(c); return c; });
			Certifications.Setup(c => c.RenewCertificationAsync(It.IsAny<int>(), Dept, It.IsAny<DateTime?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, int d, DateTime? expires, string number, string user, CancellationToken _) =>
				{
					var record = Records.Single(r => r.PersonnelCertificationId == id);
					record.ExpiresOn = expires;
					record.RecievedOn = DateTime.UtcNow.Date;
					if (record.Status == (int)PersonnelCertificationStatuses.Expired)
						record.Status = (int)PersonnelCertificationStatuses.Active;
					return record;
				});
			Certifications.Setup(c => c.RollBackCertificationAsync(It.IsAny<int>(), Dept, It.IsAny<PersonnelCertificationStatuses>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
					It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, int d, PersonnelCertificationStatuses status, DateTime? expires, DateTime? received, string reason, string user, CancellationToken _) =>
				{
					var record = Records.Single(r => r.PersonnelCertificationId == id);
					record.Status = (int)status;
					record.ExpiresOn = expires;
					record.RecievedOn = received;
					return record;
				});
			Certifications.Setup(c => c.RestoreCertificationStateAsync(It.IsAny<int>(), Dept, It.IsAny<PersonnelCertificationStatuses>(), It.IsAny<DateTime?>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, int d, PersonnelCertificationStatuses status, DateTime? expires, string reason, string user, CancellationToken _) =>
				{
					var record = Records.Single(r => r.PersonnelCertificationId == id);
					record.Status = (int)status;
					record.ExpiresOn = expires;
					return record;
				});
			Certifications.Setup(c => c.SoftDeleteCertificationAsync(It.IsAny<int>(), Dept, It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, int d, string user, CancellationToken _) => { Records.Single(r => r.PersonnelCertificationId == id).IsDeleted = true; return true; });
			Certifications.Setup(c => c.GetCertificationCreditTotalsAsync(It.IsAny<IEnumerable<int>>()))
				.ReturnsAsync((IEnumerable<int> ids) => (IReadOnlyDictionary<int, decimal>)ids.ToDictionary(i => i, i => CreditHours.TryGetValue(i, out var h) ? h : 0m));
			Certifications.Setup(c => c.GetCertificationCreditsAsync(It.IsAny<int>())).ReturnsAsync(new List<PersonnelCertificationCredit>());

			Notifier.Setup(n => n.SendAsync(Dept, It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<CultureInfo, object[]>>(), It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, IEnumerable<string> to, string title, string message, Func<CultureInfo, object[]> args, string code, CancellationToken _) =>
				{
					var list = to.ToList();
					Notices.Add((list, title, message, args?.Invoke(CultureInfo.GetCultureInfo("en")), code));
					return list.Count(Active.Contains);
				});
			Bus.Setup(b => b.SendMessage(It.IsAny<CertificationTrainingWorkflowEvent>())).Callback((CertificationTrainingWorkflowEvent e) => WorkflowEvents.Add(e));
			Bus.Setup(b => b.SendMessage(It.IsAny<AuditEvent>())).Callback((AuditEvent e) => Audits.Add(e));
			Access.Setup(a => a.CanAuthorAndManageAsync(Dept)).ReturnsAsync(true);

			var ledger = new Mock<ILmsCertificationIssuancesRepository>();
			ledger.Setup(l => l.InsertAsync(It.IsAny<LmsCertificationIssuance>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((LmsCertificationIssuance i, CancellationToken _, bool __) => { i.LmsCertificationIssuanceId = Ledger.Count + 1; Ledger.Add(i); return i; });
			ledger.Setup(l => l.GetLiveForUserAsync(Dept, It.IsAny<string>()))
				.ReturnsAsync((int d, string u) => Ledger.Where(i => i.IsLive && string.Equals(i.UserId, u, StringComparison.OrdinalIgnoreCase)).ToList());
			ledger.Setup(l => l.GetForCertificationAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int c) => Ledger.Where(i => i.PersonnelCertificationId == c).ToList());
			ledger.Setup(l => l.UpdateStateAsync(It.IsAny<LmsCertificationIssuance>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var sets = new Mock<ILmsCertificationRequirementSetsRepository>();
			sets.Setup(s => s.GetForDepartmentAsync(Dept)).ReturnsAsync(() => Sets.ToList());
			sets.Setup(s => s.GetForTypeAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int t) => Sets.FirstOrDefault(s => s.DepartmentCertificationTypeId == t));
			sets.Setup(s => s.GetDepartmentIdsWithActiveSetsAsync()).ReturnsAsync(() => Sets.Any(s => s.IsActive) ? new List<int> { Dept } : new List<int>());
			sets.Setup(s => s.HasActiveSetsAsync(Dept)).ReturnsAsync(() => Sets.Any(s => s.IsActive));
			sets.Setup(s => s.SaveOrUpdateAsync(It.IsAny<LmsCertificationRequirementSet>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((LmsCertificationRequirementSet s, CancellationToken _, bool __) =>
				{
					if (s.LmsCertificationRequirementSetId == 0)
					{
						s.LmsCertificationRequirementSetId = Sets.Count + 1;
						Sets.Add(s);
					}
					return s;
				});
			var items = new Mock<ILmsCertificationRequirementItemsRepository>();
			items.Setup(i => i.GetForSetsAsync(Dept, It.IsAny<IEnumerable<int>>()))
				.ReturnsAsync((int d, IEnumerable<int> ids) => Items.Where(i => ids.Contains(i.LmsCertificationRequirementSetId)).OrderBy(i => i.SortOrder).ToList());
			items.Setup(i => i.DeleteForSetAsync(Dept, It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, int s, CancellationToken _) => Items.RemoveAll(i => i.LmsCertificationRequirementSetId == s));
			items.Setup(i => i.InsertAsync(It.IsAny<LmsCertificationRequirementItem>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((LmsCertificationRequirementItem i, CancellationToken _, bool __) => { i.LmsCertificationRequirementItemId = 100 + Items.Count; Items.Add(i); return i; });

			var enrollments = new Mock<ILmsEnrollmentsRepository>();
			enrollments.Setup(e => e.GetByIdForDepartmentAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int id) => Enrollments.TryGetValue(id, out var e) ? e : null);

			var states = new Mock<ILmsProgramStatesRepository>();
			states.Setup(s => s.TryClaimRequirementSetSweepAsync(Dept, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, DateTime day, CancellationToken _) => { if (ClaimedDay == day.Date) return false; ClaimedDay = day.Date; return true; });
			var alerts = new Mock<ILmsCertificationAlertsRepository>();
			alerts.Setup(a => a.TryClaimAsync(It.IsAny<LmsCertificationAlert>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((LmsCertificationAlert a, CancellationToken _) =>
				{
					if (Alerts.Any(x => x.PersonnelCertificationId == a.PersonnelCertificationId && x.Kind == a.Kind && x.AlertKey == a.AlertKey))
						return false;
					a.LmsCertificationAlertId = Alerts.Count + 1;
					Alerts.Add(a);
					return true;
				});

			Courses.Setup(c => c.GetVisibleByIdAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int id) => new LmsCourse { LmsCourseId = id, DepartmentId = Dept, Title = "Course " + id });
			Courses.Setup(c => c.GetVisibleByIdsAsync(Dept, It.IsAny<IEnumerable<int>>()))
				.ReturnsAsync((int d, IEnumerable<int> ids) => ids.Select(id => new LmsCourse { LmsCourseId = id, DepartmentId = Dept, Title = "Course " + id }).ToList());
			Trainings.Setup(t => t.GetTrainingByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => new Training { TrainingId = id, DepartmentId = Dept, Name = "Training " + id });
			LearningRecords.Setup(l => l.GetCategoriesAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new List<TrainingCategory> { new TrainingCategory { TrainingCategoryId = 7, DepartmentId = Dept, Name = "Driving" } });

			var facts = new Facts(this);
			Issuance = new LmsCertificationIssuanceService(ledger.Object, Certifications.Object, Departments.Object, facts, Mock.Of<ILmsPathStepsRepository>(), enrollments.Object,
				sets.Object, items.Object, Notifier.Object, Bus.Object);
			Requirements = new LmsCertificationRequirementService(sets.Object, items.Object, Issuance, Certifications.Object, Departments.Object, Courses.Object, Trainings.Object,
				Mock.Of<ILmsPathsRepository>(), LearningRecords.Object, facts, Mock.Of<ILmsPathStepsRepository>(), states.Object, alerts.Object, Access.Object, Notifier.Object, Bus.Object);
		}

		/// <summary>The completion facts, like the Dapper projections (no voided record ever appears: the tests remove them).</summary>
		private sealed class Facts(CertificationWorld world) : ILmsRequirementFactsRepository
		{
			public Task<List<LmsRequirementFact>> GetCourseCompletionsAsync(int departmentId, IEnumerable<int> lmsCourseIds) =>
				Task.FromResult(world.CourseFacts.Where(f => lmsCourseIds.Contains(f.RefId)).ToList());

			public Task<List<LmsRequirementFact>> GetCourseCompletionsWithRecordsAsync(int departmentId, IEnumerable<int> lmsCourseIds) => GetCourseCompletionsAsync(departmentId, lmsCourseIds);

			public Task<List<LmsRequirementFact>> GetTrainingCompletionsAsync(int departmentId, IEnumerable<int> trainingIds) =>
				Task.FromResult(world.TrainingFacts.Where(f => trainingIds.Contains(f.RefId)).ToList());

			public Task<List<LmsRequirementFact>> GetCategoryHoursAsync(int departmentId, IEnumerable<int> categoryIds, DateTime fromUtc) =>
				Task.FromResult(world.HourFacts.Where(f => categoryIds.Contains(f.RefId) && f.CompletedOn >= fromUtc).ToList());

			public Task<List<LmsRequirementFact>> GetEvaluationsAsync(int departmentId, IEnumerable<string> codes) =>
				Task.FromResult(world.EvaluationFacts.Where(f => codes.Any(c => string.Equals(c, f.Code, StringComparison.OrdinalIgnoreCase))).ToList());
		}

		public DepartmentCertificationType AddType(int id, string name, Action<DepartmentCertificationType> configure = null)
		{
			var type = new DepartmentCertificationType
			{
				DepartmentCertificationTypeId = id, DepartmentId = Dept, Type = name, Code = name.ToUpperInvariant().Replace(' ', '-'), AppliesTo = (int)CertificationAppliesTo.Person,
				DefaultValidityMonths = 12, IsActive = true
			};
			configure?.Invoke(type);
			Types[id] = type;
			return type;
		}

		public PersonnelCertification AddRecord(int id, string userId, int typeId, DateTime? expires, int status = (int)PersonnelCertificationStatuses.Active, DateTime? received = null)
		{
			var record = new PersonnelCertification
			{
				PersonnelCertificationId = id, DepartmentId = Dept, UserId = userId, DepartmentCertificationTypeId = typeId, Name = Types[typeId].Type, ExpiresOn = expires, Status = status,
				RecievedOn = received ?? DateTime.UtcNow.Date.AddMonths(-11)
			};
			Records.Add(record);
			return record;
		}

		public LmsCertificationRequirementSet AddSet(int typeId, Action<LmsCertificationRequirementSet> configure, params LmsCertificationRequirementItem[] items)
		{
			var set = new LmsCertificationRequirementSet { LmsCertificationRequirementSetId = Sets.Count + 1, DepartmentId = Dept, DepartmentCertificationTypeId = typeId, CreatedOn = DateTime.UtcNow };
			configure?.Invoke(set);
			Sets.Add(set);
			var order = 0;
			foreach (var item in items)
			{
				item.LmsCertificationRequirementItemId = 100 + Items.Count;
				item.DepartmentId = Dept;
				item.LmsCertificationRequirementSetId = set.LmsCertificationRequirementSetId;
				item.SortOrder = ++order;
				Items.Add(item);
			}
			return set;
		}

		public static LmsRequirementFact Fact(string userId, int refId, DateTime completedOn, int recordId, bool recordedByInstructor = false, decimal amount = 1m, string code = null) =>
			new LmsRequirementFact
			{
				UserId = userId, RefId = refId, CompletedOn = completedOn, LearningRecordId = recordId, Amount = amount, Code = code,
				CompletionMethod = recordedByInstructor ? (int)LearningCompletionMethods.RecordedByOther : (int)LearningCompletionMethods.Self
			};
	}
}
