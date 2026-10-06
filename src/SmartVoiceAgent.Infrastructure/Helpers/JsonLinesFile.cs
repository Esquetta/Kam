using System.Text;

namespace SmartVoiceAgent.Infrastructure.Helpers;

/// <summary>
/// Reads JSON Lines logs from the end, so fetching recent entries costs the same however long the file grows.
/// </summary>
public static class JsonLinesFile
{
    private const int DefaultChunkSize = 64 * 1024;

    /// <summary>
    /// Enumerates the file's non-blank lines, newest first, reading backwards in chunks.
    /// Lines split on '\n', a byte that never occurs inside a multi-byte UTF-8 character,
    /// so a chunk edge cannot cut a character in half.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <param name="chunkSize">Bytes read per step.</param>
    /// <returns>The lines, last line first.</returns>
    public static IEnumerable<string> ReadLinesNewestFirst(string path, int chunkSize = DefaultChunkSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1,
            FileOptions.RandomAccess);

        var buffer = new byte[(int)Math.Min(chunkSize, Math.Max(stream.Length, 1))];

        // Bytes of a line whose start lies in an earlier part of the file, in file order.
        var carry = new List<byte>();
        var position = stream.Length;
        while (position > 0)
        {
            var size = (int)Math.Min(buffer.Length, position);
            position -= size;
            stream.Position = position;
            stream.ReadExactly(buffer, 0, size);

            var end = size;
            for (var index = size - 1; index >= 0; index--)
            {
                if (buffer[index] != (byte)'\n')
                {
                    continue;
                }

                var line = Decode(buffer, index + 1, end - index - 1, carry);
                carry.Clear();
                end = index;
                if (!string.IsNullOrWhiteSpace(line))
                {
                    yield return line;
                }
            }

            carry.InsertRange(0, new ArraySegment<byte>(buffer, 0, end));
        }

        if (carry.Count > 0)
        {
            var first = Encoding.UTF8.GetString(carry.ToArray()).TrimStart('﻿').TrimEnd('\r');
            if (!string.IsNullOrWhiteSpace(first))
            {
                yield return first;
            }
        }
    }

    private static string Decode(byte[] buffer, int start, int count, List<byte> carry)
    {
        string text;
        if (carry.Count == 0)
        {
            text = Encoding.UTF8.GetString(buffer, start, count);
        }
        else
        {
            var bytes = new byte[count + carry.Count];
            Array.Copy(buffer, start, bytes, 0, count);
            carry.CopyTo(bytes, count);
            text = Encoding.UTF8.GetString(bytes);
        }

        return text.TrimEnd('\r');
    }
}
