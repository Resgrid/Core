using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// A page request the browser abandoned cancels its Records queries. That reached Sentry as a Fatal
	/// TaskCanceledException from RmsRepositoryBase.RunAsync (Dashboard, Admin Assist setup prompt); a caller
	/// giving up is not a database fault, so only a cancellation nobody asked for is still logged. RepositoryBase's
	/// token-taking writes follow the same rule.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class RmsRepositoryCancellationLoggingTests
	{
		private const string Marker = "rms-run-probe-fault";

		[Test]
		public async Task A_cancelled_caller_is_not_logged()
		{
			using var source = new CancellationTokenSource();
			source.Cancel();

			var output = await CaptureConsoleAsync(async () =>
			{
				Func<Task> act = () => Probe().Run(_ => Task.FromException<int>(new TaskCanceledException(Marker)), source.Token);
				await act.Should().ThrowAsync<TaskCanceledException>();
			});

			output.Should().NotContain(Marker);
		}

		[Test]
		public async Task A_cancellation_nobody_asked_for_is_still_logged()
		{
			var output = await CaptureConsoleAsync(async () =>
			{
				Func<Task> act = () => Probe().Run(_ => Task.FromException<int>(new TaskCanceledException(Marker)), CancellationToken.None);
				await act.Should().ThrowAsync<TaskCanceledException>();
			});

			output.Should().Contain(Marker);
		}

		[Test]
		public async Task A_database_fault_is_still_logged_when_the_caller_has_cancelled()
		{
			using var source = new CancellationTokenSource();
			source.Cancel();

			var output = await CaptureConsoleAsync(async () =>
			{
				Func<Task> act = () => Probe().Run(_ => Task.FromException<int>(new InvalidOperationException(Marker)), source.Token);
				await act.Should().ThrowAsync<InvalidOperationException>();
			});

			output.Should().Contain(Marker);
		}

		[TestCase("Insert"), TestCase("Update"), TestCase("Delete"), TestCase("SaveOrUpdate"), TestCase("DeleteMultiple")]
		public async Task A_cancelled_caller_is_not_logged_by_a_base_write(string operation)
		{
			using var source = new CancellationTokenSource();
			source.Cancel();

			var output = await CaptureConsoleAsync(async () =>
			{
				Func<Task> act = () => Write(operation, new TaskCanceledException(Marker), source.Token);
				await act.Should().ThrowAsync<TaskCanceledException>();
			});

			output.Should().NotContain(Marker);
		}

		[TestCase("Insert"), TestCase("Update"), TestCase("Delete"), TestCase("SaveOrUpdate"), TestCase("DeleteMultiple")]
		public async Task A_cancellation_nobody_asked_for_is_still_logged_by_a_base_write(string operation)
		{
			var output = await CaptureConsoleAsync(async () =>
			{
				Func<Task> act = () => Write(operation, new TaskCanceledException(Marker), CancellationToken.None);
				await act.Should().ThrowAsync<TaskCanceledException>();
			});

			output.Should().Contain(Marker);
		}

		/// <summary>Runs one RepositoryBase write against a connection whose open fails with <paramref name="fault"/>.</summary>
		private static Task Write(string operation, Exception fault, CancellationToken cancellationToken)
		{
			var connection = new Mock<DbConnection>();
			connection.Setup(c => c.OpenAsync(It.IsAny<CancellationToken>())).Returns(Task.FromException(fault));
			var provider = new Mock<IConnectionProvider>();
			provider.Setup(p => p.Create()).Returns(connection.Object);
			// No ambient connection, so every write opens its own and meets the fault.
			var repository = new RepositoryBase<CallLocationKey>(provider.Object, new SqlServerConfiguration(), Mock.Of<IUnitOfWork>(), Mock.Of<IQueryFactory>());
			var entity = new CallLocationKey();

			return operation switch
			{
				"Insert" => repository.InsertAsync(entity, cancellationToken),
				"Update" => repository.UpdateAsync(entity, cancellationToken),
				"Delete" => repository.DeleteAsync(entity, cancellationToken),
				"SaveOrUpdate" => repository.SaveOrUpdateAsync(entity, cancellationToken),
				"DeleteMultiple" => repository.DeleteMultipleAsync(entity, "CallId", 1, new List<object> { 1 }, cancellationToken),
				_ => throw new ArgumentOutOfRangeException(nameof(operation))
			};
		}

		private static ProbeRepository Probe()
		{
			var unitOfWork = new Mock<IUnitOfWork>();
			var connection = Mock.Of<DbConnection>();
			unitOfWork.SetupGet(u => u.Connection).Returns(connection);
			unitOfWork.Setup(u => u.CreateOrGetConnection()).Returns(connection);
			return new ProbeRepository(Mock.Of<IConnectionProvider>(), new SqlServerConfiguration(), unitOfWork.Object, Mock.Of<IQueryFactory>());
		}

		private static async Task<string> CaptureConsoleAsync(Func<Task> action)
		{
			var originalOut = Console.Out;
			var originalError = Console.Error;
			using var writer = new StringWriter();
			Console.SetOut(writer);
			Console.SetError(writer);
			try
			{
				await action();
			}
			finally
			{
				Console.SetOut(originalOut);
				Console.SetError(originalError);
			}

			return writer.ToString();
		}

		private sealed class ProbeRepository : RmsRepositoryBase<CallLocationKey>
		{
			public ProbeRepository(IConnectionProvider connectionProvider, Resgrid.Repositories.DataRepository.Configs.SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
				: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

			public Task<int> Run(Func<DbConnection, Task<int>> work, CancellationToken cancellationToken) => RunAsync(work, cancellationToken);
		}
	}
}
