using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class AutofillsService : IAutofillsService
	{
		private readonly IAutofillsRepository _autofillsRepository;

		public AutofillsService(IAutofillsRepository autofillsRepository)
		{
			_autofillsRepository = autofillsRepository;
		}

		public async Task<Autofill> SaveAutofillAsync(Autofill autofill, CancellationToken cancellationToken = default(CancellationToken))
		{
			autofill.AddedOn = DateTime.UtcNow;

			if (autofill.Data == null)
				autofill.Data = "";

			return await _autofillsRepository.SaveOrUpdateAsync(autofill, cancellationToken);
		}

		public async Task<List<Autofill>> GetAllAutofillsForDepartmentAsync(int departmentId)
		{
			var result = await _autofillsRepository.GetAllByDepartmentIdAsync(departmentId);

			return result?.ToList();
		}

		public async Task<List<Autofill>> GetAllAutofillsForDepartmentByTypeAsync(int departmentId, AutofillTypes type)
		{
			var result = await _autofillsRepository.GetAllByDepartmentIdAsync(departmentId);

			return result?.Where(x => x.Type == (int)type).ToList();
		}

		public async Task<Autofill> GetAutofillByIdAsync(string id)
		{
			return await _autofillsRepository.GetByIdAsync(id);
		}

		public async Task<bool> DeleteAutofillAsync(string id, CancellationToken cancellationToken = default(CancellationToken))
		{
			var template = await GetAutofillByIdAsync(id);
			return await _autofillsRepository.DeleteAsync(template, cancellationToken);
		}
	}
}
