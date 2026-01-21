
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace UserManagementAPI_Gangothri
{
    // =======================
    // Models
    // =======================

    public record User(int Id, string Name, string Email, DateTimeOffset CreatedAt);

    public sealed class CreateUserRequest
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
    }

    public sealed class UpdateUserRequest
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
    }

    // =======================
    // Validation Helper
    // =======================

    public static class ValidationHelper
    {
        public static Dictionary<string, string[]> Validate(string? name, string? email)
        {
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(name))
                errors["name"] = new[] { "Name is required." };
            else if (name.Trim().Length < 2)
                errors["name"] = new[] { "Name must be at least 2 characters long." };

            if (string.IsNullOrWhiteSpace(email))
                errors["email"] = new[] { "Email is required." };
            else if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                errors["email"] = new[] { "Invalid email format." };

            return errors;
        }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            app.UseSwagger();
            app.UseSwaggerUI();

            // -----------------------
            // Middleware
            // -----------------------

            app.Use(async (context, next) =>
            {
                try
                {
                    await next();
                }
                catch (Exception ex)
                {
                    app.Logger.LogError(ex, "Unhandled exception");

                    context.Response.StatusCode = 500;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(
                        JsonSerializer.Serialize(new { error = "Internal server error" }));
                }
            });

            app.Use(async (context, next) =>
            {
                await next();
                app.Logger.LogInformation(
                    "{Method} {Path} -> {StatusCode}",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode
                );
            });

            // -----------------------
            // In-memory store (SEEDED)
            // -----------------------

            var users = new ConcurrentDictionary<int, User>();

            users[1] = new User(1, "Gangothri S", "gangothri@demo.com", DateTimeOffset.UtcNow);
            users[2] = new User(2, "Sohan Kamat", "sohan.kamat@demo.com", DateTimeOffset.UtcNow);

            int idCounter = 2;

            // -----------------------
            // Endpoints
            // -----------------------

            app.MapGet("/users", () =>
                Results.Ok(users.Values)
            );

            app.MapGet("/users/{id:int}", (int id) =>
                users.TryGetValue(id, out var user)
                    ? Results.Ok(user)
                    : Results.NotFound(new { error = "User not found" })
            );

            // ✅ FIXED POST (Swagger editable)
            app.MapPost("/users", (CreateUserRequest request) =>
            {
                var errors = ValidationHelper.Validate(request.Name, request.Email);
                if (errors.Count > 0)
                    return Results.ValidationProblem(errors);

                var id = Interlocked.Increment(ref idCounter);
                var user = new User(id, request.Name!, request.Email!, DateTimeOffset.UtcNow);

                users[id] = user;
                return Results.Created($"/users/{id}", user);
            });

            // ✅ FIXED PUT (Swagger editable)
            app.MapPut("/users/{id:int}", (int id, UpdateUserRequest request) =>
            {
                if (!users.TryGetValue(id, out var existing))
                    return Results.NotFound(new { error = "User not found" });

                var errors = ValidationHelper.Validate(request.Name, request.Email);
                if (errors.Count > 0)
                    return Results.ValidationProblem(errors);

                users[id] = existing with
                {
                    Name = request.Name!,
                    Email = request.Email!
                };

                return Results.Ok(users[id]);
            });

            app.MapDelete("/users/{id:int}", (int id) =>
                users.TryRemove(id, out _)
                    ? Results.NoContent()
                    : Results.NotFound()
            );

            app.Run();
        }
    }
}
