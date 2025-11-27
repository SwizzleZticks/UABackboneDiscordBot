using Microsoft.Playwright;

namespace UABackoneBot.Services
{
    public class CsvDownloaderService
    {
        private const bool  ACCEPT_DOWNLOADS = true;
        private const bool  RUN_HEADLESS = true;
        private const float SLO_MO_SPEED = 60f;

        public async Task<string> RunCsvDownloader()
        {
            var exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "--with-deps" });

            if (exitCode != 0)
            {
                Console.WriteLine($"Playwright install failed with exit code {exitCode}");
            }

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(GetLaunchOptions());
            var context = await browser.NewContextAsync(GetContextOptions());
            var page = await context.NewPageAsync();

            await page.GotoAsync("https://uanet.org/JobListings");
            await Login(page, EnvironmentVariable("UA_USER"), EnvironmentVariable("UA_PASS"));
            await WaitForJobListings(page);

            return await ExportCsv(page); //return file path when complete
        }
        private async Task Login(IPage page, string user, string pass)
        {
            var identifier = page.Locator("input[name='identifier']");

            await identifier.FillAsync(user);
            await page.Locator("input[name='credentials.passcode']").FillAsync(pass);
            await page.Locator("input[type='submit']").ClickAsync();
        }

        private async Task WaitForJobListings(IPage page)
        {
            await page.WaitForURLAsync(url => url.Contains("/JobListings"), new() { Timeout = 60000 });
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }

        private async Task<string> ExportCsv(IPage page)
        {
            // Find export button either top-level or in any iframe
            var exportBtn = await FindInFrames(page, "button:has-text(\"Export\")")
                           ?? throw new Exception("Export button not found.");

            await exportBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
            await exportBtn.ScrollIntoViewIfNeededAsync();

            await exportBtn.ClickAsync(new() { Force = true });

            // Find CSV dropdown item
            var csvItem = await FindInFrames(page, "text=CSV")
                         ?? throw new Exception("CSV option not found after clicking Export.");

            await csvItem.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });


            var download = await page.RunAndWaitForDownloadAsync(async () =>
            {
                await csvItem.ClickAsync(new() { Force = true });
            });

            var savePath = Path.Combine(EnvironmentVariable("FILE_PATH"), "jobs.csv");
            await download.SaveAsAsync(savePath);

            return savePath;
        }

        private async Task<ILocator?> FindInFrames(IPage page, string selector)
        {
            var top = page.Locator(selector);
            if (await top.CountAsync() > 0) return top.First;

            foreach (var frame in page.Frames)
            {
                var loc = frame.Locator(selector);
                if (await loc.CountAsync() > 0) return loc.First;
            }
            return null;
        }

        private BrowserTypeLaunchOptions GetLaunchOptions()
        {
            return new BrowserTypeLaunchOptions
            {
                Headless = RUN_HEADLESS,
                SlowMo = SLO_MO_SPEED
            };
        }
        private BrowserNewContextOptions GetContextOptions()
        {
            return new BrowserNewContextOptions
            {
                AcceptDownloads = ACCEPT_DOWNLOADS
            };
        }

        private string EnvironmentVariable(string key) =>
            Environment.GetEnvironmentVariable(key) ?? throw new InvalidOperationException($"Missing env var: {key}");

    }
}
