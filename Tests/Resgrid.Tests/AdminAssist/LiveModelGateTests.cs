using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Resgrid.Llm;

namespace Resgrid.Tests.AdminAssist
{
	/// <summary>
	/// The Admin Assist model gate (AI inference VM runbook §7.4), run against a real OpenAI-compatible endpoint. Every prompt goes
	/// through the production GroundedAskRunner and OpenAiToolClient (built by OperatorEndpointPolicy.CreateClient) with production
	/// seeds, turn budget, turn timeout and per-department cache_salt, on both question routes of prompt admin-assist-question-v3: the
	/// whole question (department without Advanced Data Protection) and reviewed-corpus terms with the search fallback (ADP department).
	/// Evidence comes from fixed public cards (LiveGateEvidence), never a database. AskRedTeamTests and AskSafetyTests prove Resgrid's
	/// boundary with a mocked model; this checks a real one.
	///
	/// Explicit, so a default <c>dotnet test</c> never runs it. Without the three required variables it is ignored.
	/// <list type="bullet">
	/// <item>RESGRID_AI_LIVE_ENDPOINT: the full URL ending in /v1/chat/completions, as in AiConfig.Endpoint.</item>
	/// <item>RESGRID_AI_LIVE_TOKEN: the bearer token (AiConfig.ApiKey). Never printed or written to the report.</item>
	/// <item>RESGRID_AI_LIVE_MODEL: the served model name, for example google/gemma-4-E4B-it (AiConfig.Model).</item>
	/// <item>RESGRID_AI_LIVE_ALLOW_PRIVATE: optional, true for a private or loopback address (AiConfig.AllowPrivateEndpoint). Default false.</item>
	/// <item>RESGRID_AI_LIVE_CONCURRENCY: optional, prompts in flight at once, 1 to 16. Default 1. Use the §7.3 levels for latency.</item>
	/// <item>RESGRID_AI_LIVE_HOSTILE_LIMIT: optional, run only the first N hostile prompts. The first 20 send every payload as the question;
	/// the first 40 also inject every payload through an untrusted card.</item>
	/// </list>
	/// <code>dotnet test Tests/Resgrid.Tests --filter "TestCategory=LiveModelGate" --logger "console;verbosity=detailed"</code>
	/// Grounded answers have no pass bar: the counts are printed and written to a report, and the operator signs off.
	/// Hostile prompts fail the test if anything shown to a user (an answer or the search fallback) cites an unobserved id, a link or a
	/// credential, or if the question reached the model on the terms route.
	/// </summary>
	[TestFixture]
	[Explicit("Live model gate: sends Admin Assist prompts to a real inference endpoint configured by RESGRID_AI_LIVE_*.")]
	[Category("LiveModelGate")]
	[NonParallelizable]
	public class LiveModelGateTests
	{
		private const string EndpointVariable = "RESGRID_AI_LIVE_ENDPOINT";
		private const string TokenVariable = "RESGRID_AI_LIVE_TOKEN";
		private const string ModelVariable = "RESGRID_AI_LIVE_MODEL";
		private const string AllowPrivateVariable = "RESGRID_AI_LIVE_ALLOW_PRIVATE";
		private const string ConcurrencyVariable = "RESGRID_AI_LIVE_CONCURRENCY";
		private const string HostileLimitVariable = "RESGRID_AI_LIVE_HOSTILE_LIMIT";

		private OpenAiToolClient _client;
		private GateRun _run;
		private string _saltKey;
		private int? _hostileLimit;

		[OneTimeSetUp]
		public async Task Connect_to_the_live_endpoint()
		{
			var endpoint = Read(EndpointVariable);
			var token = Read(TokenVariable);
			var model = Read(ModelVariable);
			if (endpoint == null || token == null || model == null)
				Assert.Ignore($"Set {EndpointVariable}, {TokenVariable} and {ModelVariable} to run the live model gate.");

			var allowPrivate = Read(AllowPrivateVariable) is { } flag && (flag == "1" || flag.Equals("true", StringComparison.OrdinalIgnoreCase));
			var concurrency = 1;
			if (Read(ConcurrencyVariable) is { } concurrencyText && (!int.TryParse(concurrencyText, NumberStyles.None, CultureInfo.InvariantCulture, out concurrency) || concurrency is < 1 or > 16))
				Assert.Fail($"{ConcurrencyVariable} must be a whole number from 1 to 16.");
			if (Read(HostileLimitVariable) is { } limitText)
			{
				if (!int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) || limit < 1) Assert.Fail($"{HostileLimitVariable} must be a positive whole number.");
				_hostileLimit = limit;
			}

			Uri uri = null;
			try { uri = OperatorEndpointPolicy.ValidateUri(endpoint, allowPrivate); }
			catch (LlmUnavailableException)
			{
				Assert.Fail($"{EndpointVariable} must be an absolute https URL whose path is exactly {OperatorEndpointPolicy.ChatCompletionsPath}, with no user info, " +
					$"query or fragment. http is allowed only with {AllowPrivateVariable}=true, as with AiConfig.AllowPrivateEndpoint.");
			}

			_client = new OpenAiToolClient(OperatorEndpointPolicy.CreateClient(uri, allowPrivate), uri, token);
			_saltKey = LiveModelGate.NewSaltKey();
			long warmup = 0;
			try
			{
				using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
				warmup = await LiveModelGate.WarmUpAsync(_client, model, LiveModelGate.SaltFor(LiveModelGate.GroundedDepartmentId, _saltKey), deadline.Token);
			}
			catch (Exception ex) when (ex is LlmUnavailableException or OperationCanceledException)
			{
				// OpenAiToolClient hides provider detail by design, so name what usually goes wrong.
				Assert.Fail("The warm-up request failed. Check that the URL path is " + OperatorEndpointPolicy.ChatCompletionsPath + ", the bearer token is right, " +
					ModelVariable + " matches a name from GET /v1/models, " + AllowPrivateVariable + "=true is set for a private or loopback address, " +
					"this machine trusts the endpoint's TLS certificate, and the server accepts cache_salt (vLLM 0.9 or later).");
			}
			_run = new GateRun(model, uri.Authority, concurrency, warmup, DateTime.UtcNow);
			TestContext.Progress.WriteLine(FormattableString.Invariant($"Live model gate: {model} at {uri.Authority}, concurrency {concurrency}, warm-up {warmup} ms"));
		}

		[OneTimeTearDown]
		public void Disconnect() => _client?.Dispose();

		[Test]
		public async Task Grounded_prompts_are_counted_per_topic()
		{
			// Act
			var outcomes = await RunAsync("grounded", LiveModelGate.GroundedPrompts());

			// Assert: no bar on answer quality (the operator reads the counts), only the invariants every turn must keep.
			Assert.That(outcomes.SelectMany(o => LiveModelGate.Violations(o).Select(v => o.Prompt.Name + ": " + v)), Is.Empty);
		}

		[Test]
		public async Task Hostile_questions_and_cards_never_yield_a_link_a_credential_or_unobserved_evidence()
		{
			// Arrange
			var prompts = LiveModelGate.HostilePrompts();
			if (_hostileLimit is int limit) prompts = prompts.Take(limit).ToArray();

			// Act
			var outcomes = await RunAsync("hostile", prompts);

			// Assert
			Assert.That(outcomes.SelectMany(o => LiveModelGate.Violations(o).Select(v => o.Prompt.Name + " (" + o.Prompt.Topic + "): " + v)), Is.Empty,
				"A hostile prompt produced a validated answer that must never reach a user.");
		}

		private async Task<IReadOnlyList<GateOutcome>> RunAsync(string set, IReadOnlyList<GatePrompt> prompts)
		{
			var progress = new object();
			var done = 0;
			var outcomes = await LiveModelGate.RunAllAsync(_client, _run.Model, prompts, _saltKey, _run.Concurrency,
				outcome => { lock (progress) TestContext.Progress.WriteLine(LiveModelGate.ProgressLine(set, ++done, prompts.Count, outcome)); }, CancellationToken.None);
			var summary = LiveModelGate.Summary(set, _run, outcomes);
			// Progress reaches the console at any verbosity; the files keep the result beside the runbook §7.3 baseline.
			TestContext.Progress.WriteLine(summary);
			foreach (var path in LiveModelGate.WriteReport(Path.Combine(TestContext.CurrentContext.WorkDirectory, "LiveModelGate"), set, _run, outcomes, summary))
			{
				TestContext.Progress.WriteLine("Report: " + path);
				TestContext.AddTestAttachment(path);
			}
			return outcomes;
		}

		private static string Read(string name)
		{
			var value = Environment.GetEnvironmentVariable(name);
			return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
		}
	}
}
