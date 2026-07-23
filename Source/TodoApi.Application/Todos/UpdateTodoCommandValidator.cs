using FluentValidation;

namespace TodoApi.Application.Todos;

public sealed class UpdateTodoCommandValidator : AbstractValidator<UpdateTodoCommand>
{
    public UpdateTodoCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id is required");

        RuleFor(x => x.Title).ValidTitle();
        RuleFor(x => x.Description).ValidDescription();
    }
}
