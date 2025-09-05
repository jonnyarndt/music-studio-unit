using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Newtonsoft.Json;

namespace flexpod.Configuration
{
    /// <summary>
    /// Local XML Configuration Model for msu.xml
    /// </summary>
    [XmlRoot("configuration")]
    public class LocalConfiguration
    {
        [XmlElement("address")]
        public AddressInfo Address { get; set; }

        [XmlElement("remote")]
        public RemoteInfo Remote { get; set; }

        [XmlElement("hvac")]
        public HVACInfo HVAC { get; set; }

        [XmlElement("dms")]
        public DMSInfo DMS { get; set; }
    }

    /// <summary>
    /// Address information from local configuration
    /// </summary>
    public class AddressInfo
    {
        [XmlElement("street")]
        public string Street { get; set; }

        [XmlElement("city")]
        public string City { get; set; }
    }

    /// <summary>
    /// Remote configuration server information
    /// </summary>
    public class RemoteInfo
    {
        [XmlElement("ip")]
        public string IP { get; set; }

        [XmlElement("port")]
        public int Port { get; set; }

        [XmlElement("file")]
        public string File { get; set; }
    }

    /// <summary>
    /// HVAC system configuration
    /// </summary>
    public class HVACInfo
    {
        [XmlElement("ip")]
        public string IP { get; set; }

        [XmlElement("port")]
        public int Port { get; set; }

        [XmlElement("idle")]
        public float IdleSetpoint { get; set; }
    }

    /// <summary>
    /// Digital Music System configuration
    /// </summary>
    public class DMSInfo
    {
        [XmlElement("ip")]
        public string IP { get; set; }

        [XmlElement("port")]
        public int Port { get; set; }

        [XmlElement("listen")]
        public int ListenPort { get; set; }
    }

    /// <summary>
    /// Remote JSON Configuration Model for building configuration
    /// </summary>
    public class RemoteConfiguration
    {
        [JsonProperty("msu_units")]
        public List<MSUConfiguration> MSUUnits { get; set; } = new List<MSUConfiguration>();
    }

    /// <summary>
    /// Individual MSU configuration from remote JSON
    /// </summary>
    public class MSUConfiguration
    {
        [JsonProperty("MSU_UID")]
        public string MSU_UID { get; set; }

        [JsonProperty("MSU_NAME")]
        public string MSU_NAME { get; set; }

        [JsonProperty("MSU_MAC")]
        public string MSU_MAC { get; set; }

        [JsonProperty("X_COORD")]
        public int X_COORD { get; set; }

        [JsonProperty("Y_COORD")]
        public int Y_COORD { get; set; }

        [JsonProperty("HVAC_ID")]
        public int HVAC_ID { get; set; }
    }
}
