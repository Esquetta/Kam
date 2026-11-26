using Avalonia;
using SmartVoiceAgent.Core.Models.Agents;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels
{
    /// <summary>What a graph node stands for.</summary>
    public enum GraphNodeKind
    {
        /// <summary>The task's main agent, where the graph starts.</summary>
        Root,

        /// <summary>A subagent run.</summary>
        Agent,

        /// <summary>The main agent again, after a group of subagents it waited for.</summary>
        Join
    }

    /// <summary>A node's place and size on the canvas.</summary>
    /// <param name="Id">"root", the run id, or "join:{n}".</param>
    /// <param name="Kind">What the node stands for.</param>
    /// <param name="Bounds">Where it is drawn.</param>
    /// <param name="Run">The subagent run of an agent node.</param>
    public sealed record GraphNodePlacement(string Id, GraphNodeKind Kind, Rect Bounds, SubagentRun? Run = null);

    /// <summary>An edge from the right middle of one node to the left middle of another.</summary>
    /// <param name="FromId">The id of the node it starts at.</param>
    /// <param name="ToId">The id of the node it ends at.</param>
    /// <param name="From">Where the edge starts.</param>
    /// <param name="To">Where it ends.</param>
    /// <param name="StatusKey">The status of the subagent it leads to or from: running, needs-you, done, failed or cancelled.</param>
    public sealed record GraphEdge(string FromId, string ToId, Point From, Point To, string StatusKey);

    /// <summary>The nodes and edges of one task's graph, and the canvas size they need.</summary>
    public sealed record AgentGraph(IReadOnlyList<GraphNodePlacement> Nodes, IReadOnlyList<GraphEdge> Edges, Size Size);

    /// <summary>
    /// Lays out a task's subagent runs as a fork-join graph from left to right: the main agent, then each group of
    /// subagents that ran at the same time stacked in a column, then the main agent again where it waited for them.
    /// Runs whose time spans overlap share a group.
    /// </summary>
    public static class AgentGraphLayout
    {
        /// <summary>Gap around the graph.</summary>
        public const double Padding = 24;

        /// <summary>Main agent node size.</summary>
        public static readonly Size RootSize = new(168, 64);

        /// <summary>Subagent node size.</summary>
        public static readonly Size AgentSize = new(244, 100);

        /// <summary>Join node diameter.</summary>
        public const double JoinSize = 14;

        /// <summary>Horizontal space between columns.</summary>
        public const double ColumnGap = 56;

        /// <summary>Vertical space between subagents of one group.</summary>
        public const double RowGap = 14;

        /// <summary>
        /// Builds the graph.
        /// </summary>
        /// <param name="runs">The task's subagent runs, in any order.</param>
        /// <param name="now">The current time, the end of runs that still run.</param>
        public static AgentGraph Build(IEnumerable<SubagentRun> runs, DateTimeOffset now)
        {
            var waves = GroupIntoWaves(runs, now);
            var tallest = waves.Count == 0 ? 0 : waves.Max(wave => wave.Count);
            var contentHeight = Math.Max(RootSize.Height, (tallest * AgentSize.Height) + (Math.Max(0, tallest - 1) * RowGap));
            var spine = Padding + (contentHeight / 2);

            var nodes = new List<GraphNodePlacement>();
            var edges = new List<GraphEdge>();
            var x = Padding;

            var root = new Rect(x, spine - (RootSize.Height / 2), RootSize.Width, RootSize.Height);
            nodes.Add(new GraphNodePlacement("root", GraphNodeKind.Root, root));
            var anchor = RightMiddle(root);
            var anchorId = "root";
            x += RootSize.Width + ColumnGap;

            for (var index = 0; index < waves.Count; index++)
            {
                var wave = waves[index];
                var top = spine - (((wave.Count * AgentSize.Height) + ((wave.Count - 1) * RowGap)) / 2);
                var placed = new List<(string Id, Rect Bounds, string Status)>();
                foreach (var run in wave)
                {
                    var bounds = new Rect(x, top, AgentSize.Width, AgentSize.Height);
                    var status = StatusKey(run);
                    nodes.Add(new GraphNodePlacement(run.RunId, GraphNodeKind.Agent, bounds, run));
                    edges.Add(new GraphEdge(anchorId, run.RunId, anchor, LeftMiddle(bounds), status));
                    placed.Add((run.RunId, bounds, status));
                    top += AgentSize.Height + RowGap;
                }

                x += AgentSize.Width + ColumnGap;
                var join = new Rect(x, spine - (JoinSize / 2), JoinSize, JoinSize);
                var joinId = $"join:{index}";
                nodes.Add(new GraphNodePlacement(joinId, GraphNodeKind.Join, join));
                foreach (var (id, bounds, status) in placed)
                {
                    edges.Add(new GraphEdge(id, joinId, RightMiddle(bounds), LeftMiddle(join), status));
                }

                anchor = RightMiddle(join);
                anchorId = joinId;
                x += JoinSize + ColumnGap;
            }

            var width = x - ColumnGap + Padding;
            return new AgentGraph(nodes, edges, new Size(width, contentHeight + (2 * Padding)));
        }

        /// <summary>
        /// Groups runs that overlapped in time, oldest group first; each group lists its runs in start order.
        /// </summary>
        /// <param name="runs">The runs.</param>
        /// <param name="now">The end of runs that still run.</param>
        public static IReadOnlyList<IReadOnlyList<SubagentRun>> GroupIntoWaves(IEnumerable<SubagentRun> runs, DateTimeOffset now)
        {
            var waves = new List<IReadOnlyList<SubagentRun>>();
            List<SubagentRun>? current = null;
            var currentEnd = DateTimeOffset.MinValue;

            foreach (var run in runs.OrderBy(run => run.StartedAt).ThenBy(run => run.RunId, StringComparer.Ordinal))
            {
                var end = run.CompletedAt ?? now;
                if (current is not null && run.StartedAt < currentEnd)
                {
                    current.Add(run);
                    currentEnd = end > currentEnd ? end : currentEnd;
                    continue;
                }

                current = [run];
                waves.Add(current);
                currentEnd = end;
            }

            return waves;
        }

        /// <summary>Gets the status key of a run: running, needs-you, done, failed or cancelled.</summary>
        /// <param name="run">The run.</param>
        public static string StatusKey(SubagentRun run) => run.Status switch
        {
            SubagentRunStatus.Running when run.IsWaitingForApproval => "needs-you",
            SubagentRunStatus.Running => "running",
            SubagentRunStatus.Failed => "failed",
            SubagentRunStatus.Cancelled => "cancelled",
            _ => "done"
        };

        /// <summary>Gets the middle of a node's left side, where edges end.</summary>
        /// <param name="bounds">The node's bounds.</param>
        public static Point LeftMiddle(Rect bounds) => new(bounds.Left, bounds.Top + (bounds.Height / 2));

        /// <summary>Gets the middle of a node's right side, where edges start.</summary>
        /// <param name="bounds">The node's bounds.</param>
        public static Point RightMiddle(Rect bounds) => new(bounds.Right, bounds.Top + (bounds.Height / 2));
    }
}
