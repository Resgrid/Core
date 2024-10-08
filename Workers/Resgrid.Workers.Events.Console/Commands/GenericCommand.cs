﻿using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Events.Console.Commands
{
	public class GenericCommand : IQuidjiboCommand
	{
		public int Id { get; }

		public GenericCommand(int id)
		{
			Id = id;
		}

		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
