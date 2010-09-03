﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Retlang.Core;

namespace Retlang.Fibers
{
    ///<summary>
    /// Allows interaction with Windows Forms.  Transparently moves actions onto the Form's thread.
    ///</summary>
    public class BaseFiber : IFiber
    {
        private readonly Subscriptions _subscriptions = new Subscriptions();
        private readonly object _lock = new object();
        private readonly IThreadAdapter _invoker;
        private readonly ActionTimer _timer;
        private readonly IBatchAndSingleExecutor _executor;
        private readonly List<Action> _queue = new List<Action>();

        private volatile ExecutionState _started = ExecutionState.Created;

        /// <summary>
        /// Creates an instance.
        /// </summary>
        public BaseFiber(IThreadAdapter invoker, IBatchAndSingleExecutor executor)
        {
            _timer = new ActionTimer(this);
            _invoker = invoker;
            _executor = executor;
        }

        /// <summary>
        /// <see cref="IDisposingExecutor.EnqueueAll(List{Action})"/>
        /// </summary>
        public void EnqueueAll(List<Action> actions)
        {
            if (_started == ExecutionState.Stopped)
            {
                return;
            }

            if (_started == ExecutionState.Created)
            {
                lock (_lock)
                {
                    if (_started == ExecutionState.Created)
                    {
                        _queue.AddRange(actions);
                        return;
                    }
                }
            }
            _invoker.Invoke(() => _executor.ExecuteAll(actions));
        }

        /// <summary>
        /// <see cref="IDisposingExecutor.Enqueue(Action)"/>
        /// </summary>
        public void Enqueue(Action action)
        {
            if (_started == ExecutionState.Stopped)
            {
                return;
            }

            if (_started == ExecutionState.Created)
            {
                lock (_lock)
                {
                    if (_started == ExecutionState.Created)
                    {
                        _queue.Add(action);
                        return;
                    }
                }
            }

            _invoker.Invoke(() => _executor.Execute(action));
        }


        ///<summary>
        /// Register unsubscriber to be called when IFiber is disposed
        ///</summary>
        ///<param name="toAdd"></param>
        public void Register(IUnsubscriber toAdd)
        {
            _subscriptions.Add(toAdd);
        }

        ///<summary>
        /// Deregister a subscription
        ///</summary>
        ///<param name="toRemove"></param>
        ///<returns></returns>
        public bool Deregister(IUnsubscriber toRemove)
        {
            return _subscriptions.Remove(toRemove);
        }

        ///<summary>
        /// Number of subscriptions
        ///</summary>
        public int NumSubscriptions
        {
            get { return _subscriptions.Count; }
        }

        /// <summary>
        /// <see cref="IScheduler.Schedule(Action,long)"/>
        /// </summary>
        public ITimerControl Schedule(Action action, long timeTilEnqueueInMs)
        {
            return _timer.Schedule(action, timeTilEnqueueInMs);
        }

        /// <summary>
        /// <see cref="IScheduler.ScheduleOnInterval(Action,long,long)"/>
        /// </summary>
        public ITimerControl ScheduleOnInterval(Action action, long firstInMs, long regularInMs)
        {
            return _timer.ScheduleOnInterval(action, firstInMs, regularInMs);
        }

        /// <summary>
        /// <see cref="IFiber.Start()"/>
        /// </summary>
        public void Start()
        {
            if (_started == ExecutionState.Running)
            {
                throw new ThreadStateException("Already Started");
            }

            lock (_lock)
            {
                var actions = _queue.ToList();
                _queue.Clear();
                if (actions.Count > 0)
                {
                    _invoker.Invoke(() => _executor.ExecuteAll(actions));
                }
                _started = ExecutionState.Running;
            }
        }

        /// <summary>
        /// <see cref="IDisposable.Dispose()"/>
        /// </summary>
        public void Dispose()
        {
            Stop();
        }

        /// <summary>
        /// Stops the fiber.
        /// </summary>
        public void Stop()
        {
            _timer.Dispose();
            _started = ExecutionState.Stopped;
            _subscriptions.Dispose();
        }
    }
}