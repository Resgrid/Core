using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Voice;

namespace Resgrid.Repositories.DataRepository
{
	public class DepartmentVoiceRepository : RepositoryBase<DepartmentVoice>, IDepartmentVoiceRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public DepartmentVoiceRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<DepartmentVoice> GetDepartmentVoiceByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<DepartmentVoice>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectVoiceByDIdQuery>();

					var dictionary = new Dictionary<string, DepartmentVoice>();
					var result = await x.QueryAsync<DepartmentVoice, DepartmentVoiceChannel, DepartmentVoice>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: DepartmentVoiceChannelMapping(dictionary),
						splitOn: "DepartmentVoiceChannelId");

					if (dictionary.Count > 0)
						return dictionary.Select(y => y.Value).FirstOrDefault();

					return result.FirstOrDefault();
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		private static Func<DepartmentVoice, DepartmentVoiceChannel, DepartmentVoice> DepartmentVoiceChannelMapping(Dictionary<string, DepartmentVoice> dictionary)
		{
			return new Func<DepartmentVoice, DepartmentVoiceChannel, DepartmentVoice>((obj, detail) =>
			{
				var dictObj = default(DepartmentVoice);

				if (detail != null)
				{
					if (dictionary.TryGetValue((string)obj.IdValue, out dictObj))
					{
						if (dictObj.Channels.All(x => x.DepartmentVoiceChannelId != detail.DepartmentVoiceChannelId))
							dictObj.Channels.Add(detail);
					}
					else
					{
						if (obj.Channels == null)
							obj.Channels = new List<DepartmentVoiceChannel>();

						obj.Channels.Add(detail);
						dictionary.Add((string)obj.IdValue, obj);

						dictObj = obj;
					}
				}
				else
				{
					obj.Channels = new List<DepartmentVoiceChannel>();
					dictObj = obj;
					dictionary.Add((string)obj.IdValue, obj);
				}

				return dictObj;
			});
		}
	}
}
