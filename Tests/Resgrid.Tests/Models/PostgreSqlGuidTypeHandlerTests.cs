using System;
using System.Data;
using Dapper;
using Moq;
using Npgsql;
using NpgsqlTypes;
using NUnit.Framework;
using Resgrid.Repositories.DataRepository.Servers.PostgreSql;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class PostgreSqlGuidTypeHandlerTests
	{
		private static SqlMapper.TypeHandler<Guid> Handler() => (SqlMapper.TypeHandler<Guid>)Activator.CreateInstance(
			typeof(PostgreSqlConnectionProvider).Assembly.GetType("Resgrid.Repositories.DataRepository.Servers.PostgreSql.PostgreSqlGuidTypeHandler"), true);

		[Test]
		public void Native_uuid_and_legacy_guid_text_both_parse()
		{
			var id = Guid.NewGuid();
			Assert.That(Handler().Parse(id), Is.EqualTo(id));
			Assert.That(Handler().Parse(id.ToString("D")), Is.EqualTo(id));
			Assert.Throws<DataException>(() => Handler().Parse("not-an-identifier"));
		}

		[Test]
		public void Postgres_parameters_remain_bound_and_column_typed()
		{
			var id = Guid.NewGuid();
			var parameter = new NpgsqlParameter();
			Handler().SetValue(parameter, id);
			Assert.That(parameter.NpgsqlDbType, Is.EqualTo(NpgsqlDbType.Unknown));
			Assert.That(parameter.Value, Is.EqualTo(id.ToString("D")));
		}

		[Test]
		public void Other_database_parameters_keep_guid_type()
		{
			var parameter = new Mock<IDbDataParameter>();
			parameter.SetupAllProperties();
			var id = Guid.NewGuid();
			Handler().SetValue(parameter.Object, id);
			Assert.That(parameter.Object.DbType, Is.EqualTo(DbType.Guid));
			Assert.That(parameter.Object.Value, Is.EqualTo(id));
		}
	}
}
