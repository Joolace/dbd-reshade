using System.Net.Http.Headers;

namespace DBDReshade.Services;

/// <summary>Single shared HttpClient (GitHub's API requires a User-Agent).</summary>
public static class Http
{
    public static HttpClient Client { get; } = Create();

    private static HttpClient Create()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DBDReshade", AppInfo.Version.ToString(3)));
        return client;
    }

    /// <summary>Downloads to a .part file first so a failed download never leaves a broken file behind.</summary>
    public static async Task DownloadFileAsync(
        string url, string destination, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        // A name of its own per download: the same preview can be fetched twice at once (list + full screen).
        var tempFile = $"{destination}.{Guid.NewGuid():N}.part";

        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = File.Create(tempFile))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    received += read;
                    if (total > 0) progress?.Report((double)received / total.Value);
                }
            }

            File.Move(tempFile, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
