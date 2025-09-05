using System;
using System.Collections.Generic;
using System.Linq;
using Crestron.SimplSharp;
using core_tools;
using flexpod.Configuration;

namespace flexpod.Services
{
    public class StudioCombinationManager : IKeyName
    {
        private readonly string _key;
        private readonly string _msuUID;
        private readonly int _xCoord;
        private readonly int _yCoord;
        private readonly byte _hvacZoneId;
        private Dictionary<string, MusicStudioUnit> _allMSUs;
        private List<MusicStudioUnit> _combinedMSUs = new List<MusicStudioUnit>();
        private StudioCombinationType _combinationType = StudioCombinationType.Single;
        
        public event EventHandler<StudioCombinationChangedEventArgs> CombinationChanged;
        
        public string Key => _key;
        public string Name => "Studio Combination Manager";
        
        public StudioCombinationType CombinationType => _combinationType;
        public IReadOnlyList<MusicStudioUnit> CombinedMSUs => _combinedMSUs.AsReadOnly();
        public bool IsCombined => _combinationType != StudioCombinationType.Single;
        public bool IsMaster => _combinationType != StudioCombinationType.Single;
        
        public StudioCombinationManager(string key, string msuUID, int xCoord, int yCoord, byte hvacZoneId, Dictionary<string, MusicStudioUnit> allMSUs)
        {
            _key = key;
            _msuUID = msuUID;
            _xCoord = xCoord;
            _yCoord = yCoord;
            _hvacZoneId = hvacZoneId;
            _allMSUs = allMSUs;
            
            // Register with device manager
            DeviceManager.AddDevice(key, this);
        }
        
        public bool CanCombineWithAdjacentMSUs(StudioCombinationType type)
        {
            if (IsCombined && !IsMaster)
            {
                // This MSU is already part of a combination but not the master
                return false;
            }
            
            // Get adjacent MSUs
            var adjacentMSUs = GetAdjacentMSUs();
            
            // Check if we have enough adjacent MSUs for the requested combination
            int requiredMSUs = (type == StudioCombinationType.Mega) ? 1 : 2;
            
            if (adjacentMSUs.Count < requiredMSUs)
            {
                return false;
            }
            
            // Check if any of the adjacent MSUs are already combined or in use
            foreach (var msu in adjacentMSUs)
            {
                if (msu.IsInUse || msu.IsCombined)
                {
                    return false;
                }
            }
            
            return true;
        }
        
        public bool CombineStudios(StudioCombinationType type)
        {
            if (!CanCombineWithAdjacentMSUs(type))
            {
                Debug.Console(0, this, "Cannot combine studios - prerequisites not met");
                return false;
            }
            
            try
            {
                // Get adjacent MSUs
                var adjacentMSUs = GetAdjacentMSUs();
                
                // Clear current combination
                _combinedMSUs.Clear();
                
                // Add this MSU as the master
                _combinedMSUs.Add(new MusicStudioUnit
                {
                    UID = _msuUID,
                    XCoord = _xCoord,
                    YCoord = _yCoord,
                    HVACZoneId = _hvacZoneId,
                    IsMaster = true
                });
                
                // Add required number of adjacent MSUs
                int requiredMSUs = (type == StudioCombinationType.Mega) ? 1 : 2;
                for (int i = 0; i < requiredMSUs; i++)
                {
                    if (i < adjacentMSUs.Count)
                    {
                        _combinedMSUs.Add(adjacentMSUs[i]);
                        adjacentMSUs[i].IsCombined = true;
                    }
                }
                
                // Update combination type
                _combinationType = type;
                
                // Notify listeners
                CombinationChanged?.Invoke(this, new StudioCombinationChangedEventArgs
                {
                    CombinationType = type,
                    CombinedMSUs = _combinedMSUs.ToList()
                });
                
                Debug.Console(1, this, "Studios combined successfully - Type: {0}, Count: {1}", type, _combinedMSUs.Count);
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error combining studios: {0}", ex.Message);
                return false;
            }
        }
        
        public bool UncombineStudios()
        {
            if (!IsCombined)
            {
                Debug.Console(1, this, "Studios are not currently combined");
                return true;
            }
            
            try
            {
                // Notify all combined MSUs that they are no longer combined
                foreach (var msu in _combinedMSUs)
                {
                    if (!msu.IsMaster)
                    {
                        msu.IsCombined = false;
                    }
                }
                
                // Clear the combined list except for this MSU
                _combinedMSUs.Clear();
                _combinedMSUs.Add(new MusicStudioUnit
                {
                    UID = _msuUID,
                    XCoord = _xCoord,
                    YCoord = _yCoord,
                    HVACZoneId = _hvacZoneId,
                    IsMaster = false
                });
                
                // Update combination type
                _combinationType = StudioCombinationType.Single;
                
                // Notify listeners
                CombinationChanged?.Invoke(this, new StudioCombinationChangedEventArgs
                {
                    CombinationType = StudioCombinationType.Single,
                    CombinedMSUs = _combinedMSUs.ToList()
                });
                
                Debug.Console(1, this, "Studios uncombined successfully");
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error uncombining studios: {0}", ex.Message);
                return false;
            }
        }
        
        private List<MusicStudioUnit> GetAdjacentMSUs()
        {
            var adjacentMSUs = new List<MusicStudioUnit>();
            
            // Check north
            string northKey = $"{_xCoord},{_yCoord+1}";
            if (_allMSUs.ContainsKey(northKey))
            {
                adjacentMSUs.Add(_allMSUs[northKey]);
            }
            
            // Check south
            string southKey = $"{_xCoord},{_yCoord-1}";
            if (_allMSUs.ContainsKey(southKey))
            {
                adjacentMSUs.Add(_allMSUs[southKey]);
            }
            
            // Check east
            string eastKey = $"{_xCoord+1},{_yCoord}";
            if (_allMSUs.ContainsKey(eastKey))
            {
                adjacentMSUs.Add(_allMSUs[eastKey]);
            }
            
            // Check west
            string westKey = $"{_xCoord-1},{_yCoord}";
            if (_allMSUs.ContainsKey(westKey))
            {
                adjacentMSUs.Add(_allMSUs[westKey]);
            }
            
            return adjacentMSUs;
        }
    }
    
    public enum StudioCombinationType
    {
        Single,   // Default - only one unit
        Mega,     // Two adjoining units
        Monster   // Three adjoining units
    }
    
    public class MusicStudioUnit
    {
        public string UID { get; set; }
        public string Name { get; set; }
        public string MAC { get; set; }
        public int XCoord { get; set; }
        public int YCoord { get; set; }
        public byte HVACZoneId { get; set; }
        public bool IsInUse { get; set; }
        public bool IsCombined { get; set; }
        public bool IsMaster { get; set; }
    }
    
    public class StudioCombinationChangedEventArgs : EventArgs
    {
        public StudioCombinationType CombinationType { get; set; }
        public List<MusicStudioUnit> CombinedMSUs { get; set; }
    }
}