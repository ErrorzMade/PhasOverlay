using System;
using System.Threading;
using System.Threading.Tasks;

namespace PhasOverlay
{
    public sealed class WeeklyRefreshCoordinator : IDisposable
    {
        internal static readonly TimeSpan OutdatedRetryInterval = TimeSpan.FromMinutes(5);
        internal static readonly TimeSpan CurrentProbeInterval = TimeSpan.FromDays(1);

        private readonly Func<bool, CancellationToken, Task<WeeklyUpdateResult>> _check;
        private readonly Func<WeeklyEntry?> _readCache;
        private readonly Func<DateTime> _utcNow;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly CancellationTokenSource _shutdown = new();
        private Task? _loop;

        public event Action<WeeklyUpdateResult>? StateChanged;

        public WeeklyRefreshCoordinator()
            : this(
                (revalidate, token) => WeeklyDataService.CheckForUpdatesAsync(revalidate, token),
                WeeklyDataService.GetWeekly,
                () => DateTime.UtcNow,
                Task.Delay)
        {
        }

        internal WeeklyRefreshCoordinator(
            Func<bool, CancellationToken, Task<WeeklyUpdateResult>> check,
            Func<WeeklyEntry?> readCache,
            Func<DateTime> utcNow,
            Func<TimeSpan, CancellationToken, Task> delay)
        {
            _check = check;
            _readCache = readCache;
            _utcNow = utcNow;
            _delay = delay;
        }

        internal Task Completion => _loop ?? Task.CompletedTask;

        public void Start()
        {
            _loop ??= RunAsync(_shutdown.Token);
        }

        internal static bool NeedsFrequentRefresh(WeeklyEntry? weekly, DateTime utcNow)
        {
            if (weekly == null) return true;
            return WeeklyDataService.WeekStartUtc(weekly.Date) < WeeklyDataService.WeekStartUtc(utcNow);
        }

        internal static TimeSpan GetNextDelay(WeeklyEntry? weekly, DateTime utcNow)
        {
            if (NeedsFrequentRefresh(weekly, utcNow)) return OutdatedRetryInterval;

            DateTime nextReset = WeeklyDataService.WeekStartUtc(utcNow).AddDays(7);
            TimeSpan untilReset = nextReset - utcNow;
            if (untilReset <= TimeSpan.Zero) return TimeSpan.Zero;
            return untilReset < CurrentProbeInterval ? untilReset : CurrentProbeInterval;
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    WeeklyUpdateResult result = await _check(true, cancellationToken).ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested) return;

                    if (result is WeeklyUpdateResult.Updated or WeeklyUpdateResult.UnsupportedSchema)
                        StateChanged?.Invoke(result);

                    if (result == WeeklyUpdateResult.UnsupportedSchema) return;

                    TimeSpan delay = GetNextDelay(_readCache(), _utcNow());
                    await _delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        public void Dispose()
        {
            _shutdown.Cancel();
            _shutdown.Dispose();
        }
    }
}
