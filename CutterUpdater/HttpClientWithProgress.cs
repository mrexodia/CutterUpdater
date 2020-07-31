using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CutterUpdater
{
    // Based on: https://stackoverflow.com/a/60935947/1806760
    public delegate void ProgressChangedHandler(long? totalFileSize, long totalBytesDownloaded, double? progressPercentage);

    public class HttpClientWithProgress : HttpClient
    {
        public event ProgressChangedHandler ProgressChanged;

        public async Task StartDownload(string downloadUrl, string destinationFilePath)
        {
            using (var response = await GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                await DownloadFileFromHttpResponseMessage(response, destinationFilePath);
        }

        public async Task StartDownload(string downloadUrl, string destinationFilePath, CancellationToken cancellationToken)
        {
            using (var response = await GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                await DownloadFileFromHttpResponseMessage(response, destinationFilePath);
        }

        private async Task DownloadFileFromHttpResponseMessage(HttpResponseMessage response, string destinationFilePath)
        {
            response.EnsureSuccessStatusCode();
            long? totalBytes = response.Content.Headers.ContentLength;
            using (var contentStream = await response.Content.ReadAsStreamAsync())
                await ProcessContentStream(totalBytes, contentStream, destinationFilePath);
        }

        private async Task ProcessContentStream(long? totalDownloadSize, Stream contentStream, string destinationFilePath)
        {
            long totalBytesRead = 0L;
            long readCount = 0L;
            byte[] buffer = new byte[8192];
            bool isMoreToRead = true;

            using (FileStream fileStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
            {
                do
                {
                    int bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        isMoreToRead = false;
                        continue;
                    }

                    await fileStream.WriteAsync(buffer, 0, bytesRead);

                    totalBytesRead += bytesRead;
                    readCount += 1;

                    if (readCount % 10 == 0)
                        TriggerProgressChanged(totalDownloadSize, totalBytesRead);
                }
                while (isMoreToRead);
            }
            TriggerProgressChanged(totalDownloadSize, totalBytesRead);
        }

        private void TriggerProgressChanged(long? totalDownloadSize, long totalBytesRead)
        {
            if (ProgressChanged == null)
                return;

            double? progressPercentage = null;
            if (totalDownloadSize.HasValue)
                progressPercentage = Math.Round((double)totalBytesRead / totalDownloadSize.Value * 100, 2);

            ProgressChanged(totalDownloadSize, totalBytesRead, progressPercentage);
        }
    }
}
