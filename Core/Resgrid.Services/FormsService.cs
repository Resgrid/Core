using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class FormsService : IFormsService
	{
		private readonly IFormsRepository _formsRepository;
		private readonly IFormAutomationsRepository _formAutomationsRepository;

		public FormsService(IFormsRepository formsRepository, IFormAutomationsRepository formAutomationsRepository)
		{
			_formsRepository = formsRepository;
			_formAutomationsRepository = formAutomationsRepository;
		}

		public async Task<List<Form>> GetAllFormsForDepartmentAsync(int departmentId)
		{
			var items = await _formsRepository.GetAllByDepartmentIdAsync(departmentId);

			foreach (var form in items)
			{
				form.Automations = (await _formAutomationsRepository.GetFormAutomationsByFormIdAsync(form.FormId)).ToList();
			}

			return items.ToList();
		}

		public async Task<List<Form>> GetAllNonDeletedFormsForDepartmentAsync(int departmentId)
		{
			var items = await _formsRepository.GetNonDeletedFormsByDepartmentIdAsync(departmentId);

			foreach (var form in items)
			{
				form.Automations = (await _formAutomationsRepository.GetFormAutomationsByFormIdAsync(form.FormId)).ToList();
			}

			return items.ToList();
		}

		public async Task<Form> SaveFormAsync(Form form, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (String.IsNullOrWhiteSpace(form.FormId))
			{
				form.UpdatedOn = DateTime.UtcNow;
				form.CreatedOn = DateTime.UtcNow;
			}
			else
				form.UpdatedOn = DateTime.UtcNow;

			var saved = await _formsRepository.SaveOrUpdateAsync(form, cancellationToken, true);

			if (form.Automations != null)
			{
				foreach (var a in form.Automations)
				{
					a.FormId = saved.FormId;
					await _formAutomationsRepository.SaveOrUpdateAsync(a, cancellationToken, true);
				}
			}

			return saved;
		}

		public async Task<Form> GetFormByIdAsync(string id)
		{
			var form = await _formsRepository.GetFormByIdAsync(id);
			form.Automations = (await _formAutomationsRepository.GetFormAutomationsByFormIdAsync(id)).ToList();

			return form;
		}

		public async Task<Form> GetNewCallFormByDepartmentIdAsync(int departmentId)
		{
			var forms = await GetAllNonDeletedFormsForDepartmentAsync(departmentId);

			if (forms != null && forms.Any())
				return forms.FirstOrDefault(x => x.Type == 0 && x.IsActive == true);

			return null;
		}

		public async Task<bool> DeleteForm(string id, CancellationToken cancellationToken = default(CancellationToken))
		{
			var form = await GetFormByIdAsync(id);
			form.IsDeleted = true;
			await _formsRepository.SaveOrUpdateAsync(form, cancellationToken);

			return true;
		}

		public async Task<bool> EnableFormByIdAsync(string id)
		{
			var form = await GetFormByIdAsync(id);
			var forms = await GetAllNonDeletedFormsForDepartmentAsync(form.DepartmentId);

			// For right now, were only going to allow one active form for each type in the system.
			var count = forms.Count(x => x.Type == form.Type && x.IsActive == true && x.FormId != form.FormId);
			if (count <= 0)
				return await _formsRepository.EnableFormByIdAsync(id);

			return false;
		}

		public async Task<bool> DisableFormByIdAsync(string id)
		{
			return await _formsRepository.DisableFormByIdAsync(id);
		}

		public FormAutomationData ProcessForm(Form form, string data)
		{
			var formData = new FormAutomationData();

			if (form == null && (form.Automations == null || !form.Automations.Any()))
				return null;

			JObject json = JObject.Parse(data);

			foreach (var automation in form.Automations)
			{
				
			}

			return formData;
		}
	}
}
