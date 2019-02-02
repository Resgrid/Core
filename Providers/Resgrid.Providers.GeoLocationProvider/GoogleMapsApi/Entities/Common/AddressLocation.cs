using System;

namespace GoogleMapsApi.Entities.Common
{
	public class AddressLocation : ILocationString
	{
		public string Address { get; private set; }

		public AddressLocation(string address)
		{
			Address = address;
		}

		public string LocationString
		{
			get { return Address; }
		}
	}
}