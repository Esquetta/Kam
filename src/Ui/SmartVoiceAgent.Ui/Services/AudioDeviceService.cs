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

    public AudioDeviceService()
    {
        try
        {
            _deviceEnumerator = new MMDeviceEnumerator();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize MMDeviceEnumerator: {ex.Message}");
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
                devices.Add(new AudioDeviceInfo
                {
                    Id = device.ID,
                    Name = device.FriendlyName,
                    IsDefault = device.ID == GetDefaultInputDeviceId(),
                    DeviceType = AudioDeviceType.Input
                });
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
                devices.Add(new AudioDeviceInfo
                {
                    Id = device.ID,
                    Name = device.FriendlyName,
                    IsDefault = device.ID == GetDefaultOutputDeviceId(),
                    DeviceType = AudioDeviceType.Output
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error enumerating output devices: {ex.Message}");
        }

        return devices;
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
            var device = _deviceEnumerator.GetDevice(deviceId);
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
            var device = _deviceEnumerator.GetDevice(deviceId);
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
            var device = _deviceEnumerator.GetDevice(deviceId);
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
            var device = _deviceEnumerator.GetDevice(deviceId);
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
    public float GetInputLevel(string deviceId)
    {
        if (_deviceEnumerator == null)
            return 0;

        try
        {
            var device = _deviceEnumerator.GetDevice(deviceId);
            if (device?.AudioMeterInformation != null)
            {
                return device.AudioMeterInformation.MasterPeakValue;
            }
        }
        catch { }

        return 0;
    }

    /// <summary>
    /// Gets the current output level (VU meter) for visualization
    /// </summary>
    public float GetOutputLevel(string deviceId)
    {
        if (_deviceEnumerator == null)
            return 0;

        try
        {
            var device = _deviceEnumerator.GetDevice(deviceId);
            if (device?.AudioMeterInformation != null)
            {
                return device.AudioMeterInformation.MasterPeakValue;
            }
        }
        catch { }

        return 0;
    }

    public void Dispose()
    {
        _deviceEnumerator?.Dispose();
        _deviceEnumerator = null;
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
