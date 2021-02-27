﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;

namespace Smartstore.Scheduling
{
    public delegate Task ProgressCallback(int value, int maximum, string message);

    /// <summary>
    /// Provides the context for the Execute method of the <see cref="ITask"/> interface.
    /// </summary>
    public class TaskExecutionContext
    {
        // TODO: (core) Implement TaskExecutionContext class
        private readonly IComponentContext _componentContext;
        private readonly ITaskExecutionInfo _originalExecutionInfo;

        public TaskExecutionContext(
            ITaskStore taskStore, 
            IComponentContext componentContext, 
            ITaskExecutionInfo originalExecutionInfo,
            IDictionary<string, string> taskParameters = null)
        {
            Guard.NotNull(taskStore, nameof(taskStore));
            Guard.NotNull(componentContext, nameof(componentContext));
            Guard.NotNull(originalExecutionInfo, nameof(originalExecutionInfo));

            _componentContext = componentContext;
            _originalExecutionInfo = originalExecutionInfo;

            if (taskParameters != null)
            {
                Parameters.Merge(taskParameters);
            }

            TaskStore = taskStore;
            ExecutionInfo = _originalExecutionInfo.Clone();
        }

        public IDictionary<string, string> Parameters { get; } = new Dictionary<string, string>();

        /// <summary>
        /// The task store.
        /// </summary>
        public ITaskStore TaskStore { get; }

        /// <summary>
        /// The cloned execution info.
        /// </summary>
        public ITaskExecutionInfo ExecutionInfo { get; }

        public T Resolve<T>(object key = null) where T : class
        {
            return key == null 
                ? _componentContext.Resolve<T>()
                : _componentContext.ResolveKeyed<T>(key);
        }

        public T ResolveNamed<T>(string name) where T : class
        {
            return _componentContext.ResolveNamed<T>(name);
        }

        /// <summary>
        /// Persists a task's progress information to the store
        /// </summary>
        /// <param name="value">Progress value (numerator)</param>
        /// <param name="maximum">Progress maximum (denominator)</param>
        /// <param name="message">Progress message. Can be <c>null</c>.</param>
        /// <param name="immediately">if <c>true</c>, saves the updated task immediately, or lazily with the next commit otherwise.</param>
        public Task SetProgressAsync(int value, int maximum, string message, bool immediately = false)
        {
            if (value == 0 && maximum == 0)
            {
                return SetProgressAsync(null, message, immediately);
            }
            else
            {
                float fraction = (float)value / (float)Math.Max(maximum, 1f);
                int percentage = (int)Math.Round(fraction * 100f, 0);

                return SetProgressAsync(Math.Min(Math.Max(percentage, 0), 100), message, immediately);
            }
        }

        /// <summary>
        /// Persists a task's progress information to the task store
        /// </summary>
        /// <param name="progress">Percentual progress. Can be <c>null</c> or a value between 0 and 100.</param>
        /// <param name="message">Progress message. Can be <c>null</c>.</param>
        /// <param name="immediately">if <c>true</c>, saves the updated task entity immediately, or lazily with the next commit otherwise.</param>
        public virtual async Task SetProgressAsync(int? progress, string message, bool immediately = false)
        {
            if (progress.HasValue)
            {
                Guard.InRange(progress.Value, 0, 100, nameof(progress));
            }

            // Update cloned task.
            ExecutionInfo.ProgressPercent = progress;
            ExecutionInfo.ProgressMessage = message;

            // Update original task.
            _originalExecutionInfo.ProgressPercent = progress;
            _originalExecutionInfo.ProgressMessage = message;

            if (immediately)
            {
                // Dont't let this abort the task on failure.
                try
                {
                    await TaskStore.UpdateExecutionInfoAsync(_originalExecutionInfo);
                }
                catch 
                { 
                }
            }
        }
    }
}
