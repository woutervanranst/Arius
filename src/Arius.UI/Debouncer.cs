namespace Arius.UI;

internal class Debouncer
{
    private CancellationTokenSource cts = new CancellationTokenSource();

    public async Task Debounce(Action action, int milliseconds = 500)
    {
        cts.Cancel();
        cts = new CancellationTokenSource();

        try
        {
            await Task.Delay(milliseconds, cts.Token);
            action();
        }
        catch (TaskCanceledException)
        {
            // swallow this exception, it's expected
        }
    }
}