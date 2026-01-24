using Discord;
using Discord.WebSocket;
using System.Threading;
using UABackoneBot.Models;

namespace UABackoneBot.Services
{
    public class JobSyncService
    {
        private readonly ulong _channelId;
        private readonly DiscordSocketClient _client;
        private readonly CsvConverterService _converter;
        private readonly CsvDownloaderService _downloader;

        private static readonly List<TimeSpan> _runTimes = new()
        {
            new TimeSpan(9, 0, 0),
            new TimeSpan(12, 0, 0),
            new TimeSpan(18, 30, 0),
        };

        private static readonly TimeZoneInfo _targetTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        private List<JobInfo> _previousJobs = new();
        private List<JobInfo> _currentJobs = new();
        private bool _hasRunOnce = false;

        private CancellationTokenSource? _cts;
        private Task? _loopTask;
        private readonly object _gate = new();

        public event Func<string, Task>? LogCurrentStatus;

        public JobSyncService(
            DiscordSocketClient client,
            CsvDownloaderService downloader,
            CsvConverterService converter,
            ulong channelId)
        {
            _client = client;
            _downloader = downloader;
            _converter = converter;
            _channelId = channelId;
        }

        public void Start()
        {
            lock (_gate)
            {
                if (_loopTask != null && !_loopTask.IsCompleted)
                    return; // already running

                _cts = new CancellationTokenSource();
                _loopTask = RunAsync(_cts.Token);
            }
        }

        public async Task StopAsync()
        {
            Task? taskToWait;

            lock (_gate)
            {
                if (_cts == null || _loopTask == null)
                    return;

                _cts.Cancel();
                taskToWait = _loopTask;
            }

            try
            {
                await taskToWait;
            }
            catch (OperationCanceledException)
            {
                // expected on stop
            }
            finally
            {
                lock (_gate)
                {
                    _cts?.Dispose();
                    _cts = null;
                    _loopTask = null;
                    _hasRunOnce = false;
                }
            }
        }

        private async Task RunAsync(CancellationToken token)
        {
            await UpdateStatus("Starting JobSyncService");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await WaitUntilNextRunTime(token);

                    await UpdateStatus("Starting CSV downloader...");
                    var filePath = await _downloader.RunCsvDownloader();

                    await UpdateStatus("Download complete, beginning conversion...");
                    _currentJobs = _converter.GetJobs(filePath);

                    var newJobs = _currentJobs
                        .Where(c => !_previousJobs.Any(p => p.JobKey == c.JobKey))
                        .ToList();

                    if (newJobs.Count == 0)
                    {
                        await UpdateStatus("No new jobs found. Skipping post.");
                    }
                    else
                    {
                        // FIX: post only the NEW jobs (not the entire current list)
                        await PostJobsAsync(newJobs, token);
                        await UpdateStatus($"Posted {newJobs.Count} new jobs");
                    }

                    _previousJobs = _currentJobs;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await UpdateStatus($"{ex}");
                }

                // Optional: keep during testing; make cancellable so it doesn't hang stop.
                await Task.Delay(10_000, token);
            }

            await UpdateStatus("JobSyncService stopped.");
        }

        private async Task PostJobsAsync(List<JobInfo> jobsToPost, CancellationToken token)
        {
            if (jobsToPost == null || jobsToPost.Count == 0)
                return;

            // If gateway is down, skip posting this run.
            if (_client.ConnectionState != ConnectionState.Connected)
            {
                await UpdateStatus("Client not connected; skipping post.");
                return;
            }

            var channel = _client.GetChannel(_channelId) as IMessageChannel;
            if (channel is null)
            {
                await UpdateStatus($"Channel {_channelId} not found; skipping post.");
                return;
            }

            const int maxFieldsPerEmbed = 25;
            int index = 0;

            while (index < jobsToPost.Count)
            {
                token.ThrowIfCancellationRequested();

                var batch = jobsToPost
                    .Skip(index)
                    .Take(maxFieldsPerEmbed)
                    .ToList();

                index += batch.Count;

                var embed = new EmbedBuilder()
                    .WithTitle("New Jobs Posted")
                    .WithDescription($"Found {jobsToPost.Count} new job(s).")
                    .WithCurrentTimestamp();

                foreach (var job in batch)
                {
                    var name = $"{job.Trade} — {job.Location}";
                    var value =
                        $"Needed: {job.AmountNeeded ?? 0}\n" +
                        $"Wages: {job.Wages}\n" +
                        $"Nat. Pension: {job.NationalPension}\n" +
                        $"Local Pension: {job.LocalPension}\n" +
                        $"Health/Welfare: {job.HealthAndWelfare}\n" +
                        $"Hours/OT: {job.Hours}\n" +
                        $"Dates: {job.StartDate} → {job.EndDate}";

                    embed.AddField(name, value, inline: true);
                }

                await channel.SendMessageAsync(embed: embed.Build());
            }

            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _targetTimeZone);

            await channel.SendMessageAsync(
                $"**Date Posted: {nowLocal:MM/dd/yyyy}**\n" +
                $"**Update times are 9:00 AM, 12:00 PM, 6:30 PM (ET)**");
        }

        private async Task WaitUntilNextRunTime(CancellationToken token)
        {
            var nowUtc = DateTime.UtcNow;
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _targetTimeZone);

            if (!_hasRunOnce)
            {
                _hasRunOnce = true;
                await UpdateStatus("First run: starting immediately.");
                return;
            }

            var todayLocal = nowLocal.Date;

            var todayRunTimesLocal = _runTimes
                .Select(t => todayLocal + t)
                .Where(t => t > nowLocal)
                .OrderBy(t => t)
                .ToList();

            DateTime nextRunLocal = todayRunTimesLocal.Any()
                ? todayRunTimesLocal.First()
                : todayLocal.AddDays(1) + _runTimes[0];

            var nextRunUtc = TimeZoneInfo.ConvertTimeToUtc(nextRunLocal, _targetTimeZone);

            var delay = nextRunUtc - nowUtc;
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

            await UpdateStatus($"Next run at {nextRunLocal} local (UTC {nextRunUtc}) (in {delay}).");
            await Task.Delay(delay, token);
        }

        public async Task UpdateStatus(string msg)
        {
            if (LogCurrentStatus != null)
                await LogCurrentStatus("[JobSyncService] " + msg);
        }
    }
}
