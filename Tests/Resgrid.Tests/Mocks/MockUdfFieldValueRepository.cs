using Resgrid.Model;
using Resgrid.Model.Repositories;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Tests.Mocks
{
	public sealed class MockUdfFieldValueRepository : IUdfFieldValueRepository
	{
		private readonly List<UdfFieldValue> _values = new List<UdfFieldValue>();

		public Task<IEnumerable<UdfFieldValue>> GetFieldValuesByEntityAsync(int entityType, string entityId, string definitionId)
		{
			var result = _values.Where(v => v.EntityType == entityType && v.EntityId == entityId && v.UdfDefinitionId == definitionId);
			return Task.FromResult<IEnumerable<UdfFieldValue>>(result.ToList());
		}

		public Task<bool> DeleteFieldValuesByEntityAndDefinitionAsync(int entityType, string entityId, string definitionId, CancellationToken cancellationToken)
		{
			_values.RemoveAll(v => v.EntityType == entityType && v.EntityId == entityId && v.UdfDefinitionId == definitionId);
			return Task.FromResult(true);
		}

		public Task<IEnumerable<UdfFieldValue>> GetAllAsync() => Task.FromResult<IEnumerable<UdfFieldValue>>(_values);

		public Task<UdfFieldValue> GetByIdAsync(object id)
		{
			var result = _values.FirstOrDefault(v => v.UdfFieldValueId == (string)id);
			return Task.FromResult(result);
		}

		public Task<IEnumerable<UdfFieldValue>> GetAllByDepartmentIdAsync(int departmentId) => Task.FromResult<IEnumerable<UdfFieldValue>>(new List<UdfFieldValue>());
		public Task<IEnumerable<UdfFieldValue>> GetAllByUserIdAsync(string userId) => Task.FromResult<IEnumerable<UdfFieldValue>>(new List<UdfFieldValue>());

		public Task<UdfFieldValue> InsertAsync(UdfFieldValue entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			_values.Add(entity);
			return Task.FromResult(entity);
		}

		public Task<UdfFieldValue> UpdateAsync(UdfFieldValue entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => Task.FromResult(entity);

		public Task<bool> DeleteAsync(UdfFieldValue entity, CancellationToken cancellationToken)
		{
			_values.Remove(entity);
			return Task.FromResult(true);
		}

		public Task<UdfFieldValue> SaveOrUpdateAsync(UdfFieldValue entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			var existing = _values.FirstOrDefault(v => v.UdfFieldValueId == entity.UdfFieldValueId);
			if (existing != null) _values.Remove(existing);
			_values.Add(entity);
			return Task.FromResult(entity);
		}

		public Task<bool> DeleteMultipleAsync(UdfFieldValue entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken) => Task.FromResult(true);
	}
}

