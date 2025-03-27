using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace Resgrid.Model.Repositories
{
	public interface IMongoRepository<TDocument> where TDocument : INoSqlDocument
	{
		IMongoCollection<TDocument> GetCollection();

		IQueryable<TDocument> AsQueryable();

		IEnumerable<TDocument> FilterBy(
			Expression<Func<TDocument, bool>> filterExpression);

		IEnumerable<TProjected> FilterBy<TProjected>(
			Expression<Func<TDocument, bool>> filterExpression,
			Expression<Func<TDocument, TProjected>> projectionExpression);

		TDocument FindOne(Expression<Func<TDocument, bool>> filterExpression);

		Task<TDocument> FindOneAsync(Expression<Func<TDocument, bool>> filterExpression);

		TDocument FindById(string id);

		Task<TDocument> FindByIdAsync(string id);

		void InsertOne(TDocument document);

		Task InsertOneAsync(TDocument document);

		void InsertMany(ICollection<TDocument> documents);

		Task InsertManyAsync(ICollection<TDocument> documents);

		void ReplaceOne(TDocument document);

		Task ReplaceOneAsync(TDocument document);

		void DeleteOne(Expression<Func<TDocument, bool>> filterExpression);

		Task DeleteOneAsync(Expression<Func<TDocument, bool>> filterExpression);

		void DeleteById(string id);

		Task DeleteByIdAsync(string id);

		void DeleteMany(Expression<Func<TDocument, bool>> filterExpression);

		Task DeleteManyAsync(Expression<Func<TDocument, bool>> filterExpression);

		Task<IEnumerable<TDocument>> FilterByAsync(Expression<Func<TDocument, bool>> filterExpression);
	}
}
