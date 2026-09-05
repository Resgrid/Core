using System;
using System.Collections.Generic;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	[TestFixture]
	public class DisclosureContentPolicyTests
	{
		[Test]
		public void Redaction_changes_only_the_selected_field_and_leaves_original_revision_intact()
		{
			var original = JObject.Parse("{\"Modules\":[{\"DetailJson\":\"{\\\"patient/name\\\":\\\"private name\\\",\\\"count\\\":2}\"}],\"Narrative\":\"Approved narrative\"}");
			var content = DisclosureContentPolicy.Prepare(original); var log = new List<RmsRedactionEntry>();
			DisclosureContentPolicy.Apply(content, "incident", new[] { new RmsDisclosureFieldDecision { Path = "/Modules/0/DetailJson/patient~1name", Withhold = true, Authority = "Fixture authority 1", Basis = "Fixture reviewed reason" } }, log);
			content.ToString().Should().NotContain("private name").And.Contain("Approved narrative");
			content["Modules"][0]["DetailJson"]["count"].Value<int>().Should().Be(2);
			original.ToString().Should().Contain("private name");
			log.Should().ContainSingle(e => e.Authority == "Fixture authority 1" && e.Field == "/Modules/0/DetailJson/patient~1name");
		}
		[TestCase("/Modules/*/DetailJson")]
		[TestCase("$..name")]
		[TestCase("/Missing")]
		[TestCase("/Modules/00")]
		[TestCase("/Modules/~2")]
		public void Forged_wildcard_or_stale_paths_fail_before_any_redaction(string invalid)
		{
			var content = JObject.Parse("{\"Narrative\":\"preserve\",\"Modules\":[{}]}"); var before = content.ToString(); var log = new List<RmsRedactionEntry>();
			Action redact = () => DisclosureContentPolicy.Apply(content, "r", new[] { new RmsDisclosureFieldDecision { Path = "/Narrative", Withhold = true, Authority = "A", Basis = "B" }, new RmsDisclosureFieldDecision { Path = invalid, Withhold = true, Authority = "A", Basis = "B" } }, log);
			redact.Should().Throw<ArgumentException>(); content.ToString().Should().Be(before); log.Should().BeEmpty();
		}
		[Test]
		public void Authority_and_case_specific_reason_are_required()
		{
			Action redact = () => DisclosureContentPolicy.Apply(JObject.Parse("{\"x\":1}"), "r", new[] { new RmsDisclosureFieldDecision { Path = "/x", Withhold = true, Basis = "Restricted" } }, new List<RmsRedactionEntry>());
			redact.Should().Throw<ArgumentException>();
		}
	}
}
