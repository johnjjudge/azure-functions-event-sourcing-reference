using Workflow.Application.EventSourcing;

namespace Workflow.Tests;

/// <summary>
/// Unit tests for deterministic event id generation.
/// </summary>
[TestClass]
public sealed class EventIdFactoryTests
{
    [TestMethod]
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

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void CreateDeterministic_Changes_When_Discriminator_Changes()
    {
        var factory = new DeterministicEventIdFactory();

        var a = factory.CreateDeterministic("p|r", "t", "c", "x", discriminator: "1");
        var b = factory.CreateDeterministic("p|r", "t", "c", "x", discriminator: "2");

        Assert.AreNotEqual(a, b);
    }
}
