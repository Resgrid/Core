using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Ai;
using Resgrid.Llm;
using Resgrid.Model.AiDispatch;

namespace Resgrid.Tests.AiDispatch
{
	/// <summary>
	/// The decision-model transport (SystemOne, Cloudflare Clef / Clef-Flash on SGLang), the AI dispatch question set built on it, and
	/// per-department prefix-cache isolation on the generative endpoint.
	/// </summary>
	[TestFixture]
	public class DecisionModelTests
	{
		private static readonly AiDispatchContext Context = new("STRUCTURE FIRE 123 MAIN ST",
			new[] { new AiDispatchChoice(11, "Structure Fire"), new AiDispatchChoice(12, "Medical") },
			new[] { new AiDispatchChoice(21, "High"), new AiDispatchChoice(22, "Low") },
			new[] { new AiDispatchCandidateCall(301, "26-120", "Vehicle Accident", "5 Oak Ave", 10) });

		private sealed class Handler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
		{
			public HttpRequestMessage Request { get; private set; }
			public string Sent { get; private set; }
			protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				Request = request;
				Sent = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
				return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
			}
		}

		private static DecisionRequest Simple() => new("Cloudflare/clef-flash", "Smoke showing, 12 Elm St", new[]
		{
			new DecisionQuestion("urgent", DecisionQuestionType.Noul, "Is it urgent?"),
			new DecisionQuestion("team", DecisionQuestionType.Choice, "Which team?", new[] { new DecisionOption("none", "No team"), new DecisionOption("fire", "Fire"), new DecisionOption("ems", "EMS") }),
			new DecisionQuestion("severity", DecisionQuestionType.Score, "How severe?", new[] { new DecisionOption("0", "Minor"), new DecisionOption("1", "Moderate"), new DecisionOption("2", "Major") })
		});

		private const string Answers = "{\"model\":\"Cloudflare/clef-flash\",\"answers\":{" +
			"\"urgent\":{\"type\":\"noul\",\"noul\":0.83}," +
			"\"team\":{\"type\":\"choice\",\"choice\":\"fire\",\"confidence\":0.7,\"probabilities\":{\"none\":0.1,\"fire\":0.7,\"ems\":0.2}}," +
			"\"severity\":{\"type\":\"score\",\"score\":1.4,\"confidence\":0.5,\"legend\":{\"0\":\"Minor\",\"1\":\"Moderate\",\"2\":\"Major\"},\"probabilities\":{\"0\":0.1,\"1\":0.4,\"2\":0.5}}}," +
			"\"usage\":{\"input_tokens\":312,\"output_tokens\":0}}";

		[Test]
		public void The_wire_body_is_the_systemone_shape()
		{
			using var document = JsonDocument.Parse(SystemOneDecisionClient.Body(Simple()));
			var root = document.RootElement;

			root.GetProperty("model").GetString().Should().Be("Cloudflare/clef-flash");
			root.GetProperty("state").GetString().Should().Be("Smoke showing, 12 Elm St");
			var questions = root.GetProperty("questions");
			questions.GetProperty("urgent").GetProperty("type").GetString().Should().Be("noul");
			questions.GetProperty("urgent").TryGetProperty("criteria", out _).Should().BeFalse();
			questions.GetProperty("team").GetProperty("criteria").EnumerateObject().Select(p => p.Name).Should().Equal("none", "fire", "ems");
			questions.GetProperty("severity").GetProperty("criteria").EnumerateArray().Select(e => e.GetString()).Should().Equal("Minor", "Moderate", "Major");
		}

		[Test]
		public void Requests_the_server_would_refuse_never_leave()
		{
			DecisionRequest With(params DecisionQuestion[] questions) => new("m", "s", questions);
			var one = new[] { new DecisionOption("a", "A") };

			Assert.Throws<LlmUnavailableException>(() => SystemOneDecisionClient.Body(With()));
			Assert.Throws<LlmUnavailableException>(() => SystemOneDecisionClient.Body(With(new DecisionQuestion("bad id!", DecisionQuestionType.Noul, "q"))));
			Assert.Throws<LlmUnavailableException>(() => SystemOneDecisionClient.Body(With(new DecisionQuestion("q", DecisionQuestionType.Noul, "q"), new DecisionQuestion("q", DecisionQuestionType.Noul, "q"))));
			Assert.Throws<LlmUnavailableException>(() => SystemOneDecisionClient.Body(With(new DecisionQuestion("q", DecisionQuestionType.Choice, "q", one))));
			Assert.Throws<LlmUnavailableException>(() => SystemOneDecisionClient.Body(With(new DecisionQuestion("q", DecisionQuestionType.Choice, "q",
				Enumerable.Range(0, SystemOneDecisionClient.MaxOptions + 1).Select(i => new DecisionOption("o" + i, "O")).ToArray()))));
			Assert.Throws<LlmUnavailableException>(() => SystemOneDecisionClient.Body(With(new DecisionQuestion("q", DecisionQuestionType.Score, "q",
				new[] { new DecisionOption("1", "A"), new DecisionOption("0", "B") }))));
			Assert.Throws<LlmUnavailableException>(() => SystemOneDecisionClient.Body(With(new DecisionQuestion("q", DecisionQuestionType.Noul, "q", one))));
			Assert.Throws<LlmUnavailableException>(() => SystemOneDecisionClient.Body(new DecisionRequest("m", new string('x', SystemOneDecisionClient.MaxBytes), new[] { new DecisionQuestion("q", DecisionQuestionType.Noul, "q") })));
		}

		[Test]
		public void Answers_are_read_for_exactly_the_questions_asked()
		{
			var result = SystemOneDecisionClient.Read(Simple(), Encoding.UTF8.GetBytes(Answers), 1000);

			result.InputTokens.Should().Be(312);
			result.Answers["urgent"].Choice.Should().Be("true");
			result.Answers["urgent"].Probability("true").Should().BeApproximately(0.83, 1e-9);
			result.Answers["team"].Choice.Should().Be("fire");
			result.Answers["team"].Confidence.Should().Be(0.7);
			result.Answers["severity"].Choice.Should().Be("2");
			result.Answers["severity"].Score.Should().BeApproximately(1.4, 1e-9);
		}

		[TestCase("\"team\":{\"type\":\"choice\",\"choice\":\"fire\",\"probabilities\":{\"fire\":0.8,\"ems\":0.2}}", TestName = "Missing option")]
		[TestCase("\"team\":{\"type\":\"choice\",\"choice\":\"fire\",\"probabilities\":{\"none\":0.1,\"fire\":0.7,\"police\":0.2}}", TestName = "Unknown option")]
		[TestCase("\"team\":{\"type\":\"choice\",\"choice\":\"fire\",\"probabilities\":{\"none\":0.5,\"fire\":0.7,\"ems\":0.2}}", TestName = "Not a softmax")]
		[TestCase("\"team\":{\"type\":\"choice\",\"choice\":\"fire\",\"probabilities\":{\"none\":0.1,\"fire\":\"0.7\",\"ems\":0.2}}", TestName = "Not a number")]
		public void An_answer_outside_the_options_asked_is_a_failure(string team)
		{
			var body = "{\"answers\":{\"urgent\":{\"noul\":0.5}," + team + ",\"severity\":{\"probabilities\":{\"0\":0.2,\"1\":0.3,\"2\":0.5}}}}";

			FluentActions.Invoking(() => SystemOneDecisionClient.Read(Simple(), Encoding.UTF8.GetBytes(body), 900)).Should().Throw<Exception>();
		}

		[Test]
		public void Ties_go_to_the_earlier_option_and_usage_falls_back_to_an_estimate()
		{
			var body = "{\"answers\":{\"urgent\":{\"probabilities\":{\"true\":0.2,\"false\":0.8}}," +
				"\"team\":{\"choice\":\"fire\",\"probabilities\":{\"none\":0.4,\"fire\":0.4,\"ems\":0.2}}," +
				"\"severity\":{\"probabilities\":{\"0\":0.2,\"1\":0.3,\"2\":0.5}}},\"usage\":{\"prompt_tokens\":77}}";

			var result = SystemOneDecisionClient.Read(Simple(), Encoding.UTF8.GetBytes(body), 900);
			result.Answers["urgent"].Choice.Should().Be("false");
			result.Answers["team"].Choice.Should().Be("none", "the server's own pick is not trusted on a tie");
			result.InputTokens.Should().Be(77);

			SystemOneDecisionClient.Read(Simple(), Encoding.UTF8.GetBytes(body.Replace(",\"usage\":{\"prompt_tokens\":77}", "")), 900).InputTokens.Should().Be(300);
		}

		[Test]
		public async Task The_transport_posts_with_the_bearer_and_sanitizes_failures()
		{
			var handler = new Handler(Answers);
			using (var client = new SystemOneDecisionClient(new HttpClient(handler), new Uri("https://decide.invalid/v1/systemone"), "private-key"))
				(await client.DecideAsync(Simple(), CancellationToken.None)).Answers.Should().HaveCount(3);
			handler.Request.Headers.Authorization.ToString().Should().Be("Bearer private-key");
			handler.Request.RequestUri.AbsolutePath.Should().Be("/v1/systemone");

			using var failing = new SystemOneDecisionClient(new HttpClient(new Handler("secret stack trace", HttpStatusCode.InternalServerError)), new Uri("https://decide.invalid/v1/systemone"), "private-key");
			var error = Assert.ThrowsAsync<LlmUnavailableException>(async () => await failing.DecideAsync(Simple(), CancellationToken.None));
			error.ToString().Should().NotContain("secret stack trace").And.NotContain("private-key");
		}

		[Test]
		public void The_decision_path_and_the_chat_path_are_not_interchangeable()
		{
			OperatorEndpointPolicy.ValidateUri("https://h.invalid/v1/systemone", false, OperatorEndpointPolicy.DecisionPath).AbsolutePath.Should().Be("/v1/systemone");
			Assert.Throws<LlmUnavailableException>(() => OperatorEndpointPolicy.ValidateUri("https://h.invalid/v1/systemone", false));
			Assert.Throws<LlmUnavailableException>(() => OperatorEndpointPolicy.ValidateUri("https://h.invalid/v1/chat/completions", false, OperatorEndpointPolicy.DecisionPath));
			Assert.Throws<LlmUnavailableException>(() => OperatorEndpointPolicy.ValidateUri("https://h.invalid/flush_cache", false, "/flush_cache"));
			Assert.Throws<LlmUnavailableException>(() => OperatorEndpointPolicy.ValidateUri("http://10.0.0.5/v1/systemone", false, OperatorEndpointPolicy.DecisionPath));
			OperatorEndpointPolicy.ValidateUri("http://10.0.0.5/v1/systemone", true, OperatorEndpointPolicy.DecisionPath).Scheme.Should().Be("http");
		}

		[Test]
		public void Dispatch_questions_offer_only_the_departments_lists_with_none_first()
		{
			var request = AiDispatchDecisions.Request("Cloudflare/clef-flash", Context);

			request.State.Should().Be(Context.Message);
			request.Questions.Select(q => q.Id).Should().Equal(AiDispatchDecisions.IsDispatchQuestion, AiDispatchDecisions.CallTypeQuestion, AiDispatchDecisions.PriorityQuestion, AiDispatchDecisions.DuplicateQuestion);
			request.Questions[1].Options.Select(o => o.Id).Should().Equal("none", "t11", "t12");
			request.Questions[2].Options.Select(o => o.Id).Should().Equal("none", "p21", "p22");
			request.Questions[3].Options.Select(o => o.Description).Should().Contain("Call 26-120: Vehicle Accident, 5 Oak Ave, logged 10 minutes ago");
			FluentActions.Invoking(() => SystemOneDecisionClient.Body(request)).Should().NotThrow();

			AiDispatchDecisions.Request("m", new AiDispatchContext("x", Array.Empty<AiDispatchChoice>(), Array.Empty<AiDispatchChoice>(), Array.Empty<AiDispatchCandidateCall>()))
				.Questions.Select(q => q.Id).Should().Equal(AiDispatchDecisions.IsDispatchQuestion);
		}

		[Test]
		public void Answers_map_back_onto_the_lists_and_merge_by_field()
		{
			static DecisionAnswer Choice(string id, double p) => new(id, p, new Dictionary<string, double> { [id] = p });
			var answers = new Dictionary<string, DecisionAnswer>
			{
				[AiDispatchDecisions.IsDispatchQuestion] = new("true", 0.9, new Dictionary<string, double> { ["true"] = 0.9, ["false"] = 0.1 }),
				[AiDispatchDecisions.CallTypeQuestion] = Choice("t12", 0.8),
				[AiDispatchDecisions.PriorityQuestion] = Choice("p21", 0.5),
				[AiDispatchDecisions.DuplicateQuestion] = Choice("c301", 0.7)
			};
			var decision = AiDispatchDecisions.Read(new DecisionResult(answers, 250), Context);
			decision.CallType.Id.Should().Be(12);
			decision.RelatedCall.CallId.Should().Be(301);

			var generated = new AiDispatchEnrichment(false, 0.55, "Title", new AiDispatchChoice(11, "Structure Fire"), null, "123 Main St", null, null, null, null, "Summary", 1);
			var merged = AiDispatchDecisions.Merge(generated, decision, 0.6);
			merged.IsDispatch.Should().BeTrue();
			merged.Confidence.Should().BeApproximately(0.9, 1e-9);
			merged.CallType.Id.Should().Be(12);
			merged.Priority.Should().BeNull("0.5 is below the floor");
			merged.RelatedCall.CallId.Should().Be(301);
			merged.Address.Should().BeNull("the generative model was below the floor, so none of its text is kept");
			merged.RejectedCount.Should().Be(1);

			answers[AiDispatchDecisions.CallTypeQuestion] = Choice("t99", 0.99);
			FluentActions.Invoking(() => AiDispatchDecisions.Read(new DecisionResult(answers, 250), Context)).Should().Throw<ArgumentException>();
			answers.Remove(AiDispatchDecisions.CallTypeQuestion);
			FluentActions.Invoking(() => AiDispatchDecisions.Read(new DecisionResult(answers, 250), Context)).Should().Throw<ArgumentException>();
		}

		[Test]
		public async Task Workers_AI_answers_are_read_inside_the_cloudflare_envelope()
		{
			var envelope = "{\"result\":" + Answers + ",\"success\":true,\"errors\":[],\"messages\":[]}";
			var handler = new Handler(envelope);
			var endpoint = new Uri("https://api.cloudflare.com/client/v4/accounts/0123456789abcdef0123456789abcdef/ai/run/@cf/cloudflare/clef-flash");
			using (var client = new SystemOneDecisionClient(new HttpClient(handler), endpoint, "cf-token", workersAi: true))
				(await client.DecideAsync(Simple() with { DataClass = DecisionDataClass.NonSensitive }, CancellationToken.None)).Answers["team"].Choice.Should().Be("fire");
			handler.Request.RequestUri.AbsolutePath.Should().EndWith("/ai/run/@cf/cloudflare/clef-flash");

			FluentActions.Invoking(() => SystemOneDecisionClient.Read(Simple(), Encoding.UTF8.GetBytes(envelope.Replace("\"success\":true", "\"success\":false")), 900, true))
				.Should().Throw<LlmUnavailableException>("a failed Cloudflare call is never read as answers");
			FluentActions.Invoking(() => SystemOneDecisionClient.Read(Simple(), Encoding.UTF8.GetBytes(Answers), 900, true))
				.Should().Throw<LlmUnavailableException>("Workers AI always wraps its answers");
		}

		[TestCase("https://api.cloudflare.com/client/v4/accounts/0123456789abcdef0123456789abcdef/ai/run/@cf/cloudflare/clef-flash", "clef-flash")]
		[TestCase("https://api.cloudflare.com/client/v4/accounts/0123456789abcdef0123456789abcdef/ai/run/@cf/cloudflare/clef", "clef")]
		[TestCase("http://api.cloudflare.com/client/v4/accounts/0123456789abcdef0123456789abcdef/ai/run/@cf/cloudflare/clef-flash", null)]
		[TestCase("https://api.cloudflare.com.evil.invalid/client/v4/accounts/0123456789abcdef0123456789abcdef/ai/run/@cf/cloudflare/clef-flash", null)]
		[TestCase("https://api.cloudflare.com/client/v4/accounts/0123456789abcdef0123456789abcdef/ai/run/@cf/meta/llama-3.1-8b-instruct", null)]
		[TestCase("https://api.cloudflare.com/client/v4/accounts/not-an-account/ai/run/@cf/cloudflare/clef-flash", null)]
		[TestCase("https://api.cloudflare.com/client/v4/accounts/0123456789abcdef0123456789abcdef/ai/run/@cf/cloudflare/clef-flash?debug=1", null)]
		[TestCase("https://gateway.ai.cloudflare.com/v1/0123456789abcdef0123456789abcdef/g/workers-ai/@cf/cloudflare/clef-flash", null)]
		public void Only_the_direct_workers_ai_route_for_a_clef_model_is_accepted(string url, string model)
		{
			if (model == null)
			{
				Assert.Throws<LlmUnavailableException>(() => OperatorEndpointPolicy.ValidateWorkersAiDecisionUri(url, out _));
				return;
			}
			OperatorEndpointPolicy.ValidateWorkersAiDecisionUri(url, out var selector).Host.Should().Be("api.cloudflare.com");
			selector.Should().Be(model);
		}

		[Test]
		public void Decision_settings_resolve_per_provider()
		{
			const string route = "https://api.cloudflare.com/client/v4/accounts/0123456789abcdef0123456789abcdef/ai/run/@cf/cloudflare/clef-flash";
			var hosted = AiDecisionSettings.Resolve(DecisionProviders.WorkersAi, route, false, "cf-token", "Cloudflare/clef-flash", "", "");
			hosted.Remote.Should().BeTrue();
			hosted.WireModel.Should().Be("clef-flash");
			hosted.AuditModel.Should().Be("@cf/cloudflare/clef-flash");
			hosted.Revision.Should().BeNull();

			var own = AiDecisionSettings.Resolve(DecisionProviders.SelfHosted, "http://10.40.0.10:8444/v1/systemone", true, "k", "Cloudflare/clef-flash", new string('c', 40), "sha256:" + new string('d', 64));
			own.Remote.Should().BeFalse();
			own.WireModel.Should().Be("Cloudflare/clef-flash");

			AiDecisionSettings.Resolve(DecisionProviders.SelfHosted, "http://10.40.0.10:8444/v1/systemone", true, "k", "Cloudflare/clef-flash", "", "").Should().BeNull("self-hosted must be pinned");
			AiDecisionSettings.Resolve(DecisionProviders.WorkersAi, route, false, "cf-token", "Cloudflare/clef", "", "").Should().BeNull("the route must run the configured model");
			AiDecisionSettings.Resolve("OpenAi", route, false, "cf-token", "Cloudflare/clef-flash", "", "").Should().BeNull();
			AiDecisionSettings.Resolve(DecisionProviders.WorkersAi, route, false, "cf-token", "Some/other-model", "", "").Should().BeNull();
		}

		[Test]
		public void An_ai_gateway_id_is_optional_and_only_for_workers_ai()
		{
			const string route = "https://api.cloudflare.com/client/v4/accounts/0123456789abcdef0123456789abcdef/ai/run/@cf/cloudflare/clef-flash";
			AiDecisionSettings.Resolve(DecisionProviders.WorkersAi, route, false, "cf-token", "Cloudflare/clef-flash", "", "", "resgrid-clef").GatewayId.Should().Be("resgrid-clef");
			AiDecisionSettings.Resolve(DecisionProviders.WorkersAi, route, false, "cf-token", "Cloudflare/clef-flash", "", "", " resgrid_clef ").GatewayId.Should().Be("resgrid_clef");
			AiDecisionSettings.Resolve(DecisionProviders.WorkersAi, route, false, "cf-token", "Cloudflare/clef-flash", "", "", "").GatewayId.Should().BeNull("blank calls Workers AI directly");
			AiDecisionSettings.Resolve(DecisionProviders.WorkersAi, route, false, "cf-token", "Cloudflare/clef-flash", "", "", "   ").GatewayId.Should().BeNull();
			AiDecisionSettings.Resolve(DecisionProviders.WorkersAi, route, false, "cf-token", "Cloudflare/clef-flash", "", "").GatewayId.Should().BeNull();

			AiDecisionSettings.Resolve(DecisionProviders.WorkersAi, route, false, "cf-token", "Cloudflare/clef-flash", "", "", "resgrid clef").Should().BeNull("a mistyped gateway must not quietly skip the gateway's limits");
			AiDecisionSettings.Resolve(DecisionProviders.WorkersAi, route, false, "cf-token", "Cloudflare/clef-flash", "", "", "g\r\nX-Injected: 1").Should().BeNull();
			AiDecisionSettings.Resolve(DecisionProviders.WorkersAi, route, false, "cf-token", "Cloudflare/clef-flash", "", "", new string('g', 65)).Should().BeNull();

			var own = AiDecisionSettings.Resolve(DecisionProviders.SelfHosted, "http://10.40.0.10:8444/v1/systemone", true, "k", "Cloudflare/clef-flash", new string('c', 40), "sha256:" + new string('d', 64), "resgrid-clef");
			own.Should().NotBeNull("a gateway id left over from Workers AI does not break a self-hosted endpoint");
			own.GatewayId.Should().BeNull("a Cloudflare gateway never applies to the operator's own server");
		}

		[Test]
		public async Task A_gateway_id_routes_the_same_workers_ai_request_through_ai_gateway()
		{
			var envelope = "{\"result\":" + Answers + ",\"success\":true,\"errors\":[],\"messages\":[]}";
			var endpoint = new Uri("https://api.cloudflare.com/client/v4/accounts/0123456789abcdef0123456789abcdef/ai/run/@cf/cloudflare/clef-flash");
			var request = Simple() with { DataClass = DecisionDataClass.DepartmentConfiguration };

			var routed = new Handler(envelope);
			using (var client = new SystemOneDecisionClient(new HttpClient(routed), endpoint, "cf-token", workersAi: true, gatewayId: "resgrid-clef"))
				(await client.DecideAsync(request, CancellationToken.None)).Answers.Should().HaveCount(3);
			routed.Request.RequestUri.Should().Be(endpoint, "the gateway is chosen by header; the destination stays api.cloudflare.com");
			routed.Request.Headers.GetValues(SystemOneDecisionClient.GatewayHeader).Should().Equal("resgrid-clef");
			routed.Request.Headers.Authorization.Parameter.Should().Be("cf-token");

			var direct = new Handler(envelope);
			using (var client = new SystemOneDecisionClient(new HttpClient(direct), endpoint, "cf-token", workersAi: true))
				await client.DecideAsync(request, CancellationToken.None);
			direct.Request.Headers.Contains(SystemOneDecisionClient.GatewayHeader).Should().BeFalse();
		}

		[Test]
		public void A_gateway_id_is_refused_for_a_self_hosted_endpoint_or_when_malformed()
		{
			var selfHosted = new Handler(Answers);
			using (var client = new SystemOneDecisionClient(new HttpClient(selfHosted), new Uri("https://decide.invalid/v1/systemone"), "k", gatewayId: "resgrid-clef"))
				Assert.ThrowsAsync<LlmUnavailableException>(async () => await client.DecideAsync(Simple(), CancellationToken.None));
			selfHosted.Request.Should().BeNull();

			var malformed = new Handler(Answers);
			var endpoint = new Uri("https://api.cloudflare.com/client/v4/accounts/0123456789abcdef0123456789abcdef/ai/run/@cf/cloudflare/clef-flash");
			using (var client = new SystemOneDecisionClient(new HttpClient(malformed), endpoint, "cf-token", workersAi: true, gatewayId: "g\r\nX-Injected: 1"))
				Assert.ThrowsAsync<LlmUnavailableException>(async () => await client.DecideAsync(Simple() with { DataClass = DecisionDataClass.NonSensitive }, CancellationToken.None));
			malformed.Request.Should().BeNull("nothing is sent with a header value that is not a gateway id");
		}

		[Test]
		public void Workers_AI_refuses_department_content_before_anything_is_sent()
		{
			var handler = new Handler(Answers);
			var endpoint = new Uri("https://api.cloudflare.com/client/v4/accounts/0123456789abcdef0123456789abcdef/ai/run/@cf/cloudflare/clef-flash");
			using var client = new SystemOneDecisionClient(new HttpClient(handler), endpoint, "cf-token", workersAi: true);

			Assert.ThrowsAsync<LlmUnavailableException>(async () => await client.DecideAsync(Simple(), CancellationToken.None));
			Assert.ThrowsAsync<LlmUnavailableException>(async () => await client.DecideAsync(AiDispatchDecisions.Request("clef-flash", Context), CancellationToken.None));
			handler.Request.Should().BeNull("no department content may leave Resgrid for a third-party host");
			Simple().DataClass.Should().Be(DecisionDataClass.DepartmentContent, "a new caller fails closed");
		}

		[Test]
		public async Task Workers_AI_accepts_non_private_department_configuration_labels()
		{
			var handler = new Handler("{\"result\":" + Answers + ",\"success\":true,\"errors\":[],\"messages\":[]}");
			var endpoint = new Uri("https://api.cloudflare.com/client/v4/accounts/0123456789abcdef0123456789abcdef/ai/run/@cf/cloudflare/clef-flash");
			using var client = new SystemOneDecisionClient(new HttpClient(handler), endpoint, "cf-token", workersAi: true);

			(await client.DecideAsync(Simple() with { DataClass = DecisionDataClass.DepartmentConfiguration }, CancellationToken.None)).Answers.Should().HaveCount(3);
			handler.Request.Should().NotBeNull();
		}

		[Test]
		public void Every_non_private_label_field_is_outside_the_adp_catalog()
		{
			var catalog = new Resgrid.Services.ProtectedFieldCatalog();
			catalog.IsProtectedField("Calls", "NatureOfCall").Should().BeTrue("a call's nature is protected, so the check below would catch a protected field");

			foreach (var field in AiNonPrivateFields.ConfigurationLabels)
			{
				var table = ((Resgrid.Model.IEntity)Activator.CreateInstance(field.Entity)).TableName;
				field.Entity.GetProperty(field.Property).Should().NotBeNull($"{field.Entity.Name}.{field.Property} must be a real column");
				catalog.IsProtectedField(table, field.Property).Should().BeFalse($"{table}.{field.Property} is ADP-protected and may not leave Resgrid");
			}
		}

		[Test]
		public async Task A_self_hosted_endpoint_scores_department_content()
		{
			var handler = new Handler(Answers);
			using var client = new SystemOneDecisionClient(new HttpClient(handler), new Uri("https://decide.invalid/v1/systemone"), "k");

			(await client.DecideAsync(Simple(), CancellationToken.None)).Answers.Should().HaveCount(3);
			handler.Sent.Should().NotContain("DataClass", "the data class never goes on the wire");
		}

		[Test]
		public void The_cache_salt_partitions_by_department_and_never_reveals_it()
		{
			var key = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

			var first = AiCacheIsolation.SaltFor(7, true, key, null);
			first.Should().Be(AiCacheIsolation.SaltFor(7, true, key, null), "the same department keeps reusing its own cache");
			first.Should().NotBe(AiCacheIsolation.SaltFor(8, true, key, null));
			first.Should().MatchRegex("^[A-Za-z0-9_-]{22}$", "an opaque 128-bit value, not the department id");
			AiCacheIsolation.SaltFor(7, true, "", key).Should().Be(first, "the audit key is the fallback");
			AiCacheIsolation.SaltFor(7, true, "", "short").Should().NotBe(AiCacheIsolation.SaltFor(7, true, "", "short"), "no key means no reuse at all");
			AiCacheIsolation.SaltFor(7, false, key, key).Should().BeNull();
		}

		[Test]
		public async Task The_generative_transport_sends_the_salt_only_when_set()
		{
			const string reply = "{\"choices\":[{\"message\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":1}}";
			var messages = new[] { new LlmMessage("system", "s"), new LlmMessage("user", "u") };

			var handler = new Handler(reply);
			using (var client = new OpenAiToolClient(new HttpClient(handler), new Uri("https://local.invalid/v1/chat/completions"), "k"))
				await client.CompleteAsync(new LlmRequest("m", messages, Array.Empty<LlmTool>(), 16, "abcdefghijklmnopqrstuv"), CancellationToken.None);
			JsonDocument.Parse(handler.Sent).RootElement.GetProperty("cache_salt").GetString().Should().Be("abcdefghijklmnopqrstuv");

			handler = new Handler(reply);
			using (var client = new OpenAiToolClient(new HttpClient(handler), new Uri("https://local.invalid/v1/chat/completions"), "k"))
				await client.CompleteAsync(new LlmRequest("m", messages, Array.Empty<LlmTool>(), 16), CancellationToken.None);
			JsonDocument.Parse(handler.Sent).RootElement.TryGetProperty("cache_salt", out _).Should().BeFalse();

			using var strict = new OpenAiToolClient(new HttpClient(new Handler(reply)), new Uri("https://local.invalid/v1/chat/completions"), "k");
			Assert.ThrowsAsync<LlmUnavailableException>(async () => await strict.CompleteAsync(new LlmRequest("m", messages, Array.Empty<LlmTool>(), 16, "dept:7"), CancellationToken.None));
		}
	}
}
