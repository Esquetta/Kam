using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartVoiceAgent.Ui.Services;

/// <summary>
/// Service for enumerating and managing audio input/output devices
/// Similar to Discord's voice settings
/// </summary>
public class AudioDeviceService : IDisposable
{
    private MMDeviceEnumerator? _deviceEnumerator;
    private readonly object _meterGate = new();
    private MMDevice? _meterDevice;
    private string? _meterDeviceId;

    /// <summary>
    /// Event fired when audio devices change (added, removed, or default changed)
    /// </summary>
    public event EventHandler? DevicesChanged;

    /// <summary>
    /// Gets whether the audio subsystem is available
    /// </summary>
    public bool IsAvailable => _deviceEnumerator != null;

    /// <summary>
    /// Gets the last error message if initialization failed
    /// </summary>
    public string? LastError { get; private set; }

    public AudioDeviceService()
    {
        try
        {
            _deviceEnumerator = new MMDeviceEnumerator();
            
            // Register for device change notifications
            _deviceEnumerator.RegisterEndpointNotificationCallback(new DeviceNotificationCallback(this));
        }
        catch (Exception ex)
        {
            LastError = $"Failed to initialize audio device enumerator: {ex.Message}";
            System.Diagnostics.Debug.WriteLine(LastError);
        }
    }

    /// <summary>
    /// Gets all available input (microphone) devices
    /// </summary>
    public List<AudioDeviceInfo> GetInputDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        
        if (_deviceEnumerator == null)
            return devices;

        try
        {
            // Get all active audio endpoints that are capture devices
            var captureDevices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            
            foreach (var device in captureDevices)
            {
                try
                {
                    devices.Add(new AudioDeviceInfo
                    {
                        Id = device.ID,
                        Name = device.FriendlyName,
                        IsDefault = device.ID == GetDefaultInputDeviceId(),
                        DeviceType = AudioDeviceType.Input,
                        IsAvailable = true
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error reading device info: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error enumerating input devices: {ex.Message}");
        }

        return devices;
    }

    /// <summary>
    /// Gets all available output (headset/speaker) devices
    /// </summary>
    public List<AudioDeviceInfo> GetOutputDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        
        if (_deviceEnumerator == null)
            return devices;

        try
        {
            // Get all active audio endpoints that are render devices
            var renderDevices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            
            foreach (var device in renderDevices)
            {
                try
                {
                    devices.Add(new AudioDeviceInfo
                    {
                        Id = device.ID,
                        Name = device.FriendlyName,
                        IsDefault = device.ID == GetDefaultOutputDeviceId(),
                        DeviceType = AudioDeviceType.Output,
                        IsAvailable = true
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error reading device info: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error enumerating output devices: {ex.Message}");
        }

        return devices;
    }

    /// <summary>
    /// Checks if a specific device is still available
    /// </summary>
    public bool IsDeviceAvailable(string deviceId)
    {
        if (_deviceEnumerator == null || string.IsNullOrEmpty(deviceId))
            return false;

        try
        {
            using var device = _deviceEnumerator.GetDevice(deviceId);
            return device?.State == DeviceState.Active;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the default input device ID
    /// </summary>
    public string? GetDefaultInputDeviceId()
    {
        if (_deviceEnumerator == null)
            return null;

        try
        {
            var defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            return defaultDevice?.ID;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the default output device ID
    /// </summary>
    public string? GetDefaultOutputDeviceId()
    {
        if (_deviceEnumerator == null)
            return null;

        try
        {
            var defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications);
            return defaultDevice?.ID;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the current input volume level (0.0 to 1.0)
    /// </summary>
    public float GetInputVolume(string deviceId)
    {
        if (_deviceEnumerator == null)
            return 0;

        try
        {
            using var device = _deviceEnumerator.GetDevice(deviceId);
            if (device?.AudioEndpointVolume != null)
            {
                return device.AudioEndpointVolume.MasterVolumeLevelScalar;
            }
        }
        catch { }

        return 0;
    }

    /// <summary>
    /// Sets the input volume level (0.0 to 1.0)
    /// </summary>
    public void SetInputVolume(string deviceId, float volume)
    {
        if (_deviceEnumerator == null)
            return;

        try
        {
            using var device = _deviceEnumerator.GetDevice(deviceId);
            if (device?.AudioEndpointVolume != null)
            {
                device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0f, 1f);
            }
        }
        catch { }
    }

    /// <summary>
    /// Gets the current output volume level (0.0 to 1.0)
    /// </summary>
    public float GetOutputVolume(string deviceId)
    {
        if (_deviceEnumerator == null)
            return 0;

        try
        {
            using var device = _deviceEnumerator.GetDevice(deviceId);
            if (device?.AudioEndpointVolume != null)
            {
                return device.AudioEndpointVolume.MasterVolumeLevelScalar;
            }
        }
        catch { }

        return 0;
    }

    /// <summary>
    /// Sets the output volume level (0.0 to 1.0)
    /// </summary>
    public void SetOutputVolume(string deviceId, float volume)
    {
        if (_deviceEnumerator == null)
            return;

        try
        {
            using var device = _deviceEnumerator.GetDevice(deviceId);
            if (device?.AudioEndpointVolume != null)
            {
                device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0f, 1f);
            }
        }
        catch { }
    }

    /// <summary>
    /// Gets the current input level (VU meter) for visualization
    /// </summary>
    public float GetInputLevel(string deviceId) => GetPeakLevel(deviceId);

    /// <summary>
    /// Gets the current output level (VU meter) for visualization
    /// </summary>
    public float GetOutputLevel(string deviceId) => GetPeakLevel(deviceId);

    /// <summary>
    /// Reads a device's peak level. The meter polls ten times a second, so the device handle is
    /// kept for the device being metered instead of opening a new COM object on every read.
    /// </summary>
    private float GetPeakLevel(string deviceId)
    {
        if (_deviceEnumerator == null)
            return 0;

        lock (_meterGate)
        {
            try
            {
                if (_meterDevice is null || _meterDeviceId != deviceId)
                {
                    ReleaseMeterDevice();
                    _meterDevice = _deviceEnumerator.GetDevice(deviceId);
                    _meterDeviceId = deviceId;
                }

                return _meterDevice.AudioMeterInformation?.MasterPeakValue ?? 0;
            }
            catch
            {
                ReleaseMeterDevice();
                return 0;
            }
        }
    }

    private void ReleaseMeterDevice()
    {
        _meterDevice?.Dispose();
        _meterDevice = null;
        _meterDeviceId = null;
    }

    /// <summary>
    /// Refreshes the device lists and raises DevicesChanged event
    /// </summary>
    public void RefreshDevices()
    {
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void OnDevicesChanged()
    {
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_meterGate)
        {
            ReleaseMeterDevice();
        }

        _deviceEnumerator?.Dispose();
        _deviceEnumerator = null;
    }

    /// <summary>
    /// Callback for device notification events
    /// </summary>
    private class DeviceNotificationCallback : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient
    {
        private readonly AudioDeviceService _service;

        public DeviceNotificationCallback(AudioDeviceService service)
        {
            _service = service;
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            _service.OnDevicesChanged();
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
            _service.OnDevicesChanged();
        }

        public void OnDeviceRemoved(string deviceId)
        {
            _service.OnDevicesChanged();
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            _service.OnDevicesChanged();
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
            // Ignore property changes
        }
    }
}

/// <summary>
/// Represents an audio device
/// </summary>
public class AudioDeviceInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsAvailable { get; set; }
    public AudioDeviceType DeviceType { get; set; }

    public override string ToString()
    {
        return IsDefault ? $"{Name} (Default)" : Name;
    }
}

public enum AudioDeviceType
{
    Input,
    Output
}
