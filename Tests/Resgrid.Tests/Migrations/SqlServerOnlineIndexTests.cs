using NUnit.Framework;
using Resgrid.Providers.Migrations;

namespace Resgrid.Tests.Migrations
{
	[TestFixture]
	public class SqlServerOnlineIndexTests
	{
		[Test]
		public void a_plain_index_asks_for_online_only_on_editions_that_support_it()
		{
			var sql = SqlServerOnlineIndex.Create("IX_Foo_Bar", "Foo",
				new[] { "[Bar] ASC", "[Baz] DESC" });

			Assert.That(sql, Does.Contain("CONVERT(int, SERVERPROPERTY('EngineEdition')) IN (3, 5, 8)"));
			Assert.That(sql, Does.Contain("THEN N'ONLINE = ON' ELSE N''"));
			Assert.That(sql, Does.Contain("N'CREATE INDEX [IX_Foo_Bar] ON [Foo] ([Bar] ASC, [Baz] DESC)'"));

			// No WITH clause at all where the edition has no online build, rather than an empty WITH ().
			Assert.That(sql, Does.Contain("CASE WHEN LEN(@indexOptions) > 0 THEN N' WITH ('"));

			// Re-runnable: the existence check lives in the batch, not in collected FluentMigrator state.
			Assert.That(sql, Does.StartWith("IF NOT EXISTS (SELECT 1 FROM sys.indexes"));
			Assert.That(sql, Does.Contain("[name] = N'IX_Foo_Bar' AND [object_id] = OBJECT_ID(N'[Foo]')"));
		}

		[Test]
		public void a_filtered_unique_index_keeps_its_predicate_and_tempdb_sort_on_every_edition()
		{
			var sql = SqlServerOnlineIndex.Create("UX_Foo_Bar", "Foo", new[] { "[Bar]" },
				unique: true, filter: "[Bar] IS NOT NULL", sortInTempDb: true);

			Assert.That(sql, Does.Contain(
				"N'CREATE UNIQUE INDEX [UX_Foo_Bar] ON [Foo] ([Bar]) WHERE [Bar] IS NOT NULL'"));

			// SORT_IN_TEMPDB is not an Enterprise-only option, so it survives the fallback.
			Assert.That(sql, Does.Contain("THEN N'ONLINE = ON, SORT_IN_TEMPDB = ON' ELSE N'SORT_IN_TEMPDB = ON'"));
		}
	}
}
