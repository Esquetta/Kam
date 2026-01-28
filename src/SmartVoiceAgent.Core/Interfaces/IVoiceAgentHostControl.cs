using System;
using System.Threading.Tasks;

namespace SmartVoiceAgent.Core.Interfaces
{
    /// <summary>
    /// Provides control over the VoiceAgent hosted service (start/stop)
    /// </summary>
    public interface IVoiceAgentHostControl
    {
        /// <summary>
        /// Gets whether the VoiceAgent Host is currently running
        /// </summary>
        bool IsRunning { get; }
        
        /// <summary>
        /// Event raised when the running state changes
        /// </summary>
        event EventHandler<bool>? StateChanged;
        
        /// <summary>
        /// Starts the VoiceAgent Host service
        /// </summary>
        Task StartAsync();
        
        /// <summary>
        /// Stops the VoiceAgent Host service
        /// </summary>
        Task StopAsync();
    }
}
