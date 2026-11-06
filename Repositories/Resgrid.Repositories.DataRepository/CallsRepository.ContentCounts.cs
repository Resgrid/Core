using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Model;

namespace Resgrid.Repositories.DataRepository
{
	public partial class CallsRepository
	{
		public async Task<IEnumerable<CallContentCounts>> GetCallContentCountsAsync(int departmentId, IEnumerable<int> callIds,
			CancellationToken cancellationToken = default)
		{
			var ids = (callIds ?? Enumerable.Empty<int>()).Distinct().ToArray();
			if (ids.Length == 0)
				return Enumerable.Empty<CallContentCounts>();

			// Counted, not loaded: an attachment row carries its file. Every row is counted, as the per-call note and attachment
			// lookups (SelectCallNotesByCallIdQuery, SelectCallAttachmentByCallIdQuery) return them.
			var postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			var schema = _sqlConfiguration.SchemaName;
			var sql = postgres
				? "SELECT c.callid AS CallId, " +
				  $"(SELECT COUNT(*) FROM {schema}.callnotes n WHERE n.callid = c.callid) AS NoteCount, " +
				  $"(SELECT COUNT(*) FROM {schema}.callattachments a WHERE a.callid = c.callid AND a.callattachmenttype = {(int)CallAttachmentTypes.DispatchAudio}) AS AudioCount, " +
				  $"(SELECT COUNT(*) FROM {schema}.callattachments a WHERE a.callid = c.callid AND a.callattachmenttype = {(int)CallAttachmentTypes.Image}) AS ImageCount, " +
				  $"(SELECT COUNT(*) FROM {schema}.callattachments a WHERE a.callid = c.callid AND a.callattachmenttype = {(int)CallAttachmentTypes.File}) AS FileCount " +
				  $"FROM {schema}.calls c " +
				  "WHERE c.departmentid = @DepartmentId AND c.callid = ANY(@CallIds)"
				: "SELECT c.[CallId] AS [CallId], " +
				  $"(SELECT COUNT(*) FROM {schema}.[CallNotes] n WHERE n.[CallId] = c.[CallId]) AS [NoteCount], " +
				  $"(SELECT COUNT(*) FROM {schema}.[CallAttachments] a WHERE a.[CallId] = c.[CallId] AND a.[CallAttachmentType] = {(int)CallAttachmentTypes.DispatchAudio}) AS [AudioCount], " +
				  $"(SELECT COUNT(*) FROM {schema}.[CallAttachments] a WHERE a.[CallId] = c.[CallId] AND a.[CallAttachmentType] = {(int)CallAttachmentTypes.Image}) AS [ImageCount], " +
				  $"(SELECT COUNT(*) FROM {schema}.[CallAttachments] a WHERE a.[CallId] = c.[CallId] AND a.[CallAttachmentType] = {(int)CallAttachmentTypes.File}) AS [FileCount] " +
				  $"FROM {schema}.[Calls] c " +
				  "WHERE c.[DepartmentId] = @DepartmentId AND c.[CallId] IN @CallIds";

			// Npgsql binds the int[] for ANY; Dapper expands it into an IN list on SQL Server.
			var parameters = new { DepartmentId = departmentId, CallIds = ids };

			try
			{
				if (_unitOfWork?.Connection != null)
					return await _unitOfWork.CreateOrGetConnection().QueryAsync<CallContentCounts>(new Dapper.CommandDefinition(sql, parameters, _unitOfWork.Transaction,
						cancellationToken: cancellationToken));

				await using var connection = _connectionProvider.Create();
				await connection.OpenAsync(cancellationToken);
				return await connection.QueryAsync<CallContentCounts>(new Dapper.CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				throw;
			}
		}
	}
}
