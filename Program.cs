using System.Collections.Concurrent;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using UserManagementAPI.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddValidatorsFromAssemblyContaining<UserRequestValidator>();
builder.Services.AddScoped<IValidator<UserRequest>, UserRequestValidator>();

var app = builder.Build();

// Order matters: catch exceptions first, then authenticate, then log the resulting request/response.
app.UseErrorHandling();
app.UseTokenAuthentication();
app.UseRequestResponseLogging();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var users = new ConcurrentDictionary<int, User>(
[
    new KeyValuePair<int, User>(1, new User(1, "Ada Lovelace", "ada@example.com")),
    new KeyValuePair<int, User>(2, new User(2, "Alan Turing", "alan@example.com"))
]);

var nextUserId = 2;

app.MapGet("/", () => "Hello, World!");

app.MapGet("/api/users", () => 

    Results.Ok(users.Values.OrderBy(user => user.Id))

).WithName("GetUsers");

app.MapGet("/api/users/{id:int}", (int id) =>
{

    return users.TryGetValue(id, out var user)
        ? Results.Ok(user)
        : Results.NotFound(new { message = $"User with ID {id} was not found." });

}).WithName("GetUserById");

app.MapPost("/api/users", async (UserRequest request, [FromServices] IValidator<UserRequest> validator) =>
{

    var validationResult = await validator.ValidateAsync(request);
    if (!validationResult.IsValid)
    {
        return Results.ValidationProblem(validationResult.ToDictionary());
    }

    var id = Interlocked.Increment(ref nextUserId);
    var user = new User(id, request.Name.Trim(), request.Email.Trim());
    users[id] = user;

    return Results.Created($"/api/users/{id}", user);

}).WithName("CreateUser");

app.MapPut("/api/users/{id:int}", async (int id, UserRequest request, [FromServices] IValidator<UserRequest> validator) =>
{

    var validationResult = await validator.ValidateAsync(request);
    if (!validationResult.IsValid)
    {
        return Results.ValidationProblem(validationResult.ToDictionary());
    }

    if (!users.TryGetValue(id, out var existingUser))
    {
        return Results.NotFound(new { message = $"User with ID {id} was not found." });
    }

    var updatedUser = new User(id, request.Name.Trim(), request.Email.Trim());

    return users.TryUpdate(id, updatedUser, existingUser)
        ? Results.Ok(updatedUser)
        : Results.NotFound(new { message = $"User with ID {id} was not found." });

}).WithName("UpdateUser");

app.MapDelete("/api/users/{id:int}", (int id) =>
{

    return users.TryRemove(id, out _)
        ? Results.NoContent()
        : Results.NotFound(new { message = $"User with ID {id} was not found." });
        
}).WithName("DeleteUser");

app.Run();

record User(int Id, string Name, string Email);
record UserRequest(string Name, string Email);

class UserRequestValidator : AbstractValidator<UserRequest>
{
    public UserRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(100).WithMessage("Name must be 100 characters or fewer.");

        RuleFor(request => request.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.")
            .MaximumLength(254).WithMessage("Email must be 254 characters or fewer.");
    }
}
