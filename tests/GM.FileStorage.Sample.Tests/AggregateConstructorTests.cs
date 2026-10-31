using GM.EntityFramework.Domain.Abstractions;
using GM.FileStorage.Sample.Domain.BoundedContext.FileStorageBoundedContext.StoredFileAggregate;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace GM.FileStorage.Sample.Tests;

public class AggregateConstructorTests
{
    // Guard for the "constructor raises Uploaded" design: EF Core materializes via a constructor and
    // prefers a parameterless one. Every aggregate must declare a parameterless constructor so loads go
    // through it (side-effect-free) rather than the identity-assigning, event-raising parameterized one —
    // otherwise a phantom uploaded event is raised on every read.
    [Fact]
    public void Every_aggregate_declares_a_parameterless_constructor_for_ef_materialization()
    {
        var aggregates = typeof(StoredFile).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IAggregateRoot).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(aggregates);

        var missing = aggregates
            .Where(t => t.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null) is null)
            .Select(t => t.Name)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"Aggregates without a parameterless constructor (EF would materialize via a side-effecting " +
            $"constructor and raise phantom created events on load): {string.Join(", ", missing)}");
    }
}
