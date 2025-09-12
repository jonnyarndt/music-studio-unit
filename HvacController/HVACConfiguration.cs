using System;
using System.Collections.Generic;

namespace musicStudioUnit.Configuration
{
    /// <summary>
    /// HVAC configuration information from JSON config
    /// </summary>
    public class HVACInfo
    {
        /// <summary>
        /// IP address of HVAC controller
        /// </summary>
        public string? IP { get; set; }

        /// <summary>
        /// TCP port for HVAC communication (default 4001 per Client-Scope.md)
        /// </summary>
        public int Port { get; set; } = 4001;

        /// <summary>
        /// Default setpoint when system is idle (in °C)
        /// </summary>
        public float IdleSetpoint { get; set; } = 21.0f;

        /// <summary>
        /// Zone IDs controlled by this HVAC instance
        /// </summary>
        public List<byte> ZoneIds { get; set; } = new List<byte>();

        /// <summary>
        /// Connection timeout in milliseconds
        /// </summary>
        public int ConnectionTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// Response timeout in milliseconds
        /// </summary>
        public int ResponseTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// Enable detailed debug logging
        /// </summary>
        public bool DebugMode { get; set; } = false;

        /// <summary>
        /// Minimum temperature allowed (°C)
        /// </summary>
        public float MinTemperature { get; set; } = -40.0f;

        /// <summary>
        /// Maximum temperature allowed (°C)
        /// </summary>
        public float MaxTemperature { get; set; } = 50.0f;

        /// <summary>
        /// Temperature increment step (°C) - must be 0.5°C per Client-Scope.md
        /// </summary>
        public float TemperatureIncrement { get; set; } = 0.5f;

        /// <summary>
        /// Auto-reconnect on connection loss
        /// </summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>
        /// Reconnect delay in milliseconds
        /// </summary>
        public int ReconnectDelayMs { get; set; } = 10000;

        /// <summary>
        /// Maximum reconnection attempts (0 = infinite)
        /// </summary>
        public int MaxReconnectAttempts { get; set; } = 0;
    }

    /// <summary>
    /// Studio zone mapping for combined operations
    /// </summary>
    public class StudioZoneMapping
    {
        /// <summary>
        /// Studio identifier (e.g., "Studio1", "Studio2+3")
        /// </summary>
        public string? StudioId { get; set; }

        /// <summary>
        /// List of HVAC zone IDs for this studio/combination
        /// </summary>
        public List<byte> ZoneIds { get; set; } = new List<byte>();

        /// <summary>
        /// Default setpoint for this studio
        /// </summary>
        public float DefaultSetpoint { get; set; } = 21.0f;

        /// <summary>
        /// Priority for conflicts (higher = higher priority)
        /// </summary>
        public int Priority { get; set; } = 1;
    }

    /// <summary>
    /// Temperature control presets
    /// </summary>
    public class TemperaturePreset
    {
        /// <summary>
        /// Preset name (e.g., "Recording", "Mixing", "Idle")
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Target temperature for this preset
        /// </summary>
        public float Temperature { get; set; }

        /// <summary>
        /// Description of the preset
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Icon or image for UI display
        /// </summary>
        public string? Icon { get; set; }
    }
}
