using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Workflow templates render into outbound email, SMS and webhooks and run UNATTENDED — no
	/// Protected Data Grant can exist on that path (plan section 8). Every cataloged value must
	/// therefore reach a template as the REDACTED placeholder, never as an rgdp envelope: a
	/// workflow is a direct egress channel, so ciphertext here would leave the system.
	/// </summary>
	[TestFixture]
	public class WorkflowTemplateRedactionTests
	{
		[Test]
		public void Enveloped_call_values_degrade_to_the_placeholder()
		{
			// The builder maps through SafeDisplay; this pins the contract those mappings rely on
			// for every cataloged call field a template can reference.
			var enveloped = new[]
			{
				"rgdp:1:1:name==", "rgdp:1:1:nature==", "rgdp:1:1:notes==", "rgdp:1:1:address==",
				"rgdp:1:1:geo==", "rgdp:1:1:contactname==", "rgdp:1:1:contactnumber==",
				"rgdp:1:1:w3w==", "rgdp:1:1:formdata==", "rgdpb:1:1:binary=="
			};

			foreach (var value in enveloped)
				ProtectedDataEnvelope.SafeDisplay(value).Should().Be(ProtectedDataEnvelope.RedactionValue, value);
		}

		[Test]
		public void Unprotected_department_values_reach_templates_unchanged()
		{
			// The redaction must not damage the ordinary case: an unprotected department's workflow
			// output has to stay byte-for-byte what the author wrote.
			ProtectedDataEnvelope.SafeDisplay("Structure fire, 2 story").Should().Be("Structure fire, 2 story");
			ProtectedDataEnvelope.SafeDisplay("39.19,-119.76").Should().Be("39.19,-119.76");
			ProtectedDataEnvelope.SafeDisplay(null).Should().BeNull();
		}
	}
}
