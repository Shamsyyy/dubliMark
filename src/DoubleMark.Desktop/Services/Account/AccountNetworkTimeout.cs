namespace DoubleMark.Desktop.Services.Account;

public static class AccountNetworkTimeout
{
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(20);

    public static async Task<T> RunAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        TimeSpan? timeout = null)
    {
        var limit = timeout ?? Default;
        var task = operation();
        if (await Task.WhenAny(task, Task.Delay(limit)).ConfigureAwait(false) != task)
            throw new TimeoutException(BuildMessage(operationName));

        return await task.ConfigureAwait(false);
    }

    public static Task RunAsync(
        Func<Task> operation,
        string operationName,
        TimeSpan? timeout = null) =>
        RunAsync(async () =>
        {
            await operation().ConfigureAwait(false);
            return true;
        }, operationName, timeout);

    private static string BuildMessage(string operationName) =>
        $"Сервер DoubleMark не отвечает ({operationName}). Проверьте интернет и попробуйте снова.";
}
