using System;
using System.Reflection;
using System.Threading;

namespace Screenzap.ViewportTests
{
    internal static class StaTest
    {
        internal static void Run(Action action)
        {
            Exception? captured = null;
            using var completed = new ManualResetEventSlim(false);

            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
                finally
                {
                    completed.Set();
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            completed.Wait();

            if (captured != null)
            {
                throw new TargetInvocationException(captured);
            }
        }
    }
}
