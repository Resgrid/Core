using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using FluentMigrator;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Allocations
{
	/// <summary>
	/// CI enforcement for the cross-project identifier allocation registry
	/// (../int-Coordination/docs/architecture/identifier-allocation-registry.md, section 5).
	/// These tests catch the exact defect class the registry exists to prevent: a duplicated
	/// append-only identifier, or the two migration dialects drifting apart.
	/// </summary>
	[TestFixture]
	public class IdentifierAllocationTests
	{
		#region Registry test 1 — no duplicate values in append-only enums

		[TestCase(typeof(PermissionTypes))]
		[TestCase(typeof(WorkflowTriggerEventType))]
		[TestCase(typeof(DepartmentSettingTypes))]
		[TestCase(typeof(Resgrid.Model.Events.EventTypes))]
		public void Append_only_enum_has_no_duplicate_values(Type enumType)
		{
			var values = Enum.GetValues(enumType).Cast<object>().Select(Convert.ToInt64).ToList();

			values.Should().OnlyHaveUniqueItems(
				$"{enumType.Name} is an append-only registry sequence; a duplicated value silently merges two features' identifiers");
		}

		#endregion

		#region Registry test 3 — migration numbers unique per dialect, and the two sets identical

		private static Dictionary<long, List<string>> MigrationsOf(System.Reflection.Assembly assembly)
		{
			return assembly.GetTypes()
				.Select(t => new { Type = t, Attribute = t.GetCustomAttributes(typeof(MigrationAttribute), false).Cast<MigrationAttribute>().FirstOrDefault() })
				.Where(x => x.Attribute != null)
				.GroupBy(x => x.Attribute.Version)
				.ToDictionary(g => g.Key, g => g.Select(x => x.Type.Name).OrderBy(n => n).ToList());
		}

		[Test]
		public void Migration_numbers_are_unique_in_each_dialect_and_the_two_sets_are_identical()
		{
			var sqlServer = MigrationsOf(typeof(Resgrid.Providers.Migrations.Migrations.M0001_InitialMigration).Assembly);
			var postgres = MigrationsOf(typeof(Resgrid.Providers.MigrationsPg.Migrations.M0001_InitialMigrationPg).Assembly);

			sqlServer.Where(kv => kv.Value.Count > 1).Should().BeEmpty(
				"a migration number registered twice in the SQL Server project runs in undefined order");
			postgres.Where(kv => kv.Value.Count > 1).Should().BeEmpty(
				"a migration number registered twice in the PostgreSQL project runs in undefined order");

			sqlServer.Keys.Except(postgres.Keys).Should().BeEmpty(
				"every SQL Server migration needs its Pg twin (a deliberate no-op still ships as a numbered twin)");
			postgres.Keys.Except(sqlServer.Keys).Should().BeEmpty(
				"every PostgreSQL migration needs its SQL Server twin");
		}

		[Test]
		public void Migration_filename_style_numbers_match_their_attributes()
		{
			foreach (var assembly in new[]
			{
				typeof(Resgrid.Providers.Migrations.Migrations.M0001_InitialMigration).Assembly,
				typeof(Resgrid.Providers.MigrationsPg.Migrations.M0001_InitialMigrationPg).Assembly
			})
			{
				var mismatches = assembly.GetTypes()
					.Select(t => new { Type = t, Attribute = t.GetCustomAttributes(typeof(MigrationAttribute), false).Cast<MigrationAttribute>().FirstOrDefault() })
					.Where(x => x.Attribute != null)
					.Select(x => new
					{
						x.Type.Name,
						x.Attribute.Version,
						NameNumber = Regex.Match(x.Type.Name, @"^M(\d{4})_") is { Success: true } m
							? long.Parse(m.Groups[1].Value)
							: -1
					})
					.Where(x => x.NameNumber != x.Version)
					.ToList();

				mismatches.Should().BeEmpty(
					"the M#### class-name prefix is how humans allocate numbers; it must equal the [Migration] attribute");
			}
		}

		#endregion

		#region Registry test 4 — no worker command ID registered twice

		[Test]
		public void Worker_command_ids_are_registered_once()
		{
			var programPath = FindRepositoryFile(Path.Combine("Workers", "Resgrid.Workers.Console", "Program.cs"));
			if (programPath == null)
			{
				Assert.Inconclusive("Workers.Console Program.cs not found relative to the test assembly; source-scan check skipped.");
				return;
			}

			// Commented-out registrations don't collide; scan active lines only.
			var source = string.Join("\n", System.IO.File.ReadAllLines(programPath)
				.Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

			// Worker command IDs are the integer ctor argument of each scheduled/published command:
			// new Commands.FooCommand(27). One ID may appear for multiple schedule lines of the SAME
			// command; two DIFFERENT commands on one ID is the defect.
			var idsToCommands = new Dictionary<int, HashSet<string>>();
			foreach (Match match in Regex.Matches(source, @"new\s+(?:Commands\.)?(\w+Command)\((\d+)\)"))
			{
				var id = int.Parse(match.Groups[2].Value);
				if (!idsToCommands.TryGetValue(id, out var commands))
					idsToCommands[id] = commands = new HashSet<string>(StringComparer.Ordinal);
				commands.Add(match.Groups[1].Value);
			}

			idsToCommands.Should().NotBeEmpty("the scan must actually find command registrations");
			idsToCommands.Where(kv => kv.Value.Count > 1).Should().BeEmpty(
				"two different worker commands sharing one ID collide in the Quidjibo schedule");
		}

		private static string FindRepositoryFile(string relativePath)
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null)
			{
				var candidate = Path.Combine(directory.FullName, relativePath);
				if (System.IO.File.Exists(candidate))
					return candidate;
				directory = directory.Parent;
			}

			return null;
		}

		#endregion
	}
}
