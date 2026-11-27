using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.Rms;

namespace Resgrid.Tests.Protocols
{
	/// <summary>
	/// Protocols P5 QA, dispatch case review: the scoring maths (items, derived criteria, the Records QA weighted formula), the sampling rules
	/// (filters, exclusions, N or a percentage), the reviewer never scoring their own call, the dispatcher seeing only their own sent reviews,
	/// one acknowledgment and one reply, the report aggregation and the rubric template.
	/// </summary>
	[TestFixture]
	public class DispatchCaseReviewTests
	{
		private const int Dept = 7;
		private const string Reviewer = "REVIEWER-1";
		private const string Dispatcher = "dispatcher-1";
		private const string OtherMember = "member-2";
		private const string Admin = "admin-1";

		#region Scoring maths

		private static List<RmsQualityCriterion> TemplateCriteria() => DispatchCaseRubrics.Template(DispatchCaseRubricTexts.English);

		private static DispatchCaseReviewItem Q(string key, DispatchCaseCompliance c) => new DispatchCaseReviewItem { NodeKey = key, Kind = (int)ProtocolDecisionNodeKinds.Question, Compliance = (int)c };

		private static DispatchCaseReviewItem I(string key, DispatchCaseCompliance c) => new DispatchCaseReviewItem { NodeKey = key, Kind = (int)ProtocolDecisionNodeKinds.Instruction, Compliance = (int)c };

		private static List<RmsQualityFinding> HandScores(int caseEntry, int determinant, int callerCare) => new List<RmsQualityFinding>
		{
			new RmsQualityFinding { Key = "case_entry", Score = caseEntry },
			new RmsQualityFinding { Key = "determinant", Score = determinant },
			new RmsQualityFinding { Key = "caller_care", Score = callerCare }
		};

		[Test]
		public void Items_score_100_50_and_0_and_not_applicable_is_left_out()
		{
			DispatchCaseScoring.Points(DispatchCaseCompliance.Compliant).Should().Be(100);
			DispatchCaseScoring.Points(DispatchCaseCompliance.Partial).Should().Be(50);
			DispatchCaseScoring.Points(DispatchCaseCompliance.NotCompliant).Should().Be(0);
			DispatchCaseScoring.Points(DispatchCaseCompliance.NotApplicable).Should().BeNull();
			DispatchCaseScoring.Points(DispatchCaseCompliance.NotScored).Should().BeNull();

			var items = new List<DispatchCaseReviewItem> { Q("q1", DispatchCaseCompliance.Compliant), Q("q2", DispatchCaseCompliance.Partial), Q("q3", DispatchCaseCompliance.NotApplicable), I("i1", DispatchCaseCompliance.NotCompliant) };
			DispatchCaseScoring.Tally(items, ProtocolDecisionNodeKinds.Question).Should().Be((2, 150));
			DispatchCaseScoring.Tally(items, ProtocolDecisionNodeKinds.Instruction).Should().Be((1, 0));
			DispatchCaseScoring.Percent(2, 150).Should().Be(75);
			DispatchCaseScoring.Percent(0, 0).Should().BeNull();
		}

		[Test]
		public void The_overall_score_weights_hand_scored_and_derived_criteria_like_the_records_formula()
		{
			var items = new List<DispatchCaseReviewItem>
			{
				Q("q1", DispatchCaseCompliance.Compliant), Q("q2", DispatchCaseCompliance.Partial), Q("q3", DispatchCaseCompliance.NotApplicable),
				I("i1", DispatchCaseCompliance.Compliant), I("i2", DispatchCaseCompliance.NotCompliant)
			};

			// Weights 2 case entry, 3 questions (75), 3 determinant, 3 instructions (50), 1 caller care: (200 + 225 + 150 + 150 + 100) / 12 = 68.75.
			DispatchCaseScoring.Overall(TemplateCriteria(), HandScores(100, 50, 100), items).Should().Be(69);
		}

		[Test]
		public void A_derived_criterion_with_nothing_that_applied_is_left_out_of_the_weights()
		{
			var items = new List<DispatchCaseReviewItem> { Q("q1", DispatchCaseCompliance.Compliant), Q("q2", DispatchCaseCompliance.Partial), I("i1", DispatchCaseCompliance.NotApplicable) };

			// Instructions drop out: (2*100 + 3*75 + 3*50 + 1*100) / 9 = 75.
			DispatchCaseScoring.Overall(TemplateCriteria(), HandScores(100, 50, 100), items).Should().Be(75);
		}

		[Test]
		public void Without_derived_criteria_the_score_is_exactly_the_records_qa_weighted_average()
		{
			var criteria = new List<RmsQualityCriterion> { new RmsQualityCriterion { Key = "a", Text = "A", Weight = 1 }, new RmsQualityCriterion { Key = "b", Text = "B", Weight = 3 } };
			var findings = new List<RmsQualityFinding> { new RmsQualityFinding { Key = "a", Score = 80 }, new RmsQualityFinding { Key = "b", Score = 41 } };

			// RecordsQualityReviewService.ScoreAsync: (int)Math.Round(sum(weight * score) / (double)sum(weight)).
			var records = (int)Math.Round((1 * 80 + 3 * 41) / 4.0);
			DispatchCaseScoring.Overall(criteria, findings, new List<DispatchCaseReviewItem>()).Should().Be(records).And.Be(51);
		}

		[Test]
		public void A_hand_scored_criterion_without_a_score_leaves_the_review_unscored_and_is_listed_as_missing()
		{
			var items = new List<DispatchCaseReviewItem> { Q("q1", DispatchCaseCompliance.Compliant), I("i1", DispatchCaseCompliance.NotScored) };
			var findings = HandScores(100, 50, 100).Where(f => f.Key != "determinant").ToList();

			DispatchCaseScoring.Overall(TemplateCriteria(), findings, items).Should().BeNull();
			DispatchCaseScoring.Missing(TemplateCriteria(), findings, items).Should().BeEquivalentTo(new[] { "i1", "determinant" });
		}

		[Test]
		public void The_template_marks_its_rubric_so_it_can_never_be_a_records_definition()
		{
			var criteria = TemplateCriteria();
			criteria.Select(c => c.Key).Should().Contain(new[] { DispatchCaseRubrics.QuestionsKey, DispatchCaseRubrics.InstructionsKey });
			criteria.Count(c => DispatchCaseRubrics.IsDerived(c.Key)).Should().Be(2);
			RecordDefinitionKeys.IsValidDefinitionKey(DispatchCaseRubrics.DefinitionKey).Should().BeFalse("Records sampling must never pick a dispatch case rubric up");
			DispatchCaseRubrics.IsDispatchRubric(new RmsQualityRubric { DefinitionKey = DispatchCaseRubrics.DefinitionKey }).Should().BeTrue();
			DispatchCaseRubrics.IsDispatchRubric(new RmsQualityRubric { DefinitionKey = null }).Should().BeFalse();
		}

		[Test]
		public void Rubric_criteria_are_normalized_like_records_rubrics()
		{
			var clean = DispatchCaseRubrics.Normalize(new[]
			{
				new RmsQualityCriterion { Key = " a ", Text = " First ", Weight = 0 },
				new RmsQualityCriterion { Key = null, Text = "Second", Weight = 50 },
				new RmsQualityCriterion { Key = "x", Text = " " }
			});
			clean.Select(c => (c.Key, c.Text, c.Weight)).Should().Equal(("a", "First", 1), ("c2", "Second", 10));

			DispatchCaseRubrics.Normalize(new[] { new RmsQualityCriterion { Key = "a", Text = "1" }, new RmsQualityCriterion { Key = "A", Text = "2" } }).Should().BeNull("keys must be unique");
			DispatchCaseRubrics.Normalize(Array.Empty<RmsQualityCriterion>()).Should().BeNull();
		}

		[Test]
		public void Items_are_the_questions_and_instructions_of_the_recorded_case_in_order()
		{
			var record = new CallProtocolCase
			{
				Steps = new List<ProtocolCaseStep>
				{
					new ProtocolCaseStep { NodeKey = "q1", Kind = (int)ProtocolDecisionNodeKinds.Question, Stage = 0, Text = "Address?", Answer = "1 Main St" },
					new ProtocolCaseStep { NodeKey = "i1", Kind = (int)ProtocolDecisionNodeKinds.Instruction, Text = "Stay on the line" },
					new ProtocolCaseStep { NodeKey = "o1", Kind = (int)ProtocolDecisionNodeKinds.Outcome, Text = "Echo" }
				}
			};

			var items = DispatchCaseReviewItem.FromCase(record);
			items.Select(i => i.NodeKey).Should().Equal("q1", "i1");
			items[0].Answer.Should().Be("1 Main St");
			DispatchCaseReviewItem.Parse(DispatchCaseReviewItem.ToJson(items)).Select(i => i.Text).Should().Equal("Address?", "Stay on the line");
			DispatchCaseReviewItem.Parse("not json").Should().BeEmpty();
		}

		#endregion Scoring maths

		#region Sampling rules

		private static DispatchCaseCandidate Candidate(int id, string attachedBy = Dispatcher, string reportedBy = null, bool reviewed = false) => new DispatchCaseCandidate
		{
			CallProtocolId = id, CallId = 1000 + id, DispatchProtocolId = 5, ProtocolVersionId = 50, AttachedByUserId = attachedBy, ReportingUserId = reportedBy,
			LoggedOn = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc), CallNumber = "C" + id, AlreadyReviewed = reviewed
		};

		[Test]
		public void Calls_already_reviewed_without_a_dispatcher_or_the_assignees_own_are_left_out()
		{
			var candidates = new[]
			{
				Candidate(1), Candidate(2, reviewed: true), Candidate(3, attachedBy: null, reportedBy: null), Candidate(4, attachedBy: null, reportedBy: Dispatcher),
				Candidate(5, attachedBy: Reviewer.ToLowerInvariant()), Candidate(6, attachedBy: null, reportedBy: Reviewer)
			};
			var tally = new DispatchCaseSampleResult();

			var eligible = DispatchCaseSampling.Eligible(candidates, Reviewer, tally);

			eligible.Select(c => c.CallProtocolId).Should().Equal(1, 4);
			eligible.Single(c => c.CallProtocolId == 4).DispatcherUserId.Should().Be(Dispatcher, "the reporting member is the dispatcher when nobody attached the protocol");
			(tally.AlreadyReviewed, tally.NoDispatcher, tally.AssigneesOwn).Should().Be((1, 1, 2));
			DispatchCaseSampling.Eligible(candidates, null).Select(c => c.CallProtocolId).Should().Equal(1, 4, 5, 6);
		}

		[TestCase(0, 10, null, 0)]
		[TestCase(3, 10, null, 3)]
		[TestCase(500, 10, null, 10)]
		[TestCase(500, 200, null, DispatchCaseSampling.MaxSample)]
		[TestCase(7, null, 10, 1)]
		[TestCase(25, null, 10, 3)]
		[TestCase(40, null, 100, 40)]
		[TestCase(1000, null, 50, DispatchCaseSampling.MaxSample)]
		public void The_sample_is_n_or_a_rounded_up_percentage_capped_by_the_eligible_and_the_maximum(int eligible, int? count, int? percent, int expected)
		{
			DispatchCaseSampling.Size(eligible, count, percent).Should().Be(expected);
		}

		[Test]
		public void A_pick_is_a_seeded_shuffle_of_distinct_eligible_cases()
		{
			var eligible = Enumerable.Range(1, 60).Select(i => Candidate(i)).ToList();

			var first = DispatchCaseSampling.Pick(eligible, 10, "seed-a");
			first.Should().HaveCount(10).And.OnlyHaveUniqueItems(c => c.CallProtocolId);
			first.Should().OnlyContain(c => eligible.Contains(c));
			DispatchCaseSampling.Pick(eligible, 10, "seed-a").Select(c => c.CallProtocolId).Should().Equal(first.Select(c => c.CallProtocolId), "the same seed draws the same sample");
			DispatchCaseSampling.Pick(eligible, 10, "seed-b").Select(c => c.CallProtocolId).Should().NotEqual(first.Select(c => c.CallProtocolId));
			first.Select(c => c.CallProtocolId).Should().NotEqual(Enumerable.Range(1, 10), "the order is not the newest calls first");
			DispatchCaseSampling.NewSeed().Should().NotBe(DispatchCaseSampling.NewSeed());
		}

		#endregion Sampling rules

		#region Service

		private sealed class FakeReviews : InMemoryRepo<DispatchCaseReview>, IDispatchCaseReviewsRepository
		{
			public List<DispatchCaseCandidate> Candidates { get; } = new List<DispatchCaseCandidate>();
			public Dictionary<int, string> Cases { get; } = new Dictionary<int, string>();
			public DispatchCaseCandidateFilter LastFilter { get; private set; }
			private int _next = 1;

			public Task<DispatchCaseReview> GetByIdForDepartmentAsync(int departmentId, int id) =>
				Task.FromResult(Rows.FirstOrDefault(r => r.DepartmentId == departmentId && r.DispatchCaseReviewId == id));

			public Task<List<DispatchCaseReview>> GetOpenAsync(int departmentId, int take) =>
				Task.FromResult(Rows.Where(r => r.DepartmentId == departmentId && DispatchCaseReviewRules.IsOpen(r)).Take(take).ToList());

			public Task<List<DispatchCaseReview>> GetSentForDispatcherAsync(int departmentId, string userId, int take) =>
				// Deliberately loose (the service must filter again): every sent review of the department.
				Task.FromResult(Rows.Where(r => r.DepartmentId == departmentId && r.IsSent).ToList());

			public Task<List<DispatchCaseReview>> GetSentForReportAsync(int departmentId, DateTime fromUtc, DateTime toUtc, int? protocolId, int? versionId, string dispatcher, int take) =>
				Task.FromResult(Rows.Where(r => r.DepartmentId == departmentId && r.IsSent && r.CallLoggedOn >= fromUtc && r.CallLoggedOn < toUtc).ToList());

			public Task<Dictionary<int, int>> GetStatusCountsAsync(int departmentId) =>
				Task.FromResult(Rows.Where(r => r.DepartmentId == departmentId).GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count()));

			public Task<List<DispatchCaseCandidate>> GetCandidatesAsync(int departmentId, DispatchCaseCandidateFilter filter, int take)
			{
				LastFilter = filter;
				foreach (var candidate in Candidates)
					candidate.AlreadyReviewed = Rows.Any(r => r.CallProtocolId == candidate.CallProtocolId);
				return Task.FromResult(Candidates.ToList());
			}

			public Task<Dictionary<int, string>> GetCaseJsonAsync(int departmentId, IReadOnlyCollection<int> ids) =>
				Task.FromResult(Cases.Where(c => ids.Contains(c.Key)).ToDictionary(c => c.Key, c => c.Value));

			public Task<bool> TryInsertAsync(DispatchCaseReview review, CancellationToken cancellationToken = default)
			{
				if (Rows.Any(r => r.DepartmentId == review.DepartmentId && r.CallProtocolId == review.CallProtocolId))
					return Task.FromResult(false);
				review.DispatchCaseReviewId = _next++;
				Rows.Add(review);
				return Task.FromResult(true);
			}

			public Task<bool> TrySaveScoresAsync(DispatchCaseReview review, long expected, CancellationToken cancellationToken = default)
			{
				// The service updates the instance it read; the store only checks the version and that it was still open.
				if (review.RowVersion != expected)
					return Task.FromResult(false);
				review.RowVersion = expected + 1;
				return Task.FromResult(true);
			}

			public Task<bool> TryAssignAsync(int departmentId, int id, string assignee, long expected, DateTime nowUtc, CancellationToken cancellationToken = default)
			{
				var row = Rows.FirstOrDefault(r => r.DepartmentId == departmentId && r.DispatchCaseReviewId == id && r.RowVersion == expected && DispatchCaseReviewRules.IsOpen(r));
				if (row == null) return Task.FromResult(false);
				row.AssignedToUserId = assignee;
				row.RowVersion++;
				return Task.FromResult(true);
			}

			public Task<bool> TryAcknowledgeAsync(int departmentId, int id, string userId, string comment, DateTime nowUtc, CancellationToken cancellationToken = default)
			{
				var row = Rows.FirstOrDefault(r => r.DepartmentId == departmentId && r.DispatchCaseReviewId == id && r.Status == (int)DispatchCaseReviewStatuses.Sent &&
					string.Equals(r.DispatcherUserId, userId, StringComparison.OrdinalIgnoreCase));
				if (row == null) return Task.FromResult(false);
				row.Status = (int)DispatchCaseReviewStatuses.Acknowledged;
				row.AcknowledgedOn = nowUtc;
				if (comment != null && row.DispatcherComment == null) { row.DispatcherComment = comment; row.DispatcherCommentOn = nowUtc; }
				return Task.FromResult(true);
			}

			public Task<bool> TryCommentAsync(int departmentId, int id, string userId, string comment, DateTime nowUtc, CancellationToken cancellationToken = default)
			{
				var row = Rows.FirstOrDefault(r => r.DepartmentId == departmentId && r.DispatchCaseReviewId == id && r.IsSent && r.DispatcherComment == null &&
					string.Equals(r.DispatcherUserId, userId, StringComparison.OrdinalIgnoreCase));
				if (row == null) return Task.FromResult(false);
				row.DispatcherComment = comment;
				row.DispatcherCommentOn = nowUtc;
				return Task.FromResult(true);
			}

			public Task<bool> TryDeleteOpenAsync(int departmentId, int id, CancellationToken cancellationToken = default) =>
				Task.FromResult(Rows.RemoveAll(r => r.DepartmentId == departmentId && r.DispatchCaseReviewId == id && DispatchCaseReviewRules.IsOpen(r)) == 1);
		}

		private sealed class Harness
		{
			public readonly FakeReviews Reviews = new FakeReviews();
			public readonly FakeRubrics Rubrics = new FakeRubrics();
			public readonly Mock<ICallProtocolsRepository> CallProtocols = new Mock<ICallProtocolsRepository>();
			public readonly Mock<ICallsService> Calls = new Mock<ICallsService>();
			public readonly Mock<IProtocolAccessService> Access = new Mock<IProtocolAccessService>();
			public readonly Mock<IProtocolCaseService> Cases = new Mock<IProtocolCaseService>();
			public readonly Mock<IProtocolVersionsRepository> Versions = new Mock<IProtocolVersionsRepository>();
			public readonly Mock<IAuthorizationService> Authorization = new Mock<IAuthorizationService>();
			public readonly Mock<IDepartmentsService> Departments = new Mock<IDepartmentsService>();
			public readonly Mock<IDispatchCaseReviewNotifier> Notifier = new Mock<IDispatchCaseReviewNotifier>();
			public readonly Mock<IEventAggregator> Events = new Mock<IEventAggregator>();
			public readonly Mock<IPermissionsService> Permissions = new Mock<IPermissionsService>();
			public readonly Mock<IPersonnelRolesService> Roles = new Mock<IPersonnelRolesService>();
			public readonly Mock<IDepartmentGroupsService> Groups = new Mock<IDepartmentGroupsService>();
			public readonly Mock<IProtectedReadService> ProtectedRead = new Mock<IProtectedReadService>();
			public readonly Mock<IProtectedGrantContext> Grant = new Mock<IProtectedGrantContext>();
			public readonly List<AuditEvent> Audits = new List<AuditEvent>();
			public readonly DispatchCaseReviewService Service;
			public CallProtocol Row = new CallProtocol { CallProtocolId = 11, CallId = 1011, DispatchProtocolId = 5, AttachedByUserId = Dispatcher };
			public Call Call = new Call { CallId = 1011, DepartmentId = Dept, ReportingUserId = Dispatcher, Priority = 1 };

			public Harness(IProtectedReadService seam = null, IProtectedGrantContext grant = null)
			{
				Access.Setup(a => a.GetReaderAsync(Dept, It.IsAny<string>())).ReturnsAsync((int d, string u) => new ProtocolReader
				{
					DepartmentId = d, UserId = u, IsCurrentMember = true,
					IsDepartmentAdmin = string.Equals(u, Admin, StringComparison.OrdinalIgnoreCase),
					CanReviewDispatchCases = string.Equals(u, Reviewer, StringComparison.OrdinalIgnoreCase) || string.Equals(u, Dispatcher + "-reviewer", StringComparison.OrdinalIgnoreCase)
				});
				Authorization.Setup(a => a.CanUserViewCallAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(true);
				CallProtocols.Setup(c => c.GetForDepartmentCallAsync(Dept, It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(() => Row);
				Calls.Setup(c => c.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(() => Call);
				Versions.Setup(v => v.GetByIdsAsync(Dept, It.IsAny<IEnumerable<int>>(), It.IsAny<bool>()))
					.ReturnsAsync(new List<ProtocolVersion> { new ProtocolVersion { ProtocolVersionId = 50, DepartmentId = Dept, Major = 2, Minor = 1, Status = (int)ProtocolVersionStatuses.Published } });
				Notifier.Setup(n => n.NotifySentAsync(It.IsAny<DispatchCaseReview>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
				Notifier.Setup(n => n.NotifyReplyAsync(It.IsAny<DispatchCaseReview>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
				Events.Setup(e => e.SendMessage(It.IsAny<AuditEvent>())).Callback<AuditEvent>(Audits.Add);
				Service = new DispatchCaseReviewService(Reviews, Rubrics, CallProtocols.Object, Calls.Object, Access.Object, Cases.Object, Versions.Object, Authorization.Object,
					Departments.Object, Notifier.Object, Events.Object, Permissions.Object, Roles.Object, Groups.Object,
					new Lazy<IProtectedReadService>(() => seam ?? ProtectedRead.Object), grant ?? Grant.Object);
			}

			public static string CaseJson(string code = "09") => new CallProtocolCase
			{
				ProtocolVersionId = 50, ProtocolCode = code, ProtocolName = "Cardiac arrest", Determinant = "09-E-01", Status = (int)ProtocolCaseStatuses.Complete,
				Steps = new List<ProtocolCaseStep>
				{
					new ProtocolCaseStep { NodeKey = "q1", Kind = (int)ProtocolDecisionNodeKinds.Question, Text = "Is the patient breathing?", Answer = "No" },
					new ProtocolCaseStep { NodeKey = "i1", Kind = (int)ProtocolDecisionNodeKinds.Instruction, Text = "Start compressions" },
					new ProtocolCaseStep { NodeKey = "o1", Kind = (int)ProtocolDecisionNodeKinds.Outcome, Text = "Echo" }
				}
			}.ToJson();

			public void AddCandidate(int callProtocolId, string attachedBy = Dispatcher)
			{
				Reviews.Candidates.Add(Candidate(callProtocolId, attachedBy));
				Reviews.Cases[callProtocolId] = CaseJson();
			}

			public DispatchCaseReview AddReview(DispatchCaseReviewStatuses status = DispatchCaseReviewStatuses.Queued, string dispatcher = Dispatcher)
			{
				var review = new DispatchCaseReview
				{
					DepartmentId = Dept, CallId = 1011, CallProtocolId = 11 + Reviews.Rows.Count, DispatchProtocolId = 5, ProtocolVersionId = 50, DispatcherUserId = dispatcher,
					Status = (int)status, CriteriaJson = JsonConvert.SerializeObject(TemplateCriteria()), CallLoggedOn = DateTime.UtcNow.AddDays(-2), RowVersion = 1,
					ItemsJson = DispatchCaseReviewItem.ToJson(DispatchCaseReviewItem.FromCase(CallProtocolCase.Parse(CaseJson()))),
					SentOn = status >= DispatchCaseReviewStatuses.Sent ? DateTime.UtcNow.AddDays(-1) : (DateTime?)null, ReviewerUserId = status >= DispatchCaseReviewStatuses.Sent ? Reviewer : null
				};
				Reviews.TryInsertAsync(review).Wait();
				return review;
			}

			public static DispatchCaseScoreInput FullScores() => new DispatchCaseScoreInput
			{
				Items = new Dictionary<string, DispatchCaseItemScore>
				{
					["q1"] = new DispatchCaseItemScore { Compliance = (int)DispatchCaseCompliance.Compliant },
					["i1"] = new DispatchCaseItemScore { Compliance = (int)DispatchCaseCompliance.Partial, Note = "Count aloud" }
				},
				Findings = HandScores(100, 100, 75),
				Comments = "Good call"
			};
		}

		private static DispatchCaseSampleRequest Request(int? count = 10, int? percent = null, string assignTo = null) => new DispatchCaseSampleRequest
		{
			FromUtc = DateTime.UtcNow.AddDays(-30), ToUtc = DateTime.UtcNow, Count = count, Percent = percent, AssignToUserId = assignTo, Determinant = " 09-e ", DispatchProtocolId = 5
		};

		[Test]
		public async Task Only_reviewers_sample_and_the_filters_reach_the_candidate_query()
		{
			var h = new Harness();
			h.AddCandidate(1);

			(await h.Service.SampleAsync(Dept, OtherMember, Request())).Result.Should().Be(DispatchCaseReviewResults.Forbidden);
			h.Reviews.Rows.Should().BeEmpty();

			var result = await h.Service.SampleAsync(Dept, Reviewer, Request());
			result.Result.Should().Be(DispatchCaseReviewResults.Done);
			h.Reviews.LastFilter.DispatchProtocolId.Should().Be(5);
			h.Reviews.LastFilter.Determinant.Should().Be("09-e", "the determinant is normalized; the column compares case-insensitively");
		}

		[Test]
		public async Task Adp_a_sealed_case_is_sampled_without_its_answers_and_shown_only_to_a_grant_holder()
		{
			// Round 8, catalog 39: the case answers are caller content. Sampling works without a grant (the items wait for a reader who may see
			// the case), the review never stores an answer, and the review page shows the answers only to a grant holder.
			using var kit = new Resgrid.Tests.Adp.AdpTestKit(departmentId: Dept);
			var h = new Harness(kit.Seam, kit.Attended(false));
			h.AddCandidate(1);
			h.Reviews.Cases[1] = kit.Seal("callprotocols.casejson", "1", Harness.CaseJson());

			var sampled = await h.Service.SampleAsync(Dept, Reviewer, Request(count: 1));
			sampled.Sampled.Should().ContainSingle("a protected department samples like any other");
			var review = sampled.Sampled[0];
			review.Determinant.Should().Be(Candidate(1).Determinant, "the determinant is operational");
			DispatchCaseReviewItem.Parse(review.ItemsJson).Should().BeEmpty("without a grant the items are deferred, never copied from a sealed case");
			kit.Decrypted.Should().BeEmpty();

			h.Row.CaseJson = kit.Seal("callprotocols.casejson", h.Row.CallProtocolId.ToString(), Harness.CaseJson());
			review.CallProtocolId = h.Row.CallProtocolId;
			var withheld = await h.Service.GetDetailAsync(Dept, Reviewer, review.DispatchCaseReviewId);
			withheld.Should().NotBeNull("the review page opens without a grant");
			withheld.CaseWithheld.Should().BeTrue("the page offers the step-up reveal");
			withheld.Case.Should().BeNull();
			withheld.Items.Should().BeEmpty();

			var granted = new Harness(kit.Seam, kit.Attended(true));
			granted.Row.CaseJson = kit.Seal("callprotocols.casejson", granted.Row.CallProtocolId.ToString(), Harness.CaseJson());
			granted.AddCandidate(2);
			granted.Reviews.Cases[2] = kit.Seal("callprotocols.casejson", "2", Harness.CaseJson());
			var second = (await granted.Service.SampleAsync(Dept, Reviewer, Request(count: 1))).Sampled.Single();
			DispatchCaseReviewItem.Parse(second.ItemsJson).Select(i => i.NodeKey).Should().Equal("q1", "i1");
			DispatchCaseReviewItem.Parse(second.ItemsJson).Should().OnlyContain(i => i.Answer == null, "a review never stores the caller's answers");

			second.CallProtocolId = granted.Row.CallProtocolId;
			var shown = await granted.Service.GetDetailAsync(Dept, Reviewer, second.DispatchCaseReviewId);
			shown.Items.Single(i => i.NodeKey == "q1").Answer.Should().Be("No", "a grant holder sees the answer, read from the call's case");
			shown.CaseWithheld.Should().BeFalse();
		}

		[Test]
		public async Task A_sample_queues_snapshots_and_never_draws_a_call_protocol_twice()
		{
			var h = new Harness();
			foreach (var id in Enumerable.Range(1, 6))
				h.AddCandidate(id);

			var first = await h.Service.SampleAsync(Dept, Reviewer, Request(count: 4));
			first.Sampled.Should().HaveCount(4);
			h.Rubrics.Rows.Should().ContainSingle(r => DispatchCaseRubrics.IsDispatchRubric(r), "the first sample of a department without a dispatch case rubric creates the template");

			var review = first.Sampled[0];
			review.Status.Should().Be((int)DispatchCaseReviewStatuses.Queued);
			review.DispatcherUserId.Should().Be(Dispatcher);
			review.VersionLabel.Should().Be("2.1");
			review.ProtocolCode.Should().Be("09");
			DispatchCaseReviewItem.Parse(review.ItemsJson).Select(i => i.NodeKey).Should().Equal("q1", "i1");
			JsonConvert.DeserializeObject<List<RmsQualityCriterion>>(review.CriteriaJson).Should().HaveCount(5);

			var second = await h.Service.SampleAsync(Dept, Reviewer, Request(count: 10));
			second.AlreadyReviewed.Should().Be(4);
			second.Sampled.Should().HaveCount(2);
			h.Reviews.Rows.Select(r => r.CallProtocolId).Should().OnlyHaveUniqueItems();
			h.Audits.Count(a => a.Type == AuditLogTypes.DispatchCasesSampled).Should().Be(2);
		}

		[Test]
		public async Task A_sample_assigned_to_an_officer_leaves_out_that_officers_own_calls_and_a_percentage_rounds_up()
		{
			var h = new Harness();
			foreach (var id in Enumerable.Range(1, 9))
				h.AddCandidate(id);
			h.AddCandidate(10, attachedBy: Reviewer.ToLowerInvariant());

			var preview = await h.Service.PreviewSampleAsync(Dept, Reviewer, Request(count: null, percent: 15, assignTo: Reviewer));
			(preview.Candidates, preview.AssigneesOwn, preview.Eligible, preview.SampleSize).Should().Be((10, 1, 9, 2));
			h.Reviews.Rows.Should().BeEmpty("a preview writes nothing");

			var result = await h.Service.SampleAsync(Dept, Reviewer, Request(count: null, percent: 15, assignTo: Reviewer));
			result.Sampled.Should().HaveCount(2).And.OnlyContain(r => r.AssignedToUserId == Reviewer && r.CallProtocolId != 10);
		}

		[Test]
		public async Task Invalid_requests_and_assignees_who_do_not_review_are_refused()
		{
			var h = new Harness();
			h.AddCandidate(1);

			(await h.Service.SampleAsync(Dept, Reviewer, Request(count: 0))).Result.Should().Be(DispatchCaseReviewResults.Invalid);
			(await h.Service.SampleAsync(Dept, Reviewer, Request(count: null, percent: 101))).Result.Should().Be(DispatchCaseReviewResults.Invalid);
			(await h.Service.SampleAsync(Dept, Reviewer, new DispatchCaseSampleRequest { FromUtc = DateTime.UtcNow, ToUtc = DateTime.UtcNow.AddDays(-1), Count = 1 })).Result
				.Should().Be(DispatchCaseReviewResults.Invalid);
			(await h.Service.SampleAsync(Dept, Reviewer, new DispatchCaseSampleRequest { FromUtc = DateTime.UtcNow.AddDays(-400), ToUtc = DateTime.UtcNow, Count = 1 })).Result
				.Should().Be(DispatchCaseReviewResults.Invalid, "a sample covers at most a year");
			(await h.Service.SampleAsync(Dept, Reviewer, Request(assignTo: OtherMember))).Result.Should().Be(DispatchCaseReviewResults.Invalid);
			h.Reviews.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task A_reviewer_never_scores_their_own_call()
		{
			var h = new Harness();
			var own = h.AddReview(dispatcher: Reviewer.ToLowerInvariant());
			(await h.Service.SaveScoresAsync(Dept, Reviewer, own.DispatchCaseReviewId, Harness.FullScores(), true)).Should().Be(DispatchCaseReviewResults.OwnCase);

			// Also when the reviewer reported the call or overrode its determinant.
			var reported = h.AddReview();
			h.Call.ReportingUserId = Reviewer;
			(await h.Service.SaveScoresAsync(Dept, Reviewer, reported.DispatchCaseReviewId, Harness.FullScores(), true)).Should().Be(DispatchCaseReviewResults.OwnCase);

			h.Call.ReportingUserId = Dispatcher;
			var record = CallProtocolCase.Parse(Harness.CaseJson());
			record.Overridden = true;
			record.OverriddenByUserId = Reviewer;
			h.Row.CaseJson = record.ToJson();
			(await h.Service.SaveScoresAsync(Dept, Reviewer, reported.DispatchCaseReviewId, Harness.FullScores(), true)).Should().Be(DispatchCaseReviewResults.OwnCase);
			(await h.Service.TakeAsync(Dept, Reviewer, own.DispatchCaseReviewId)).Should().Be(DispatchCaseReviewResults.OwnCase);

			var detail = await h.Service.GetDetailAsync(Dept, Reviewer, reported.DispatchCaseReviewId);
			detail.IsOwnCase.Should().BeTrue();
			detail.CanScore.Should().BeFalse();
			h.Notifier.Verify(n => n.NotifySentAsync(It.IsAny<DispatchCaseReview>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_review_is_sent_only_when_everything_is_scored_and_then_never_changes()
		{
			var h = new Harness();
			var review = h.AddReview();

			var partial = Harness.FullScores();
			partial.Items.Remove("i1");
			(await h.Service.SaveScoresAsync(Dept, Reviewer, review.DispatchCaseReviewId, partial, true)).Should().Be(DispatchCaseReviewResults.Incomplete);
			(await h.Service.SaveScoresAsync(Dept, Reviewer, review.DispatchCaseReviewId, partial, false)).Should().Be(DispatchCaseReviewResults.Done);
			review.Status.Should().Be((int)DispatchCaseReviewStatuses.InReview);
			review.AssignedToUserId.Should().Be(Reviewer, "the first save takes the review into the reviewer's queue");

			// The draft keeps its hand scores; sending posts only the missing item.
			var rest = new DispatchCaseScoreInput { Items = { ["i1"] = new DispatchCaseItemScore { Compliance = (int)DispatchCaseCompliance.Partial } }, Comments = "Good call" };
			(await h.Service.SaveScoresAsync(Dept, Reviewer, review.DispatchCaseReviewId, rest, true)).Should().Be(DispatchCaseReviewResults.Done);

			review.Status.Should().Be((int)DispatchCaseReviewStatuses.Sent);
			review.SentOn.Should().NotBeNull();
			review.ReviewerUserId.Should().Be(Reviewer);
			(review.QuestionCount, review.QuestionPoints, review.InstructionCount, review.InstructionPoints).Should().Be((1, 100, 1, 50));
			// (2*100 + 3*100 + 3*100 + 3*50 + 1*75) / 12 = 85.4.
			review.Score.Should().Be(85);
			JsonConvert.DeserializeObject<List<RmsQualityFinding>>(review.FindingsJson).Select(f => f.Key).Should()
				.Equal("case_entry", "questions", "determinant", "instructions", "caller_care");
			h.Notifier.Verify(n => n.NotifySentAsync(review, It.IsAny<CancellationToken>()), Times.Once);
			h.Audits.Should().ContainSingle(a => a.Type == AuditLogTypes.DispatchCaseReviewSent);

			(await h.Service.SaveScoresAsync(Dept, Reviewer, review.DispatchCaseReviewId, Harness.FullScores(), false)).Should().Be(DispatchCaseReviewResults.AlreadySent);
			(await h.Service.RemoveAsync(Dept, Reviewer, review.DispatchCaseReviewId)).Should().Be(DispatchCaseReviewResults.AlreadySent);
		}

		[Test]
		public async Task Scores_out_of_range_and_other_reviewers_queue_items_are_refused()
		{
			var h = new Harness();
			var review = h.AddReview();
			var bad = Harness.FullScores();
			bad.Findings[0].Score = 101;
			(await h.Service.SaveScoresAsync(Dept, Reviewer, review.DispatchCaseReviewId, bad, false)).Should().Be(DispatchCaseReviewResults.Invalid);
			bad = Harness.FullScores();
			bad.Items["q1"].Compliance = 9;
			(await h.Service.SaveScoresAsync(Dept, Reviewer, review.DispatchCaseReviewId, bad, false)).Should().Be(DispatchCaseReviewResults.Invalid);

			review.AssignedToUserId = Admin;
			(await h.Service.SaveScoresAsync(Dept, Reviewer, review.DispatchCaseReviewId, Harness.FullScores(), false)).Should().Be(DispatchCaseReviewResults.Conflict);
			(await h.Service.TakeAsync(Dept, Reviewer, review.DispatchCaseReviewId)).Should().Be(DispatchCaseReviewResults.Conflict);
			(await h.Service.RemoveAsync(Dept, Reviewer, review.DispatchCaseReviewId)).Should().Be(DispatchCaseReviewResults.Conflict);
			(await h.Service.SaveScoresAsync(Dept, OtherMember, review.DispatchCaseReviewId, Harness.FullScores(), false)).Should().Be(DispatchCaseReviewResults.Forbidden);
		}

		[Test]
		public async Task The_dispatcher_sees_only_their_own_reviews_and_only_once_sent()
		{
			var h = new Harness();
			var queued = h.AddReview();
			var mine = h.AddReview(DispatchCaseReviewStatuses.Sent);
			var someoneElses = h.AddReview(DispatchCaseReviewStatuses.Sent, dispatcher: OtherMember);

			(await h.Service.GetDetailAsync(Dept, Dispatcher, queued.DispatchCaseReviewId)).Should().BeNull("a review in the queue is not the dispatcher's to read yet");
			(await h.Service.GetDetailAsync(Dept, Dispatcher, someoneElses.DispatchCaseReviewId)).Should().BeNull();
			var detail = await h.Service.GetDetailAsync(Dept, Dispatcher.ToUpperInvariant(), mine.DispatchCaseReviewId);
			detail.Should().NotBeNull();
			detail.IsDispatcher.Should().BeTrue();
			detail.IsReviewer.Should().BeFalse();
			detail.CanAcknowledge.Should().BeTrue();

			(await h.Service.GetMineAsync(Dept, Dispatcher)).Select(r => r.DispatchCaseReviewId).Should().Equal(mine.DispatchCaseReviewId);
			(await h.Service.GetMineAsync(Dept, OtherMember)).Select(r => r.DispatchCaseReviewId).Should().Equal(someoneElses.DispatchCaseReviewId);
			(await h.Service.GetQueueAsync(Dept, Dispatcher, false)).Should().BeEmpty("the queue is for reviewers");
			(await h.Service.GetDetailAsync(Dept, Reviewer, someoneElses.DispatchCaseReviewId)).Should().NotBeNull("a reviewer reads every review");

			h.Authorization.Setup(a => a.CanUserViewCallAsync(Reviewer, It.IsAny<int>())).ReturnsAsync(false);
			(await h.Service.GetDetailAsync(Dept, Reviewer, someoneElses.DispatchCaseReviewId)).Should().BeNull("a reviewer who cannot see the call cannot read its review");
		}

		[Test]
		public async Task A_review_is_acknowledged_once_and_replied_to_once_by_its_dispatcher_only()
		{
			var h = new Harness();
			var queued = h.AddReview();
			var review = h.AddReview(DispatchCaseReviewStatuses.Sent);

			(await h.Service.AcknowledgeAsync(Dept, Dispatcher, queued.DispatchCaseReviewId, null)).Should().Be(DispatchCaseReviewResults.NotSent);
			(await h.Service.AcknowledgeAsync(Dept, OtherMember, review.DispatchCaseReviewId, null)).Should().Be(DispatchCaseReviewResults.NotFound);
			(await h.Service.AcknowledgeAsync(Dept, Reviewer, review.DispatchCaseReviewId, null)).Should().Be(DispatchCaseReviewResults.NotFound, "a reviewer does not acknowledge for the dispatcher");

			(await h.Service.AcknowledgeAsync(Dept, Dispatcher, review.DispatchCaseReviewId, null)).Should().Be(DispatchCaseReviewResults.Done);
			review.Status.Should().Be((int)DispatchCaseReviewStatuses.Acknowledged);
			(await h.Service.AcknowledgeAsync(Dept, Dispatcher, review.DispatchCaseReviewId, "again")).Should().Be(DispatchCaseReviewResults.AlreadyAcknowledged);
			review.DispatcherComment.Should().BeNull();

			(await h.Service.CommentAsync(Dept, Dispatcher, review.DispatchCaseReviewId, "  ")).Should().Be(DispatchCaseReviewResults.CommentRequired);
			(await h.Service.CommentAsync(Dept, Dispatcher, review.DispatchCaseReviewId, "Thanks, noted")).Should().Be(DispatchCaseReviewResults.Done);
			(await h.Service.CommentAsync(Dept, Dispatcher, review.DispatchCaseReviewId, "And another")).Should().Be(DispatchCaseReviewResults.AlreadyCommented);
			review.DispatcherComment.Should().Be("Thanks, noted");
			h.Notifier.Verify(n => n.NotifyReplyAsync(review, It.IsAny<CancellationToken>()), Times.Once);
			h.Audits.Count(a => a.Type == AuditLogTypes.DispatchCaseReviewAcknowledged).Should().Be(1);
			h.Audits.Count(a => a.Type == AuditLogTypes.DispatchCaseReviewReplied).Should().Be(1);
		}

		[Test]
		public async Task Acknowledging_with_a_comment_is_the_one_reply()
		{
			var h = new Harness();
			var review = h.AddReview(DispatchCaseReviewStatuses.Sent);

			(await h.Service.AcknowledgeAsync(Dept, Dispatcher, review.DispatchCaseReviewId, "Understood")).Should().Be(DispatchCaseReviewResults.Done);
			review.DispatcherComment.Should().Be("Understood");
			(await h.Service.CommentAsync(Dept, Dispatcher, review.DispatchCaseReviewId, "More")).Should().Be(DispatchCaseReviewResults.AlreadyCommented);
			h.Notifier.Verify(n => n.NotifyReplyAsync(review, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_notice_that_fails_never_undoes_the_review()
		{
			var h = new Harness();
			var review = h.AddReview();
			h.Notifier.Setup(n => n.NotifySentAsync(It.IsAny<DispatchCaseReview>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("push down"));

			(await h.Service.SaveScoresAsync(Dept, Reviewer, review.DispatchCaseReviewId, Harness.FullScores(), true)).Should().Be(DispatchCaseReviewResults.Done);
			review.Status.Should().Be((int)DispatchCaseReviewStatuses.Sent);
		}

		[Test]
		public async Task Rubrics_are_edited_by_administrators_and_reviewers_only_seed_the_first()
		{
			var h = new Harness();
			(await h.Service.CreateTemplateRubricAsync(Dept, OtherMember, null)).Should().BeNull();
			(await h.Service.CreateTemplateRubricAsync(Dept, Reviewer, null)).Should().NotBeNull();
			(await h.Service.CreateTemplateRubricAsync(Dept, Reviewer, null)).Should().BeNull("a reviewer only seeds the department's first rubric");
			(await h.Service.CreateTemplateRubricAsync(Dept, Admin, null)).Should().NotBeNull();

			var criteria = new List<RmsQualityCriterion> { new RmsQualityCriterion { Key = "questions", Text = "Questions", Weight = 2 } };
			(await h.Service.SaveRubricAsync(Dept, Reviewer, null, "Mine", true, criteria)).Should().BeNull();
			var saved = await h.Service.SaveRubricAsync(Dept, Admin, null, "Short", true, criteria);
			saved.DefinitionKey.Should().Be(DispatchCaseRubrics.DefinitionKey);

			// A Records rubric is not a dispatch case rubric and is neither listed nor editable here.
			h.Rubrics.Rows.Add(new RmsQualityRubric { RmsQualityRubricId = "rms", DepartmentId = Dept, Name = "Records", DefinitionKey = "fire_incident", IsActive = true, CriteriaJson = "[]" });
			(await h.Service.GetRubricsAsync(Dept, Reviewer)).Should().HaveCount(3).And.OnlyContain(r => DispatchCaseRubrics.IsDispatchRubric(r));
			(await h.Service.SaveRubricAsync(Dept, Admin, "rms", "Hijack", true, criteria)).Should().BeNull();
			h.Audits.Count(a => a.Type == AuditLogTypes.DispatchCaseRubricSaved).Should().Be(3);
		}

		[Test]
		public async Task Reviewers_are_admins_and_the_holders_of_the_permission_row_among_active_members()
		{
			var h = new Harness();
			h.Departments.Setup(d => d.GetActiveAdminsForDepartmentAsync(Dept)).ReturnsAsync(new List<Resgrid.Model.Identity.IdentityUser> { new Resgrid.Model.Identity.IdentityUser { Id = Admin } });
			h.Departments.Setup(d => d.GetActiveMemberUserIdsAsync(Dept)).ReturnsAsync(new HashSet<string>(new[] { Admin, Reviewer, Dispatcher }, StringComparer.OrdinalIgnoreCase));
			h.Permissions.Setup(p => p.GetPermissionByDepartmentTypeAsync(Dept, PermissionTypes.ReviewDispatchCases))
				.ReturnsAsync(new Permission { PermissionType = (int)PermissionTypes.ReviewDispatchCases, Action = (int)PermissionActions.DepartmentAdminsAndSelectRoles, Data = "3" });
			h.Roles.Setup(r => r.GetRoleByIdAsync(3)).ReturnsAsync(new PersonnelRole { PersonnelRoleId = 3, DepartmentId = Dept });
			h.Roles.Setup(r => r.GetAllMembersOfRoleAsync(3)).ReturnsAsync(new List<PersonnelRoleUser> { new PersonnelRoleUser { UserId = Reviewer.ToLowerInvariant() }, new PersonnelRoleUser { UserId = "gone" } });

			(await h.Service.GetReviewerUserIdsAsync(Dept)).Should().BeEquivalentTo(new[] { Admin, Reviewer.ToLowerInvariant() });
		}

		[Test]
		public async Task The_gdpr_export_holds_only_the_members_own_sent_reviews()
		{
			var h = new Harness();
			h.AddReview();
			var mine = h.AddReview(DispatchCaseReviewStatuses.Sent);
			h.AddReview(DispatchCaseReviewStatuses.Sent, dispatcher: OtherMember);
			var section = new DispatchCaseReviewGdprExportSection(h.Reviews);

			section.EntryName.Should().Be("dispatch_case_reviews.json");
			var json = JsonConvert.SerializeObject(await section.BuildAsync(Dispatcher.ToUpperInvariant(), Dept));
			var rows = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);
			rows.Should().ContainSingle();
			Convert.ToInt32(rows[0]["DispatchCaseReviewId"]).Should().Be(mine.DispatchCaseReviewId);
			json.Should().Contain("Is the patient breathing?");
		}

		#endregion Service

		#region Reports

		private static DispatchCaseReview Sent(int protocolId, int? versionId, string dispatcher, int? score, int qc, int qp, int ic, int ip, DateTime logged, bool overridden = false,
			bool acknowledged = false, string items = null) => new DispatchCaseReview
		{
			DepartmentId = Dept, DispatchProtocolId = protocolId, ProtocolVersionId = versionId, ProtocolCode = "P" + protocolId, ProtocolName = "Protocol " + protocolId, VersionLabel = versionId?.ToString(),
			DispatcherUserId = dispatcher, Score = score, QuestionCount = qc, QuestionPoints = qp, InstructionCount = ic, InstructionPoints = ip, CallLoggedOn = logged, Overridden = overridden,
			Status = (int)(acknowledged ? DispatchCaseReviewStatuses.Acknowledged : DispatchCaseReviewStatuses.Sent), SentOn = logged.AddDays(1), ItemsJson = items
		};

		[Test]
		public void Compliance_adds_points_before_dividing_and_groups_by_protocol_version_dispatcher_and_month()
		{
			var jan = new DateTime(2026, 1, 31, 23, 30, 0, DateTimeKind.Utc);
			var reviews = new List<DispatchCaseReview>
			{
				Sent(1, 10, "disp-a", 90, 10, 900, 2, 200, jan, overridden: true, acknowledged: true),
				Sent(1, 11, "DISP-A", 60, 1, 0, 0, 0, jan.AddDays(5)),
				Sent(2, 20, "disp-b", null, 0, 0, 1, 50, jan.AddDays(10)),
				new DispatchCaseReview { DepartmentId = Dept, DispatchProtocolId = 1, Status = (int)DispatchCaseReviewStatuses.Queued, CallLoggedOn = jan }
			};

			// Department time two hours ahead: 31 January 23:30 UTC is already February.
			var report = DispatchCaseReports.Aggregate(reviews, new DispatchCaseReportFilter(), id => id == null ? null : "Name " + id.ToLowerInvariant(), d => d.AddHours(2));

			report.Total.Reviews.Should().Be(3, "queued reviews are not in the report");
			report.Total.AverageScore.Should().Be(75, "the mean of the scored reviews");
			report.Total.QuestionCompliance.Should().Be(81.8, "900 points over 11 questions, not the mean of 90% and 0%");
			report.Total.InstructionCompliance.Should().Be(83.3);
			report.Total.Overrides.Should().Be(1);
			report.Total.Acknowledged.Should().Be(1);

			report.ByProtocol.Select(r => (r.Label, r.Reviews)).Should().Equal(("P1 Protocol 1", 2), ("P2 Protocol 2", 1));
			report.ByVersion.Should().HaveCount(3);
			report.ByVersion[0].Label.Should().Be("P1 Protocol 1 v10");
			report.ByDispatcher.Should().HaveCount(2, "dispatcher ids compare case-insensitively");
			report.ByDispatcher.Single(r => r.Label == "Name disp-a").Reviews.Should().Be(2);
			report.ByMonth.Select(r => (r.Key, r.Reviews)).Should().Equal(("2026-02", 3));
			report.ByItem.Should().BeEmpty("questions are grouped only for one protocol");
		}

		[Test]
		public void For_one_protocol_compliance_is_reported_question_by_question()
		{
			var items1 = DispatchCaseReviewItem.ToJson(new[] { Q("q1", DispatchCaseCompliance.Compliant), Q("q2", DispatchCaseCompliance.NotCompliant), I("i1", DispatchCaseCompliance.NotApplicable) });
			var items2 = DispatchCaseReviewItem.ToJson(new[] { Q("q1", DispatchCaseCompliance.Partial), Q("q2", DispatchCaseCompliance.NotCompliant), I("i1", DispatchCaseCompliance.Compliant) });
			var when = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
			var report = DispatchCaseReports.Aggregate(new List<DispatchCaseReview> { Sent(1, 10, "a", 50, 2, 100, 0, 0, when, items: items1), Sent(1, 10, "a", 50, 2, 50, 1, 100, when, items: items2) },
				new DispatchCaseReportFilter { DispatchProtocolId = 1 }, null, null);

			report.ByItem.Select(r => (r.Key, r.QuestionCompliance, r.InstructionCompliance)).Should()
				.Equal(new (string, double?, double?)[] { ("q2", 0, null), ("q1", 75, null), ("i1", null, 100) });
		}

		#endregion Reports

		#region Seams and identifiers

		[Test]
		public void Identifiers_are_the_allocated_ones()
		{
			((int)PermissionTypes.ReviewDispatchCases).Should().Be(134);
			((int)EventTypes.DispatchCaseReviewNotice).Should().Be(76);
			typeof(EventTypes).GetField(nameof(EventTypes.DispatchCaseReviewNotice)).GetCustomAttributes(typeof(NotMappedAttribute), false).Should().NotBeEmpty();
			Resgrid.Web.Areas.User.Models.Security.PermissionScreenCatalog.Get((int)PermissionTypes.ReviewDispatchCases).Should().NotBeNull();
			DispatchCaseReviewNotifier.EventCode(42).Should().Be("PQ:42");
			new ProtocolReader { IsCurrentMember = true, CanReviewDispatchCases = true }.MayReviewDispatchCases.Should().BeTrue();
			new ProtocolReader { IsCurrentMember = false, IsDepartmentAdmin = true }.MayReviewDispatchCases.Should().BeFalse();
		}

		[Test]
		public async Task The_notice_goes_only_to_an_active_dispatcher_with_the_review_code()
		{
			var communication = new Mock<ICommunicationService>();
			communication.Setup(c => c.SendNotificationAsync(It.IsAny<string>(), Dept, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(),
				It.IsAny<UserProfile>(), It.IsAny<bool>(), It.IsAny<string>())).ReturnsAsync(true);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetActiveMemberUserIdsAsync(Dept)).ReturnsAsync(new HashSet<string>(new[] { Dispatcher }, StringComparer.OrdinalIgnoreCase));
			departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept });
			var notifier = new DispatchCaseReviewNotifier(new Lazy<ICommunicationService>(() => communication.Object), departments.Object, Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<IUserProfileService>());

			var review = new DispatchCaseReview { DispatchCaseReviewId = 9, DepartmentId = Dept, DispatcherUserId = Dispatcher.ToUpperInvariant(), ReviewerUserId = "gone", CallNumber = "26-1", Score = 12 };
			// Other fixtures switch broadcasting off process-wide; this one needs it on.
			var doNotBroadcast = Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast;
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = false;
			try
			{
				(await notifier.NotifySentAsync(review)).Should().BeTrue();
				(await notifier.NotifyReplyAsync(review)).Should().BeFalse("the reviewer is no longer an active member");
			}
			finally
			{
				Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = doNotBroadcast;
			}
			communication.Verify(c => c.SendNotificationAsync(Dispatcher.ToUpperInvariant(), Dept, It.Is<string>(m => m.Contains("26-1") && !m.Contains("12")), It.IsAny<string>(),
				It.IsAny<Department>(), It.IsAny<string>(), null, false, "PQ:9"), Times.Once);
		}

		#endregion Seams and identifiers

		#region Workflow events (round 7C, triggers 255-256)

		[Test]
		public async Task Sending_raises_review_sent_once_and_a_draft_or_refused_send_raises_nothing()
		{
			var h = new Harness();
			var raised = new List<DispatchCaseWorkflowEvent>();
			h.Events.Setup(e => e.SendMessage(It.IsAny<DispatchCaseWorkflowEvent>())).Callback<DispatchCaseWorkflowEvent>(raised.Add);
			var review = h.AddReview();

			var partial = Harness.FullScores();
			partial.Items.Remove("i1");
			(await h.Service.SaveScoresAsync(Dept, Reviewer, review.DispatchCaseReviewId, partial, true)).Should().Be(DispatchCaseReviewResults.Incomplete);
			(await h.Service.SaveScoresAsync(Dept, Reviewer, review.DispatchCaseReviewId, partial, false)).Should().Be(DispatchCaseReviewResults.Done);
			raised.Should().BeEmpty("a draft is not sent");

			var full = Harness.FullScores();
			full.Comments = "Good call on the bleeding control";
			(await h.Service.SaveScoresAsync(Dept, Reviewer, review.DispatchCaseReviewId, full, true)).Should().Be(DispatchCaseReviewResults.Done);
			(await h.Service.SaveScoresAsync(Dept, Reviewer, review.DispatchCaseReviewId, Harness.FullScores(), true)).Should().Be(DispatchCaseReviewResults.AlreadySent);

			raised.Should().ContainSingle();
			var sent = raised.Single();
			sent.Trigger.Should().Be(WorkflowTriggerEventType.DispatchCaseReviewSent);
			sent.ReviewId.Should().Be(review.DispatchCaseReviewId);
			sent.UserId.Should().Be(Dispatcher, "the member the review is about");
			sent.ReviewerUserId.Should().Be(Reviewer);
			sent.ReviewScore.Should().Be(review.Score);
			JsonConvert.SerializeObject(sent).Should().NotContain("bleeding control", "the reviewer's comments stay in the review");
		}

		[Test]
		public async Task Acknowledging_raises_review_acknowledged_once()
		{
			var h = new Harness();
			var raised = new List<DispatchCaseWorkflowEvent>();
			h.Events.Setup(e => e.SendMessage(It.IsAny<DispatchCaseWorkflowEvent>())).Callback<DispatchCaseWorkflowEvent>(raised.Add);
			var queued = h.AddReview();
			var review = h.AddReview(DispatchCaseReviewStatuses.Sent);

			(await h.Service.AcknowledgeAsync(Dept, Dispatcher, queued.DispatchCaseReviewId, null)).Should().Be(DispatchCaseReviewResults.NotSent);
			(await h.Service.AcknowledgeAsync(Dept, Dispatcher, review.DispatchCaseReviewId, "Understood, will slow down")).Should().Be(DispatchCaseReviewResults.Done);
			(await h.Service.AcknowledgeAsync(Dept, Dispatcher, review.DispatchCaseReviewId, null)).Should().Be(DispatchCaseReviewResults.AlreadyAcknowledged);

			raised.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.DispatchCaseReviewAcknowledged && e.ReviewId == review.DispatchCaseReviewId && e.UserId == Dispatcher);
			JsonConvert.SerializeObject(raised.Single()).Should().NotContain("slow down");
		}

		#endregion Workflow events (round 7C, triggers 255-256)
	}
}
