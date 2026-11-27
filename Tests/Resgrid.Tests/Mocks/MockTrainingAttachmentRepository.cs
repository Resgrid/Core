using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;

namespace Resgrid.Tests.Mocks
{
	/// <summary>
	/// In-memory mock for <see cref="ITrainingAttachmentRepository"/>
	/// </summary>
	public sealed class MockTrainingAttachmentRepository : ITrainingAttachmentRepository
	{
		private readonly List<TrainingAttachment> _attachments = new List<TrainingAttachment>();
		private int _nextId = 1;

		public Task<IEnumerable<TrainingAttachment>> GetAllAsync()
			=> Task.FromResult<IEnumerable<TrainingAttachment>>(_attachments.ToList());

		public Task<TrainingAttachment> GetByIdAsync(object id)
		{
			var intId = (int)id;
			var attachment = _attachments.FirstOrDefault(a => a.TrainingAttachmentId == intId);
			return Task.FromResult(attachment);
		}

		public Task<IEnumerable<TrainingAttachment>> GetAllByDepartmentIdAsync(int departmentId)
			=> Task.FromResult<IEnumerable<TrainingAttachment>>(new List<TrainingAttachment>());

		public Task<IEnumerable<TrainingAttachment>> GetAllByUserIdAsync(string userId)
			=> Task.FromResult<IEnumerable<TrainingAttachment>>(new List<TrainingAttachment>());

		public Task<TrainingAttachment> InsertAsync(TrainingAttachment entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			entity.TrainingAttachmentId = _nextId++;
			_attachments.Add(entity);
			return Task.FromResult(entity);
		}

		public Task<TrainingAttachment> UpdateAsync(TrainingAttachment entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			var existing = _attachments.FirstOrDefault(a => a.TrainingAttachmentId == entity.TrainingAttachmentId);
			if (existing != null)
			{
				_attachments.Remove(existing);
			}
			_attachments.Add(entity);
			return Task.FromResult(entity);
		}

		public Task<bool> DeleteAsync(TrainingAttachment entity, CancellationToken cancellationToken)
		{
			var existing = _attachments.FirstOrDefault(a => a.TrainingAttachmentId == entity.TrainingAttachmentId);
			if (existing != null)
			{
				_attachments.Remove(existing);
			}
			return Task.FromResult(true);
		}

		public Task<TrainingAttachment> SaveOrUpdateAsync(TrainingAttachment entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			if (entity.TrainingAttachmentId == 0)
			{
				entity.TrainingAttachmentId = _nextId++;
				_attachments.Add(entity);
			}
			else
			{
				var existing = _attachments.FirstOrDefault(a => a.TrainingAttachmentId == entity.TrainingAttachmentId);
				if (existing != null)
				{
					_attachments.Remove(existing);
				}
				_attachments.Add(entity);
			}
			return Task.FromResult(entity);
		}

		public Task<bool> DeleteMultipleAsync(TrainingAttachment entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken)
			=> Task.FromResult(true);

		public Task<IEnumerable<TrainingAttachment>> GetTrainingAttachmentsByTrainingIdAsync(int trainingId)
		{
			var result = _attachments.Where(a => a.TrainingId == trainingId).ToList();
			return Task.FromResult<IEnumerable<TrainingAttachment>>(result);
		}

		public Task<List<TrainingAttachment>> GetAttachmentMetadataByTrainingIdAsync(int trainingId)
			=> Task.FromResult(_attachments.Where(a => a.TrainingId == trainingId)
				.Select(a => new TrainingAttachment { TrainingAttachmentId = a.TrainingAttachmentId, TrainingId = a.TrainingId, FileName = a.FileName, FileType = a.FileType, FileSize = a.Data?.LongLength }).ToList());

		public Task<TrainingAttachment> GetAttachmentMetadataByIdAsync(int trainingAttachmentId)
			=> Task.FromResult(_attachments.Where(a => a.TrainingAttachmentId == trainingAttachmentId)
				.Select(a => new TrainingAttachment { TrainingAttachmentId = a.TrainingAttachmentId, TrainingId = a.TrainingId, FileName = a.FileName, FileType = a.FileType, FileSize = a.Data?.LongLength }).FirstOrDefault());

		public Task<TrainingAttachment> GetAttachmentWithDataAsync(int trainingAttachmentId)
			=> Task.FromResult(_attachments.FirstOrDefault(a => a.TrainingAttachmentId == trainingAttachmentId));

		public Task<int> CopyAttachmentsAsync(int fromTrainingId, int toTrainingId, CancellationToken cancellationToken = default)
		{
			var copies = _attachments.Where(a => a.TrainingId == fromTrainingId).ToList();
			foreach (var a in copies)
				SeedAttachment(new TrainingAttachment { TrainingId = toTrainingId, FileName = a.FileName, FileType = a.FileType, Data = a.Data });
			return Task.FromResult(copies.Count);
		}

		public void SeedAttachment(TrainingAttachment attachment)
		{
			if (attachment.TrainingAttachmentId == 0)
			{
				attachment.TrainingAttachmentId = _nextId++;
			}
			else
			{
				_nextId = System.Math.Max(_nextId, attachment.TrainingAttachmentId + 1);
			}
			_attachments.Add(attachment);
		}
	}
}