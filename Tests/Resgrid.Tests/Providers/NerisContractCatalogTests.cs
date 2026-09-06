using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Providers.Neris;

namespace Resgrid.Tests.Providers
{
	[TestFixture]
	public class NerisContractCatalogTests
	{
		[Test]
		public void Analysis_accepts_the_pinned_parent_identifier_and_rejects_the_previous_invented_fields()
		{
			const string valid = "{\"base\":{\"neris_id_incident\":\"FD24027000|INC-123|1788264000\",\"incident_number\":\"INC-123\"}}";
			NerisContractCatalog.Instance.Validate("IncidentAnalysisPayload", valid, 1, "test").Should().BeEmpty();
			const string invalid = "{\"base\":{\"incident_neris_id\":\"private-wrong-value\",\"general_cause\":\"ACCIDENTAL\"}}";
			var issues = NerisContractCatalog.Instance.Validate("IncidentAnalysisPayload", invalid, 1, "test");
			issues.Should().Contain(i => i.RuleKey == "neris.schema.required");
			issues.Should().Contain(i => i.RuleKey == "neris.schema.additionalProperties");
			string.Join(" ", issues.Select(i => i.Message)).Should().NotContain("private-wrong-value");
		}

		[Test]
		public void Section_validation_resolves_nested_references_and_required_fields()
		{
			var issues = NerisContractCatalog.Instance.Validate("FirePayload", "{}", 1, "test");
			issues.Should().Contain(i => i.RuleKey == "neris.schema.required");
			NerisContractCatalog.Instance.GetSchema("FirePayload")["required"].Should().NotBeNull();
		}
	}
}
