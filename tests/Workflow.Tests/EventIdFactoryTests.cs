using Workflow.Application.EventSourcing;

namespace Workflow.Tests;

/// <summary>
/// Unit tests for deterministic event id generation.
/// </summary>
public sealed class EventIdFactoryTests
{
    [Fact]
    public void CreateDeterministic_Is_Stable_For_Same_Inputs()
    {
        var factory = new DeterministicEventIdFactory();

        var a = factory.CreateDeterministic(
            aggregateId: "p|r",
            eventType: "workflow.request.discovered.v1",
            correlationId: "corr",
            causationId: "cause");

        var b = factory.CreateDeterministic(
            aggregateId: "p|r",
            eventType: "workflow.request.discovered.v1",
            correlationId: "corr",
            causationId: "cause");

        Assert.Equal(a, b);
    }

    [Fact]
    public void CreateDeterministic_Changes_When_Discriminator_Changes()
    {
        var factory = new DeterministicEventIdFactory();

        var a = factory.CreateDeterministic("p|r", "t", "c", "x", discriminator: "1");
        var b = factory.CreateDeterministic("p|r", "t", "c", "x", discriminator: "2");

        Assert.NotEqual(a, b);
    }
}
