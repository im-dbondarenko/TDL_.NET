using FluentValidation;

namespace TodoApi.Application.Todos;

public static class TodoValidationRules
{
    public static IRuleBuilderOptions<T, string> ValidTitle<T>(this IRuleBuilder<T, string> rule)
    {
        return rule
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(200).WithMessage("Title must be 200 characters or fewer");
    }

    public static IRuleBuilderOptions<T, string?> ValidDescription<T>(this IRuleBuilder<T, string?> rule)
    {
        return rule
            .MaximumLength(1000).WithMessage("Description must be 1000 characters or fewer");
    }
}
