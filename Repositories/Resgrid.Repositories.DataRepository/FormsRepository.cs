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
using Resgrid.Repositories.DataRepository.Queries.Forms;

namespace Resgrid.Repositories.DataRepository
{
	public class FormsRepository : RepositoryBase<Form>, IFormsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public FormsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<Form> GetFormByIdAsync(string formId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<Form>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("FormId", formId);

					var query = _queryFactory.GetQuery<SelectFormByIdQuery>();

					var dictionary = new Dictionary<string, Form>();
					var result = await x.QueryAsync<Form, FormAutomation, Form>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: FormAutomationMapping(dictionary),
						splitOn: "FormAutomationId");

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

		public async Task<IEnumerable<Form>> GetFormsByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<Form>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectFormsByDIdQuery>();

					var dictionary = new Dictionary<string, Form>();
					var result = await x.QueryAsync<Form, FormAutomation, Form>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: FormAutomationMapping(dictionary),
						splitOn: "FormAutomationId");

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

		public async Task<IEnumerable<Form>> GetNonDeletedFormsByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<Form>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectFormsByDIdQuery>();

					var dictionary = new Dictionary<string, Form>();
					var result = await x.QueryAsync<Form, FormAutomation, Form>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: FormAutomationMapping(dictionary),
						splitOn: "FormAutomationId");

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

		public async Task<bool> EnableFormByIdAsync(string id)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("FormId", id);

					var query = _queryFactory.GetQuery<UpdateFormsToEnableQuery>();

					var result = await x.QueryAsync<Call>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);

					return true;
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

				throw;
			}
		}

		public async Task<bool> DisableFormByIdAsync(string id)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("FormId", id);

					var query = _queryFactory.GetQuery<UpdateFormsToDisableQuery>();

					var result = await x.QueryAsync<Call>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);

					return true;
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

				throw;
			}
		}

		private static Func<Form, FormAutomation, Form> FormAutomationMapping(Dictionary<string, Form> dictionary)
		{
			return new Func<Form, FormAutomation, Form>((obj, detail) =>
			{
				var dictObj = default(Form);

				if (detail != null)
				{
					if (dictionary.TryGetValue((string)obj.IdValue, out dictObj))
					{
						if (dictObj.Automations.All(x => x.FormAutomationId != detail.FormAutomationId))
							dictObj.Automations.Add(detail);
					}
					else
					{
						if (obj.Automations == null)
							obj.Automations = new List<FormAutomation>();

						obj.Automations.Add(detail);
						dictionary.Add((string)obj.IdValue, obj);

						dictObj = obj;
					}
				}
				else
				{
					obj.Automations = new List<FormAutomation>();
					dictObj = obj;
					dictionary.Add((string)obj.IdValue, obj);
				}

				return dictObj;
			});
		}
	}
}
