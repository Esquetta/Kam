namespace SmartVoiceAgent.Core.Models.Session;

public sealed class ApplicationSessionContext
{
    public GitHubSessionContext GitHub { get; set; } = new();

    public DateTimeOffset LastUpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class GitHubSessionContext
{
    public string RepositoryFullName { get; set; } = string.Empty;

    public GitHubWorkflowRunSessionContext? ActiveWorkflowRun { get; set; }

    public GitHubPullRequestSessionContext? ActivePullRequest { get; set; }
}

public sealed class GitHubWorkflowRunSessionContext
{
    public string RepositoryFullName { get; set; } = string.Empty;

    public long RunId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string DisplayTitle { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Conclusion { get; set; } = string.Empty;

    public string Event { get; set; } = string.Empty;

    public string HeadBranch { get; set; } = string.Empty;

    public string HtmlUrl { get; set; } = string.Empty;

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class GitHubPullRequestSessionContext
{
    public string RepositoryFullName { get; set; } = string.Empty;

    public int Number { get; set; }

    public string Title { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string AuthorLogin { get; set; } = string.Empty;

    public string HtmlUrl { get; set; } = string.Empty;

    public string HeadRefName { get; set; } = string.Empty;

    public string BaseRefName { get; set; } = string.Empty;

    public bool IsDraft { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
