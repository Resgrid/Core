using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Tests.TrainingT4
{
	/// <summary>
	/// Training T4 part 2 (round 5C) services over in-memory repositories: evaluator eligibility and self-evaluation refused, the gate failing
	/// closed, crew-wide evaluation writing identical records and splitting a member out, the next-signer restriction, learning records
	/// written once (in the shape 4B's "N passed evaluations" counts), the sealed PDF failing closed, offline replay, the task book flow to an
	/// Active certification, validity expiry, the digest on worker 96 skipping inactive members, the signature source and access rule.
	/// </summary>
	[TestFixture]
	public class LmsSkillServiceTests
	{
		private const int Dept = 7;
		private const string Evaluator = "EVAL-1";
		private const string MemberA = "MEMBER-A";
		private const string MemberB = "MEMBER-B";
		private const string Officer = "OFFICER-1";
		private const string Inactive = "GONE-1";

		private sealed class Store
		{
			public readonly List<LmsSkillSheet> Sheets = new List<LmsSkillSheet>();
			public readonly List<LmsSkillSheetVersion> Versions = new List<LmsSkillSheetVersion>();
			public readonly List<LmsSkillEvaluation> Evaluations = new List<LmsSkillEvaluation>();
			public readonly List<LmsSkillEvaluationMember> Members = new List<LmsSkillEvaluationMember>();
			public readonly List<LmsSkillEvaluationFile> Files = new List<LmsSkillEvaluationFile>();
			public readonly List<LmsTaskBook> Books = new List<LmsTaskBook>();
			public readonly List<LmsTaskBookDefinition> Definitions = new List<LmsTaskBookDefinition>();
			public readonly List<LmsSkillNotification> Notifications = new List<LmsSkillNotification>();
			public int NextId = 100;

			public readonly Mock<ILmsSkillSheetsRepository> SheetsRepo = new Mock<ILmsSkillSheetsRepository>();
			public readonly Mock<ILmsSkillSheetVersionsRepository> VersionsRepo = new Mock<ILmsSkillSheetVersionsRepository>();
			public readonly Mock<ILmsSkillEvaluationsRepository> EvaluationsRepo = new Mock<ILmsSkillEvaluationsRepository>();
			public readonly Mock<ILmsSkillEvaluationMembersRepository> MembersRepo = new Mock<ILmsSkillEvaluationMembersRepository>();
			public readonly Mock<ILmsSkillEvaluationFilesRepository> FilesRepo = new Mock<ILmsSkillEvaluationFilesRepository>();
			public readonly Mock<ILmsTaskBooksRepository> BooksRepo = new Mock<ILmsTaskBooksRepository>();
			public readonly Mock<ILmsTaskBookDefinitionsRepository> DefinitionsRepo = new Mock<ILmsTaskBookDefinitionsRepository>();
			public readonly Mock<ILmsSkillNotificationsRepository> NotificationsRepo = new Mock<ILmsSkillNotificationsRepository>();

			public Store()
			{
				SheetsRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => Sheets.FirstOrDefault(s => s.DepartmentId == d && s.LmsSkillSheetId == id));
				SheetsRepo.Setup(r => r.GetByIdsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>())).ReturnsAsync((int d, IEnumerable<int> ids) => Sheets.Where(s => s.DepartmentId == d && ids.Contains(s.LmsSkillSheetId)).ToList());
				VersionsRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => Versions.FirstOrDefault(v => v.DepartmentId == d && v.LmsSkillSheetVersionId == id));

				EvaluationsRepo.Setup(r => r.InsertAsync(It.IsAny<LmsSkillEvaluation>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((LmsSkillEvaluation e, CancellationToken c, bool f) => { e.LmsSkillEvaluationId = NextId++; Evaluations.Add(e); return e; });
				EvaluationsRepo.Setup(r => r.TryInsertAsync(It.IsAny<LmsSkillEvaluation>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((LmsSkillEvaluation e, CancellationToken c) =>
					{
						if (Evaluations.Any(x => x.DepartmentId == e.DepartmentId && x.IdempotencyKey == e.IdempotencyKey))
							return false;
						e.LmsSkillEvaluationId = NextId++;
						Evaluations.Add(e);
						return true;
					});
				EvaluationsRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => Evaluations.FirstOrDefault(e => e.DepartmentId == d && e.LmsSkillEvaluationId == id));
				EvaluationsRepo.Setup(r => r.GetByIdempotencyKeyAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync((int d, string k) => Evaluations.FirstOrDefault(e => e.DepartmentId == d && e.IdempotencyKey == k));
				EvaluationsRepo.Setup(r => r.TryUpdateAsync(It.IsAny<LmsSkillEvaluation>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((LmsSkillEvaluation e, long expected, CancellationToken c) => { if (e.RowVersion != expected) return false; e.RowVersion++; return true; });
				EvaluationsRepo.Setup(r => r.GetForMemberAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>()))
					.ReturnsAsync((int d, string u, int? sheet) => Evaluations.Where(e => e.DepartmentId == d && (!sheet.HasValue || e.LmsSkillSheetId == sheet) &&
						Members.Any(m => m.LmsSkillEvaluationId == e.LmsSkillEvaluationId && m.IsIncluded && string.Equals(m.UserId, u, StringComparison.OrdinalIgnoreCase))).ToList());
				EvaluationsRepo.Setup(r => r.GetForTaskBookAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int b) => Evaluations.Where(e => e.DepartmentId == d && e.LmsTaskBookId == b).ToList());

				MembersRepo.Setup(r => r.InsertAsync(It.IsAny<LmsSkillEvaluationMember>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((LmsSkillEvaluationMember m, CancellationToken c, bool f) => { m.LmsSkillEvaluationMemberId = NextId++; Members.Add(m); return m; });
				MembersRepo.Setup(r => r.UpdateAsync(It.IsAny<LmsSkillEvaluationMember>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((LmsSkillEvaluationMember m, CancellationToken c, bool f) => m);
				MembersRepo.Setup(r => r.GetForEvaluationAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => Members.Where(m => m.DepartmentId == d && m.LmsSkillEvaluationId == id).ToList());
				MembersRepo.Setup(r => r.GetForEvaluationsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>())).ReturnsAsync((int d, IEnumerable<int> ids) => Members.Where(m => m.DepartmentId == d && ids.Contains(m.LmsSkillEvaluationId)).ToList());

				FilesRepo.Setup(r => r.GetWithDataAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => Files.FirstOrDefault(f => f.DepartmentId == d && f.LmsSkillEvaluationFileId == id));
				FilesRepo.Setup(r => r.GetForEvaluationAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => Files.Where(f => f.DepartmentId == d && f.LmsSkillEvaluationId == id).ToList());
				FilesRepo.Setup(r => r.InsertAsync(It.IsAny<LmsSkillEvaluationFile>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((LmsSkillEvaluationFile f, CancellationToken c, bool x) => { f.LmsSkillEvaluationFileId = NextId++; Files.Add(f); return f; });

				BooksRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => Books.FirstOrDefault(b => b.DepartmentId == d && b.LmsTaskBookId == id));
				BooksRepo.Setup(r => r.GetForMemberAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync((int d, string u) => Books.Where(b => b.DepartmentId == d && string.Equals(b.UserId, u, StringComparison.OrdinalIgnoreCase)).ToList());
				BooksRepo.Setup(r => r.InsertAsync(It.IsAny<LmsTaskBook>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((LmsTaskBook b, CancellationToken c, bool f) => { b.LmsTaskBookId = NextId++; Books.Add(b); return b; });
				BooksRepo.Setup(r => r.TryUpdateAsync(It.IsAny<LmsTaskBook>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((LmsTaskBook b, long expected, CancellationToken c) => { if (b.RowVersion != expected) return false; b.RowVersion++; return true; });
				BooksRepo.Setup(r => r.GetExpiredOpenAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
					.ReturnsAsync((DateTime now, int take) => Books.Where(b => b.Status == (int)LmsTaskBookStatuses.Open && b.ExpiresOn <= now).ToList());

				DefinitionsRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => Definitions.FirstOrDefault(x => x.DepartmentId == d && x.LmsTaskBookDefinitionId == id));

				NotificationsRepo.Setup(r => r.InsertAsync(It.IsAny<LmsSkillNotification>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((LmsSkillNotification n, CancellationToken c, bool f) => { n.LmsSkillNotificationId = NextId++; Notifications.Add(n); return n; });
			}
		}

		private Store _store;
		private Mock<ISignatureRequestService> _requests;
		private Mock<ISignerEligibilityService> _eligibility;
		private Mock<IDocumentSealingService> _sealing;
		private Mock<ILearningRecordWriter> _writer;
		private Mock<ILmsTaskBookService> _taskBooksMock;
		private Mock<ILmsSkillNotificationService> _notifications;
		private Mock<IDepartmentsService> _departments;
		private Mock<ILmsGateService> _gate;
		private Mock<ICertificationService> _certifications;
		private LmsSkillEvaluationService _service;
		private List<EvaluationRequestInput> _evaluationRequests;

		private static LmsActor Actor(string userId, bool manager = false, bool addon = true, bool flag = true) => new LmsActor
		{
			DepartmentId = Dept, UserId = userId, IsCurrentMember = true, CanManageCourses = manager,
			Gate = new LmsDepartmentGate { DepartmentId = Dept, ModuleOn = true, FlagOn = flag, AddonActive = addon }
		};

		[SetUp]
		public void SetUp()
		{
			_store = new Store();
			_requests = new Mock<ISignatureRequestService>();
			_evaluationRequests = new List<EvaluationRequestInput>();
			_requests.Setup(r => r.CreateEvaluationAsync(It.IsAny<EvaluationRequestInput>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((EvaluationRequestInput input, CancellationToken c) =>
				{
					_evaluationRequests.Add(input);
					return new SignatureRequestResult { Request = new SignatureRequestRecord { SignatureRequestId = 500 + _evaluationRequests.Count, DepartmentId = Dept } };
				});
			_eligibility = new Mock<ISignerEligibilityService>();
			_sealing = new Mock<IDocumentSealingService>();
			_writer = new Mock<ILearningRecordWriter>();
			_writer.Setup(w => w.WriteSourceAsync(It.IsAny<int>(), It.IsAny<LearningRecordSourceKinds>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<LearningRecordDraft>>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new LearningRecordWriteResult());
			_taskBooksMock = new Mock<ILmsTaskBookService>();
			_notifications = new Mock<ILmsSkillNotificationService>();
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetActiveMemberUserIdsAsync(Dept)).ReturnsAsync(new HashSet<string>(new[] { Evaluator, MemberA, MemberB, Officer }, StringComparer.OrdinalIgnoreCase));
			_departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(Dept)).ReturnsAsync(new List<PersonName>());
			_departments.Setup(d => d.GetActiveAdminsForDepartmentAsync(Dept)).ReturnsAsync(new List<Resgrid.Model.Identity.IdentityUser> { new Resgrid.Model.Identity.IdentityUser { Id = Officer } });
			_gate = new Mock<ILmsGateService>();
			_certifications = new Mock<ICertificationService>();
			_service = new LmsSkillEvaluationService(_store.SheetsRepo.Object, _store.VersionsRepo.Object, _store.EvaluationsRepo.Object, _store.MembersRepo.Object,
				_store.FilesRepo.Object, _store.BooksRepo.Object, _requests.Object, _eligibility.Object, _sealing.Object, _writer.Object, _taskBooksMock.Object,
				_notifications.Object, _departments.Object, _gate.Object, Mock.Of<IEventAggregator>());
		}

		/// <summary>A published sheet: three pass/fail steps, the last critical; an evaluator rule (certification type 9).</summary>
		private LmsSkillSheet Sheet(bool crew = false, bool self = false, SignerEligibility next = null, int maxAttempts = 0)
		{
			var sheet = new LmsSkillSheet
			{
				LmsSkillSheetId = _store.NextId++, DepartmentId = Dept, Title = "SCBA donning", Code = "SKL-SCBA", CategoryId = 4, AllowCrewEvaluation = crew, AllowSelfEvaluation = self,
				EvaluatorEligibilityJson = new SignerEligibility { CertificationTypeIds = new List<int> { 9 } }.ToJson(), NextSignerEligibilityJson = next?.ToJson()
			};
			var content = new LmsSkillSheetContent
			{
				Steps = new List<LmsSkillStep>
				{
					new LmsSkillStep { Key = "a", Type = (int)LmsSkillStepTypes.PassFail, Label = "A", Required = true },
					new LmsSkillStep { Key = "b", Type = (int)LmsSkillStepTypes.PassFail, Label = "B", Required = true },
					new LmsSkillStep { Key = "c", Type = (int)LmsSkillStepTypes.PassFail, Label = "C", Required = true, Critical = true }
				}
			};
			var version = new LmsSkillSheetVersion
			{
				LmsSkillSheetVersionId = _store.NextId++, DepartmentId = Dept, LmsSkillSheetId = sheet.LmsSkillSheetId, Major = 1, Status = (int)ProtocolVersionStatuses.Published,
				ContentJson = content.ToJson(), PassThreshold = 60m, MaxAttempts = maxAttempts, DefaultMinutes = 30, ContentHash = "hash"
			};
			sheet.CurrentVersionId = version.LmsSkillSheetVersionId;
			_store.Sheets.Add(sheet);
			_store.Versions.Add(version);
			return sheet;
		}

		private void Eligible(params string[] userIds) =>
			_eligibility.Setup(e => e.IsEligibleAsync(Dept, It.IsAny<string>(), It.IsAny<SignerEligibility>()))
				.ReturnsAsync((int d, string u, SignerEligibility r) => userIds.Contains(u, StringComparer.OrdinalIgnoreCase));

		private static LmsSkillAnswers Answers(string a, string b, string c)
		{
			var answers = new LmsSkillAnswers();
			answers.Steps["a"] = new LmsSkillAnswer { Status = 1, Value = a };
			answers.Steps["b"] = new LmsSkillAnswer { Status = 1, Value = b };
			answers.Steps["c"] = new LmsSkillAnswer { Status = 1, Value = c, Note = c == "fail" ? "missed" : null };
			return answers;
		}

		private async Task<LmsSkillEvaluation> SubmittedAsync(LmsSkillSheet sheet, string[] members, LmsSkillAnswers answers, string next = null)
		{
			var actor = Actor(Evaluator);
			var started = await _service.StartAsync(actor, new LmsSkillEvaluationStart { LmsSkillSheetId = sheet.LmsSkillSheetId, MemberUserIds = members.ToList() });
			var saved = await _service.SaveAnswersAsync(actor, started.LmsSkillEvaluationId, started.RowVersion, answers, "ok", false);
			return await _service.SubmitAsync(actor, saved.LmsSkillEvaluationId, new LmsSkillEvaluationSubmit { ExpectedRowVersion = saved.RowVersion, NextSignerUserId = next });
		}

		private static SignatureRequestRecord Completed(LmsSkillEvaluation evaluation) => new SignatureRequestRecord
		{
			SignatureRequestId = evaluation.SignatureRequestId.Value, DepartmentId = Dept, SubjectKind = (int)SignatureSubjectKinds.SkillEvaluation,
			SubjectId = LmsSkillRules.EvaluationSubjectId(evaluation.LmsSkillEvaluationId), Status = (int)SignatureRequestStatuses.Completed, CompletedOn = DateTime.UtcNow,
			CreatedByUserId = Evaluator
		};

		#region Eligibility, self-evaluation and the gate

		[Test]
		public async Task An_evaluator_outside_the_sheet_rule_is_refused()
		{
			var sheet = Sheet();
			Eligible(Officer);
			Func<Task> start = () => _service.StartAsync(Actor(Evaluator), new LmsSkillEvaluationStart { LmsSkillSheetId = sheet.LmsSkillSheetId, MemberUserIds = { MemberA } });
			(await start.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_skill_not_eligible");

			// Without a rule only LMS managers evaluate.
			sheet.EvaluatorEligibilityJson = null;
			await start.Should().ThrowAsync<InvalidOperationException>();
			(await _service.StartAsync(Actor(Evaluator, manager: true), new LmsSkillEvaluationStart { LmsSkillSheetId = sheet.LmsSkillSheetId, MemberUserIds = { MemberA } }))
				.Should().NotBeNull();
		}

		[Test]
		public async Task Self_evaluation_is_refused_unless_the_sheet_allows_it()
		{
			var sheet = Sheet();
			Eligible(Evaluator);
			Func<Task> self = () => _service.StartAsync(Actor(Evaluator), new LmsSkillEvaluationStart { LmsSkillSheetId = sheet.LmsSkillSheetId, MemberUserIds = { Evaluator } });
			(await self.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_skill_self_evaluation");

			sheet.AllowSelfEvaluation = true;
			await self.Should().NotThrowAsync();
		}

		[Test]
		public async Task A_crew_needs_a_sheet_that_allows_crews_and_members_must_be_active()
		{
			var sheet = Sheet();
			Eligible(Evaluator);
			(await FluentActions.Awaiting(() => _service.StartAsync(Actor(Evaluator), new LmsSkillEvaluationStart { LmsSkillSheetId = sheet.LmsSkillSheetId, MemberUserIds = { MemberA, MemberB } }))
				.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_skill_crew_not_allowed");
			(await FluentActions.Awaiting(() => _service.StartAsync(Actor(Evaluator), new LmsSkillEvaluationStart { LmsSkillSheetId = sheet.LmsSkillSheetId, MemberUserIds = { Inactive } }))
				.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_skill_member_invalid");
		}

		[Test]
		public async Task The_gate_fails_closed_for_new_evaluations()
		{
			var sheet = Sheet();
			Eligible(Evaluator);
			(await FluentActions.Awaiting(() => _service.StartAsync(Actor(Evaluator, addon: false), new LmsSkillEvaluationStart { LmsSkillSheetId = sheet.LmsSkillSheetId, MemberUserIds = { MemberA } }))
				.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_skill_addon_required");
			var unknown = Actor(Evaluator);
			unknown.Gate.AddonActive = null;
			(await FluentActions.Awaiting(() => _service.StartAsync(unknown, new LmsSkillEvaluationStart { LmsSkillSheetId = sheet.LmsSkillSheetId, MemberUserIds = { MemberA } }))
				.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_skill_addon_required", "billing that cannot answer is no add-on");
			(await FluentActions.Awaiting(() => _service.StartAsync(LmsActor.Nobody(Dept), new LmsSkillEvaluationStart { LmsSkillSheetId = sheet.LmsSkillSheetId, MemberUserIds = { MemberA } }))
				.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_skill_not_allowed");
		}

		[Test]
		public async Task Attempts_beyond_the_limit_are_refused()
		{
			var sheet = Sheet(maxAttempts: 1);
			Eligible(Evaluator);
			var failed = await SubmittedAsync(sheet, new[] { MemberA }, Answers("pass", "pass", "fail"));
			failed.Result.Should().Be((int)LmsSkillEvaluationResults.Failed);
			(await FluentActions.Awaiting(() => _service.StartAsync(Actor(Evaluator), new LmsSkillEvaluationStart { LmsSkillSheetId = sheet.LmsSkillSheetId, MemberUserIds = { MemberA } }))
				.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_skill_attempts_exhausted");
			(await _service.StartAsync(Actor(Evaluator), new LmsSkillEvaluationStart { LmsSkillSheetId = sheet.LmsSkillSheetId, MemberUserIds = { MemberB } }))
				.Should().NotBeNull("attempts are counted per member");
		}

		#endregion

		#region Crew, split, critical and the next signer

		[Test]
		public async Task A_critical_failure_fails_the_submitted_evaluation_and_only_passes_write_records()
		{
			var sheet = Sheet();
			Eligible(Evaluator);
			var evaluation = await SubmittedAsync(sheet, new[] { MemberA }, Answers("pass", "pass", "fail"));

			evaluation.Status.Should().Be((int)LmsSkillEvaluationStatuses.AwaitingSignature);
			evaluation.Result.Should().Be((int)LmsSkillEvaluationResults.Failed);
			evaluation.CriticalFailed.Should().BeTrue();
			evaluation.ContentHash.Should().HaveLength(64);
			_evaluationRequests.Should().ContainSingle().Which.EvaluatorUserId.Should().Be(Evaluator);

			(await _service.CompleteFromSignatureAsync(Completed(evaluation))).Should().BeTrue();
			_writer.Verify(w => w.WriteSourceAsync(It.IsAny<int>(), It.IsAny<LearningRecordSourceKinds>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<LearningRecordDraft>>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never, "a failed evaluation never counts toward 'N passed evaluations'");
		}

		[Test]
		public async Task A_crew_evaluation_writes_identical_records_for_every_member_once()
		{
			var sheet = Sheet(crew: true);
			Eligible(Evaluator);
			var evaluation = await SubmittedAsync(sheet, new[] { MemberA, MemberB }, Answers("pass", "pass", "pass"));
			evaluation.Mode.Should().Be((int)LmsSkillEvaluationModes.Crew);
			evaluation.Result.Should().Be((int)LmsSkillEvaluationResults.Passed);

			IReadOnlyCollection<LearningRecordDraft> written = null;
			_writer.Setup(w => w.WriteSourceAsync(Dept, LearningRecordSourceKinds.SkillEvaluation, LmsSkillRules.EvaluationSourceRef(evaluation.LmsSkillEvaluationId),
					It.IsAny<IReadOnlyCollection<LearningRecordDraft>>(), Evaluator, It.IsAny<CancellationToken>()))
				.Callback((int d, LearningRecordSourceKinds k, string s, IReadOnlyCollection<LearningRecordDraft> drafts, string u, CancellationToken c) => written = drafts)
				.ReturnsAsync(new LearningRecordWriteResult { Written = 2 });

			var request = Completed(evaluation);
			(await _service.CompleteFromSignatureAsync(request)).Should().BeTrue();
			(await _service.CompleteFromSignatureAsync(request)).Should().BeFalse("OnSignedAsync runs per signed step; the second call finds it completed");

			_writer.Verify(w => w.WriteSourceAsync(It.IsAny<int>(), LearningRecordSourceKinds.SkillEvaluation, It.IsAny<string>(), It.IsAny<IReadOnlyCollection<LearningRecordDraft>>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
			written.Should().HaveCount(2);
			written.Select(d => d.UserId).Should().BeEquivalentTo(new[] { MemberA, MemberB });
			written.Select(d => (d.Title, d.CourseCode, d.Hours, d.CompletedOn, d.CategoryId, d.Status, d.SessionKey)).Distinct().Should().ContainSingle("one form, identical records");
			var draft = written.First();
			draft.CourseCode.Should().Be("SKL-SCBA", "4B's 'N passed evaluations' counts verified SkillEvaluation records by course code");
			draft.Status.Should().Be(LearningRecordStatuses.Verified);
			draft.CategoryId.Should().Be(4);
			_store.Evaluations.Single(e => e.LmsSkillEvaluationId == evaluation.LmsSkillEvaluationId).Status.Should().Be((int)LmsSkillEvaluationStatuses.Completed);
			_notifications.Verify(n => n.QueueAsync(Dept, LmsSkillNotificationKinds.EvaluationCompleted, It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), true, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_failing_member_is_split_out_of_a_crew_and_graded_alone()
		{
			var sheet = Sheet(crew: true);
			Eligible(Evaluator);
			var actor = Actor(Evaluator);
			var crew = await _service.StartAsync(actor, new LmsSkillEvaluationStart { LmsSkillSheetId = sheet.LmsSkillSheetId, MemberUserIds = { MemberA, MemberB } });
			crew = await _service.SaveAnswersAsync(actor, crew.LmsSkillEvaluationId, crew.RowVersion, Answers("pass", "pass", "pass"), null, false);

			var split = await _service.SplitMemberAsync(actor, crew.LmsSkillEvaluationId, MemberB);

			split.LmsSkillEvaluationId.Should().NotBe(crew.LmsSkillEvaluationId);
			split.ParentEvaluationId.Should().Be(crew.LmsSkillEvaluationId);
			split.AnswersJson.Should().Be(crew.AnswersJson, "the answers so far are copied");
			_store.Members.Single(m => m.LmsSkillEvaluationId == crew.LmsSkillEvaluationId && m.UserId == MemberB).State.Should().Be((int)LmsSkillEvaluationMemberStates.SplitOut);
			_store.Members.Should().Contain(m => m.LmsSkillEvaluationId == split.LmsSkillEvaluationId && m.UserId == MemberB && m.IsIncluded);
			_store.Evaluations.Single(e => e.LmsSkillEvaluationId == crew.LmsSkillEvaluationId).Mode.Should().Be((int)LmsSkillEvaluationModes.Individual, "one member is left");

			split = await _service.SaveAnswersAsync(actor, split.LmsSkillEvaluationId, split.RowVersion, Answers("pass", "pass", "fail"), null, false);
			(await _service.SubmitAsync(actor, split.LmsSkillEvaluationId, new LmsSkillEvaluationSubmit { ExpectedRowVersion = split.RowVersion })).Result
				.Should().Be((int)LmsSkillEvaluationResults.Failed);
			(await FluentActions.Awaiting(() => _service.SplitMemberAsync(actor, crew.LmsSkillEvaluationId, MemberA)).Should().ThrowAsync<InvalidOperationException>())
				.WithMessage("lms_skill_split_not_crew");
		}

		[Test]
		public async Task The_evaluator_picks_a_next_signer_only_from_the_sheet_rule()
		{
			var sheet = Sheet(next: new SignerEligibility { PersonnelRoleIds = new List<int> { 3 } });
			_eligibility.Setup(e => e.IsEligibleAsync(Dept, It.IsAny<string>(), It.IsAny<SignerEligibility>()))
				.ReturnsAsync((int d, string u, SignerEligibility r) => u == Evaluator ? r.CertificationTypeIds.Contains(9) : u == Officer && r.PersonnelRoleIds.Contains(3));

			(await FluentActions.Awaiting(() => SubmittedAsync(sheet, new[] { MemberA }, Answers("pass", "pass", "pass"))).Should().ThrowAsync<InvalidOperationException>())
				.WithMessage("lms_skill_next_signer_required");
			(await FluentActions.Awaiting(() => SubmittedAsync(sheet, new[] { MemberB }, Answers("pass", "pass", "pass"), MemberA)).Should().ThrowAsync<InvalidOperationException>())
				.WithMessage("lms_skill_next_signer_not_eligible");

			var evaluation = await SubmittedAsync(sheet, new[] { MemberA }, Answers("pass", "pass", "pass"), Officer);
			evaluation.NextSignerUserId.Should().Be(Officer);
			_evaluationRequests.Last().CertifyingOfficialUserId.Should().Be(Officer);
			_evaluationRequests.Last().CertifyingEligibility.PersonnelRoleIds.Should().Equal(3);

			var noRule = Sheet();
			(await FluentActions.Awaiting(() => SubmittedAsync(noRule, new[] { MemberB }, Answers("pass", "pass", "pass"), Officer)).Should().ThrowAsync<InvalidOperationException>())
				.WithMessage("lms_skill_next_signer_not_allowed");
		}

		[Test]
		public async Task A_refused_signature_request_leaves_the_evaluation_in_progress()
		{
			var sheet = Sheet();
			Eligible(Evaluator);
			_requests.Setup(r => r.CreateEvaluationAsync(It.IsAny<EvaluationRequestInput>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(SignatureRequestResult.Refused(SignatureRequestRefusals.NotEligible));
			(await FluentActions.Awaiting(() => SubmittedAsync(sheet, new[] { MemberA }, Answers("pass", "pass", "pass"))).Should().ThrowAsync<InvalidOperationException>())
				.WithMessage("lms_skill_signature_refused*");
			var evaluation = _store.Evaluations.Single();
			evaluation.Status.Should().Be((int)LmsSkillEvaluationStatuses.InProgress);
			evaluation.ContentHash.Should().BeNull();
		}

		#endregion

		#region Sealing, offline replay and the post-class hook

		[Test]
		public async Task The_sealed_pdf_fails_closed_while_sealing_is_pending()
		{
			var sheet = Sheet();
			Eligible(Evaluator);
			var evaluation = await SubmittedAsync(sheet, new[] { MemberA }, Answers("pass", "pass", "pass"));
			await _service.CompleteFromSignatureAsync(Completed(evaluation));

			_requests.Setup(r => r.GetAsync(Dept, evaluation.SignatureRequestId.Value)).ReturnsAsync(new SignatureRequestRecord { SignatureRequestId = evaluation.SignatureRequestId.Value, SealedDocumentId = 31 });
			_sealing.Setup(s => s.GetByIdAsync(Dept, 31)).ReturnsAsync(new SealedDocument { SealedDocumentId = 31, Status = (int)SealedDocumentStatuses.Pending });
			(await _service.GetSealedPdfAsync(Dept, evaluation.LmsSkillEvaluationId)).Should().BeNull();
			(await _service.GetViewAsync(Dept, evaluation.LmsSkillEvaluationId)).SealState.Should().Be("pending");
			_sealing.Verify(s => s.OpenAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never, "an unsealed rendering is never handed out as the record");

			_sealing.Setup(s => s.GetByIdAsync(Dept, 31)).ReturnsAsync(new SealedDocument { SealedDocumentId = 31, Status = (int)SealedDocumentStatuses.Sealed });
			_sealing.Setup(s => s.OpenAsync(Dept, 31, It.IsAny<CancellationToken>())).ReturnsAsync(new byte[] { 1, 2, 3 });
			(await _service.GetSealedPdfAsync(Dept, evaluation.LmsSkillEvaluationId)).Should().Equal(1, 2, 3);
			(await _service.GetViewAsync(Dept, evaluation.LmsSkillEvaluationId)).SealState.Should().Be("sealed");
		}

		[Test]
		public async Task The_signature_document_carries_the_evidence_hash_and_its_hash_moves_with_the_evaluation()
		{
			var sheet = Sheet();
			Eligible(Evaluator);
			var actor = Actor(Evaluator);
			var started = await _service.StartAsync(actor, new LmsSkillEvaluationStart { LmsSkillSheetId = sheet.LmsSkillSheetId, MemberUserIds = { MemberA } });
			var file = await _service.AddEvidenceAsync(actor, started.LmsSkillEvaluationId, "c", "photo.jpg", "image/jpeg", new byte[] { 9, 9, 9 });
			(await _service.AddEvidenceAsync(actor, started.LmsSkillEvaluationId, "c", "photo.jpg", "image/jpeg", new byte[] { 9, 9, 9 })).LmsSkillEvaluationFileId
				.Should().Be(file.LmsSkillEvaluationFileId, "an outbox replay of the same file is the same evidence");
			(await FluentActions.Awaiting(() => _service.AddEvidenceAsync(actor, started.LmsSkillEvaluationId, null, "x.exe", "application/x-msdownload", new byte[] { 1 }))
				.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_skill_evidence_type");

			(await _service.DescribeForSignatureAsync(Dept, started.LmsSkillEvaluationId)).Should().BeNull("nothing is signed before submission");
			var saved = await _service.SaveAnswersAsync(actor, started.LmsSkillEvaluationId, started.RowVersion, Answers("pass", "pass", "pass"), null, false);
			var submitted = await _service.SubmitAsync(actor, saved.LmsSkillEvaluationId, new LmsSkillEvaluationSubmit { ExpectedRowVersion = saved.RowVersion });

			var info = await _service.DescribeForSignatureAsync(Dept, submitted.LmsSkillEvaluationId);
			info.RevisionSha256.Should().Be(submitted.ContentHash);
			info.DocumentHtml.Should().Contain(file.Sha256).And.Contain("data:image/jpeg;base64,");
			_store.Evaluations.Single().AnswersJson = Answers("pass", "fail", "pass").ToJson();
			(await _service.DescribeForSignatureAsync(Dept, submitted.LmsSkillEvaluationId)).RevisionSha256.Should().NotBe(submitted.ContentHash, "a changed evaluation is a different document");
		}

		[Test]
		public async Task An_offline_replay_returns_the_first_evaluation()
		{
			var sheet = Sheet();
			Eligible(Evaluator);
			var submission = new LmsSkillOfflineSubmission
			{
				IdempotencyKey = "omni-outbox-1", LmsSkillSheetId = sheet.LmsSkillSheetId, LmsSkillSheetVersionId = sheet.CurrentVersionId.Value, MemberUserIds = { MemberA },
				Answers = Answers("pass", "pass", "pass"), StartedOnUtc = DateTime.UtcNow.AddMinutes(-20), EndedOnUtc = DateTime.UtcNow.AddMinutes(-5)
			};

			var first = await _service.SubmitOfflineAsync(Actor(Evaluator), submission);
			var replay = await _service.SubmitOfflineAsync(Actor(Evaluator), submission);

			replay.LmsSkillEvaluationId.Should().Be(first.LmsSkillEvaluationId);
			_store.Evaluations.Should().ContainSingle();
			_evaluationRequests.Should().ContainSingle();
			first.Status.Should().Be((int)LmsSkillEvaluationStatuses.AwaitingSignature);
			first.DurationSeconds.Should().BeInRange(899, 901, "the device's own start and end decide the timer");

			(await FluentActions.Awaiting(() => _service.SubmitOfflineAsync(Actor(Evaluator), new LmsSkillOfflineSubmission { LmsSkillSheetId = sheet.LmsSkillSheetId }))
				.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_skill_idempotency_key_required");
		}

		[Test]
		public async Task The_post_class_hook_starts_one_crew_evaluation_per_session_and_sheet()
		{
			var sheet = Sheet(); // crews are not allowed on the sheet; a class roster is one anyway
			Eligible(Evaluator);
			_gate.Setup(g => g.GetActorAsync(Dept, Evaluator)).ReturnsAsync(Actor(Evaluator));
			var request = new LmsPostClassEvaluationRequest { DepartmentId = Dept, ClassSessionRef = "class-session:42", LmsSkillSheetId = sheet.LmsSkillSheetId, EvaluatorUserId = Evaluator, MemberUserIds = { MemberA, MemberB } };

			var first = await _service.StartPostClassEvaluationAsync(request);
			var again = await _service.StartPostClassEvaluationAsync(request);

			again.LmsSkillEvaluationId.Should().Be(first.LmsSkillEvaluationId);
			first.Mode.Should().Be((int)LmsSkillEvaluationModes.Crew);
			first.ClassSessionRef.Should().Be("class-session:42");
			LmsSkillEvaluationService.Drafts(first, sheet, null, _store.Members).Select(d => d.SessionKey).Should().OnlyContain(k => k == "class-session:42",
				"the evaluation is the same session as its class (counted once)");
		}

		[Test]
		public async Task Voiding_a_completed_evaluation_voids_its_learning_records()
		{
			var sheet = Sheet();
			Eligible(Evaluator);
			var evaluation = await SubmittedAsync(sheet, new[] { MemberA }, Answers("pass", "pass", "pass"));
			await _service.CompleteFromSignatureAsync(Completed(evaluation));

			(await FluentActions.Awaiting(() => _service.VoidAsync(Actor(Evaluator), evaluation.LmsSkillEvaluationId, "wrong member")).Should().ThrowAsync<InvalidOperationException>())
				.WithMessage("lms_skill_not_allowed");
			await _service.VoidAsync(Actor(Officer, manager: true), evaluation.LmsSkillEvaluationId, "wrong member");
			_writer.Verify(w => w.VoidSourceAsync(Dept, LearningRecordSourceKinds.SkillEvaluation, LmsSkillRules.EvaluationSourceRef(evaluation.LmsSkillEvaluationId), "wrong member", Officer,
				It.IsAny<CancellationToken>()), Times.Once);
		}

		#endregion

		#region Task books

		private LmsTaskBookService TaskBooks(Mock<ISignatureRequestService> requests = null) =>
			new LmsTaskBookService(_store.DefinitionsRepo.Object, _store.BooksRepo.Object, _store.EvaluationsRepo.Object, _store.SheetsRepo.Object, _certifications.Object, _writer.Object,
				(requests ?? _requests).Object, _eligibility.Object, _departments.Object, _notifications.Object, Mock.Of<IEventAggregator>());

		private LmsTaskBookDefinition Definition()
		{
			var definition = new LmsTaskBookDefinition
			{
				LmsTaskBookDefinitionId = _store.NextId++, DepartmentId = Dept, PositionCode = "FFT2", Name = "Firefighter Type 2", Family = (int)LmsTaskBookFamilies.Wildland,
				ValidityMonths = 36, CertificationTypeId = 12,
				TasksJson = Newtonsoft.Json.JsonConvert.SerializeObject(new[] { new LmsTaskBookTask { Key = "t1", Number = "1", Title = "FFT2 task 1" },
					new LmsTaskBookTask { Key = "t2", Number = "2", Title = "FFT2 task 2", RequiresIncident = true } }),
				EvaluatorEligibilityJson = new SignerEligibility { CertificationTypeIds = new List<int> { 9 } }.ToJson(),
				CertifyingEligibilityJson = new SignerEligibility { PersonnelRoleIds = new List<int> { 5 } }.ToJson()
			};
			_store.Definitions.Add(definition);
			_certifications.Setup(c => c.GetCertificationTypeByIdAsync(12)).ReturnsAsync(new DepartmentCertificationType
			{
				DepartmentCertificationTypeId = 12, DepartmentId = Dept, Type = "FFT2", AppliesTo = (int)CertificationAppliesTo.Person, DefaultValidityMonths = 36
			});
			return definition;
		}

		[Test]
		public async Task A_task_book_runs_from_initiation_to_an_active_certification()
		{
			var definition = Definition();
			var books = TaskBooks();
			_certifications.Setup(c => c.GetCertificationsForDepartmentAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<PersonnelCertification>());
			_certifications.Setup(c => c.SaveCertificationAsync(It.IsAny<PersonnelCertification>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((PersonnelCertification p, CancellationToken c) => { p.PersonnelCertificationId = 880; return p; });

			var book = await books.InitiateAsync(Actor(Officer, manager: true), definition.LmsTaskBookDefinitionId, MemberA, DateTime.UtcNow.AddDays(-10));
			book.ExpiresOn.Should().BeCloseTo(DateTime.UtcNow.AddDays(-10).AddMonths(36), TimeSpan.FromMinutes(1), "valid for 3 years");
			book.PersonnelCertificationId.Should().Be(880);
			_certifications.Verify(c => c.SaveCertificationAsync(It.Is<PersonnelCertification>(p => p.Status == (int)PersonnelCertificationStatuses.Trainee && p.UserId == MemberA),
				It.IsAny<CancellationToken>()), Times.Once);
			(await FluentActions.Awaiting(() => books.InitiateAsync(Actor(Officer, manager: true), definition.LmsTaskBookDefinitionId, MemberA, DateTime.UtcNow))
				.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_task_book_already_open");

			// A trainee never signs their own task; an eligible evaluator does.
			Eligible(Evaluator, Officer);
			(await FluentActions.Awaiting(() => _service.StartAsync(Actor(MemberA), new LmsSkillEvaluationStart { LmsTaskBookId = book.LmsTaskBookId, TaskKey = "t1", MemberUserIds = { MemberA } }))
				.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_skill_self_evaluation");

			(await FluentActions.Awaiting(() => books.RequestCertificationAsync(Actor(Officer, manager: true), book.LmsTaskBookId, Evaluator, Officer, "en"))
				.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_task_book_tasks_incomplete");

			foreach (var (task, call) in new[] { ("t1", (int?)null), ("t2", (int?)901) })
			{
				var actor = Actor(Evaluator);
				var started = await _service.StartAsync(actor, new LmsSkillEvaluationStart { LmsTaskBookId = book.LmsTaskBookId, TaskKey = task, MemberUserIds = { MemberA }, CallId = call });
				started.Title.Should().StartWith("FFT2 " + task.Substring(1));
				var answers = new LmsSkillAnswers();
				answers.Steps["task"] = new LmsSkillAnswer { Status = 1, Value = "pass" };
				var saved = await _service.SaveAnswersAsync(actor, started.LmsSkillEvaluationId, started.RowVersion, answers, null, false);
				var submitted = await _service.SubmitAsync(actor, saved.LmsSkillEvaluationId, new LmsSkillEvaluationSubmit { ExpectedRowVersion = saved.RowVersion });
				await _service.CompleteFromSignatureAsync(Completed(submitted));
			}
			(await books.GetViewAsync(Dept, book.LmsTaskBookId)).AllTasksComplete.Should().BeTrue();

			// The certifying official must meet the definition's rule (role 5); with one who does, the ordered request is made.
			_eligibility.Setup(e => e.IsEligibleAsync(Dept, It.IsAny<string>(), It.IsAny<SignerEligibility>()))
				.ReturnsAsync((int d, string u, SignerEligibility r) => u == Evaluator ? r.CertificationTypeIds.Contains(9) : u == Officer && r.PersonnelRoleIds.Contains(5));
			MultiSignerRequestInput multi = null;
			_requests.Setup(r => r.CreateMultiSignerAsync(It.IsAny<MultiSignerRequestInput>(), It.IsAny<CancellationToken>()))
				.Callback((MultiSignerRequestInput i, CancellationToken c) => multi = i)
				.ReturnsAsync(new SignatureRequestResult { Request = new SignatureRequestRecord { SignatureRequestId = 777 } });
			(await FluentActions.Awaiting(() => books.RequestCertificationAsync(Actor(Officer, manager: true), book.LmsTaskBookId, Officer, Evaluator, "en"))
				.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_task_book_final_evaluator_not_eligible");
			(await FluentActions.Awaiting(() => books.RequestCertificationAsync(Actor(Officer, manager: true), book.LmsTaskBookId, Evaluator, MemberB, "en"))
				.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_task_book_certifying_official_not_eligible");
			(await FluentActions.Awaiting(() => books.RequestCertificationAsync(Actor(Officer, manager: true), book.LmsTaskBookId, Evaluator, MemberA, "en"))
				.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_task_book_signers_invalid", "the trainee never certifies their own book");
			book = await books.RequestCertificationAsync(Actor(Officer, manager: true), book.LmsTaskBookId, Evaluator, Officer, "en");
			book.Status.Should().Be((int)LmsTaskBookStatuses.AwaitingCertification);
			multi.Ordered.Should().BeTrue();
			multi.SubjectId.Should().Be(LmsSkillRules.TaskBookSubjectId(book.LmsTaskBookId));
			multi.Steps.Select(s => (s.Role, s.Order, s.UserId)).Should().Equal((SignatureSignerRoles.Evaluator, 1, Evaluator), (SignatureSignerRoles.CertifyingOfficial, 2, Officer));

			// Completion: Trainee → Active, renewed to the validity, a TaskBookSignoff record once.
			_certifications.Setup(c => c.GetCertificationsForDepartmentAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<PersonnelCertification>
			{
				new PersonnelCertification { PersonnelCertificationId = 880, DepartmentId = Dept, UserId = MemberA, DepartmentCertificationTypeId = 12, Status = (int)PersonnelCertificationStatuses.Trainee }
			});
			_certifications.Setup(c => c.SetCertificationStatusAsync(880, Dept, PersonnelCertificationStatuses.Active, It.IsAny<string>(), Officer, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new PersonnelCertification { PersonnelCertificationId = 880, Status = (int)PersonnelCertificationStatuses.Active });
			_requests.Setup(r => r.GetSignersAsync(Dept, 777)).ReturnsAsync(new List<SignatureRequestSigner>
			{
				new SignatureRequestSigner { Role = (int)SignatureSignerRoles.Evaluator, Status = (int)SignatureSignerStatuses.Signed, UserId = Evaluator, SignedOn = DateTime.UtcNow.AddHours(-1) },
				new SignatureRequestSigner { Role = (int)SignatureSignerRoles.CertifyingOfficial, Status = (int)SignatureSignerStatuses.Signed, UserId = Officer, SignedOn = DateTime.UtcNow }
			});
			var completed = new SignatureRequestRecord { SignatureRequestId = 777, DepartmentId = Dept, SubjectKind = (int)SignatureSubjectKinds.SkillEvaluation,
				SubjectId = LmsSkillRules.TaskBookSubjectId(book.LmsTaskBookId), Status = (int)SignatureRequestStatuses.Completed };
			(await books.CertifyFromSignatureAsync(completed)).Should().BeTrue();
			(await books.CertifyFromSignatureAsync(completed)).Should().BeFalse();

			_store.Books.Single().Status.Should().Be((int)LmsTaskBookStatuses.Certified);
			_store.Books.Single().CertifiedByUserId.Should().Be(Officer);
			_certifications.Verify(c => c.SetCertificationStatusAsync(880, Dept, PersonnelCertificationStatuses.Active, It.IsAny<string>(), Officer, It.IsAny<CancellationToken>()), Times.Once);
			_certifications.Verify(c => c.RenewCertificationAsync(880, Dept, It.IsAny<DateTime?>(), null, Officer, It.IsAny<CancellationToken>()), Times.Once);
			_writer.Verify(w => w.WriteSourceAsync(Dept, LearningRecordSourceKinds.TaskBookSignoff, LmsSkillRules.TaskBookSourceRef(book.LmsTaskBookId),
				It.Is<IReadOnlyCollection<LearningRecordDraft>>(d => d.Single().UserId == MemberA && d.Single().CourseCode == "FFT2"), Officer, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task An_incident_task_counts_only_evaluations_on_a_call_or_deployment()
		{
			var definition = Definition();
			_certifications.Setup(c => c.GetCertificationsForDepartmentAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<PersonnelCertification>());
			var books = TaskBooks();
			var book = await books.InitiateAsync(Actor(Officer, manager: true), definition.LmsTaskBookDefinitionId, MemberA, DateTime.UtcNow);
			_store.Evaluations.Add(new LmsSkillEvaluation { LmsSkillEvaluationId = 1, DepartmentId = Dept, LmsTaskBookId = book.LmsTaskBookId, TaskKey = "t2", Status = (int)LmsSkillEvaluationStatuses.Completed,
				Result = (int)LmsSkillEvaluationResults.Passed, StartedOn = DateTime.UtcNow });
			(await books.GetViewAsync(Dept, book.LmsTaskBookId)).Tasks.Single(t => t.Task.Key == "t2").Complete.Should().BeFalse();
		}

		[Test]
		public async Task Open_task_books_past_their_validity_expire_once()
		{
			var books = TaskBooks();
			_store.Books.Add(new LmsTaskBook { LmsTaskBookId = 1, DepartmentId = Dept, UserId = MemberA, PositionCode = "FFT2", Name = "FFT2", Status = (int)LmsTaskBookStatuses.Open, ExpiresOn = DateTime.UtcNow.AddDays(-1) });
			_store.Books.Add(new LmsTaskBook { LmsTaskBookId = 2, DepartmentId = Dept, UserId = MemberB, PositionCode = "FFT2", Name = "FFT2", Status = (int)LmsTaskBookStatuses.Open, ExpiresOn = DateTime.UtcNow.AddDays(5) });

			(await books.ExpireDueAsync(DateTime.UtcNow)).Should().Be(1);
			(await books.ExpireDueAsync(DateTime.UtcNow)).Should().Be(0);

			_store.Books.Single(b => b.LmsTaskBookId == 1).Status.Should().Be((int)LmsTaskBookStatuses.Expired);
			_store.Books.Single(b => b.LmsTaskBookId == 2).Status.Should().Be((int)LmsTaskBookStatuses.Open);
			_notifications.Verify(n => n.QueueAsync(Dept, LmsSkillNotificationKinds.TaskBookExpired, It.IsAny<string>(), It.Is<IEnumerable<string>>(u => u.Single() == MemberA), true,
				It.IsAny<CancellationToken>()), Times.Once);
		}

		#endregion

		#region Digest, worker 96, the signature source and access rule

		[Test]
		public async Task The_digest_reads_due_notices_only_skips_inactive_members_and_sends_one_message_per_person()
		{
			var now = DateTime.UtcNow;
			var due = new List<LmsSkillNotification>
			{
				new LmsSkillNotification { LmsSkillNotificationId = 1, DepartmentId = Dept, UserId = MemberA, Kind = 0, Text = "one", CreatedOn = now.AddMinutes(-3), DueOn = now },
				new LmsSkillNotification { LmsSkillNotificationId = 2, DepartmentId = Dept, UserId = MemberA, Kind = 0, Text = "two", CreatedOn = now.AddMinutes(-2), DueOn = now },
				new LmsSkillNotification { LmsSkillNotificationId = 3, DepartmentId = Dept, UserId = Inactive, Kind = 0, Text = "gone", CreatedOn = now, DueOn = now }
			};
			_store.NotificationsRepo.Setup(r => r.GetDueAsync(now, It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(due);
			_store.NotificationsRepo.Setup(r => r.ClaimAsync(It.IsAny<int>(), now, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_departments.Setup(d => d.GetDepartmentByIdAsync(Dept, false)).ReturnsAsync(new Department { DepartmentId = Dept });
			var communication = new Mock<ICommunicationService>();
			communication.Setup(c => c.SendNotificationAsync(It.IsAny<string>(), Dept, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(),
				It.IsAny<UserProfile>(), It.IsAny<bool>(), It.IsAny<string>())).ReturnsAsync(true);
			var service = new LmsSkillNotificationService(_store.NotificationsRepo.Object, Mock.Of<ILmsSkillSettingsService>(), _departments.Object,
				Mock.Of<IDepartmentSettingsService>(), new Lazy<ICommunicationService>(() => communication.Object));

			(await service.SendDueAsync(now)).Should().Be(1);

			communication.Verify(c => c.SendNotificationAsync(MemberA, Dept, It.Is<string>(m => m.Contains("one") && m.Contains("two")), It.IsAny<string>(), It.IsAny<Department>(),
				"Training skills digest", It.IsAny<UserProfile>(), false, "SK:0"), Times.Once);
			communication.Verify(c => c.SendNotificationAsync(Inactive, It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(),
				It.IsAny<UserProfile>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never, "inactive members are never addressed by automation");
			_store.NotificationsRepo.Verify(r => r.MarkSentAsync(It.Is<IEnumerable<int>>(ids => ids.Count() == 1 && ids.First() == 3), now, true, It.IsAny<CancellationToken>()), Times.Once, "skipped, so it is never read again");
			_store.NotificationsRepo.Verify(r => r.MarkSentAsync(It.Is<IEnumerable<int>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { 1, 2 })), now, false, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Queued_notices_follow_the_settings_and_never_sweep_departments()
		{
			var settings = new Mock<ILmsSkillSettingsService>();
			settings.Setup(s => s.GetAsync(Dept)).ReturnsAsync(new LmsSkillSettings { DigestMode = (int)LmsSkillDigestModes.Daily, DigestHour = 7, NotifyMembers = true, NotifyOfficers = false }.Normalized());
			_departments.Setup(d => d.GetDepartmentByIdAsync(Dept, false)).ReturnsAsync((Department)null);
			var service = new LmsSkillNotificationService(_store.NotificationsRepo.Object, settings.Object, _departments.Object, Mock.Of<IDepartmentSettingsService>(),
				new Lazy<ICommunicationService>(() => Mock.Of<ICommunicationService>()));

			(await service.QueueAsync(Dept, LmsSkillNotificationKinds.EvaluationCompleted, "done", new[] { MemberA, MemberA.ToLowerInvariant() }, true)).Should().Be(1);
			_store.Notifications.Single().DueOn.Should().BeAfter(DateTime.UtcNow, "a daily digest is due at the next 07:00");
			_departments.Verify(d => d.GetActiveAdminsForDepartmentAsync(It.IsAny<int>()), Times.Never, "officers are off");

			settings.Setup(s => s.GetAsync(Dept)).ReturnsAsync(new LmsSkillSettings { DigestMode = (int)LmsSkillDigestModes.Off }.Normalized());
			(await service.QueueAsync(Dept, LmsSkillNotificationKinds.EvaluationCompleted, "done", new[] { MemberA }, true)).Should().Be(0);
		}

		[Test]
		public async Task Worker_96_runs_both_steps_and_one_failing_never_stops_the_other()
		{
			var notifications = new Mock<ILmsSkillNotificationService>();
			notifications.Setup(n => n.SendDueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("smtp down"));
			var books = new Mock<ILmsTaskBookService>();
			books.Setup(b => b.ExpireDueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(2);

			var result = await LmsSkillsLogic.Process(notifications.Object, books.Object, DateTime.UtcNow);

			result.TaskBooksExpired.Should().Be(2);
			result.Failures.Should().Be(1);
		}

		[Test]
		public async Task The_signature_source_dispatches_by_subject_and_completes_once_per_request()
		{
			var evaluations = new Mock<ILmsSkillEvaluationService>();
			var books = new Mock<ILmsTaskBookService>();
			var source = new LmsSkillSignatureSource(new Lazy<ILmsSkillEvaluationService>(() => evaluations.Object), new Lazy<ILmsTaskBookService>(() => books.Object));
			source.Kind.Should().Be(SignatureSubjectKinds.SkillEvaluation);

			await source.DescribeAsync(Dept, "evaluation:5", Evaluator);
			await source.DescribeAsync(Dept, "task-book:6", Evaluator);
			(await source.DescribeAsync(Dept, "other:7", Evaluator)).Should().BeNull();
			evaluations.Verify(e => e.DescribeForSignatureAsync(Dept, 5), Times.Once);
			books.Verify(b => b.DescribeForSignatureAsync(Dept, 6), Times.Once);

			var open = new SignatureRequestRecord { SubjectId = "evaluation:5", Status = (int)SignatureRequestStatuses.PartiallySigned };
			await source.OnSignedAsync(open, new SignatureRequestSigner(), Evaluator);
			evaluations.Verify(e => e.CompleteFromSignatureAsync(It.IsAny<SignatureRequestRecord>(), It.IsAny<CancellationToken>()), Times.Never);
			open.Status = (int)SignatureRequestStatuses.Completed;
			await source.OnSignedAsync(open, new SignatureRequestSigner(), Evaluator);
			evaluations.Verify(e => e.CompleteFromSignatureAsync(open, It.IsAny<CancellationToken>()), Times.Once);
			await source.OnSignedAsync(new SignatureRequestRecord { SubjectId = "task-book:6", Status = (int)SignatureRequestStatuses.Completed }, new SignatureRequestSigner(), Officer);
			books.Verify(b => b.CertifyFromSignatureAsync(It.IsAny<SignatureRequestRecord>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task The_access_rule_lets_managers_and_the_evaluator_act_and_fails_closed_when_dark()
		{
			_store.Evaluations.Add(new LmsSkillEvaluation { LmsSkillEvaluationId = 5, DepartmentId = Dept, EvaluatorUserId = Evaluator, CreatedByUserId = Evaluator });
			_gate.Setup(g => g.GetActorAsync(Dept, Evaluator)).ReturnsAsync(Actor(Evaluator));
			_gate.Setup(g => g.GetActorAsync(Dept, MemberA)).ReturnsAsync(Actor(MemberA));
			_gate.Setup(g => g.GetActorAsync(Dept, Officer)).ReturnsAsync(Actor(Officer, manager: true));
			var rule = new LmsSkillSignatureAccessRule(_gate.Object, _store.EvaluationsRepo.Object, _store.BooksRepo.Object);

			(await rule.CanActOnAsync(Dept, Evaluator, "evaluation:5")).Should().BeTrue();
			(await rule.CanActOnAsync(Dept, Officer, "evaluation:5")).Should().BeTrue();
			(await rule.CanActOnAsync(Dept, MemberA, "evaluation:5")).Should().BeFalse();
			(await rule.CanActOnAsync(Dept, Evaluator, "evaluation:999")).Should().BeFalse();

			_gate.Setup(g => g.GetActorAsync(Dept, Officer)).ReturnsAsync(Actor(Officer, manager: true, flag: false));
			(await rule.CanActOnAsync(Dept, Officer, "evaluation:5")).Should().BeFalse("dark behind Training.Lms");
		}

		[Test]
		public async Task Skill_sheets_and_task_book_tasks_are_competency_subjects_with_evidence()
		{
			var sheet = Sheet();
			var definition = Definition();
			_store.Books.Add(new LmsTaskBook { LmsTaskBookId = 70, DepartmentId = Dept, LmsTaskBookDefinitionId = definition.LmsTaskBookDefinitionId, UserId = MemberA });
			_store.Evaluations.Add(new LmsSkillEvaluation { LmsSkillEvaluationId = 71, DepartmentId = Dept, LmsSkillSheetId = sheet.LmsSkillSheetId, Status = (int)LmsSkillEvaluationStatuses.Completed,
				Result = (int)LmsSkillEvaluationResults.Passed, CompletedOn = DateTime.UtcNow, StartedOn = DateTime.UtcNow, LmsTaskBookId = 70, TaskKey = "t1", Title = "x" });
			_store.Members.Add(new LmsSkillEvaluationMember { LmsSkillEvaluationId = 71, DepartmentId = Dept, UserId = MemberA });
			var subjects = new LmsSkillCompetencySubjects(_store.SheetsRepo.Object, _store.DefinitionsRepo.Object, _store.BooksRepo.Object, _store.EvaluationsRepo.Object);

			(await subjects.ExistsAsync(Dept, LmsCompetencySubjectKinds.SkillSheet, sheet.LmsSkillSheetId.ToString())).Should().BeTrue();
			(await subjects.ExistsAsync(Dept, LmsCompetencySubjectKinds.TaskBookTask, definition.LmsTaskBookDefinitionId + ":t2")).Should().BeTrue();
			(await subjects.ExistsAsync(Dept, LmsCompetencySubjectKinds.TaskBookTask, definition.LmsTaskBookDefinitionId + ":t9")).Should().BeFalse();
			var evidence = await subjects.GetEvidenceAsync(Dept, MemberA);
			evidence.Select(e => (e.Kind, e.SubjectId)).Should().BeEquivalentTo(new[]
			{
				(LmsCompetencySubjectKinds.SkillSheet, sheet.LmsSkillSheetId.ToString()),
				(LmsCompetencySubjectKinds.TaskBookTask, LmsSkillCompetencySubjects.TaskSubjectId(definition.LmsTaskBookDefinitionId, "t1"))
			});
		}

		#endregion
	}
}
