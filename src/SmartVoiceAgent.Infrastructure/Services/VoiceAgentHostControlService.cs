using SmartVoiceAgent.Core.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SmartVoiceAgent.Infrastructure.Services
{
    /// <summary>
    /// Service that provides control over the VoiceAgent hosted service
    /// Allows starting/stopping command processing without stopping the entire host
    /// </summary>
    public class VoiceAgentHostControlService : IVoiceAgentHostControl
    {
        private CancellationTokenSource _cancellationTokenSource;
        private readonly object _lock = new();

        public bool IsRunning { get; private set; }

        public event EventHandler<bool>? StateChanged;

        public VoiceAgentHostControlService()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            IsRunning = true; // Start in running state
        }

        public Task StartAsync()
        {
            lock (_lock)
            {
                if (IsRunning)
                    return Task.CompletedTask;

                // Create new cancellation token source
                _cancellationTokenSource = new CancellationTokenSource();
                IsRunning = true;
                StateChanged?.Invoke(this, true);
            }
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            lock (_lock)
            {
                if (!IsRunning)
                    return Task.CompletedTask;

                // Cancel the token to signal shutdown
                _cancellationTokenSource.Cancel();
                IsRunning = false;
                StateChanged?.Invoke(this, false);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Gets the current cancellation token for command processing
        /// </summary>
        public CancellationToken GetCancellationToken() => _cancellationTokenSource.Token;

        /// <summary>
        /// Checks if processing should continue
        /// </summary>
        public bool ShouldProcess() => IsRunning && !_cancellationTokenSource.IsCancellationRequested;
    }
}
