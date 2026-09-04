using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Providers.Claims;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// The system API key principal (SMTP relay) is built with a fixed resource list and full actions on each.
	/// RMS-1 requires that it grants no Record policy at all — mutating or otherwise — so a relay credential can
	/// never author, finalize, void, export or even read Records (plan RMS-1 package, registry section 4.4).
	/// The handler is source-inspected the same way the helper-parity test is, because it is an
	/// AuthenticationHandler that only yields its claims inside a request pipeline.
	/// </summary>
	[TestFixture]
	public class SystemApiKeyRecordPolicyTests
	{
		private static string HandlerSource()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			directory.Should().NotBeNull("the repository root should be locatable from the test directory");
			var path = Path.Combine(directory!.FullName, "Web", "Resgrid.Web.Services", "Middleware", "SystemApiKeyAuthHandler.cs");
			File.Exists(path).Should().BeTrue("the system API key handler should exist at its documented path");
			return File.ReadAllText(path);
		}

		[Test]
		public void The_system_api_key_principal_receives_no_record_resource()
		{
			var source = HandlerSource();
			source.Should().Contain("ResgridClaimTypes.Resources.Log", "the file read should be the handler that enumerates resources");

			var recordResources = typeof(ResgridClaimTypes.Resources)
				.GetFields(BindingFlags.Public | BindingFlags.Static)
				.Where(f => f.Name.StartsWith("Record"))
				.Select(f => f.Name)
				.ToList();

			recordResources.Should().NotBeEmpty();
			foreach (var resource in recordResources)
				Regex.IsMatch(source, $@"Resources\.{resource}\b").Should().BeFalse($"the relay principal must not carry the {resource} resource");
		}

		[Test]
		public void The_system_api_key_handler_names_no_record_policy()
		{
			var source = HandlerSource();

			var recordPolicies = typeof(ResgridResources)
				.GetFields(BindingFlags.Public | BindingFlags.Static)
				.Where(f => f.Name.StartsWith("Record"))
				.Select(f => f.Name)
				.ToList();

			recordPolicies.Should().NotBeEmpty();
			foreach (var policy in recordPolicies)
				Regex.IsMatch(source, $@"\b{policy}\b").Should().BeFalse($"the relay handler must not reference the {policy} policy");
		}
	}
}
