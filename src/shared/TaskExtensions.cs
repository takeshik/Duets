internal static class TaskExtensions
{
    extension(Task task)
    {
        public void Forget(Action<Exception>? errorHandler = null)
        {
            if (!task.IsCompleted || task.IsFaulted)
            {
                _ = ForgetAwaited(task, errorHandler);
            }

            return;

            static async Task ForgetAwaited(Task task, Action<Exception>? errorHandler)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    errorHandler?.Invoke(ex);
                }
            }
        }
    }

    extension(ValueTask task)
    {
        public void Forget(Action<Exception>? errorHandler = null)
        {
            if (!task.IsCompleted || task.IsFaulted)
            {
                _ = ForgetAwaited(task, errorHandler);
            }

            return;

            static async Task ForgetAwaited(ValueTask task, Action<Exception>? errorHandler)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    errorHandler?.Invoke(ex);
                }
            }
        }
    }
}
