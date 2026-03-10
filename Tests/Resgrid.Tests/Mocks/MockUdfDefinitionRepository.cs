using Resgrid.Model;
using Resgrid.Model.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Tests.Mocks
{
	public sealed class MockUdfDefinitionRepository : IUdfDefinitionRepository
	{
		private readonly List<UdfDefinition> _definitions = new List<UdfDefinition>();

		public Task<UdfDefinition> GetActiveDefinitionByDepartmentAndEntityTypeAsync(int departmentId, int entityType)
		{
			var result = _definitions.FirstOrDefault(d => d.DepartmentId == departmentId && d.EntityType == entityType && d.IsActive);
			return Task.FromResult(result);
		}

		public Task<IEnumerable<UdfDefinition>> GetAllDefinitionVersionsByDepartmentAndEntityTypeAsync(int departmentId, int entityType)
		{
			var result = _definitions.Where(d => d.DepartmentId == departmentId && d.EntityType == entityType)
				.OrderByDescending(d => d.Version);
			return Task.FromResult<IEnumerable<UdfDefinition>>(result.ToList());
		}

		public Task<bool> DeactivateDefinitionsByDepartmentAndEntityTypeAsync(int departmentId, int entityType, CancellationToken cancellationToken)
		{
			foreach (var d in _definitions.Where(d => d.DepartmentId == departmentId && d.EntityType == entityType && d.IsActive))
				d.IsActive = false;
			return Task.FromResult(true);
		}

		public Task<IEnumerable<UdfDefinition>> GetAllAsync() => Task.FromResult<IEnumerable<UdfDefinition>>(_definitions);

		public Task<UdfDefinition> GetByIdAsync(object id)
		{
			var result = _definitions.FirstOrDefault(d => d.UdfDefinitionId == (string)id);
			return Task.FromResult(result);
		}

		public Task<IEnumerable<UdfDefinition>> GetAllByDepartmentIdAsync(int departmentId)
		{
			var result = _definitions.Where(d => d.DepartmentId == departmentId);
			return Task.FromResult<IEnumerable<UdfDefinition>>(result.ToList());
		}

		public Task<IEnumerable<UdfDefinition>> GetAllByUserIdAsync(string userId) => Task.FromResult<IEnumerable<UdfDefinition>>(new List<UdfDefinition>());

		public Task<UdfDefinition> InsertAsync(UdfDefinition entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			_definitions.Add(entity);
			return Task.FromResult(entity);
		}

		public Task<UdfDefinition> UpdateAsync(UdfDefinition entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => Task.FromResult(entity);

		public Task<bool> DeleteAsync(UdfDefinition entity, CancellationToken cancellationToken)
		{
			_definitions.Remove(entity);
			return Task.FromResult(true);
		}

		public Task<UdfDefinition> SaveOrUpdateAsync(UdfDefinition entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			var existing = _definitions.FirstOrDefault(d => d.UdfDefinitionId == entity.UdfDefinitionId);
			if (existing != null) _definitions.Remove(existing);
			_definitions.Add(entity);
			return Task.FromResult(entity);
		}

		public Task<bool> DeleteMultipleAsync(UdfDefinition entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken) => Task.FromResult(true);
	}
}

