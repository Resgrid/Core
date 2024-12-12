using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Protocols;

namespace Resgrid.Repositories.DataRepository
{
	public class DispatchProtocolQuestionsRepository : RepositoryBase<DispatchProtocolQuestion>, IDispatchProtocolQuestionsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public DispatchProtocolQuestionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<DispatchProtocolQuestion>> GetDispatchProtocolQuestionsByProtocolIdAsync(int protocolId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<DispatchProtocolQuestion>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("ProtocolId", protocolId);

					var query = _queryFactory.GetQuery<SelectProtocolQuestionsByProIdQuery>();

					var dictionary = new Dictionary<int, DispatchProtocolQuestion>();
					var result = await x.QueryAsync<DispatchProtocolQuestion, DispatchProtocolQuestionAnswer, DispatchProtocolQuestion>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: DispatchProtocolQuestionAnswerMapping(dictionary),
						splitOn: "DispatchProtocolQuestionAnswerId");

					if (dictionary.Count > 0)
						return dictionary.Select(y => y.Value);

					return result;
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

		private static Func<DispatchProtocolQuestion, DispatchProtocolQuestionAnswer, DispatchProtocolQuestion> DispatchProtocolQuestionAnswerMapping(Dictionary<int, DispatchProtocolQuestion> dictionary)
		{
			return new Func<DispatchProtocolQuestion, DispatchProtocolQuestionAnswer, DispatchProtocolQuestion>((obj, detail) =>
			{
				var dictObj = default(DispatchProtocolQuestion);

				if (detail != null)
				{
					if (dictionary.TryGetValue((int)obj.IdValue, out dictObj))
					{
						if (dictObj.Answers.All(x => x.IdValue != detail.IdValue))
							dictObj.Answers.Add(detail);
					}
					else
					{
						if (obj.Answers == null)
							obj.Answers = new List<DispatchProtocolQuestionAnswer>();

						obj.Answers.Add(detail);
						dictionary.Add((int)obj.IdValue, obj);

						dictObj = obj;
					}
				}
				else
				{
					obj.Answers = new List<DispatchProtocolQuestionAnswer>();
					dictObj = obj;
					dictionary.Add((int)obj.IdValue, obj);
				}

				return dictObj;
			});
		}
	}
}
