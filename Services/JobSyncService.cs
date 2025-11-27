using Discord;
using Discord.WebSocket;
using System.Text;
using UABackoneBot.Models;

namespace UABackoneBot.Services
{
    public class JobSyncService
    {
        private readonly ulong _channelId;
        private readonly DiscordSocketClient?  _client;
        private readonly CsvConverterService?  _converter;
        private readonly CsvDownloaderService? _downloader;
        private readonly List<TimeSpan> _runTimes = new()
        {
            new TimeSpan(14, 15, 0),
        };
        private List<JobInfo> _previousJobs;
        private List<JobInfo> _currentJobs;
         
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
                while (true)
                {
                    try
                    {
                        await WaitUntilNextRunTime();

                        var filePath = await _downloader.RunCsvDownloader();
                        _currentJobs = _converter.GetJobs(filePath);

                        var newJobs = _currentJobs
                            .Where(c => !_previousJobs.Any(p => p.JobKey == c.JobKey))
                            .ToList();


                        if (newJobs.Count == 0)
                        {
                            Console.WriteLine("[JobSyncService] No new jobs found. Skipping post.");
                        }
                        else
                        {
                            await PostJobsAsync(newJobs);
                            Console.WriteLine($"[JobSyncService] Posted {newJobs.Count} new jobs");
                        }

                            _previousJobs = _currentJobs;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[JobSyncService ERROR] {ex}");
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
                await channel.SendMessageAsync($"**Date Posted: {DateTime.Now.ToShortDateString()}**\n**Update times are 9:30 AM, 12 PM, 6:30 PM**");
            }
        }
        private async Task WaitUntilNextRunTime()
        {
            var now = DateTime.Now;

            // Build list of today's run times
            var todayRunTimes = _runTimes
                .Select(t => DateTime.Today + t)
                .Where(t => t > now)
                .OrderBy(t => t)
                .ToList();

            DateTime nextRunTime;

            if (todayRunTimes.Any())
            {
                nextRunTime = todayRunTimes.First();
            }
            else
            {
                // all today's times have passed → use first time tomorrow
                nextRunTime = DateTime.Today.AddDays(1) + _runTimes[0];
            }

            var delay = nextRunTime - now;
            if (delay < TimeSpan.Zero)
                delay = TimeSpan.Zero;

            Console.WriteLine($"[JobSyncService] Next run at {nextRunTime} (in {delay}).");

            await Task.Delay(delay);
        }
    }
}
