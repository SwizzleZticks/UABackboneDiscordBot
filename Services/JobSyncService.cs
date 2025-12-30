using Discord;
using Discord.WebSocket;
using System.Text;
using System.Threading.Tasks;
using UABackoneBot.Models;

namespace UABackoneBot.Services
{
    public class JobSyncService
    {
        private readonly ulong _channelId;
        private readonly DiscordSocketClient?  _client;
        private readonly CsvConverterService?  _converter;
        private readonly CsvDownloaderService? _downloader;
        private static readonly List<TimeSpan> _runTimes = new()
        {
            new TimeSpan(9, 0, 0),
            new TimeSpan(12, 0, 0),
            new TimeSpan(18, 30, 0),
        };
        private static readonly TimeZoneInfo _targetTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        private List<JobInfo> _previousJobs;
        private List<JobInfo> _currentJobs;
        private bool _hasRunOnce = false;

        public event Func<string, Task> LogCurrentStatus;
         
        public JobSyncService(DiscordSocketClient client, CsvDownloaderService downloader, CsvConverterService converter, ulong channelId)
        {
            _client     = client;
            _downloader = downloader;
            _converter  = converter;
            _channelId  = channelId;
            _previousJobs = new List<JobInfo>();
            _currentJobs  = new List<JobInfo>();
        }

        public void Start()
        {
            _ = Task.Run(async () =>
            {
            await UpdateStatus("Starting JobSyncService");
            while (true)
            {
                try
                {
                    await WaitUntilNextRunTime();
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
                            await PostJobsAsync(_currentJobs);
                            await UpdateStatus($"Posted {newJobs.Count} new jobs");
                    }

                            _previousJobs = _currentJobs;
                    }
                    catch (Exception ex)
                    {
                        await UpdateStatus($"{ex}");
                    }

                    await Task.Delay(10000); // temporary 10 sec delay so it doesn't spam
                }
            });
        }

        private async Task PostJobsAsync(List<JobInfo> newJobs)
        {
            var channel = _client?.GetChannel(_channelId) as IMessageChannel;
            if (channel is null || newJobs is null || newJobs.Count == 0)
                return;

            const int maxFieldsPerEmbed = 25;
            int index = 0;

            while (index < newJobs.Count)
            {
                var batch = newJobs
                    .Skip(index)
                    .Take(maxFieldsPerEmbed)
                    .ToList();
                index += batch.Count;

                var embed = new EmbedBuilder()
                    .WithTitle("New Jobs Posted")
                    .WithDescription($"Found {newJobs.Count} new job(s).")
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
            await channel.SendMessageAsync($"**Date Posted: {DateTime.Now.ToShortDateString()}**\n**Update times are 9:30 AM, 12 PM, 6:30 PM**");
        }
        private async Task WaitUntilNextRunTime()
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

            DateTime nextRunLocal;

            if (todayRunTimesLocal.Any())
            {
                nextRunLocal = todayRunTimesLocal.First();
            }
            else
            {
                // all runs for today have passed – schedule first run tomorrow
                nextRunLocal = todayLocal.AddDays(1) + _runTimes[0];
            }

            // convert back to UTC for delay calculation
            var nextRunUtc = TimeZoneInfo.ConvertTimeToUtc(nextRunLocal, _targetTimeZone);

            var delay = nextRunUtc - nowUtc;
            if (delay < TimeSpan.Zero)
                delay = TimeSpan.Zero;

            await UpdateStatus($"Next run at {nextRunLocal} local (UTC {nextRunUtc}) (in {delay}).");

            await Task.Delay(delay);
        }


        public async Task UpdateStatus(string msg)
        {
            if (LogCurrentStatus != null)
            {
                await LogCurrentStatus("[JobSyncService] " + msg);
            }
        }
    }
}
