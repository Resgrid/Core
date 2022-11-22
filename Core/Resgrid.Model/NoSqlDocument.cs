using MongoDB.Bson;
using System;

namespace Resgrid.Model
{
	public class NoSqlDocument: INoSqlDocument
	{
		public ObjectId Id { get; set; }

		public DateTime CreatedAt => Id.CreationTime;
	}
}
