using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	public class IncidentCommandRepository : RepositoryBase<IncidentCommand>, IIncidentCommandRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public IncidentCommandRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public async Task<IncidentCommand> GetByPublicShareTokenAsync(string publicShareToken)
		{
			if (string.IsNullOrWhiteSpace(publicShareToken))
				return null;

			try
			{
				var parameters = new DynamicParametersExtension();
				parameters.Add("PublicShareToken", publicShareToken);
				var notation = _sqlConfiguration.ParameterNotation;
				var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.incidentcommands WHERE publicsharetoken = {notation}PublicShareToken AND publicshareenabled = true"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[IncidentCommands] WHERE [PublicShareToken] = {notation}PublicShareToken AND [PublicShareEnabled] = 1";

				var select = new Func<DbConnection, Task<IEnumerable<IncidentCommand>>>(connection =>
					connection.QueryAsync<IncidentCommand>(sql, parameters, _unitOfWork.Transaction));

				if (_unitOfWork?.Connection == null)
				{
					using var connection = _connectionProvider.Create();
					await connection.OpenAsync();
					return (await select(connection)).FirstOrDefault();
				}

				return (await select(_unitOfWork.CreateOrGetConnection())).FirstOrDefault();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}

	public class CommandStructureNodeRepository : RepositoryBase<CommandStructureNode>, ICommandStructureNodeRepository
	{
		public CommandStructureNodeRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
		}
	}

	public class ResourceAssignmentRepository : RepositoryBase<ResourceAssignment>, IResourceAssignmentRepository
	{
		public ResourceAssignmentRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
		}
	}

	public class TacticalObjectiveRepository : RepositoryBase<TacticalObjective>, ITacticalObjectiveRepository
	{
		public TacticalObjectiveRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
		}
	}

	public class IncidentTimerRepository : RepositoryBase<IncidentTimer>, IIncidentTimerRepository
	{
		public IncidentTimerRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
		}
	}

	public class IncidentMapAnnotationRepository : RepositoryBase<IncidentMapAnnotation>, IIncidentMapAnnotationRepository
	{
		public IncidentMapAnnotationRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
		}
	}

	public class CommandLogEntryRepository : RepositoryBase<CommandLogEntry>, ICommandLogEntryRepository
	{
		public CommandLogEntryRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
		}
	}

	public class CommandTransferRepository : RepositoryBase<CommandTransfer>, ICommandTransferRepository
	{
		public CommandTransferRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
		}
	}

	public class IncidentNeedRepository : RepositoryBase<IncidentNeed>, IIncidentNeedRepository
	{
		public IncidentNeedRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
		}
	}

	public class IncidentNoteRepository : RepositoryBase<IncidentNote>, IIncidentNoteRepository
	{
		public IncidentNoteRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
		}
	}

	public class IncidentAttachmentRepository : RepositoryBase<IncidentAttachment>, IIncidentAttachmentRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IUnitOfWork _unitOfWork;

		public IncidentAttachmentRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<IncidentAttachment>> GetAllMetadataByDepartmentIdAsync(int departmentId)
		{
			var parameters = new DynamicParametersExtension();
			parameters.Add("DepartmentId", departmentId);
			var notation = _sqlConfiguration.ParameterNotation;
			var sql = DataConfig.DatabaseType == DatabaseTypes.Postgres
				? $"SELECT incidentattachmentid, incidentcommandid, departmentid, callid, visibility, filename, contenttype, contentlength, sha256hash, description, uploadedbyuserid, uploadedon, deletedon, deletedbyuserid, modifiedon FROM {_sqlConfiguration.SchemaName}.incidentattachments WHERE departmentid = {notation}DepartmentId"
				: $"SELECT [IncidentAttachmentId], [IncidentCommandId], [DepartmentId], [CallId], [Visibility], [FileName], [ContentType], [ContentLength], [Sha256Hash], [Description], [UploadedByUserId], [UploadedOn], [DeletedOn], [DeletedByUserId], [ModifiedOn] FROM {_sqlConfiguration.SchemaName}.[IncidentAttachments] WHERE [DepartmentId] = {notation}DepartmentId";

			var select = new Func<DbConnection, Task<IEnumerable<IncidentAttachment>>>(connection =>
				connection.QueryAsync<IncidentAttachment>(sql, parameters, _unitOfWork.Transaction));

			if (_unitOfWork?.Connection == null)
			{
				using var connection = _connectionProvider.Create();
				await connection.OpenAsync();
				return await select(connection);
			}

			return await select(_unitOfWork.CreateOrGetConnection());
		}
	}

	public class VoiceTransmissionLogRepository : RepositoryBase<VoiceTransmissionLog>, IVoiceTransmissionLogRepository
	{
		public VoiceTransmissionLogRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
		}
	}
}
