using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace Resgrid.Model
{
	public interface INoSqlDocument
	{
		[BsonId]
		[BsonRepresentation(BsonType.String)]
		ObjectId Id { get; set; }

		DateTime CreatedAt { get; }
	}
}
