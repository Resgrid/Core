using System;

namespace Resgrid.Model
{
	public sealed class RecordIdempotencyException : InvalidOperationException
	{
		public RecordIdempotencyException(string message) : base(message) { }
	}
}
