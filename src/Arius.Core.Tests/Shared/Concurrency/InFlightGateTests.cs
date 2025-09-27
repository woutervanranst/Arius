using Arius.Core.Shared.Concurrency;
using Mediator;
using Shouldly;

namespace Arius.Core.Tests.Shared.Concurrency;

public class InFlightGateTests
{
    [Fact]
    public void Enter_FirstCall_ShouldReturnOwner()
    {
        var gate = new InFlightGate<string, int>();

        var (isOwner, waitTask) = gate.Enter("test");

        isOwner.ShouldBeTrue();
        waitTask.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public void Enter_SecondCall_ShouldReturnFollower()
    {
        var gate = new InFlightGate<string, int>();

        var (isOwner1, waitTask1) = gate.Enter("test");
        var (isOwner2, waitTask2) = gate.Enter("test");

        isOwner1.ShouldBeTrue();
        isOwner2.ShouldBeFalse();
        ReferenceEquals(waitTask1, waitTask2).ShouldBeTrue();
    }

    [Fact]
    public async Task Complete_ShouldResolveAllWaiters()
    {
        var gate = new InFlightGate<string, int>();

        var (isOwner, _) = gate.Enter("test");
        var (_, waitTask1) = gate.Enter("test");
        var (_, waitTask2) = gate.Enter("test");

        isOwner.ShouldBeTrue();

        gate.Complete("test", 42);

        (await waitTask1).ShouldBe(42);
        (await waitTask2).ShouldBe(42);
    }

    [Fact]
    public async Task Fault_ShouldPropagateExceptionToAllWaiters()
    {
        var gate = new InFlightGate<string, int>();
        var testException = new InvalidOperationException("Test error");

        var (isOwner, _) = gate.Enter("test");
        var (_, waitTask1) = gate.Enter("test");
        var (_, waitTask2) = gate.Enter("test");

        isOwner.ShouldBeTrue();

        gate.Fault("test", testException);

        var ex1 = await Should.ThrowAsync<InvalidOperationException>(async () => await waitTask1);
        var ex2 = await Should.ThrowAsync<InvalidOperationException>(async () => await waitTask2);

        ex1.Message.ShouldBe("Test error");
        ex2.Message.ShouldBe("Test error");
    }

    [Fact]
    public async Task Cancel_ShouldCancelAllWaiters()
    {
        var gate = new InFlightGate<string, int>();
        using var cts = new CancellationTokenSource();

        var (isOwner, _) = gate.Enter("test");
        var (_, waitTask1) = gate.Enter("test");
        var (_, waitTask2) = gate.Enter("test");

        isOwner.ShouldBeTrue();

        gate.Cancel("test", cts.Token);

        await Should.ThrowAsync<OperationCanceledException>(async () => await waitTask1);
        await Should.ThrowAsync<OperationCanceledException>(async () => await waitTask2);
    }

    [Fact]
    public async Task ConcurrentOperations_ShouldCoalesceCorrectly()
    {
        var gate = new InFlightGate<int, string>();
        var workCounter = 0;
        var results = new List<string>();

        async Task<string> DoWork(int key)
        {
            var (isOwner, waitTask) = gate.Enter(key);
            if (!isOwner)
            {
                return await waitTask;
            }

            try
            {
                // Simulate work
                Interlocked.Increment(ref workCounter);
                await Task.Delay(50);
                var result = $"result-{key}";
                gate.Complete(key, result);
                return result;
            }
            catch (Exception ex)
            {
                gate.Fault(key, ex);
                throw;
            }
        }

        // Start 10 concurrent operations for the same key
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => DoWork(42))
            .ToArray();

        var allResults = await Task.WhenAll(tasks);

        // All should get the same result
        allResults.ShouldAllBe(result => result == "result-42");

        // Work should only be performed once
        workCounter.ShouldBe(1);
    }

    [Fact]
    public async Task DifferentKeys_ShouldNotInterfere()
    {
        var gate = new InFlightGate<string, int>();

        var (isOwner1, waitTask1) = gate.Enter("key1");
        var (isOwner2, waitTask2) = gate.Enter("key2");

        isOwner1.ShouldBeTrue();
        isOwner2.ShouldBeTrue();

        gate.Complete("key1", 1);
        gate.Complete("key2", 2);

        (await waitTask1).ShouldBe(1);
        (await waitTask2).ShouldBe(2);
    }

    [Fact]
    public void Enter_AfterComplete_ShouldReturnNewOwner()
    {
        var gate = new InFlightGate<string, int>();

        var (isOwner1, _) = gate.Enter("test");
        gate.Complete("test", 42);

        var (isOwner2, waitTask2) = gate.Enter("test");

        isOwner1.ShouldBeTrue();
        isOwner2.ShouldBeTrue();
        waitTask2.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public void UnitValue_ShouldBeDefault()
    {
        var unit1 = Unit.Value;
        var unit2 = default(Unit);

        unit1.ShouldBe(unit2);
    }

    [Fact]
    public async Task InFlightGateWithUnit_ShouldWorkForVoidOperations()
    {
        var gate = new InFlightGate<string, Unit>();
        var workDone = false;

        async Task DoVoidWork(string key)
        {
            var (isOwner, waitTask) = gate.Enter(key);
            if (!isOwner)
            {
                await waitTask;
                return;
            }

            try
            {
                await Task.Delay(10);
                workDone = true;
                gate.Complete(key, Unit.Value);
            }
            catch (Exception ex)
            {
                gate.Fault(key, ex);
                throw;
            }
        }

        // Start multiple operations
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => DoVoidWork("test"))
            .ToArray();

        await Task.WhenAll(tasks);

        workDone.ShouldBeTrue();
    }
}