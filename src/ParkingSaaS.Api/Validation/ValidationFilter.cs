using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;
using ParkingSaaS.Application.Common.Exceptions;

namespace ParkingSaaS.Api.Validation;

/// <summary>
/// Runs any registered FluentValidation validator against incoming action
/// arguments before the action executes, so controllers stay free of validation
/// plumbing. Failures surface as a <see cref="ValidationAppException"/>.
/// </summary>
public sealed class ValidationFilter : IAsyncActionFilter
{
    private readonly IServiceProvider _services;

    public ValidationFilter(IServiceProvider services) => _services = services;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (_services.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            if (!result.IsValid)
            {
                var errors = result.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                throw new ValidationAppException(errors);
            }
        }

        await next();
    }
}
