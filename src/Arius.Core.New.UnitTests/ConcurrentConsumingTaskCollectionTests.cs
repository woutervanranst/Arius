namespace Arius.Core.New.UnitTests;
public class ConcurrentConsumingTaskCollectionTests
{
    [Fact]
    public async Task TasksAreProcessedInCompletionOrder()
    {
        // Arrange
        var taskQueue = new ConcurrentConsumingTaskCollection<string>();
        var t1 = SimulateTask("Task1", 3000);
        var t2 = SimulateTask("Task2", 1000);
        var t3 = SimulateTask("Task3", 2000);

        taskQueue.Add(t1);
        taskQueue.Add(t2);
        taskQueue.Add(t3);
        taskQueue.CompleteAdding();

        // Act
        var processedTasks = new List<string>();
        await foreach (var result in taskQueue.GetConsumingEnumerable())
        {
            processedTasks.Add(result);
        }

        // Assert
        Assert.Equal(new[] { "Task2", "Task3", "Task1" }, processedTasks);
    }

    [Fact]
    public async Task QueueStopsWhenAllTasksAreCompleted()
    {
        // Arrange
        var taskQueue = new ConcurrentConsumingTaskCollection<string>();
        var t1 = SimulateTask("Task1", 1000);

        taskQueue.Add(t1);
        taskQueue.CompleteAdding();

        // Act
        var processedTasks = new List<string>();
        await foreach (var result in taskQueue.GetConsumingEnumerable())
        {
            processedTasks.Add(result);
        }

        // Assert
        Assert.Single(processedTasks);
        Assert.Equal("Task1", processedTasks.First());
    }

    [Fact]
    public void CannotAddTasksAfterCompleteAdding()
    {
        // Arrange
        var taskQueue = new ConcurrentConsumingTaskCollection<string>();
        taskQueue.CompleteAdding();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => taskQueue.Add(SimulateTask("Task1", 1000)));
    }

    [Fact]
    public async Task QueueHandlesEmptyTaskSet()
    {
        // Arrange
        var taskQueue = new ConcurrentConsumingTaskCollection<string>();
        taskQueue.CompleteAdding();

        // Act
        var processedTasks = new List<string>();
        await foreach (var result in taskQueue.GetConsumingEnumerable())
        {
            processedTasks.Add(result);
        }

        // Assert
        Assert.Empty(processedTasks);
    }

    [Fact]
    public async Task ProcessingStopsWhenCancelled()
    {
        // Arrange
        var taskQueue = new ConcurrentConsumingTaskCollection<string>();
        var t1 = SimulateTask("Task1", 2000);
        var t2 = SimulateTask("Task2", 4000);

        taskQueue.Add(t1);
        taskQueue.Add(t2);
        taskQueue.CompleteAdding();

        var cts = new CancellationTokenSource();
        cts.CancelAfter(2500);  // Cancel after 2.5 seconds

        // Act
        var processedTasks = new List<string>();
        await foreach (var result in taskQueue.GetConsumingEnumerable(cts.Token))
        {
            processedTasks.Add(result);
        }

        // Assert
        Assert.Single(processedTasks);  // Only Task1 should complete before cancellation
        Assert.Equal("Task1", processedTasks.First());
    }

    [Fact]
    public async Task ConcurrentProducersAndConsumersProcessCorrectly()
    {
        // Arrange
        var taskQueue = new ConcurrentConsumingTaskCollection<string>();

        var producer = Task.Run(async () =>
        {
            taskQueue.Add(SimulateTask("Task1", 3000));
            await Task.Delay(500);
            taskQueue.Add(SimulateTask("Task2", 1000));
            await Task.Delay(500);
            taskQueue.Add(SimulateTask("Task3", 2000));
            taskQueue.CompleteAdding();
        });

        var processedTasks = new List<string>();
        var consumer1 = Task.Run(async () =>
        {
            await foreach (var result in taskQueue.GetConsumingEnumerable())
            {
                processedTasks.Add(result);
            }
        });

        var consumer2 = Task.Run(async () =>
        {
            await foreach (var result in taskQueue.GetConsumingEnumerable())
            {
                processedTasks.Add(result);
            }
        });

        // Act
        await producer;
        await Task.WhenAll(consumer1, consumer2);

        // Assert
        Assert.Equal(3, processedTasks.Count);
        Assert.Contains("Task1", processedTasks);
        Assert.Contains("Task2", processedTasks);
        Assert.Contains("Task3", processedTasks);
    }

    [Fact]
    public async Task TaskSetTemporarilyEmptyButMoreTasksAreAdded()
    {
        // Arrange
        var taskQueue = new ConcurrentConsumingTaskCollection<string>();

        var producer = Task.Run(async () =>
        {
            taskQueue.Add(SimulateTask("Task1", 1000));  // Task1 added first
            await Task.Delay(1500);                      // Simulate delay where the task set becomes temporarily empty
            taskQueue.Add(SimulateTask("Task2", 1000));  // Task2 added after some time
            taskQueue.CompleteAdding();                  // Complete adding tasks
        });

        var processedTasks = new List<string>();

        // Act
        await foreach (var result in taskQueue.GetConsumingEnumerable())
        {
            processedTasks.Add(result);
        }

        // Assert
        Assert.Equal(2, processedTasks.Count);
        Assert.Contains("Task1", processedTasks);
        Assert.Contains("Task2", processedTasks);
    }


    private async Task<string> SimulateTask(string name, int delay)
    {
        await Task.Delay(delay);
        return name;
    }
    
    
    //[Fact]
    //public void TestTaskQueueSingleProducerSingleConsumer()
    //{
    //    // Using Coyote's systematic testing engine
    //    var configuration = Configuration.Create();
    //    var engine = TestingEngine.Create(configuration, this.TestSingleProducerSingleConsumer);
    //    engine.Run();
    //    Assert.Equal(0, engine.TestReport.NumOfFoundBugs);
    //}

    //[Fact]
    //public void TestTaskQueueMultipleProducersMultipleConsumers()
    //{
    //    // Using Coyote's systematic testing engine
    //    var configuration = Configuration.Create();
    //    var engine = TestingEngine.Create(configuration, this.TestMultipleProducersMultipleConsumers);
    //    engine.Run();
    //    Assert.Equal(0, engine.TestReport.NumOfFoundBugs);
    //}

    //private void TestSingleProducerSingleConsumer(IActorRuntime runtime)
    //{
    //    var taskQueue = new ConcurrentConsumingTaskCollection<string>();
    //    var actualOrder = new List<string>();
    //    var expectedOrder = new List<string> { "Task2", "Task3", "Task1" };  // Expected order based on task delays

    //    // Producer: Add tasks to the queue
    //    Task.Run(async () =>
    //    {
    //        taskQueue.Add(SimulateTask("Task1", 3000));  // Long-running task
    //        taskQueue.Add(SimulateTask("Task2", 1000));  // Short-running task
    //        taskQueue.Add(SimulateTask("Task3", 2000));  // Medium-running task
    //        taskQueue.CompleteAdding();
    //    });

    //    // Consumer: Consume tasks in completion order and store the result
    //    Task.Run(async () =>
    //    {
    //        await foreach (var result in taskQueue.ConsumingEnumerable())
    //        {
    //            actualOrder.Add(result);
    //        }
    //    }).Wait();

    //    // Assert that the tasks were processed in the correct order
    //    Assert.Equal(expectedOrder, actualOrder);
    //}

    //private void TestMultipleProducersMultipleConsumers(IActorRuntime runtime)
    //{
    //    var taskQueue = new ConcurrentConsumingTaskCollection<string>();
    //    var actualOrder = new List<string>();
    //    var expectedOrder = new List<string> { "Producer1_Task2", "Producer2_Task2", "Producer2_Task1", "Producer1_Task1" };

    //    // Producer 1: Add tasks to the queue
    //    Task.Run(async () =>
    //    {
    //        taskQueue.Add(SimulateTask("Producer1_Task1", 3000));  // Long-running task
    //        taskQueue.Add(SimulateTask("Producer1_Task2", 1000));  // Short-running task
    //    });

    //    // Producer 2: Add tasks to the queue
    //    Task.Run(async () =>
    //    {
    //        taskQueue.Add(SimulateTask("Producer2_Task1", 2000));  // Medium-running task
    //        taskQueue.Add(SimulateTask("Producer2_Task2", 1500));  // Medium-short task
    //        taskQueue.CompleteAdding();
    //    });

    //    // Consumer 1: Consume tasks in completion order and store the result
    //    Task.Run(async () =>
    //    {
    //        await foreach (var result in taskQueue.ConsumingEnumerable())
    //        {
    //            actualOrder.Add(result);
    //        }
    //    });

    //    // Consumer 2: Consume tasks in completion order and store the result
    //    Task.Run(async () =>
    //    {
    //        await foreach (var result in taskQueue.ConsumingEnumerable())
    //        {
    //            actualOrder.Add(result);
    //        }
    //    }).Wait();

    //    // Assert that the tasks were processed in the correct order
    //    Assert.Equal(expectedOrder, actualOrder);
    //}

    //private async Task<string> SimulateTask(string name, int delay)
    //{
    //    await Task.Delay(delay);  // Simulate work
    //    return name;
    //}
}
