using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using File = System.IO.File;

namespace Resgrid.Tests.Wellness
{
	/// <summary>
	/// Owner decision 2026-11-26: wellness and member-health rows are purged when a department is deleted. These guards keep
	/// <see cref="MemberWellnessDataTables.All"/> complete and purgeable without a database, so a new wellness table that the deletion
	/// would leave behind fails CI here; DepartmentDeletionDatabaseTests (SQL Server, the real delete) and
	/// MemberWellnessDepartmentPurgeDatabaseTests (both engines) then prove every listed table is emptied for the department.
	/// </summary>
	[TestFixture]
	public class MemberWellnessDataTablesGuardTests
	{
		// Tables whose names look like wellness but are not member wellness data, each with the reason.
		private static readonly Dictionary<string, string> NotWellness = new(StringComparer.OrdinalIgnoreCase)
		{
			["RmsExposures"] = "NERIS property exposure (a building the fire spread to), M0168; Records data, not a member's health"
		};

		private static readonly Regex WellnessName = new("(Exposure|Injur|Clearance|Wellness|Screening|MemberHealth|DutyPeriod)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			directory.Should().NotBeNull("the repository root should be locatable from the test directory");
			return directory!.FullName;
		}

		[Test]
		public void Every_table_of_a_wellness_model_is_listed_for_department_deletion()
		{
			var folder = Path.Combine(RepositoryRoot(), "Core", "Resgrid.Model", "Wellness");
			var tables = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
				.SelectMany(f => Regex.Matches(File.ReadAllText(f), "TableName\\s*=>\\s*\"([A-Za-z0-9_]+)\"").Select(m => m.Groups[1].Value))
				.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			tables.Should().NotBeEmpty();
			tables.Should().BeSubsetOf(MemberWellnessDataTables.All,
				"a wellness table must be listed in MemberWellnessDataTables so the deletion tests prove it is purged with the department");
		}

		[Test]
		public void Every_wellness_named_table_a_migration_creates_is_listed_for_department_deletion()
		{
			var root = RepositoryRoot();
			var created = new[] { Path.Combine(root, "Providers", "Resgrid.Providers.Migrations", "Migrations"), Path.Combine(root, "Providers", "Resgrid.Providers.MigrationsPg", "Migrations") }
				.SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
				.SelectMany(f => Regex.Matches(File.ReadAllText(f), "Create\\.Table\\(\"([A-Za-z0-9_]+)\"\\)|CREATE TABLE (?:IF NOT EXISTS )?(?:\\[?dbo\\]?\\.)?\\[?([A-Za-z0-9_]+)", RegexOptions.IgnoreCase)
					.Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value))
				.Where(t => WellnessName.IsMatch(t) && !NotWellness.ContainsKey(t))
				.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			created.Should().NotBeEmpty();
			created.Should().OnlyContain(t => MemberWellnessDataTables.All.Contains(t, StringComparer.OrdinalIgnoreCase),
				"a wellness table must be listed in MemberWellnessDataTables (or in NotWellness here, with a reason)");
		}

		[Test]
		public void Every_listed_table_is_one_the_records_cleanup_of_a_department_deletion_purges()
		{
			// RmsDepartmentCleanup discovers every Rms* table with a DepartmentId column and deletes the department's rows from it.
			var entities = typeof(RmsResponderExposure).Assembly.GetTypes()
				.Where(t => typeof(IEntity).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
				.Select(t => (Type: t, Table: TableOf(t)))
				.Where(e => e.Table != null)
				.ToList();

			MemberWellnessDataTables.All.Should().OnlyHaveUniqueItems();
			foreach (var table in MemberWellnessDataTables.All)
			{
				table.Should().StartWith("Rms", "the Records cleanup discovers Rms* tables; a wellness table outside that prefix needs its own purge");
				var entity = entities.Where(e => string.Equals(e.Table, table, StringComparison.OrdinalIgnoreCase)).Select(e => e.Type).FirstOrDefault();
				entity.Should().NotBeNull($"{table} should have an entity");
				var departmentId = entity!.GetProperty("DepartmentId", BindingFlags.Public | BindingFlags.Instance);
				departmentId.Should().NotBeNull($"{table} must carry DepartmentId, which the cleanup deletes by");
				departmentId!.PropertyType.Should().Be(typeof(int), $"{table}.DepartmentId");
			}
		}

		private static string TableOf(Type type)
		{
			try
			{
				return (Activator.CreateInstance(type) as IEntity)?.TableName;
			}
			catch (Exception)
			{
				return null;
			}
		}
	}
}
