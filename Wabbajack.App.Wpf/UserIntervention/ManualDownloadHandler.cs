using Microsoft.Web.WebView2.Core;
using System;
using System.Threading;
using System.Threading.Tasks;
using Wabbajack.DTOs.DownloadStates;
using Wabbajack.DTOs.Interventions;

namespace Wabbajack;

public class ManualDownloadHandler : BrowserWindowViewModel
{
    // Initial wait after navigation before starting click attempts.
    // Nexus Mods is a SPA and the download button may not be in the DOM
    // immediately after the NavigationCompleted event fires.
    private static readonly TimeSpan NexusPageLoadDelay = TimeSpan.FromSeconds(2);

    // How long to wait between click attempts.
    private static readonly TimeSpan NexusClickPollInterval = TimeSpan.FromSeconds(2);

    public ManualDownload Intervention { get; set; }

    public ManualDownloadHandler(IServiceProvider serviceProvider) : base(serviceProvider) { }

    protected override async Task Run(CancellationToken token)
    {
        var dowloadState = default(ManualDownload.BrowserDownloadState);
        try
        {
            var archive = Intervention.Archive;
            var md = Intervention.Archive.State as Manual;

            HeaderText = $"Manual download for {archive.Name} ({md.Url.Host})";

            Instructions = string.IsNullOrWhiteSpace(md.Prompt) ? $"Please download {archive.Name}" : md.Prompt;

            dowloadState = await NavigateAndLoadDownloadState(md.Url, token);
        }
        finally
        {
            Intervention.Finish(dowloadState);
        }
    }

    private async Task<ManualDownload.BrowserDownloadState> NavigateAndLoadDownloadState(Uri downloadPageUrl, CancellationToken token)
    {
        var source = new TaskCompletionSource<Uri>();
        var referer = Browser.Source;
        await WaitForReady(token);

        EventHandler<CoreWebView2DownloadStartingEventArgs> handler = null!;

        handler = (_, args) =>
        {
            try
            {
                source.TrySetResult(new Uri(args.DownloadOperation.Uri));
            }
            catch (Exception)
            {
                source.TrySetCanceled(token);
            }

            args.Cancel = true;
            args.Handled = true;
        };

        Browser.CoreWebView2.DownloadStarting += handler;

        await NavigateTo(downloadPageUrl);

        try
        {
            Uri uri;
            if (downloadPageUrl.Host.EndsWith("nexusmods.com", StringComparison.OrdinalIgnoreCase))
            {
                // Give the SPA time to finish rendering before the first click attempt.
                await Task.Delay(NexusPageLoadDelay, token);
                uri = await WaitWhileAutoClicking(source.Task, token);
            }
            else
            {
                uri = await WaitWhileRemovingIframes(source.Task, token);
            }

            var cookies = await GetCookies(uri.Host, token);

            return new ManualDownload.BrowserDownloadState(
                uri,
                cookies,
                new[]
                {
                ("Referer", referer?.ToString() ?? uri.ToString())
                },
                Browser.CoreWebView2.Settings.UserAgent);
        }
        finally
        {
            Browser.CoreWebView2.DownloadStarting -= handler;
        }
    }

    private async Task<T> WaitWhileAutoClicking<T>(Task<T> mainTask, CancellationToken token)
    {
        var attempt = 0;
        while (!token.IsCancellationRequested && !mainTask.IsCompleted)
        {
            attempt++;
            await RunJavaScript("Array.from(document.getElementsByTagName(\"iframe\")).forEach(f => {if (f.title != \"SP Consent Message\" && !f.src.includes(\"challenges.cloudflare.com\")) f.remove()})");
            await RunJavaScript(BuildClickAttemptScript(attempt));
            await Task.WhenAny(mainTask, Task.Delay(NexusClickPollInterval, token));
        }
        token.ThrowIfCancellationRequested();
        return mainTask.Result;
    }

    private static string BuildClickAttemptScript(int attempt) => $$"""
        (function() {
            var attempt = {{attempt}};
            console.log('[WJ] Click attempt #' + attempt);

            // Primary: slow download button inside the mod-file-download block.
            var host = document.querySelector('mod-file-download');
            if (!host) {
                console.log('[WJ]   mod-file-download -> not in DOM');
            } else {
                console.log('[WJ]   mod-file-download -> found');
                var root = host.shadowRoot || host;
                var btn = root.querySelector('button.nxm-button-secondary-filled-weak');
                if (!btn) {
                    console.log('[WJ]   button.nxm-button-secondary-filled-weak -> not in DOM');
                } else {
                    var visible = btn.offsetParent !== null || btn.getBoundingClientRect().height > 0;
                    console.log('[WJ]   button.nxm-button-secondary-filled-weak -> found, visible=' + visible + ', text="' + btn.textContent.trim() + '"');
                    if (visible) {
                        console.log('[WJ]   clicking');
                        btn.click();
                        return 'clicked:nxm-button-secondary-filled-weak';
                    }
                }
            }

            console.log('[WJ]   no clickable target found, will retry');
            return 'not-found';
        })();
        """;
}