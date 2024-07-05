using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mars.Clouds.Extensions
{
    // fixed thread count alternative to Parallel.For(), currently unused
    internal class ParallelTasks : IDisposable
    {
        private bool isDisposed;
        private readonly List<Exception> taskExceptions;
        private readonly Task[] tasks;
        private readonly CancellationTokenSource cancellationTokenSource;

        public ParallelTasks(int taskCount, Action taskBody, CancellationTokenSource cancellationTokenSource)
        {
            this.cancellationTokenSource = cancellationTokenSource;
            this.taskExceptions = [];
            this.tasks = new Task[taskCount];
            for (int taskIndex = 0; taskIndex < this.tasks.Length; ++taskIndex)
            {
                this.tasks[taskIndex] = Task.Run(taskBody, this.cancellationTokenSource.Token);
            }
        }

        public int Count
        {
            get { return this.tasks.Length; }
        }

        public void Dispose()
        {
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.isDisposed)
            {
                if (disposing)
                {
                    for (int taskIndex = 0; taskIndex < this.tasks.Length; ++taskIndex)
                    {
                        Task task = this.tasks[taskIndex];
                        if (task.IsCompleted)
                        {
                            // incomplete tasks can't be disposed
                            // In normal execution all tasks will have completed. If WaitAll() throws on a fault then it's likely some
                            // tasks may still be running depending on the timing of using scope exit, disposal, and cancellation token
                            // handling.
                            task.Dispose();
                        }
                    }
                }

                this.isDisposed = true;
            }
        }

        public bool WaitAll(TimeSpan timeout)
        {
            if (Task.WaitAll(tasks, (int)timeout.TotalMilliseconds, this.cancellationTokenSource.Token)) // unlike some other overloads, does not rethrow if a task faults
            {
                return true;
            }

            int tasksFaulted = 0;
            for (int taskIndex = 0; taskIndex < this.tasks.Length; ++taskIndex)
            {
                Task task = this.tasks[taskIndex];
                if (task.IsFaulted)
                {
                    ++tasksFaulted;
                    if (task.Exception != null)
                    {
                        this.taskExceptions.Add(task.Exception);
                    }
                }
            }

            if (tasksFaulted > 0)
            {
                if (this.cancellationTokenSource.IsCancellationRequested == false)
                {
                    this.cancellationTokenSource.Cancel(); // cancel any tasks stil running
                }

                string message = tasksFaulted + " of " + this.tasks.Length + " parallel tasks faulted.";
                throw new AggregateException(message, this.taskExceptions);
            }

            return false;
        }
    }
}
