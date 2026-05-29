using ProtoBuf;
using Resgrid.Model.Identity;

namespace Resgrid.Model.Helpers
{
	public static class SerializerHelper
	{
		public static void WarmUpProtobufSerializer()
		{
			Serializer.PrepareSerializer<Department>();
			Serializer.PrepareSerializer<Address>();
			Serializer.PrepareSerializer<DepartmentMember>();
			Serializer.PrepareSerializer<Payment>();
			Serializer.PrepareSerializer<PaymentAddon>();
			Serializer.PrepareSerializer<Plan>();
			Serializer.PrepareSerializer<PlanAddon>();
			Serializer.PrepareSerializer<PlanLimit>();
			Serializer.PrepareSerializer<IdentityUser>();
			Serializer.PrepareSerializer<UserProfile>();
		}
	}
}
