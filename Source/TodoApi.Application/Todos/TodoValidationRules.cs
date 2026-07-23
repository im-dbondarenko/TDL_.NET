using System.Text.RegularExpressions;
using FluentValidation;

namespace TodoApi.Application.Todos;

public static partial class TodoValidationRules
{
    [GeneratedRegex(@"^[a-zA-Z0-9\s\p{P}\p{S}]*$")]
    private static partial Regex LatinOnlyRegex();

    public static IRuleBuilderOptions<T, string> ValidTitle<T>(this IRuleBuilder<T, string> rule)
    {
        return rule
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(200).WithMessage("Title must be 200 characters or fewer")
            .Matches(LatinOnlyRegex()).WithMessage("Title must contain only Latin characters");
    }

    public static IRuleBuilderOptions<T, string?> ValidDescription<T>(this IRuleBuilder<T, string?> rule)
    {
        return rule
            .MaximumLength(1000).WithMessage("Description must be 1000 characters or fewer")
            .Matches(LatinOnlyRegex()).WithMessage("Description must contain only Latin characters");
    }
}
