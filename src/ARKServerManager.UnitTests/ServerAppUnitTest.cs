using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServerManagerTool.Lib;
using System;
using System.Collections.Generic;
using System.Threading;

namespace ServerManagerTool.UnitTests
{
    /// <summary>
    /// The locks that keep the scheduled tasks (backup, update, shutdown) from working on a server at the same time.
    /// </summary>
    [TestClass]
    public class ServerAppUnitTest
    {
        [TestMethod]
        public void WaitForLock_HolderEndedWithoutReleasing_TakesTheLockOver()
        {
            var name = $"JVHArkAscendedServerManager.UnitTest.{Guid.NewGuid():N}";
            Mutex held = null;
            // a task process ended from Task Scheduler while holding the lock: its thread is gone, the lock never released
            var holder = new Thread(() => held = new Mutex(true, name));
            holder.Start();
            holder.Join();

            try
            {
                using var mutex = new Mutex(false, name);
                var messages = new List<string>();

                var locked = ServerApp.WaitForLock(mutex, TimeSpan.FromSeconds(5), messages.Add);

                Assert.IsTrue(locked);
                Assert.HasCount(1, messages);
                mutex.ReleaseMutex();
            }
            finally
            {
                held?.Dispose();
            }
        }

        [TestMethod]
        public void WaitForLock_HeldByARunningProcess_TimesOut()
        {
            var name = $"JVHArkAscendedServerManager.UnitTest.{Guid.NewGuid():N}";
            using var holding = new ManualResetEventSlim();
            using var done = new ManualResetEventSlim();
            var holder = new Thread(() =>
            {
                using var mutex = new Mutex(true, name);
                holding.Set();
                done.Wait();
                mutex.ReleaseMutex();
            });
            holder.Start();
            holding.Wait();

            try
            {
                using var mutex = new Mutex(false, name);
                var messages = new List<string>();

                var locked = ServerApp.WaitForLock(mutex, TimeSpan.FromMilliseconds(200), messages.Add);

                Assert.IsFalse(locked);
                Assert.IsEmpty(messages);
            }
            finally
            {
                done.Set();
                holder.Join();
            }
        }
    }
}
