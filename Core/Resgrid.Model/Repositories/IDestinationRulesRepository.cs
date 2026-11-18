namespace Resgrid.Model.Repositories
{
	/// <summary>A department's destination rules (DestinationRules, M0317). Rows are read by department (GetAllByDepartmentIdAsync).</summary>
	public interface IDestinationRulesRepository : IRepository<DestinationRule>
	{
	}
}
