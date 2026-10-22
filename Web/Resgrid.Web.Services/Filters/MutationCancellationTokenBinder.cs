using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Resgrid.Web.Services.Filters
{
	/// <summary>
	/// Binds the <see cref="CancellationToken"/> an action takes. A read gets the request-aborted token, as MVC's own binder
	/// gives it, so a client that went away stops work nobody will see. A mutating request (POST/PUT/PATCH/DELETE) gets a
	/// token that never fires: its writes go out in several steps with no transaction around them (a call's row, then its
	/// dispatches and unit statuses), and an app that drops its connection partway (a phone changing networks, a request
	/// timed out on the device) would otherwise leave the record half saved -- a call closed with its units never
	/// released, which no retry can release because the call is no longer active. Opt back in with
	/// <see cref="CancelOnClientDisconnectAttribute"/>. Code that reads HttpContext.RequestAborted itself is unaffected.
	/// </summary>
	public sealed class MutationCancellationTokenBinder : IModelBinder
	{
		public Task BindModelAsync(ModelBindingContext bindingContext)
		{
			var token = RunsToCompletion(bindingContext.ActionContext)
				? CancellationToken.None
				: bindingContext.HttpContext.RequestAborted;

			// Boxed once so the validation state and the result hold the same reference, as MVC's own binder does.
			var model = (object)token;
			bindingContext.ValidationState.Add(model, new ValidationStateEntry { SuppressValidation = true });
			bindingContext.Result = ModelBindingResult.Success(model);

			return Task.CompletedTask;
		}

		private static bool RunsToCompletion(ActionContext context)
		{
			var method = context.HttpContext.Request.Method;
			var isMutation = HttpMethods.IsPost(method) || HttpMethods.IsPut(method) ||
							 HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

			return isMutation && context.ActionDescriptor.EndpointMetadata?.OfType<CancelOnClientDisconnectAttribute>().Any() != true;
		}
	}

	/// <summary>
	/// Puts <see cref="MutationCancellationTokenBinder"/> ahead of MVC's own CancellationToken binder.
	/// </summary>
	public sealed class MutationCancellationTokenBinderProvider : IModelBinderProvider
	{
		private static readonly IModelBinder Binder = new MutationCancellationTokenBinder();

		public static void Register(MvcOptions options)
		{
			// First: the binder providers are asked in order, and MVC's CancellationTokenModelBinderProvider answers for
			// every CancellationToken parameter it is asked about.
			options.ModelBinderProviders.Insert(0, new MutationCancellationTokenBinderProvider());
		}

		public IModelBinder GetBinder(ModelBinderProviderContext context)
		{
			return context.Metadata.ModelType == typeof(CancellationToken) ? Binder : null;
		}
	}
}
