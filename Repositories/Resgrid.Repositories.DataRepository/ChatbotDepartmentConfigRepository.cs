using System;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	public class ChatbotDepartmentConfigRepository : AuditedConfigurationRepository<ChatbotDepartmentConfig>, IChatbotDepartmentConfigRepository
	{
		public ChatbotDepartmentConfigRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory, Resgrid.Model.AdminAssist.IConfigurationChangeJournal configurationJournal = null)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory, configurationJournal)
		{
		}

		public async Task<ChatbotDepartmentConfig> GetByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var all = await GetAllByDepartmentIdAsync(departmentId);
				return all?.FirstOrDefault();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}
}
