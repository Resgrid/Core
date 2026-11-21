using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentMigrator;
using FluentMigrator.Expressions;
using FluentMigrator.Infrastructure;
using Moq;
using NUnit.Framework;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Providers.MigrationsPg.Migrations;

namespace Resgrid.Tests.Migrations
{
	/// <summary>
	/// PR #588: the Intake.DedicatedNumber flag description ("the department's own phone number") was concatenated into a
	/// single-quoted literal unescaped, so migration 323 failed to parse on both engines. Every raw statement the intake
	/// migrations queue must keep its single-quoted literals closed.
	/// </summary>
	[TestFixture]
	public class IntakeMigrationSeedLiteralTests
	{
		[TestCase(false), TestCase(true)]
		public void Intake_migration_sql_keeps_every_literal_closed(bool postgres)
		{
			var statements = Statements(postgres
				? new IMigration[] { new M0323_AddIntakeChannelsAndDedicatedNumbersPg(), new M0325_AddIntakeConversationsPg() }
				: new IMigration[] { new M0323_AddIntakeChannelsAndDedicatedNumbers(), new M0325_AddIntakeConversations() });

			statements.Should().NotBeEmpty();
			foreach (var sql in statements)
				LiteralsClosed(sql).Should().BeTrue(sql);

			statements.Should().Contain(s => s.Contains("the department''s own phone number"),
				"the dedicated number flag description carries an apostrophe that must be doubled");
		}

		[Test]
		public void Literal_doubles_apostrophes_and_tolerates_null()
		{
			M0323_AddIntakeChannelsAndDedicatedNumbers.Literal("a'b''c").Should().Be("a''b''''c");
			M0323_AddIntakeChannelsAndDedicatedNumbersPg.Literal(null).Should().BeEmpty();
		}

		private static List<string> Statements(IEnumerable<IMigration> migrations)
		{
			var expressions = new List<IMigrationExpression>();
			var context = new Mock<IMigrationContext>();
			context.SetupGet(c => c.Expressions).Returns(expressions);
			var schema = new Mock<IQuerySchema>();
			// Existing tables: the create branches are skipped, the seed statements still run (they are unconditional).
			schema.Setup(s => s.TableExists(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
			context.SetupGet(c => c.QuerySchema).Returns(schema.Object);

			foreach (var migration in migrations)
				migration.GetUpExpressions(context.Object);

			return expressions.OfType<ExecuteSqlStatementExpression>().Select(e => e.SqlStatement).ToList();
		}

		/// <summary>Walks the statement; a doubled quote inside a literal is an escaped apostrophe.</summary>
		private static bool LiteralsClosed(string sql)
		{
			var inLiteral = false;
			for (var i = 0; i < sql.Length; i++)
			{
				if (sql[i] != '\'')
					continue;

				if (inLiteral && i + 1 < sql.Length && sql[i + 1] == '\'')
				{
					i++;
					continue;
				}

				inLiteral = !inLiteral;
			}

			return !inLiteral;
		}
	}
}
