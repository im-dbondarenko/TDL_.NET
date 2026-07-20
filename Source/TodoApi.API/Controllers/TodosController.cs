using MediatR;
using Microsoft.AspNetCore.Mvc;
using TodoApi.Application.Commands;
using TodoApi.Application.Queries;

namespace TodoApi.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class TodosController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var todos = await mediator.Send(new GetTodosQuery(), ct);
        return Ok(todos);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var todo = await mediator.Send(new GetTodoByIdQuery(id), ct);
        return todo is not null ? Ok(todo) : NotFound();
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTodoCommand command, CancellationToken ct)
    {
        var todo = await mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = todo.Id }, todo);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTodoCommand command, CancellationToken ct)
    {
        if (id != command.Id)
            return BadRequest("Route id must match body id");

        var todo = await mediator.Send(command, ct);
        return todo is not null ? Ok(todo) : NotFound();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deleted = await mediator.Send(new DeleteTodoCommand(id), ct);
        return deleted ? NoContent() : NotFound();
    }
}
