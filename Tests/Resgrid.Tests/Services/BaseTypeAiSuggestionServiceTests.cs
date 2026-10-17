using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Ai;
using Resgrid.Config;
using Resgrid.Llm;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Enhanced AI base-type suggestions (BaseTypeDecisions on Clef-Flash): only list names leave Resgrid, always as a
	/// DepartmentConfiguration request, only for entitled departments, only for names the keyword suggester has no rule for, and a
	/// suggestion is offered only when the model is confident it is not "none".
	/// </summary>
	[TestFixture, NonParallelizable]
	public class BaseTypeAiSuggestionServiceTests
	{
		private const int DepartmentId = 4127;
		private const string WorkersAiRoute = "https://api.cloudflare.com/client/v4/accounts/0123456789abcdef0123456789abcdef/ai/run/@cf/cloudflare/clef-flash";
		// No keyword rule matches this in any list, so it is always a model question.
		private const string Unhinted = "Zulu Kilo";

		private (string, string, bool, string, string, string, string) _savedConfig;
		private Mock<IEnhancedAiAccessService> _enhancedAi;
		private Mock<ICacheProvider> _cache;
		private Mock<IDecisionClient> _decider;
		private List<DecisionRequest> _requests;
		private Func<BaseTypeQuestion, DecisionAnswer> _answer;
		private BaseTypeAiSuggestionService _service;

		[SetUp]
		public void SetUp()
		{
			_savedConfig = (AiConfig.DecisionProvider, AiConfig.DecisionEndpoint, AiConfig.AllowPrivateEndpoint, AiConfig.DecisionApiKey, AiConfig.DecisionModel,
				AiConfig.DecisionModelRevision, AiConfig.DecisionRuntimeDigest);
			AiConfig.DecisionProvider = DecisionProviders.WorkersAi;
			AiConfig.DecisionEndpoint = WorkersAiRoute;
			AiConfig.AllowPrivateEndpoint = false;
			AiConfig.DecisionApiKey = "cf-token";
			AiConfig.DecisionModel = "Cloudflare/clef-flash";
			AiConfig.DecisionModelRevision = "";
			AiConfig.DecisionRuntimeDigest = "";

			_enhancedAi = new Mock<IEnhancedAiAccessService>(MockBehavior.Strict);
			_enhancedAi.Setup(x => x.CanUseAsync(DepartmentId, BaseTypeAiSuggestionService.CapabilityFlag)).ReturnsAsync(true);
			_cache = new Mock<ICacheProvider>();
			_cache.Setup(x => x.GetStringAsync(It.IsAny<string>())).ReturnsAsync((string)null);
			_cache.Setup(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);

			_requests = new List<DecisionRequest>();
			_answer = _ => Answer(BaseTypeDecisions.None, 0.9);
			_decider = new Mock<IDecisionClient>(MockBehavior.Strict);
			_decider.Setup(x => x.DecideAsync(It.IsAny<DecisionRequest>(), It.IsAny<CancellationToken>()))
				.Returns<DecisionRequest, CancellationToken>((request, _) =>
				{
					_requests.Add(request);
					// The questions are rebuilt from the request itself, so each answer is for the name actually sent.
					var answers = request.Questions.ToDictionary(q => q.Id, q => _answer(QuestionOf(q)), StringComparer.Ordinal);
					return Task.FromResult(new DecisionResult(answers, 120 * request.Questions.Count));
				});

			_service = new BaseTypeAiSuggestionService(_enhancedAi.Object, _cache.Object, new Lazy<IDecisionClient>(() => _decider.Object));
		}

		[TearDown]
		public void TearDown()
		{
			(AiConfig.DecisionProvider, AiConfig.DecisionEndpoint, AiConfig.AllowPrivateEndpoint, AiConfig.DecisionApiKey, AiConfig.DecisionModel,
				AiConfig.DecisionModelRevision, AiConfig.DecisionRuntimeDigest) = _savedConfig;
		}

		private static DecisionAnswer Answer(string choice, double confidence) =>
			new(choice, confidence, new Dictionary<string, double>(StringComparer.Ordinal) { [choice] = confidence });

		private static string Option(int baseType) => "b" + baseType;

		// The list and the name are recovered from the wire text, never from what the test passed in.
		private static BaseTypeQuestion QuestionOf(DecisionQuestion question)
		{
			var start = question.Instructions.IndexOf('"') + 1;
			var label = question.Instructions.Substring(start, question.Instructions.IndexOf('"', start) - start);
			var list = question.Instructions.Contains("unit statuses") ? BaseTypeList.UnitStatus :
				question.Instructions.Contains("personnel statuses") ? BaseTypeList.PersonnelStatus :
				question.Instructions.Contains("staffing levels") ? BaseTypeList.Staffing :
				question.Instructions.Contains("call types") ? BaseTypeList.CallType :
				question.Instructions.Contains("unit types") ? BaseTypeList.UnitType : BaseTypeList.CallPriority;
			return new BaseTypeQuestion(list, label);
		}

		private static CustomState StatusSet(CustomStateTypes type, params CustomStateDetail[] details) => new()
		{
			CustomStateId = 77, DepartmentId = DepartmentId, Type = (int)type, Name = "SET-NAME-SENTINEL", Description = "SET-DESCRIPTION-SENTINEL",
			Details = details.ToList()
		};

		private static CustomStateDetail Detail(int id, string name, int baseType = -1) => new()
		{
			CustomStateDetailId = id, CustomStateId = 77, ButtonText = name, ButtonColor = "#C0FFEE", TextColor = "#BADA55", BaseType = baseType,
			NextStateDetailIds = "DETAIL-NEXT-SENTINEL", Order = id
		};

		private static void SetEveryOtherString(object entity, string labelProperty)
		{
			foreach (var property in entity.GetType().GetProperties().Where(p => p.PropertyType == typeof(string) && p.CanWrite && p.Name != labelProperty))
				property.SetValue(entity, $"SENTINEL-{entity.GetType().Name}-{property.Name}");
		}

		[Test]
		public void Every_list_reads_its_names_from_one_registry_field_only()
		{
			foreach (var list in Enum.GetValues<BaseTypeList>())
			{
				BaseTypeDecisions.Sources.Should().ContainKey(list);
				AiNonPrivateFields.ConfigurationLabels.Should().Contain(BaseTypeDecisions.Sources[list],
					$"{list} names may leave Resgrid only through a field ADP does not protect");
			}

			var detail = Detail(1, Unhinted);
			SetEveryOtherString(detail, nameof(CustomStateDetail.ButtonText));
			BaseTypeDecisions.LabelOf(BaseTypeList.UnitStatus, detail).Should().Be(Unhinted);
			BaseTypeDecisions.LabelOf(BaseTypeList.CallType, detail).Should().BeNull("a status option is not a call type");
			BaseTypeDecisions.LabelOf(BaseTypeList.CallType, new Call { Type = "Structure Fire", Name = "Someone's house" })
				.Should().BeNull("a call's own Calls.Type is call data, not a department configuration label, and is never read");
			BaseTypeDecisions.LabelOf(BaseTypeList.UnitStatus, Detail(2, "  Code\t\"7\"  " + new string('x', 200))).Should()
				.StartWith("Code '7' x").And.HaveLength(BaseTypeDecisions.MaxLabelLength, "names are one bounded line with no quote of their own");
		}

		[Test]
		public async Task Only_list_names_are_sent_for_every_list()
		{
			var detail = Detail(11, Unhinted);
			SetEveryOtherString(detail, nameof(CustomStateDetail.ButtonText));
			var callType = new CallType { CallTypeId = 21, DepartmentId = DepartmentId, Type = Unhinted + " Call" };
			SetEveryOtherString(callType, nameof(CallType.Type));
			var unitType = new UnitType { UnitTypeId = 31, DepartmentId = DepartmentId, Type = Unhinted + " Unit" };
			SetEveryOtherString(unitType, nameof(UnitType.Type));
			var priority = new DepartmentCallPriority { DepartmentCallPriorityId = 41, DepartmentId = DepartmentId, Name = Unhinted + " Priority" };
			SetEveryOtherString(priority, nameof(DepartmentCallPriority.Name));

			await _service.SuggestStatusesAsync(DepartmentId, StatusSet(CustomStateTypes.Unit, detail), CancellationToken.None);
			await _service.SuggestCallTypeClassesAsync(DepartmentId, new[] { callType }, CancellationToken.None);
			await _service.SuggestUnitTypeClassesAsync(DepartmentId, new[] { unitType }, CancellationToken.None);
			await _service.SuggestCallPrioritiesAsync(DepartmentId, new[] { priority }, CancellationToken.None);

			_requests.Should().HaveCount(4);
			var sent = string.Concat(_requests.Select(r => Encoding.UTF8.GetString(SystemOneDecisionClient.Body(r))));
			foreach (var label in new[] { Unhinted, Unhinted + " Call", Unhinted + " Unit", Unhinted + " Priority" })
				sent.Should().Contain($"\\u0022{label}\\u0022", "each entry's name is in its own question");
			sent.Should().NotContain("SENTINEL", "no field outside the registry is ever sent");
			sent.Should().NotContain("#C0FFEE").And.NotContain("#BADA55");
			sent.Should().NotContain(DepartmentId.ToString(), "not even the department id");
			_requests.SelectMany(r => r.Questions).Should().OnlyContain(q => q.Type == DecisionQuestionType.Choice && q.Options[0].Id == BaseTypeDecisions.None,
				"one choice question per entry, with none listed first");
		}

		[Test]
		public async Task Every_request_is_department_configuration_and_Workers_AI_accepts_it()
		{
			await _service.SuggestCallTypeClassesAsync(DepartmentId, new[] { new CallType { CallTypeId = 5, DepartmentId = DepartmentId, Type = Unhinted } }, CancellationToken.None);
			_requests.Should().ContainSingle().Which.DataClass.Should().Be(DecisionDataClass.DepartmentConfiguration);

			// The real Workers AI transport, end to end: it accepts the request and the answer comes back as a suggestion.
			var handler = new Handler("{\"result\":{\"answers\":{\"q0\":{\"type\":\"choice\",\"choice\":\"b6\",\"confidence\":0.8,\"probabilities\":{" +
				string.Join(",", BaseTypeDecisions.Values(BaseTypeList.CallType).Select(v => $"\"b{v}\":{(v == 6 ? "0.8" : "0")}").Prepend("\"none\":0.2")) +
				"}}},\"usage\":{\"prompt_tokens\":420}},\"success\":true,\"errors\":[],\"messages\":[]}");
			using var workersAi = new SystemOneDecisionClient(new HttpClient(handler), new Uri(WorkersAiRoute), "cf-token", workersAi: true);
			var service = new BaseTypeAiSuggestionService(_enhancedAi.Object, _cache.Object, new Lazy<IDecisionClient>(() => workersAi));

			var suggestions = await service.SuggestCallTypeClassesAsync(DepartmentId, new[] { new CallType { CallTypeId = 5, DepartmentId = DepartmentId, Type = Unhinted } }, CancellationToken.None);

			suggestions.Should().Equal(new Dictionary<int, int> { [5] = (int)CallTypeClasses.Alarm });
			handler.Sent.Should().Contain(Unhinted).And.NotContain("DataClass");
			Assert.ThrowsAsync<LlmUnavailableException>(async () => await workersAi.DecideAsync(_requests[0] with { DataClass = DecisionDataClass.DepartmentContent }, CancellationToken.None),
				"the transport still refuses department content");
		}

		[Test]
		public async Task Without_Enhanced_AI_nothing_is_sent()
		{
			_enhancedAi.Setup(x => x.CanUseAsync(DepartmentId, BaseTypeAiSuggestionService.CapabilityFlag)).ReturnsAsync(false);

			var suggestions = await _service.SuggestStatusesAsync(DepartmentId, StatusSet(CustomStateTypes.Personnel, Detail(1, Unhinted)), CancellationToken.None);

			suggestions.Should().BeEmpty();
			_decider.Verify(x => x.DecideAsync(It.IsAny<DecisionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
			_cache.Verify(x => x.GetStringAsync(It.IsAny<string>()), Times.Never, "not even cached answers are shown without the add-on");
		}

		[Test]
		public async Task Without_a_resolvable_decision_model_nothing_is_sent_or_checked()
		{
			AiConfig.DecisionProvider = "OpenAi";

			(await _service.SuggestCallTypeClassesAsync(DepartmentId, new[] { new CallType { CallTypeId = 5, DepartmentId = DepartmentId, Type = Unhinted } }, CancellationToken.None))
				.Should().BeEmpty();

			_decider.Verify(x => x.DecideAsync(It.IsAny<DecisionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
			_enhancedAi.Verify(x => x.CanUseAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never, "the billing check is skipped when there is no model to ask");
		}

		[Test]
		public async Task Low_confidence_and_none_answers_give_no_suggestion()
		{
			_answer = q => q.Label switch
			{
				"Alpha Kilo" => Answer(Option((int)ActionBaseTypes.Searching), BaseTypeAiSuggestionService.MinimumConfidence - 0.01),
				"Bravo Kilo" => Answer(BaseTypeDecisions.None, 0.95),
				"Charlie Kilo" => Answer(Option((int)ActionBaseTypes.Searching), BaseTypeAiSuggestionService.MinimumConfidence),
				_ => throw new InvalidOperationException(q.Label)
			};

			var suggestions = await _service.SuggestStatusesAsync(DepartmentId,
				StatusSet(CustomStateTypes.Personnel, Detail(1, "Alpha Kilo"), Detail(2, "Bravo Kilo"), Detail(3, "Charlie Kilo")), CancellationToken.None);

			suggestions.Should().Equal(new Dictionary<int, int> { [3] = (int)ActionBaseTypes.Searching });
		}

		[Test]
		public async Task The_keyword_suggester_wins_when_it_has_a_rule()
		{
			var suggestions = await _service.SuggestStatusesAsync(DepartmentId,
				StatusSet(CustomStateTypes.Unit, Detail(1, "In Quarters"), Detail(2, "Responding"), Detail(3, Unhinted)), CancellationToken.None);

			suggestions.Should().BeEmpty("the model answered none for the only name it was asked");
			_requests.Should().ContainSingle().Which.Questions.Should().ContainSingle().Which.Instructions.Should().Contain(Unhinted);
			Encoding.UTF8.GetString(SystemOneDecisionClient.Body(_requests[0])).Should().NotContain("\\u0022In Quarters\\u0022").And.NotContain("\\u0022Responding\\u0022");

			_requests.Clear();
			_enhancedAi.Invocations.Clear();
			(await _service.SuggestCallTypeClassesAsync(DepartmentId, new[] { new CallType { CallTypeId = 9, DepartmentId = DepartmentId, Type = "Structure Fire" } }, CancellationToken.None))
				.Should().BeEmpty();
			_requests.Should().BeEmpty("a name with a keyword rule is never sent");
			_enhancedAi.Verify(x => x.CanUseAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never, "nothing to ask means no entitlement check either");
		}

		[Test]
		public async Task Entries_that_already_have_a_meaning_or_belong_elsewhere_are_never_sent()
		{
			_answer = _ => Answer(Option((int)UnitTypeClasses.Rescue), 0.9);

			var suggestions = await _service.SuggestUnitTypeClassesAsync(DepartmentId, new[]
			{
				new UnitType { UnitTypeId = 1, DepartmentId = DepartmentId, Type = "Has Meaning", BaseType = (int)UnitTypeClasses.Support },
				new UnitType { UnitTypeId = 2, DepartmentId = DepartmentId + 1, Type = "Other Department" },
				new UnitType { UnitTypeId = 3, DepartmentId = DepartmentId, Type = Unhinted },
				new UnitType { UnitTypeId = 4, DepartmentId = DepartmentId, Type = "   " }
			}, CancellationToken.None);

			suggestions.Should().Equal(new Dictionary<int, int> { [3] = (int)UnitTypeClasses.Rescue });
			_requests.Single().Questions.Should().ContainSingle();
			var otherSet = StatusSet(CustomStateTypes.Unit, Detail(5, Unhinted));
			otherSet.DepartmentId = DepartmentId + 1;
			(await _service.SuggestStatusesAsync(DepartmentId, otherSet, CancellationToken.None)).Should().BeEmpty("a set from another department is never asked about");
			_requests.Should().ContainSingle();
		}

		[Test]
		public async Task A_unit_status_is_never_suggested_as_not_responding()
		{
			await _service.SuggestStatusesAsync(DepartmentId, StatusSet(CustomStateTypes.Unit, Detail(1, Unhinted)), CancellationToken.None);
			await _service.SuggestStatusesAsync(DepartmentId, StatusSet(CustomStateTypes.Personnel, Detail(1, Unhinted)), CancellationToken.None);

			_requests[0].Questions[0].Options.Select(o => o.Id).Should().NotContain(Option((int)ActionBaseTypes.NotResponding));
			_requests[1].Questions[0].Options.Select(o => o.Id).Should().Contain(Option((int)ActionBaseTypes.NotResponding));

			// An answer outside the options asked is a failed request: no suggestion, and no error page.
			_answer = _ => Answer(Option((int)ActionBaseTypes.NotResponding), 0.99);
			(await _service.SuggestStatusesAsync(DepartmentId, StatusSet(CustomStateTypes.Unit, Detail(1, "Delta Kilo")), CancellationToken.None)).Should().BeEmpty();
		}

		[Test]
		public async Task Many_names_are_batched_within_the_transport_bounds_and_each_is_asked_once()
		{
			var details = Enumerable.Range(1, 100).Select(i => Detail(i, $"Kilo {i:D3}")).ToList();
			details.Add(Detail(101, "KILO-001"));
			_answer = _ => Answer(Option((int)ActionBaseTypes.Standby), 0.9);

			var suggestions = await _service.SuggestStatusesAsync(DepartmentId, StatusSet(CustomStateTypes.Personnel, details.ToArray()), CancellationToken.None);

			suggestions.Should().HaveCount(101, "names that normalize alike share one answer");
			_requests.Should().HaveCountGreaterThan(1);
			_requests.Should().OnlyContain(r => r.Questions.Count <= SystemOneDecisionClient.MaxQuestions && SystemOneDecisionClient.Body(r).Length <= SystemOneDecisionClient.MaxBytes);
			_requests.SelectMany(r => r.Questions).Should().HaveCount(100);
		}

		[Test]
		public async Task Cached_answers_are_reused_and_new_ones_are_cached()
		{
			var stored = new Dictionary<string, string>();
			_cache.Setup(x => x.GetStringAsync(It.IsAny<string>())).ReturnsAsync((string key) => stored.TryGetValue(key, out var value) ? value : null);
			_cache.Setup(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
				.ReturnsAsync((string key, string value, TimeSpan _) => { stored[key] = value; return true; });
			_answer = _ => Answer(Option((int)CallPriority.High), 0.75);
			var priority = new DepartmentCallPriority { DepartmentCallPriorityId = 8, DepartmentId = DepartmentId, Name = Unhinted };

			(await _service.SuggestCallPrioritiesAsync(DepartmentId, new[] { priority }, CancellationToken.None)).Should().Equal(new Dictionary<int, int> { [8] = (int)CallPriority.High });
			(await _service.SuggestCallPrioritiesAsync(DepartmentId, new[] { priority }, CancellationToken.None)).Should().Equal(new Dictionary<int, int> { [8] = (int)CallPriority.High });

			_requests.Should().ContainSingle("the second page load reads the cached answer");
			stored.Should().ContainSingle().Which.Key.Should().NotContain(Unhinted).And.NotContain("Zulu", "keys hold a hash, not the name");
		}

		[Test]
		public async Task A_failed_model_gives_no_suggestions_and_no_error()
		{
			_decider.Setup(x => x.DecideAsync(It.IsAny<DecisionRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new LlmUnavailableException());

			(await _service.SuggestCallTypeClassesAsync(DepartmentId, new[] { new CallType { CallTypeId = 5, DepartmentId = DepartmentId, Type = Unhinted } }, CancellationToken.None))
				.Should().BeEmpty();
			_cache.Verify(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never, "failures are never cached");
		}

		private sealed class Handler(string body) : HttpMessageHandler
		{
			public string Sent { get; private set; }

			protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				Sent = await request.Content.ReadAsStringAsync(cancellationToken);
				return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
			}
		}
	}
}
