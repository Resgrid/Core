using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Repository for per-department chatbot configuration (ChatbotDepartmentConfigs). Standard CRUD
	/// is inherited from <see cref="IRepository{T}"/> (including GetAllByDepartmentIdAsync).
	/// </summary>
	public interface IChatbotDepartmentConfigRepository : IRepository<ChatbotDepartmentConfig>
	{
		/// <summary>Gets the single configuration row for a department, or null if none exists.</summary>
		Task<ChatbotDepartmentConfig> GetByDepartmentIdAsync(int departmentId);
	}
}
