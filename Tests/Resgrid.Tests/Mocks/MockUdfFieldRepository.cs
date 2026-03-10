using Resgrid.Model;
using Resgrid.Model.Repositories;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Tests.Mocks
{
	public sealed class MockUdfFieldRepository : IUdfFieldRepository
	{
		private readonly List<UdfField> _fields = new List<UdfField>();

		public Task<IEnumerable<UdfField>> GetFieldsByDefinitionIdAsync(string definitionId)
		{
			var result = _fields.Where(f => f.UdfDefinitionId == definitionId).OrderBy(f => f.SortOrder);
			return Task.FromResult<IEnumerable<UdfField>>(result.ToList());
		}

		public Task<IEnumerable<UdfField>> GetAllAsync() => Task.FromResult<IEnumerable<UdfField>>(_fields);

		public Task<UdfField> GetByIdAsync(object id)
		{
			var result = _fields.FirstOrDefault(f => f.UdfFieldId == (string)id);
			return Task.FromResult(result);
		}

		public Task<IEnumerable<UdfField>> GetAllByDepartmentIdAsync(int departmentId) => Task.FromResult<IEnumerable<UdfField>>(new List<UdfField>());
		public Task<IEnumerable<UdfField>> GetAllByUserIdAsync(string userId) => Task.FromResult<IEnumerable<UdfField>>(new List<UdfField>());

		public Task<UdfField> InsertAsync(UdfField entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			_fields.Add(entity);
			return Task.FromResult(entity);
		}

		public Task<UdfField> UpdateAsync(UdfField entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => Task.FromResult(entity);

		public Task<bool> DeleteAsync(UdfField entity, CancellationToken cancellationToken)
		{
			_fields.Remove(entity);
			return Task.FromResult(true);
		}

		public Task<UdfField> SaveOrUpdateAsync(UdfField entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			var existing = _fields.FirstOrDefault(f => f.UdfFieldId == entity.UdfFieldId);
			if (existing != null) _fields.Remove(existing);
			_fields.Add(entity);
			return Task.FromResult(entity);
		}

		public Task<bool> DeleteMultipleAsync(UdfField entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken) => Task.FromResult(true);
	}
}

