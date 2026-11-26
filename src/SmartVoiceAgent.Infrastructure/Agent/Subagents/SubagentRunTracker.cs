using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Infrastructure.Helpers;

namespace SmartVoiceAgent.Infrastructure.Agent.Subagents;

/// <summary>
/// Keeps subagent runs in memory and appends each finished run to <c>%AppData%/Kam/subagent-runs.jsonl</c>,
/// so the history survives a restart. Long texts are shortened before they are saved.
/// </summary>
public sealed class SubagentRunTracker : ISubagentRunTracker
{
    private const int MaxRuns = 500;
    private const int MaxPromptCharacters = 8000;
    private const int MaxResultCharacters = 16000;
    private const int MaxArgumentsCharacters = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();
    private readonly List<SubagentRun> _runs = [];
    private readonly Dictionary<string, CancellationTokenSource> _cancellations = new(StringComparer.Ordinal);
    private readonly string? _path;
    private readonly ILogger<SubagentRunTracker>? _logger;
    private bool _loaded;

    /// <summary>Creates the tracker with its history in the default file.</summary>
    /// <param name="logger">Logger.</param>
    public SubagentRunTracker(ILogger<SubagentRunTracker>? logger = null)
        : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kam", "subagent-runs.jsonl"),
            logger)
    {
    }

    /// <summary>Creates the tracker.</summary>
    /// <param name="path">The history file; null keeps runs in memory only.</param>
    /// <param name="logger">Logger.</param>
    public SubagentRunTracker(string? path, ILogger<SubagentRunTracker>? logger = null)
    {
        _path = path;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<SubagentRun>? Changed;

    /// <inheritdoc />
    public IReadOnlyList<SubagentRun> List(string? parentSessionId = null, int maxCount = 200)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _runs
                .Where(run => parentSessionId is null || run.ParentSessionId == parentSessionId)
                .Take(Math.Max(0, maxCount))
                .ToList();
        }
    }

    /// <inheritdoc />
    public SubagentRun? Get(string runId)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _runs.FirstOrDefault(run => run.RunId == runId);
        }
    }

    /// <inheritdoc />
    public bool Cancel(string runId)
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            _cancellations.TryGetValue(runId, out cancellation);
        }

        if (cancellation is null)
        {
            return false;
        }

        try
        {
            cancellation.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Start(SubagentRun run, CancellationTokenSource cancellation)
    {
        lock (_gate)
        {
            EnsureLoaded();
            _runs.Insert(0, run);
            _cancellations[run.RunId] = cancellation;
            if (_runs.Count > MaxRuns)
            {
                _runs.RemoveRange(MaxRuns, _runs.Count - MaxRuns);
            }
        }

        Raise(run);
    }

    /// <inheritdoc />
    public void Update(string runId, Func<SubagentRun, SubagentRun> change)
    {
        var updated = Replace(runId, change);
        if (updated is not null)
        {
            Raise(updated);
        }
    }

    /// <inheritdoc />
    public void Finish(string runId, Func<SubagentRun, SubagentRun> change)
    {
        SubagentRun? finished;
        lock (_gate)
        {
            _cancellations.Remove(runId);
            finished = Replace(runId, change);
        }

        if (finished is null)
        {
            return;
        }

        Append(finished);
        Raise(finished);
    }

    /// <summary>
    /// Tells each subscriber on its own, so one that throws neither stops the others nor the runtime that reported the change.
    /// </summary>
    private void Raise(SubagentRun run)
    {
        foreach (var handler in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler<SubagentRun>)handler)(this, run);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "A subagent run subscriber failed for {RunId}", run.RunId);
            }
        }
    }

    private SubagentRun? Replace(string runId, Func<SubagentRun, SubagentRun> change)
    {
        lock (_gate)
        {
            var index = _runs.FindIndex(run => run.RunId == runId);
            if (index < 0)
            {
                return null;
            }

            var updated = change(_runs[index]);
            _runs[index] = updated;
            return updated;
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (_path is null || !File.Exists(_path))
        {
            return;
        }

        try
        {
            var dropped = false;
            foreach (var line in JsonLinesFile.ReadLinesNewestFirst(_path))
            {
                if (_runs.Count >= MaxRuns)
                {
                    dropped = true;
                    break;
                }

                try
                {
                    if (JsonSerializer.Deserialize<SubagentRun>(line, JsonOptions) is { } run
                        && _runs.All(existing => existing.RunId != run.RunId))
                    {
                        _runs.Add(run);
                    }
                }
                catch (JsonException)
                {
                    // A line cut short by a crash is skipped.
                }
            }

            // Older runs past the cap are dropped from the file once, so it stops growing.
            if (dropped)
            {
                Rewrite();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Could not read the subagent history {Path}", _path);
        }
    }

    private void Rewrite()
    {
        var lines = new StringBuilder();
        for (var index = _runs.Count - 1; index >= 0; index--)
        {
            lines.Append(JsonSerializer.Serialize(_runs[index], JsonOptions)).Append('\n');
        }

        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, lines.ToString(), new UTF8Encoding(false));
        File.Move(temporary, _path!, overwrite: true);
    }

    private void Append(SubagentRun run)
    {
        if (_path is null)
        {
            return;
        }

        var stored = run with
        {
            Prompt = Shorten(run.Prompt, MaxPromptCharacters),
            Result = run.Result is null ? null : Shorten(run.Result, MaxResultCharacters),
            Steps = run.StepList.Select(step => step with { ArgumentsJson = Shorten(step.ArgumentsJson, MaxArgumentsCharacters) }).ToList()
        };

        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, JsonSerializer.Serialize(stored, JsonOptions) + "\n", new UTF8Encoding(false));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Could not save subagent run {RunId}", run.RunId);
        }
    }

    private static string Shorten(string text, int maxCharacters) =>
        text.Length <= maxCharacters ? text : text[..maxCharacters] + "...";
}
