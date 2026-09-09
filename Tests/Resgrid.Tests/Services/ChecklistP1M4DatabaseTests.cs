using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.Checklists;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Tests.Services
{
	public partial class ChecklistDatabaseTests
	{
		[Test, Order(97)]
		public async Task Reporting_query_scopes_the_UTC_cohort_and_pages_without_dropping_ties_on_both_databases()
		{
			var connections = Connections(); using var uow = new UnitOfWork(connections); var store = Repository(connections, uow);
			var start = new DateTime(2025, 8, 1, 0, 0, 0, DateTimeKind.Utc); var end = start.AddMonths(1);
			await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
			var definition = Row<ChecklistDefinition>(); await store.WriteAsync(definition, true);
			var version = Row<ChecklistDefinitionVersion>(definition.Id); version.Version = 1; await store.WriteAsync(version, true);
			for (var i = 0; i < 505; i++)
			{
				var row = Row<ChecklistOccurrence>(definition.Id); row.VersionId = version.Id; row.CompletionId = Guid.NewGuid().ToString(); row.TargetType = i % 2 == 0 ? 1 : 5; row.TargetId = i % 2 == 0 ? "1" : Guid.NewGuid().ToString();
				row.CreatedOn = start; row.PeriodStartUtc = i == 504 ? null : start; await store.WriteAsync(row, true);
			}
			foreach (var at in new[] { start.AddSeconds(-1), end })
			{ var row = Row<ChecklistOccurrence>(definition.Id); row.VersionId = version.Id; row.CompletionId = Guid.NewGuid().ToString(); row.TargetType = 1; row.TargetId = "1"; row.CreatedOn = at; row.PeriodStartUtc = at; await store.WriteAsync(row, true); }
			uow.CommitChanges(); var timer = Stopwatch.StartNew();
			var first = await store.ReportOccurrencesAsync(77, start, end, 0); var second = await store.ReportOccurrencesAsync(77, start, end, 500);
			timer.Stop(); TestContext.Out.WriteLine($"{_type}: report query returned 505 rows in {timer.ElapsedMilliseconds} ms (two pages; existing indexes).");
			first.Should().HaveCount(500); second.Should().HaveCount(5); first.Concat(second).Select(o => o.Id).Should().OnlyHaveUniqueItems();
			first.Concat(second).Should().ContainSingle(o => o.PeriodStartUtc == null); (await store.ReportOccurrencesAsync(88, start, end, 0)).Should().BeEmpty();
		}
	}
}
