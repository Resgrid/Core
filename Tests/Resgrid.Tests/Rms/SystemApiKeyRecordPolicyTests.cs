using System.Collections.Generic;
using System.IO;
using IoFile = System.IO.File;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// System principals — the SMTP relay key and the client_credentials service accounts — are built with a
	/// fixed resource list and full actions on each. Records is the one exception (Identifier Allocation
	/// Registry section 4.4): a system principal is restricted to <c>Record_View</c> under an explicitly
	/// configured department+purpose grant, every mutating and restricted Record policy stays denied, and a
	/// non-department-wide grant sees only the groups it names.
	/// The two issuance sites are source-inspected the same way the helper-parity test is, because both build
	/// their claims inside a request/token pipeline that a unit test cannot enter.
	/// </summary>
	[TestFixture]
	public class SystemApiKeyRecordPolicyTests
	{
		private static string _originalGrants;

		[SetUp]
		public void SetUp()
		{
			_originalGrants = Resgrid.Config.RecordsSystemAccessConfig.Grants;
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Config.RecordsSystemAccessConfig.Grants = _originalGrants;
		}

		private static DirectoryInfo RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !IoFile.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			directory.Should().NotBeNull("the repository root should be locatable from the test directory");
			return directory;
		}

		private static string SourceOf(params string[] segments)
		{
			var path = Path.Combine(new[] { RepositoryRoot().FullName }.Concat(segments).ToArray());
			IoFile.Exists(path).Should().BeTrue($"{path} should exist at its documented path");
			return IoFile.ReadAllText(path);
		}

		private static string HandlerSource() =>
			SourceOf("Web", "Resgrid.Web.Services", "Middleware", "SystemApiKeyAuthHandler.cs");

		private static string ConnectSource() =>
			SourceOf("Web", "Resgrid.Web.Services", "Controllers", "v4", "ConnectController.cs");

		private static List<string> RecordResourceNames() =>
			typeof(ResgridClaimTypes.Resources)
				.GetFields(BindingFlags.Public | BindingFlags.Static)
				.Where(f => f.Name.StartsWith("Record"))
				.Select(f => f.Name)
				.ToList();

		/// <summary>The blanket resource array must stay free of every Record resource, including plain Record.</summary>
		private static void AssertNoRecordResourceInBlanketList(string source, string arrayAnchor)
		{
			var start = source.IndexOf(arrayAnchor, System.StringComparison.Ordinal);
			start.Should().BeGreaterThan(-1, $"the source should contain the blanket resource list anchored at {arrayAnchor}");
			var end = source.IndexOf("};", start, System.StringComparison.Ordinal);
			end.Should().BeGreaterThan(start);
			var block = source.Substring(start, end - start);

			foreach (var resource in RecordResourceNames())
				Regex.IsMatch(block, $@"Resources\.{resource}\b").Should().BeFalse($"the blanket resource list must not carry the {resource} resource");
		}

		[Test]
		public void The_relay_blanket_resource_list_carries_no_record_resource()
		{
			var source = HandlerSource();
			source.Should().Contain("ResgridClaimTypes.Resources.Log", "the file read should be the handler that enumerates resources");
			AssertNoRecordResourceInBlanketList(source, "var resources = new[]");
		}

		[Test]
		public void The_connect_blanket_resource_list_carries_no_record_resource()
		{
			var source = ConnectSource();
			source.Should().Contain("AddAllResourceClaims", "the file read should be the controller that issues service-account claims");
			AssertNoRecordResourceInBlanketList(source, "var resources = new[]");
		}

		[Test]
		public void The_only_record_resource_either_site_names_is_record_with_the_view_action()
		{
			foreach (var source in new[] { HandlerSource(), ConnectSource() })
			{
				// Exactly one shape is permitted: Resources.Record paired with Actions.View.
				var restricted = RecordResourceNames().Where(n => n != "Record").ToList();
				restricted.Should().NotBeEmpty();
				foreach (var resource in restricted)
					Regex.IsMatch(source, $@"Resources\.{resource}\b").Should().BeFalse($"a system principal must never receive the {resource} resource");

				foreach (Match match in Regex.Matches(source, @"Resources\.Record\s*,\s*ResgridClaimTypes\.Actions\.(\w+)"))
					match.Groups[1].Value.Should().Be("View", "a system principal receives Record_View and nothing else");
			}
		}

		[Test]
		public void Neither_site_names_a_mutating_or_restricted_record_policy()
		{
			// Record_View is the one Record policy a system principal may ever reach, so it is the one name
			// allowed to appear here (in prose or in code). Every other Record policy must be absent.
			var policies = typeof(ResgridResources)
				.GetFields(BindingFlags.Public | BindingFlags.Static)
				.Where(f => f.Name.StartsWith("Record") && f.Name != nameof(ResgridResources.Record_View))
				.Select(f => f.Name)
				.ToList();

			policies.Should().NotBeEmpty();
			foreach (var source in new[] { HandlerSource(), ConnectSource() })
			{
				foreach (var policy in policies)
					Regex.IsMatch(source, $@"\b{policy}\b").Should().BeFalse($"the issuance site must not reference the {policy} policy");
			}
		}

		[Test]
		public void The_relay_only_adds_the_record_claim_when_a_grant_is_configured()
		{
			HandlerSource().Should().Contain("SystemPrincipalRecordGrant.AnyConfigured()",
				"the relay must not carry Record_View when no grant exists at all");
		}

		[Test]
		public void No_grant_is_configured_by_default()
		{
			Resgrid.Config.RecordsSystemAccessConfig.Grants = "";
			SystemPrincipalRecordGrant.AnyConfigured().Should().BeFalse();
			SystemPrincipalRecordGrant.For(12).Should().BeNull();
		}

		[Test]
		public void A_department_wide_grant_parses_and_sees_the_whole_department()
		{
			Resgrid.Config.RecordsSystemAccessConfig.Grants = "12|NerisAudit|DepartmentWide";

			var grant = SystemPrincipalRecordGrant.For(12);
			grant.Should().NotBeNull();
			grant.Purpose.Should().Be("NerisAudit");
			grant.DepartmentWide.Should().BeTrue();
			grant.VisibleGroupIds().Should().BeNull("null is the 'whole department' filter every read path already understands");
			SystemPrincipalRecordGrant.For(13).Should().BeNull("a grant covers exactly the department it names");
		}

		[Test]
		public void A_group_scoped_grant_sees_only_the_groups_it_names()
		{
			Resgrid.Config.RecordsSystemAccessConfig.Grants = "45|StationReporting|Groups:102,101,102";

			var grant = SystemPrincipalRecordGrant.For(45);
			grant.Should().NotBeNull();
			grant.DepartmentWide.Should().BeFalse();
			grant.GroupIds.Should().Equal(101, 102);
			grant.VisibleGroupIds().Should().Equal(101, 102);
		}

		[Test]
		public void A_malformed_grant_is_dropped_rather_than_widened()
		{
			Resgrid.Config.RecordsSystemAccessConfig.Grants = "12;abc|X|DepartmentWide;13||DepartmentWide;14|Reporting|Everything;15|Reporting|Groups:";

			var grants = SystemPrincipalRecordGrant.All();
			grants.Select(g => g.DepartmentId).Should().NotContain(new[] { 12, 13, 14 });

			// "Groups:" with no ids is well-formed but empty: the principal sees nothing, which is the safe reading.
			var empty = SystemPrincipalRecordGrant.For(15);
			empty.Should().NotBeNull();
			empty.DepartmentWide.Should().BeFalse();
			empty.GroupIds.Should().BeEmpty();
		}

		[Test]
		public void A_department_named_twice_resolves_deterministically_to_the_first_entry()
		{
			Resgrid.Config.RecordsSystemAccessConfig.Grants = "12|First|Groups:1;12|Second|DepartmentWide";

			SystemPrincipalRecordGrant.For(12).Purpose.Should().Be("First");
		}

		[Test]
		public void A_user_principal_never_resolves_a_grant()
		{
			Resgrid.Config.RecordsSystemAccessConfig.Grants = "12|NerisAudit|DepartmentWide";

			var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
			{
				new Claim(ResgridClaimTypes.Data.UserId, "a-real-member"),
				new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.View)
			}, "Cookies"));

			RecordsSystemPrincipal.IsSystemPrincipal(user).Should().BeFalse();
			RecordsSystemPrincipal.ResolveGrant(user, 12).Should().BeNull("a member's visibility comes from their own groups, never from a grant");
		}

		[Test]
		public void A_system_principal_resolves_only_its_own_departments_grant()
		{
			Resgrid.Config.RecordsSystemAccessConfig.Grants = "12|NerisAudit|DepartmentWide";

			var system = new ClaimsPrincipal(new ClaimsIdentity(new[]
			{
				new Claim(ResgridClaimTypes.Data.ServiceAccount, "true"),
				new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.View)
			}, "SystemApiKey"));

			RecordsSystemPrincipal.IsSystemPrincipal(system).Should().BeTrue();
			RecordsSystemPrincipal.ResolveGrant(system, 12).Should().NotBeNull();
			RecordsSystemPrincipal.ResolveGrant(system, 99).Should().BeNull("an ungranted department is a denial, not unrestricted access");
		}

		[Test]
		public void A_revoked_grant_stops_working_for_an_already_minted_token()
		{
			Resgrid.Config.RecordsSystemAccessConfig.Grants = "12|NerisAudit|DepartmentWide";
			var system = new ClaimsPrincipal(new ClaimsIdentity(new[]
			{
				new Claim(ResgridClaimTypes.Data.ServiceAccount, "true"),
				new Claim(ResgridClaimTypes.Data.RecordGrantPurpose, "NerisAudit"),
				new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.View)
			}, "Bearer"));

			RecordsSystemPrincipal.ResolveGrant(system, 12).Should().NotBeNull();

			Resgrid.Config.RecordsSystemAccessConfig.Grants = "";
			RecordsSystemPrincipal.ResolveGrant(system, 12).Should().BeNull("the grant is re-read per request, not trusted from the token");
		}
	}
}
