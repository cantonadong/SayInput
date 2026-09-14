namespace VoiceTyper.Windows.Storage;

internal static class AtomicFile
{
    // ponytail: serialize rare settings/credential writes; use per-path gates only if write volume grows.
    private static readonly SemaphoreSlim WriterGate = new(1, 1);

    public static async Task WriteAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken)
    {
        await WriterGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            path = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Same-directory rename is the commit point: readers see the old or the new complete file.
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            WriterGate.Release();
        }
    }
}
