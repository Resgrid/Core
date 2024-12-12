using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Common;

namespace Resgrid.Repositories.DataRepository
{
	public class RepositoryBase<T> : IDisposable, IRepository<T> where T : class, IEntity
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public RepositoryBase(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public virtual async Task<IEnumerable<T>> GetAllAsync()
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<T>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();

					var query = _queryFactory.GetQuery<SelectAllQuery, T>();

					return await x.QueryAsync<T>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);
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

		public virtual async Task<T> GetByIdAsync(object id)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<T>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();

					if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
						dynamicParameters.Add("id", id);
					else
						dynamicParameters.Add("Id", id);

					var query = _queryFactory.GetQuery<SelectByIdQuery, T>();

					return await x.QueryFirstOrDefaultAsync<T>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);
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

		public virtual async Task<IEnumerable<T>> GetAllByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<T>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();

					if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
						dynamicParameters.Add("departmentid", departmentId);
					else
						dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectByDepartmentIdQuery, T>();

					return await x.QueryAsync<T>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);
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

		public virtual async Task<T> InsertAsync(T entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			try
			{
				var insertFunction = new Func<DbConnection, Task<T>>(async x =>
				{
					var dynamicParameters = new DynamicParameters(entity);

					var query = _queryFactory.GetInsertQuery<InsertQuery, T>(entity);

					if (((IEntity)entity).IdType == 0)
					{
						var result = await x.QuerySingleAsync<int>(query, dynamicParameters, _unitOfWork.Transaction);
						((IEntity)entity).IdValue = result;
					}
					else
					{
						var result = await x.QueryAsync(query, dynamicParameters, _unitOfWork.Transaction);
					}

					if (!firstLevelOnly)
						await HandleChildObjects(entity, cancellationToken);

					return entity;
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync(cancellationToken);

						return await insertFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await insertFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				throw;
			}
		}

		public virtual async Task<T> UpdateAsync(T entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			try
			{
				var updateFunction = new Func<DbConnection, Task<T>>(async x =>
				{


					var dynamicParameters = new DynamicParameters(entity);

					var query = _queryFactory.GetUpdateQuery<UpdateQuery, T>(entity);

					var result = await x.ExecuteAsync(query, dynamicParameters, _unitOfWork.Transaction);

					if (!firstLevelOnly)
					{
						await SyncChildArrayUpdates(entity, cancellationToken);
						await HandleChildObjects(entity, cancellationToken);
					}

					return entity;
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync(cancellationToken);
						return await updateFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await updateFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				throw;
			}
		}

		public virtual async Task<bool> DeleteAsync(T entity, CancellationToken cancellationToken)
		{
			if (entity != null)
			{
				try
				{
					var removeFunction = new Func<DbConnection, Task<bool>>(async x =>
					{
						var dynamicParameters = new DynamicParametersExtension();
						dynamicParameters.Add("Id", ((IEntity)entity).IdValue);

						var query = _queryFactory.GetDeleteQuery<DeleteQuery, T>(entity);

						var result = await x.ExecuteAsync(query, dynamicParameters, _unitOfWork.Transaction);

						return result > 0;
					});

					DbConnection conn = null;
					if (_unitOfWork?.Connection == null)
					{
						using (conn = _connectionProvider.Create())
						{
							await conn.OpenAsync(cancellationToken);

							return await removeFunction(conn);
						}
					}
					else
					{
						conn = _unitOfWork.CreateOrGetConnection();
						return await removeFunction(conn);
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);

					throw;
				}
			}

			return false;
		}

		public virtual async Task<T> SaveOrUpdateAsync(T entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			bool didParse = false;
			int idValue = 0;
			if (((IEntity)entity).IdValue != null)
				didParse = int.TryParse(entity.IdValue.ToString(), out idValue);

			if (((IEntity)entity).IdValue == null || (didParse && idValue == 0))
			{
				if (((IEntity)entity).IdType == 1)
					((IEntity)entity).IdValue = Guid.NewGuid().ToString();

				return await InsertAsync(entity, cancellationToken, firstLevelOnly);
			}

			return await UpdateAsync(entity, cancellationToken, firstLevelOnly);
		}

		public virtual async Task<IEnumerable<T>> GetAllByUserIdAsync(string userId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<T>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);

					var query = _queryFactory.GetQuery<SelectByUserIdQuery, T>();

					return await x.QueryAsync<T>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);
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

		public virtual async Task<bool> DeleteMultipleAsync(T entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken)
		{
			try
			{
				var removeFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					var usersToQuery = String.Join(",", ids.Select(p => $"{p.ToString()}").ToArray());
					dynamicParameters.Add("ParentId", parentKeyId);

					var query = _queryFactory.GetDeleteQuery<DeleteMultipleQuery, T>(entity);
					query = query.Replace("%IDS%", usersToQuery, StringComparison.InvariantCultureIgnoreCase);
					query = query.Replace("%PARENTKEYNAME%", parentKeyName, StringComparison.InvariantCultureIgnoreCase);

					var result = await x.ExecuteAsync(query, dynamicParameters, _unitOfWork.Transaction);

					return result > 0;
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync(cancellationToken);

						return await removeFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await removeFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				throw;
			}
		}

		private async Task HandleChildObjects(T entity, CancellationToken cancellationToken)
		{
			var properties = entity.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
			var idName = ((IEntity)entity).IdName;
			var idValue = ((IEntity)entity).IdValue;

			if (properties != null)
			{
				foreach (var property in properties)
				{
					if (Attribute.IsDefined(property, typeof(System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute)))
						continue;

					if (property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(ICollection<>))
					{
						var collection = (IEnumerable)property.GetValue(entity, null);

						if (collection != null)
						{
							MethodInfo genericSave = null;
							object baseRepo = null;

							foreach (var item in collection)
							{
								if (item.GetType().GetInterfaces().Contains(typeof(IEntity)))
								{
									var objectProperties = item.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
									var idFields = objectProperties.Where(x => x.Name.Contains(idName));

									if (genericSave == null || baseRepo == null)
									{
										var d1 = typeof(RepositoryBase<>);
										Type[] typeArgs = { item.GetType() };
										var makeme = d1.MakeGenericType(typeArgs);
										genericSave = makeme.GetMethod("SaveOrUpdateAsync");
										baseRepo = Activator.CreateInstance(makeme, new object[] { _connectionProvider, _sqlConfiguration, _unitOfWork, _queryFactory });
									}

									if (idFields != null && idFields.Any())
									{
										if (idFields.Count() == 1)
										{
											idFields.First().SetValue(item, idValue, null);
											await genericSave.InvokeAsync(baseRepo, new[] { item, cancellationToken, true });
										}

									}
								}
							}
						}
					}
					else if (property.PropertyType.GetInterfaces().Contains(typeof(IEntity)))
					{
						var objectProperties = property.PropertyType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.GetProperty);
						var idFields = objectProperties.Where(x => x.Name.Contains(idName));

						if (idFields != null && idFields.Any())
						{
							var d1 = typeof(RepositoryBase<>);
							Type[] typeArgs = { property.PropertyType };
							var makeme = d1.MakeGenericType(typeArgs);
							MethodInfo genericSave = makeme.GetMethod("SaveOrUpdateAsync");
							var baseRepo = Activator.CreateInstance(makeme, new object[] { _connectionProvider, _sqlConfiguration, _unitOfWork, _queryFactory });

							if (idFields.Count() == 1)
							{
								ReflectionHelpers.SetProperty($"{property.Name}.{idFields.First().Name}", entity, idValue);
								var subObj = property.GetValue(entity, null);

								await genericSave.InvokeAsync(baseRepo, new[] { subObj, cancellationToken, true });
							}

						}

					}
				}
			}
		}

		private async Task SyncChildArrayUpdates(T entity, CancellationToken cancellationToken)
		{
			var properties = entity.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
			var parentIdName = ((IEntity)entity).IdName;
			var parentIdValue = ((IEntity)entity).IdValue;

			if (properties != null)
			{
				foreach (var property in properties)
				{
					if (Attribute.IsDefined(property, typeof(System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute)))
						continue;

					if (property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(ICollection<>))
					{
						var collection = (IEnumerable)property.GetValue(entity, null);
						object obj = null;

						if (collection != null)
						{
							var ids = new List<object>();

							foreach (var item in collection)
							{
								obj = item;

								int idValue = 0;
								if (((IEntity)item).IdValue != null)
									if (int.TryParse(((IEntity)item).IdValue.ToString(), out idValue))
										if (idValue > 0 && !ids.Contains(idValue))
											ids.Add(idValue);
							}

							if (ids.Any())
							{
								var d1 = typeof(RepositoryBase<>);
								Type[] typeArgs = { property.PropertyType.GetGenericArguments()[0] };
								var makeme = d1.MakeGenericType(typeArgs);
								MethodInfo genericDeleteMulti = makeme.GetMethod("DeleteMultipleAsync");
								var baseRepo = Activator.CreateInstance(makeme, new object[] { _connectionProvider, _sqlConfiguration, _unitOfWork, _queryFactory });

								await genericDeleteMulti.InvokeAsync(baseRepo, new object[] { obj, parentIdName, parentIdValue, ids, cancellationToken });
							}
						}
					}
				}
			}
		}

		public void Dispose()
		{
			_unitOfWork?.Dispose();
		}
	}
}
