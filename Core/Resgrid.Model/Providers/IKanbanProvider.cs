using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IKanbanProvider
	{
		Task<bool> CreateDepartmentInSalesPipeline(Department department, string name, string emailAddress);
	}
}