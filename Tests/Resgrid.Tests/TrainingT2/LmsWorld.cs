using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.TrainingT1;

namespace Resgrid.Tests.TrainingT2
{
	/// <summary>
	/// The LMS services wired to in-memory repositories, the real learning-record writer (over the T1 fake store) and mocks for
	/// everything baseline. One department, an administrator, two members and a grader.
	/// </summary>
	public sealed class LmsWorld
	{
		public const int DepartmentId = 31;
		public const string Admin = "lms-admin";
		public const string Member = "lms-member-1";
		public const string Member2 = "lms-member-2";
		public const string Grader = "lms-grader";

		public readonly FakeLmsCourses Courses = new FakeLmsCourses();
		public readonly FakeLmsCourseVersions Versions = new FakeLmsCourseVersions();
		public readonly FakeLmsApprovals Approvals = new FakeLmsApprovals();
		public readonly FakeLmsAudiences Audiences = new FakeLmsAudiences();
		public readonly FakeLmsOwners Owners = new FakeLmsOwners();
		public readonly FakeLmsModules Modules = new FakeLmsModules();
		public readonly FakeLmsLessons Lessons = new FakeLmsLessons();
		public readonly FakeLmsBanks Banks = new FakeLmsBanks();
		public readonly FakeLmsQuestions Questions = new FakeLmsQuestions();
		public readonly FakeLmsAttempts Attempts = new FakeLmsAttempts();
		public readonly FakeLmsEnrollments Enrollments = new FakeLmsEnrollments();
		public readonly FakeLmsProgress Progress = new FakeLmsProgress();
		public readonly FakeLmsFiles Files = new FakeLmsFiles();
		public readonly FakeLmsSurveys Surveys = new FakeLmsSurveys();
		public readonly FakeLmsViewings Viewings = new FakeLmsViewings();
		public readonly FakeLmsAttendees Attendees = new FakeLmsAttendees();
		public readonly FakeLmsPaths Paths = new FakeLmsPaths();
		public readonly FakeLmsStates States = new FakeLmsStates();
		public readonly FakeLmsMedia Media = new FakeLmsMedia();
		public readonly Mock<ILmsSetFiles> SetFiles = new Mock<ILmsSetFiles>();
		public readonly FakeLearningRecordsRepository Records = new FakeLearningRecordsRepository();
		public readonly Resgrid.Tests.Academy.FakeAcademyLessonUpdates AcademyUpdates;

		public LmsDepartmentGate Gate = new LmsDepartmentGate { DepartmentId = DepartmentId, ModuleOn = true, FlagOn = true, AddonActive = true, LastEntitledOn = DateTime.UtcNow };
		public AcademyEntitlements Entitlements = new AcademyEntitlements();
		public HashSet<string> ResolvedMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Member, Member2 };

		/// <summary>
		/// Round 10 A7: the members holding an LMS seat (ILmsGateService.HoldsLearnerSeatAsync / GetLearnerSeatHoldersAsync). Every member by
		/// default; a test removes one to see a learner action refused, or assigns its own set.
		/// </summary>
		public HashSet<string> SeatedMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Admin, Member, Member2, Grader };

		/// <summary>Round 10 A7: how many times the services asked for the seat holders (one read per request, R-L9).</summary>
		public int SeatReads;
		public readonly List<PersonnelCertification> Certifications = new List<PersonnelCertification>();
		public readonly Dictionary<int, DepartmentCertificationType> CertificationTypes = new Dictionary<int, DepartmentCertificationType>();
		public readonly List<TrainingCategory> Categories = new List<TrainingCategory>();
		public readonly List<SignatureRecord> Signatures = new List<SignatureRecord>();

		/// <summary>K3: the knowledge reindex queue the course service notifies on publish, retire and catalog changes.</summary>
		public readonly Mock<IKnowledgeIndexQueue> KnowledgeQueue = new Mock<IKnowledgeIndexQueue>();

		/// <summary>T5 (round 6F): the publish hooks the course service calls after a version goes into force (flashcard supersede flags).</summary>
		public readonly List<IContentVersionPublishedHook> PublishedHooks = new List<IContentVersionPublishedHook>();

		public readonly Mock<ICertificationService> CertificationService = new Mock<ICertificationService>();
		public readonly Mock<ISignatureService> SignatureService = new Mock<ISignatureService>();
		public readonly Mock<ILmsMediaStore> Store = new Mock<ILmsMediaStore>();
		public readonly Mock<ILmsVideoTranscoder> Transcoder = new Mock<ILmsVideoTranscoder>();
		public readonly Mock<IDepartmentsService> Departments = new Mock<IDepartmentsService>();

		/// <summary>T5: the xAPI statement queue the learner and assessment services report to, and the builder's standards lesson check.</summary>
		public readonly Mock<ILmsXapiStatementQueue> Xapi = new Mock<ILmsXapiStatementQueue>();
		public readonly Mock<ILmsExternalLearnersRepository> ExternalLearners = new Mock<ILmsExternalLearnersRepository>();
		public readonly Mock<ILmsStandardsLessonValidator> StandardsLessons = new Mock<ILmsStandardsLessonValidator>();

		/// <summary>Security review M3: the protocol a ProtocolVersion lesson shows, and its header and audience (the public-page rules for external learners).</summary>
		public readonly Mock<IProtocolVersionsRepository> ProtocolVersions = new Mock<IProtocolVersionsRepository>();
		public readonly Mock<IDispatchProtocolRepository> DispatchProtocols = new Mock<IDispatchProtocolRepository>();
		public readonly Mock<IProtocolAudiencesRepository> ProtocolAudiences = new Mock<IProtocolAudiencesRepository>();

		public readonly Mock<IEventAggregator> Events = new Mock<IEventAggregator>();
		public readonly List<Resgrid.Model.Events.CourseWorkflowEvent> CourseEvents = new List<Resgrid.Model.Events.CourseWorkflowEvent>();

		/// <summary>Round 9A: the certification issuance ledger (in memory), the engine over it, the learning-record observers the writer tells.</summary>
		public readonly List<LmsCertificationIssuance> Issuances = new List<LmsCertificationIssuance>();
		public readonly List<ILearningRecordObserver> Observers = new List<ILearningRecordObserver>();
		public readonly Mock<ILmsProgramNotifier> CertificationNotifier = new Mock<ILmsProgramNotifier>();
		public readonly LmsCertificationIssuanceService CertificationIssuance;
		public readonly LearningRecordWriter Writer;

		public readonly LmsLearnerService Learner;
		public readonly LmsAssessmentService Assessment;
		public readonly LmsCourseService CourseService;
		public readonly LmsCrewViewingService Crew;
		public readonly LmsAcademyService Academy;
		public readonly LmsMediaService MediaService;
		public readonly LmsQuestionBankService BankService;

		public LmsWorld()
		{
			AcademyUpdates = new Resgrid.Tests.Academy.FakeAcademyLessonUpdates(Enrollments);
			var gate = new Mock<ILmsGateService>();
			gate.Setup(g => g.GetGateAsync(It.IsAny<int>())).ReturnsAsync(() => Gate);
			gate.Setup(g => g.HoldsLearnerSeatAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string user) => { SeatReads++; return d == DepartmentId && Gate.FlagOn && user != null && SeatedMembers.Contains(user); });
			gate.Setup(g => g.GetLearnerSeatHoldersAsync(It.IsAny<int>()))
				.ReturnsAsync((int d) => { SeatReads++; return d == DepartmentId && Gate.FlagOn ? new HashSet<string>(SeatedMembers, StringComparer.OrdinalIgnoreCase) : new HashSet<string>(); });

			var entitlements = new Mock<ILmsEntitlementService>();
			entitlements.Setup(e => e.GetEntitlementsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>())).ReturnsAsync(() => Entitlements);
			entitlements.Setup(e => e.IsVisibleAsync(It.IsAny<int>(), It.IsAny<LmsCourse>()))
				.ReturnsAsync((int d, LmsCourse c) => c != null && (string.IsNullOrWhiteSpace(c.VisibilityKeys) || Entitlements.Allows(c.VisibilityKeys)));

			var audience = new Mock<IContentAudienceService>();
			audience.Setup(a => a.ResolveAsync(DepartmentId, It.IsAny<IEnumerable<IContentAudienceRule>>()))
				.ReturnsAsync((int d, IEnumerable<IContentAudienceRule> rules) =>
				{
					var list = rules.ToList();
					var users = list.Any(r => r.Kind == (int)ContentAudienceKinds.User)
						? list.Where(r => r.Kind == (int)ContentAudienceKinds.User).Select(r => r.RefId)
						: ResolvedMembers;
					return new ContentAudienceResolution { MemberUserIds = new HashSet<string>(users, StringComparer.OrdinalIgnoreCase) };
				});

			var department = new Department { DepartmentId = DepartmentId, Name = "Station 31", ManagingUserId = Admin };
			Departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(department);
			Departments.Setup(d => d.GetDepartmentMemberAsync(It.IsAny<string>(), DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync((string user, int d, bool b) => new DepartmentMember { DepartmentId = DepartmentId, UserId = user });
			Departments.Setup(d => d.GetActiveMemberUserIdsAsync(DepartmentId))
				.ReturnsAsync(() => new HashSet<string>(new[] { Admin, Member, Member2, Grader }, StringComparer.OrdinalIgnoreCase));

			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetTrainingRecordSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new TrainingRecordSettings());
			var profiles = new Mock<IUserProfileService>();

			CertificationService.Setup(c => c.GetCertificationTypeByIdAsync(It.IsAny<int>()))
				.ReturnsAsync((int id) => CertificationTypes.TryGetValue(id, out var type) ? type : null);
			CertificationService.Setup(c => c.GetCertificationsForDepartmentAsync(DepartmentId, It.IsAny<IEnumerable<string>>()))
				.ReturnsAsync((int d, IEnumerable<string> users) => Certifications.Where(c => users == null || users.Contains(c.UserId)).ToList());
			CertificationService.Setup(c => c.SaveCertificationAsync(It.IsAny<PersonnelCertification>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((PersonnelCertification c, CancellationToken ct) =>
				{
					c.PersonnelCertificationId = 900 + Certifications.Count;
					Certifications.Add(c);
					return c;
				});
			CertificationService.Setup(c => c.RenewCertificationAsync(It.IsAny<int>(), DepartmentId, It.IsAny<DateTime?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, int d, DateTime? expires, string number, string user, CancellationToken ct) =>
				{
					var existing = Certifications.Single(c => c.PersonnelCertificationId == id);
					existing.ExpiresOn = expires;
					var type = CertificationTypes[existing.DepartmentCertificationTypeId.Value];
					existing.Status = type.RequiresVerification ? (int)PersonnelCertificationStatuses.PendingVerification : (int)PersonnelCertificationStatuses.Active;
					return existing;
				});

			var recordService = new Mock<ILearningRecordService>();
			recordService.Setup(r => r.GetCategoriesAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => Categories.ToList());
			recordService.Setup(r => r.SaveCategoryAsync(It.IsAny<TrainingCategory>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((TrainingCategory c, string u, CancellationToken ct) =>
				{
					c.TrainingCategoryId = 70 + Categories.Count;
					Categories.Add(c);
					return c;
				});

			var writer = BuildWriter(settings);
			Writer = writer;
			var events = Events;
			CertificationIssuance = BuildIssuance(Departments);
			// Round 7C: the course workflow events (triggers 259-261) the services raise.
			Events.Setup(e => e.SendMessage(It.IsAny<Resgrid.Model.Events.CourseWorkflowEvent>())).Callback((Resgrid.Model.Events.CourseWorkflowEvent e) => CourseEvents.Add(e));
			var communication = new Mock<ICommunicationService>();
			var protocolVersions = ProtocolVersions;

			Learner = new LmsLearnerService(Courses, Versions, Modules, Lessons, Audiences, Owners, Enrollments, Progress, Files, Attempts, Surveys, Media,
				protocolVersions.Object, gate.Object, entitlements.Object, audience.Object, Departments.Object, settings.Object, profiles.Object, writer, recordService.Object,
				CertificationService.Object, events.Object, new Lazy<ICommunicationService>(() => communication.Object), Xapi.Object, ExternalLearners.Object,
				DispatchProtocols.Object, ProtocolAudiences.Object, CertificationIssuance);
			Assessment = new LmsAssessmentService(Attempts, Enrollments, Lessons, Versions, Courses, Owners, Questions, gate.Object, Learner, events.Object, Xapi.Object);

			SignatureService.Setup(s => s.SignAsync(It.IsAny<SignatureRequest>(), It.IsAny<SignatureProof>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((SignatureRequest request, SignatureProof proof, CancellationToken ct) =>
				{
					var record = new SignatureRecord
					{
						SignatureRecordId = 500 + Signatures.Count,
						DepartmentId = request.DepartmentId,
						SignerUserId = proof.UserId,
						Level = (int)request.Use.Floor,
						SubjectKind = (int)request.Use.SubjectKind,
						SubjectId = request.SubjectId,
						ArtifactSha256 = request.ArtifactSha256
					};
					Signatures.Add(record);
					return new SignatureResult { Outcome = SignatureOutcomes.Signed, Record = record };
				});
			SignatureService.Setup(s => s.GetByIdAsync(DepartmentId, It.IsAny<int>())).ReturnsAsync((int d, int id) => Signatures.FirstOrDefault(s => s.SignatureRecordId == id));
			SignatureService.Setup(s => s.Verify(It.IsAny<SignatureRecord>(), It.IsAny<string>()))
				.Returns((SignatureRecord r, string hash) => new SignatureVerification
				{
					Record = r,
					State = r != null && r.ArtifactSha256 == hash ? SignatureVerificationStates.Valid : SignatureVerificationStates.ArtifactChanged
				});

			var unitOfWork = new Mock<IUnitOfWork>();
			CourseService = new LmsCourseService(Courses, Versions, Approvals, Audiences, Owners, Modules, Lessons, Enrollments, Banks, Media, protocolVersions.Object,
				SignatureService.Object, Departments.Object, settings.Object, profiles.Object, events.Object, unitOfWork.Object,
				new Lazy<ICommunicationService>(() => communication.Object), KnowledgeQueue.Object, PublishedHooks, StandardsLessons.Object, Questions,
				Mock.Of<IContentSetCopyLocks>(), SetFiles.Object);
			Crew = new LmsCrewViewingService(Viewings, Attendees, Courses, Owners, Lessons, entitlements.Object, Learner, Departments.Object, events.Object);
			Academy = new LmsAcademyService(Courses, Versions, Modules, Lessons, Banks, Questions, Enrollments, Paths, entitlements.Object, Learner, audience.Object, events.Object, AcademyUpdates);
			Store.SetupGet(s => s.IsConfigured).Returns(true);
			Store.Setup(s => s.KeyFor(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
				.Returns((int d, int a, string relative) => "lms/" + d + "/" + a + "/" + relative);
			MediaService = new LmsMediaService(Store.Object, Transcoder.Object, Media, Lessons, Enrollments, Versions, Courses, Owners, States, gate.Object, events.Object);
			BankService = new LmsQuestionBankService(Banks, Questions, Owners, events.Object, KnowledgeQueue.Object);
		}

		private LearningRecordWriter BuildWriter(Mock<IDepartmentSettingsService> settings)
		{
			var mappings = new Mock<ICategoryCreditMappingsRepository>();
			mappings.Setup(m => m.GetForDepartmentAsync(It.IsAny<int>())).ReturnsAsync(new List<CategoryCreditMapping>());
			var categories = new Mock<ITrainingCategoriesRepository>();
			categories.Setup(c => c.GetForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(() => Categories.ToList());
			var certificationRecords = new Mock<IPersonnelCertificationRepository>();
			certificationRecords.Setup(c => c.GetForDepartmentAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<PersonnelCertification>());
			var credits = new Mock<IPersonnelCertificationCreditsRepository>();
			credits.Setup(c => c.GetByLearningRecordIdsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<PersonnelCertificationCredit>());
			return new LearningRecordWriter(Records, mappings.Object, categories.Object, certificationRecords.Object, credits.Object,
				new Lazy<ICertificationService>(() => CertificationService.Object), Mock.Of<ITrainingRepository>(), Mock.Of<ITrainingUserRepository>(),
				Mock.Of<ITrainingCompletionHistoriesRepository>(), Mock.Of<ILogUsersRepository>(), Mock.Of<IRmsOperationalRecordsRepository>(),
				Mock.Of<IRmsRecordParticipantsRepository>(), Mock.Of<ICalendarItemsRepository>(), Mock.Of<ICalendarItemTypeRepository>(), settings.Object, Mock.Of<IEventAggregator>(),
				new Lazy<IEnumerable<ILearningRecordObserver>>(() => Observers));
		}

		/// <summary>Round 9A: the real issuance engine over an in-memory ledger, the mocked certification service and enrolment-backed course facts.</summary>
		private LmsCertificationIssuanceService BuildIssuance(Mock<IDepartmentsService> departments)
		{
			var ledger = new Mock<ILmsCertificationIssuancesRepository>();
			ledger.Setup(l => l.InsertAsync(It.IsAny<LmsCertificationIssuance>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((LmsCertificationIssuance i, CancellationToken _, bool __) => { i.LmsCertificationIssuanceId = Issuances.Count + 1; Issuances.Add(i); return i; });
			ledger.Setup(l => l.GetLiveForUserAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string u) => Issuances.Where(i => i.DepartmentId == d && i.IsLive && string.Equals(i.UserId, u, StringComparison.OrdinalIgnoreCase)).ToList());
			ledger.Setup(l => l.GetForCertificationAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int c) => Issuances.Where(i => i.DepartmentId == d && i.PersonnelCertificationId == c).ToList());
			ledger.Setup(l => l.UpdateStateAsync(It.IsAny<LmsCertificationIssuance>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			return new LmsCertificationIssuanceService(ledger.Object, CertificationService.Object, departments.Object, new WorldFacts(this), Mock.Of<ILmsPathStepsRepository>(),
				Enrollments, Mock.Of<ILmsCertificationRequirementSetsRepository>(), Mock.Of<ILmsCertificationRequirementItemsRepository>(), CertificationNotifier.Object, Events.Object);
		}

		/// <summary>Course completions read from the enrolments (with the ledger record of each), like the Dapper projection.</summary>
		private sealed class WorldFacts(LmsWorld world) : ILmsRequirementFactsRepository
		{
			public Task<List<LmsRequirementFact>> GetCourseCompletionsAsync(int departmentId, IEnumerable<int> lmsCourseIds) =>
				Task.FromResult(world.Enrollments.Rows.Where(e => e.DepartmentId == departmentId && e.Status == (int)LmsEnrollmentStatuses.Completed && e.CompletedOn.HasValue &&
					lmsCourseIds.Contains(e.LmsCourseId)).Select(e => new LmsRequirementFact
					{
						UserId = e.UserId, RefId = e.LmsCourseId, CompletedOn = e.CompletedOn.Value, CompletionMethod = e.CompletionMethod,
						LearningRecordId = e.LearningRecordId ?? world.Records.Live.FirstOrDefault(r => r.SourceRefId == LmsLearnerService.SourceRefFor(e))?.LearningRecordId
					}).ToList());

			public Task<List<LmsRequirementFact>> GetCourseCompletionsWithRecordsAsync(int departmentId, IEnumerable<int> lmsCourseIds) => GetCourseCompletionsAsync(departmentId, lmsCourseIds);
			public Task<List<LmsRequirementFact>> GetTrainingCompletionsAsync(int departmentId, IEnumerable<int> trainingIds) => Task.FromResult(new List<LmsRequirementFact>());
			public Task<List<LmsRequirementFact>> GetCategoryHoursAsync(int departmentId, IEnumerable<int> categoryIds, DateTime fromUtc) => Task.FromResult(new List<LmsRequirementFact>());
			public Task<List<LmsRequirementFact>> GetEvaluationsAsync(int departmentId, IEnumerable<string> codes) => Task.FromResult(new List<LmsRequirementFact>());
		}

		/// <summary>Forgets the process-wide "the Academy is seeded" memo so a fresh store is seeded again.</summary>
		public static void ResetAcademySeedMemo() =>
			typeof(LmsAcademyService).GetField("_seededVersion", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, 0);

		public LmsActor Actor(string userId, bool manage = false, bool grade = false, bool assign = false, bool admin = false) => new LmsActor
		{
			DepartmentId = DepartmentId,
			UserId = userId,
			IsCurrentMember = true,
			IsDepartmentAdmin = admin,
			CanManageCourses = manage,
			CanGrade = grade,
			CanAssignTraining = assign,
			Gate = Gate,
			Facts = new ContentAudienceMemberFacts { UserId = userId, RoleIds = new HashSet<int>() }
		};

		public LmsActor AdminActor => Actor(Admin, manage: true, grade: true, assign: true, admin: true);

		/// <summary>A published department course (rows written straight to the store): one module, the given lessons in order.</summary>
		public async Task<(LmsCourse Course, LmsCourseVersion Version, List<LmsLesson> Lessons)> PublishedCourseAsync(string title, params LmsLesson[] lessons)
		{
			var course = new LmsCourse { DepartmentId = DepartmentId, PublisherKind = (int)LmsPublisherKinds.Department, Title = title, Status = (int)ProtocolStatuses.Published, IsSelfEnrollable = true };
			await Courses.InsertAsync(course, CancellationToken.None);
			var version = await AddVersionAsync(course, 1, 0, lessons);
			return (course, version, (await Lessons.GetForVersionAsync(DepartmentId, version.LmsCourseVersionId)).ToList());
		}

		public async Task<LmsCourseVersion> AddVersionAsync(LmsCourse course, int major, int minor, params LmsLesson[] lessons)
		{
			var version = new LmsCourseVersion
			{
				DepartmentId = course.DepartmentId, LmsCourseId = course.LmsCourseId, Major = major, Minor = minor, Status = (int)ProtocolVersionStatuses.Published,
				Title = course.Title, Hours = 2m, PublishedOn = DateTime.UtcNow
			};
			await Versions.InsertAsync(version, CancellationToken.None);
			var module = new LmsCourseModule { DepartmentId = course.DepartmentId, LmsCourseVersionId = version.LmsCourseVersionId, Title = "Module", SortOrder = 1 };
			await Modules.InsertAsync(module, CancellationToken.None);
			var order = 0;
			foreach (var lesson in lessons)
			{
				var copy = MemRepo<LmsLesson>.Copy(lesson);
				copy.LmsLessonId = 0;
				copy.DepartmentId = course.DepartmentId;
				copy.LmsCourseVersionId = version.LmsCourseVersionId;
				copy.LmsCourseModuleId = module.LmsCourseModuleId;
				copy.SortOrder = ++order;
				await Lessons.InsertAsync(copy, CancellationToken.None);
			}

			var stored = Courses.Rows.Single(c => c.LmsCourseId == course.LmsCourseId);
			stored.CurrentVersionId = version.LmsCourseVersionId;
			stored.Status = (int)ProtocolStatuses.Published;
			course.CurrentVersionId = version.LmsCourseVersionId;
			return version;
		}

		public async Task<LmsQuestionBank> BankAsync(params LmsQuestion[] questions)
		{
			var bank = new LmsQuestionBank { DepartmentId = DepartmentId, Name = "Bank" };
			await Banks.InsertAsync(bank, CancellationToken.None);
			foreach (var question in questions)
			{
				question.DepartmentId = DepartmentId;
				question.LmsQuestionBankId = bank.LmsQuestionBankId;
				await Questions.InsertAsync(question, CancellationToken.None);
			}
			return bank;
		}

		public static LmsQuestion Single(string text, string correct, params string[] keys)
		{
			var question = new LmsQuestion { Kind = (int)LmsQuestionKinds.SingleChoice, Text = text, Points = 1 };
			question.Options = keys.Select(k => new LmsQuestionOption { Key = k, Text = text + " " + k, IsCorrect = k == correct }).ToList();
			return question;
		}

		public static LmsLesson Reading(string title, int seatSeconds = 0) =>
			new LmsLesson { Kind = (int)LmsLessonKinds.Blocks, Title = title, ContentHtml = "<p>" + title + "</p>", IsRequired = true, MinimumSeatSeconds = seatSeconds };

		public static LmsLesson Quiz(int bankId, int passMark = 80, LmsShowAnswersPolicies policy = LmsShowAnswersPolicies.AfterPassing) => new LmsLesson
		{
			Kind = (int)LmsLessonKinds.Assessment, Title = "Quiz", IsRequired = true, LmsQuestionBankId = bankId, PassMark = passMark, ShowAnswersPolicy = (int)policy, RandomizeAnswers = true
		};

		/// <summary>Answers an attempt from the server-side snapshot (what a perfect or a careless learner would send).</summary>
		public List<LmsAttemptAnswer> Answers(int attemptId, bool correct)
		{
			var attempt = Attempts.Rows.Single(a => a.LmsAssessmentAttemptId == attemptId);
			return LmsAttemptSnapshot.Parse(attempt.SnapshotJson).Questions.Select(q => new LmsAttemptAnswer
			{
				QuestionId = q.QuestionId,
				Keys = q.Kind == (int)LmsQuestionKinds.Ordering ? (correct ? q.CorrectOrder : q.CorrectOrder.AsEnumerable().Reverse().ToList())
					: correct ? q.CorrectKeys : q.Options.Select(o => o.Key).Where(k => !q.CorrectKeys.Contains(k)).Take(1).ToList(),
				Text = q.Kind == (int)LmsQuestionKinds.ShortAnswer ? "A considered answer." : null
			}).ToList();
		}
	}
}
