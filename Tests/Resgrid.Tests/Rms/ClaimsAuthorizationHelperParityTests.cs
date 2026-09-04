using System;
using System.Collections.Generic;
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
	/// The Web and Web.Services copies of ClaimsAuthorizationHelper and of the Startup policy table drift
	/// silently (RMS plan section 9). These tests pin the Records subset in both copies so an RMS
	/// divergence is a failing build. Pre-RMS drift between the copies (Contacts/Routes helpers, SSO/
	/// SCIM/CommunicationTest/WeatherAlert policies) is real but out of RMS scope, so the comparison is
	/// deliberately limited to Record* members.
	/// </summary>
	[TestFixture]
	public class ClaimsAuthorizationHelperParityTests
	{
		private static readonly string[] ExpectedHelpers =
		{
			"CanViewRecords", "CanCreateRecord", "CanReviewRecords", "CanApproveRecords", "CanFinalizeRecords",
			"CanSubmitRecords", "CanAmendRecords", "CanVoidRecords", "CanExportRecords", "CanShareRecords",
			"CanReassignRecordDrafts", "CanViewLegacyRecords", "CanViewRestrictedRecords", "CanManageRecordDefinitions",
			"CanPublishRecordDefinitions", "CanManageRecordReports", "CanManageRecordDisclosures", "CanManageRecordLegalHold"
		};

		[Test]
		public void Both_helper_copies_expose_the_same_records_members()
		{
			var web = RecordMembers(typeof(Resgrid.Web.Helpers.ClaimsAuthorizationHelper));
			var services = RecordMembers(typeof(Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper));

			web.Except(services).Should().BeEmpty("Resgrid.Web helper has Records members missing from Resgrid.Web.Services");
			services.Except(web).Should().BeEmpty("Resgrid.Web.Services helper has Records members missing from Resgrid.Web");
			web.Should().HaveCount(ExpectedHelpers.Length);
		}

		[Test]
		public void Both_helper_copies_expose_every_records_policy_helper()
		{
			foreach (var type in new[] { typeof(Resgrid.Web.Helpers.ClaimsAuthorizationHelper), typeof(Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper) })
			{
				var names = type.GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name).ToHashSet();
				foreach (var name in ExpectedHelpers)
					names.Should().Contain(name, $"{type.FullName} is missing {name}");
			}
		}

		[Test]
		public void Both_startup_files_register_every_records_policy()
		{
			var webStartup = FindRepositoryFile(Path.Combine("Web", "Resgrid.Web", "Startup.cs"));
			var servicesStartup = FindRepositoryFile(Path.Combine("Web", "Resgrid.Web.Services", "Startup.cs"));
			if (webStartup == null || servicesStartup == null)
			{
				Assert.Inconclusive("Startup.cs sources not found relative to the test assembly; source-scan check skipped.");
				return;
			}

			var recordPolicies = typeof(ResgridResources).GetFields(BindingFlags.Public | BindingFlags.Static)
				.Where(f => f.Name.StartsWith("Record", StringComparison.Ordinal))
				.Select(f => f.Name)
				.ToList();
			recordPolicies.Should().HaveCount(18);

			var webPolicies = RecordPolicies(webStartup);
			var servicesPolicies = RecordPolicies(servicesStartup);

			webPolicies.Should().BeEquivalentTo(recordPolicies, "every Records policy must be registered in Resgrid.Web");
			servicesPolicies.Should().BeEquivalentTo(recordPolicies, "every Records policy must be registered in Resgrid.Web.Services");
		}

		private static HashSet<string> RecordMembers(Type type)
		{
			return type.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
				.Where(m => m.Name.Contains("Record", StringComparison.Ordinal))
				.Select(m => m.MemberType + " " + m)
				.ToHashSet();
		}

		private static HashSet<string> RecordPolicies(string path)
		{
			var source = string.Join("\n", File.ReadAllLines(path).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
			return Regex.Matches(source, @"AddPolicy\(ResgridResources\.(Record\w+)")
				.Select(m => m.Groups[1].Value)
				.ToHashSet();
		}

		private static string FindRepositoryFile(string relativePath)
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null)
			{
				var candidate = Path.Combine(directory.FullName, relativePath);
				if (File.Exists(candidate))
					return candidate;
				directory = directory.Parent;
			}

			return null;
		}
	}
}
