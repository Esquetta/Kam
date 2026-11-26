using SmartVoiceAgent.Core.Models.Agents;
using System.Collections.Generic;

namespace SmartVoiceAgent.Ui.ViewModels
{
    /// <summary>
    /// Folds consecutive read-only tool steps of the main agent into one group item, so a run of reads and
    /// searches shows as one summary line. Agent text, a step of another risk, a subagent and a step that asked
    /// for approval end a group. The live chat and a thread rebuilt from history use the same rules.
    /// </summary>
    public static class ToolStepGrouping
    {
        /// <summary>
        /// Adds a main-agent tool step at the end of a timeline. A read-only step joins the group right before it,
        /// or forms a group with the read-only step right before it; any other step is added on its own.
        /// </summary>
        /// <param name="items">The timeline's top-level items.</param>
        /// <param name="step">The new step.</param>
        public static void Append(IList<AgentChatMessageViewModel> items, AgentChatMessageViewModel step)
        {
            if (CanGroup(step) && items.Count > 0)
            {
                var last = items[^1];
                if (last.IsToolGroup)
                {
                    last.Children.Add(step);
                    return;
                }

                if (CanGroup(last))
                {
                    var group = AgentChatMessageViewModel.ToolGroup();
                    group.Children.Add(last);
                    group.Children.Add(step);
                    items[^1] = group;
                    return;
                }
            }

            items.Add(step);
        }

        /// <summary>
        /// Takes a step out of its group and puts it on its own line right after the group, such as a read that
        /// asks for approval. A group left with one step gives way to that step.
        /// </summary>
        /// <param name="items">The timeline's top-level items.</param>
        /// <param name="step">The step.</param>
        public static void Release(IList<AgentChatMessageViewModel> items, AgentChatMessageViewModel step)
        {
            for (var index = 0; index < items.Count; index++)
            {
                var group = items[index];
                if (!group.IsToolGroup || !group.Children.Remove(step))
                {
                    continue;
                }

                items.Insert(index + 1, step);
                if (group.Children.Count == 1)
                {
                    var single = group.Children[0];
                    group.Children.Clear();
                    items[index] = single;
                }

                return;
            }
        }

        private static bool CanGroup(AgentChatMessageViewModel item) =>
            item.IsToolStep
            && !item.IsSubagentStep
            && !item.AskedForApproval
            && item.Risk is ToolRisk.Read or ToolRisk.Network;
    }
}
