using System;

namespace musicStudioUnit.HvacController
{
    // HVAC Event Arguments
    public class HVACStatusUpdatedEventArgs : EventArgs
    {
        public HVACStatus Status { get; set; }
        public HVACStatusUpdatedEventArgs(HVACStatus status)
        {
            Status = status;
        }
    }

    public class HVACSetpointChangedEventArgs : EventArgs
    {
        public byte ZoneId { get; set; }
        public float Setpoint { get; set; }
        public HVACSetpointChangedEventArgs(byte zoneId, float setpoint)
        {
            ZoneId = zoneId;
            Setpoint = setpoint;
        }
    }

    public class HVACConnectedEventArgs : EventArgs
    {
        public string Address { get; set; }
        public HVACConnectedEventArgs(string address)
        {
            Address = address;
        }
    }

    public class HVACDisconnectedEventArgs : EventArgs
    {
        public string Address { get; set; }
        public string Reason { get; set; }
        public HVACDisconnectedEventArgs(string address, string reason = "")
        {
            Address = address;
            Reason = reason;
        }
    }

    public class HVACErrorEventArgs : EventArgs
    {
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
        public HVACErrorEventArgs(string errorMessage, Exception ex = null)
        {
            ErrorMessage = errorMessage;
            Exception = ex;
        }
    }

    // HVAC Status Model
    public class HVACStatus
    {
        public bool IsConnected { get; set; }
        public float CurrentSetpoint { get; set; }
        public float ExternalTemperature { get; set; }
        public bool OverTemp { get; set; }
        public bool PressureFault { get; set; }
        public bool VoltageFault { get; set; }
        public bool AirflowBlocked { get; set; }
        public System.Collections.Generic.Dictionary<byte, float> ZoneSetpoints { get; set; } = new System.Collections.Generic.Dictionary<byte, float>();
    }

    // Temperature Event Arguments
    public class TemperatureChangedEventArgs : EventArgs
    {
        public byte ZoneId { get; set; }
        public float Temperature { get; set; }
        public TemperatureChangedEventArgs(byte zoneId, float temperature)
        {
            ZoneId = zoneId;
            Temperature = temperature;
        }
    }

    public class TemperatureStatusEventArgs : EventArgs
    {
        public HVACStatus Status { get; set; }
        public TemperatureStatusEventArgs(HVACStatus status)
        {
            Status = status;
        }
    }
}
