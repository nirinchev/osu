﻿// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu/master/LICENCE

using System.Threading;
using osu.Framework.Platform;

namespace osu.Game.Database
{
    public class DatabaseContextFactory : IDatabaseContextFactory
    {
        private readonly GameHost host;

        private const string database_name = @"client";

        private ThreadLocal<OsuDbContext> threadContexts;

        private readonly object writeLock = new object();

        private bool currentWriteDidWrite;
        private volatile int currentWriteUsages;

        public DatabaseContextFactory(GameHost host)
        {
            this.host = host;
            recycleThreadContexts();
        }

        /// <summary>
        /// Get a context for read-only usage.
        /// </summary>
        public OsuDbContext Get() => threadContexts.Value;

        /// <summary>
        /// Request a context for write usage. Can be consumed in a nested fashion (and will return the same underlying context).
        /// This method may block if a write is already active on a different thread.
        /// </summary>
        /// <returns>A usage containing a usable context.</returns>
        public DatabaseWriteUsage GetForWrite()
        {
            Monitor.Enter(writeLock);

            Interlocked.Increment(ref currentWriteUsages);

            return new DatabaseWriteUsage(threadContexts.Value, usageCompleted);
        }

        private void usageCompleted(DatabaseWriteUsage usage)
        {
            int usages = Interlocked.Decrement(ref currentWriteUsages);

            try
            {
                currentWriteDidWrite |= usage.PerformedWrite;

                if (usages > 0) return;

                if (currentWriteDidWrite)
                {
                    currentWriteDidWrite = false;
                    // once all writes are complete, we want to refresh thread-specific contexts to make sure they don't have stale local caches.
                    recycleThreadContexts();
                }
            }
            finally
            {
                Monitor.Exit(writeLock);
            }
        }

        private void recycleThreadContexts()
        {
            if (threadContexts != null)
                foreach (var context in threadContexts.Values)
                    context.Dispose();

            threadContexts = new ThreadLocal<OsuDbContext>(CreateContext, true);
        }

        protected virtual OsuDbContext CreateContext()
        {
            var ctx = new OsuDbContext(host.Storage.GetDatabaseConnectionString(database_name));
            ctx.Database.AutoTransactionsEnabled = false;

            return ctx;
        }

        public void ResetDatabase()
        {
            lock (writeLock)
            {
                recycleThreadContexts();
                host.Storage.DeleteDatabase(database_name);
            }
        }
    }
}
