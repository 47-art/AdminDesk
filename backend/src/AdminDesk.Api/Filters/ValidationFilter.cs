using AdminDesk.SharedKernel.Responses;
using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;
using ValidationException = AdminDesk.SharedKernel.Exceptions.ValidationException;

namespace AdminDesk.Api.Filters;

// Runs the registered validator for every action argument before the action executes.
public class ValidationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new List<FieldError>();

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            if (result.IsValid)
            {
                continue;
            }

            errors.AddRange(result.Errors.Select(f => new FieldError
            {
                Field = ToCamelCase(f.PropertyName),
                Message = f.ErrorMessage
            }));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        await next();
    }

    private static string ToCamelCase(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        var parts = path.Split('.');
        for (var i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            var bracket = p.IndexOf('[');
            var name = bracket >= 0 ? p[..bracket] : p;
            var rest = bracket >= 0 ? p[bracket..] : string.Empty;
            if (name.Length > 0)
            {
                name = char.ToLowerInvariant(name[0]) + name[1..];
            }
            parts[i] = name + rest;
        }
        return string.Join('.', parts);
    }
}
