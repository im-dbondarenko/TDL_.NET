using NSubstitute;
using TodoApi.Application;
using TodoApi.Application.Commands;
using TodoApi.Domain;

namespace TodoApi.Tests;

public sealed class CreateTodoHandlerTests
{
    private readonly ITodoRepository _repository = Substitute.For<ITodoRepository>();
    private readonly CreateTodoHandler _handler;

    public CreateTodoHandlerTests()
    {
        _repository.CreateAsync(Arg.Any<TodoItem>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<TodoItem>());

        _handler = new CreateTodoHandler(_repository);
    }

    [Fact]
    public async Task Handle_SetsTitle()
    {
        var command = new CreateTodoCommand("Buy groceries", null);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.Equal("Buy groceries", result.Title);
    }

    [Fact]
    public async Task Handle_SetsDescription()
    {
        var command = new CreateTodoCommand("Buy groceries", "Milk, bread, eggs");

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.Equal("Milk, bread, eggs", result.Description);
    }

    [Fact]
    public async Task Handle_CallsRepository()
    {
        var command = new CreateTodoCommand("Test", null);

        await _handler.Handle(command, CancellationToken.None);

        await _repository.Received(1).CreateAsync(Arg.Any<TodoItem>(), Arg.Any<CancellationToken>());
    }
}
