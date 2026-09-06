using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Tests.Rms
{
	[TestFixture]
	public class RmsTransactionLifecycleTests
	{
		[Test]
		public async Task Sequential_journal_phases_use_fresh_connections_and_transactions()
		{
			var provider = new Mock<IConnectionProvider>();
			var connections = new List<Mock<DbConnection>>();
			var transactions = new List<Mock<DbTransaction>>();
			provider.Setup(p => p.Create()).Returns(() =>
			{
				var connection = new Mock<DbConnection>();
				var transaction = new Mock<DbTransaction>();
				connection.Protected().Setup<DbTransaction>("BeginDbTransaction", ItExpr.IsAny<IsolationLevel>()).Returns(transaction.Object);
				connections.Add(connection); transactions.Add(transaction);
				return connection.Object;
			});
			using var work = new UnitOfWork(provider.Object);
			for (var phase = 0; phase < 3; phase++)
			{
				var connection = await work.CreateOrGetConnectionAsync();
				work.CreateOrGetConnection().Should().BeSameAs(connection);
				work.CommitChanges();
				work.Transaction.Should().BeNull();
				work.Connection.Should().BeNull();
			}
			connections.Should().HaveCount(3);
			foreach (var transaction in transactions)
			{
				transaction.Verify(t => t.Commit(), Times.Once);
				transaction.Protected().Verify("Dispose", Times.Once(), new object[] { true });
			}
		}

		[TestCase(false)]
		[TestCase(true)]
		public void Failed_commit_or_rollback_cannot_poison_the_next_transaction(bool commit)
		{
			var transaction = new Mock<DbTransaction>();
			if (commit) transaction.Setup(t => t.Commit()).Throws<InvalidOperationException>();
			else transaction.Setup(t => t.Rollback()).Throws<InvalidOperationException>();
			var connection = new Mock<DbConnection>();
			connection.Protected().Setup<DbTransaction>("BeginDbTransaction", ItExpr.IsAny<IsolationLevel>()).Returns(transaction.Object);
			var provider = new Mock<IConnectionProvider>();
			provider.Setup(p => p.Create()).Returns(connection.Object);
			using var work = new UnitOfWork(provider.Object);
			work.CreateOrGetConnection();
			Action finish = () => { if (commit) work.CommitChanges(); else work.DiscardChanges(); };
			finish.Should().Throw<InvalidOperationException>();
			work.Transaction.Should().BeNull();
			work.Connection.Should().BeNull();
			work.DiscardChanges(); // exception handling after a failed commit must not reuse its completed transaction
			work.CreateOrGetConnection();
			provider.Verify(p => p.Create(), Times.Exactly(2));
		}
	}
}
