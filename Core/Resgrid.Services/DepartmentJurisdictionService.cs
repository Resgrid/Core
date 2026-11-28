using System;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>G8 CA1: the department's address (country, state or province) normalized to a template jurisdiction code.</summary>
	public class DepartmentJurisdictionService : IDepartmentJurisdictionService
	{
		private readonly IDepartmentsService _departments;
		private readonly IAddressService _addresses;

		public DepartmentJurisdictionService(IDepartmentsService departments, IAddressService addresses)
		{
			_departments = departments;
			_addresses = addresses;
		}

		public async Task<string> GetJurisdictionAsync(int departmentId)
		{
			if (departmentId <= 0) return null;
			try
			{
				var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
				var address = department?.Address ?? (department?.AddressId is int id && id > 0 ? await _addresses.GetAddressByIdAsync(id) : null);
				return address == null ? null : TemplateJurisdictions.FromAddress(address.Country, address.State);
			}
			catch (Exception ex)
			{
				// A catalog view must still open: an unreadable address only means the catalog shows everything.
				Framework.Logging.LogException(ex);
				return null;
			}
		}
	}
}
