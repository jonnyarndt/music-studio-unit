
using System;
using core_tools;
using Crestron.SimplSharp;
using musicStudioUnit.Services;

namespace musicStudioUnit
{
    public class UserManager : IKeyName
    {
        private readonly string _key;
        private readonly UserDatabase _userDatabase;
        private int _currentUserId;
        private string _currentUserName;
        private DateTime _currentUserBirthday;
        private bool _isLoggedIn;
        private bool _isBirthday;
        
        public event EventHandler<UserLoginEventArgs> UserLoggedIn;
        public event EventHandler UserLoggedOut;
        
        public string Key => _key;
        public string Name => "User Manager";
        
        public bool IsLoggedIn => _isLoggedIn;
        public int CurrentUserId => _currentUserId;
        public string CurrentUserName => _currentUserName;
        public DateTime CurrentUserBirthday => _currentUserBirthday;
        public bool IsBirthday => _isBirthday;
        
        public UserManager(string key)
        {
            _key = key;
            _userDatabase = new UserDatabase();
            
            // Register with device manager
            DeviceManager.AddDevice(key, this);
            
            // Register callback for user data
            // TODO: Fix UserDatabase API - UserDataReceived event does not exist
            // _userDatabase.UserDataReceived += OnUserDataReceived;
        }
        
        public void Login(int userId)
        {
            if (userId < 1 || userId > 60000)
            {
                Debug.Console(0, this, "Invalid user ID: {0}. Must be between 1 and 60000.", userId);
                return;
            }
            
            Debug.Console(1, this, "Looking up user ID: {0}", userId);
            
            _currentUserId = userId;
            // TODO: Fix UserDatabase API - LookupUID method does not exist  
            // _userDatabase.LookupUID(userId);
        }
        
        public void Logout()
        {
            if (!_isLoggedIn)
            {
                Debug.Console(1, this, "No user is currently logged in");
                return;
            }
            
            Debug.Console(1, this, "Logging out user: {0}", _currentUserName);
            
            _isLoggedIn = false;
            _currentUserId = 0;
            _currentUserName = "";
            _currentUserBirthday = DateTime.MinValue;
            _isBirthday = false;
            
            UserLoggedOut?.Invoke(this, EventArgs.Empty);
        }
        
        private void OnUserDataReceived(string userData)
        {
            try
            {
                Debug.Console(2, this, "User data received: {0}", userData);
                
                // Parse user data (format: "Name||yyyymmdd")
                string[] parts = userData.Split(new[] { "||" }, StringSplitOptions.None);
                
                if (parts.Length == 2)
                {
                    _currentUserName = parts[0];
                    
                    // Parse birthday
                    if (DateTime.TryParseExact(parts[1], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime birthday))
                    {
                        _currentUserBirthday = birthday;
                        
                        // Check if today is user's birthday
                        DateTime today = DateTime.Today;
                        _isBirthday = (today.Month == birthday.Month && today.Day == birthday.Day);
                        
                        _isLoggedIn = true;
                        
                        Debug.Console(1, this, "User logged in: {0}, Birthday: {1}, Is Birthday: {2}", 
                            _currentUserName, _currentUserBirthday.ToString("yyyy-MM-dd"), _isBirthday);
                        
                        UserLoggedIn?.Invoke(this, new UserLoginEventArgs
                        {
                            UserId = _currentUserId,
                            UserName = _currentUserName,
                            Birthday = _currentUserBirthday,
                            IsBirthday = _isBirthday
                        });
                    }
                    else
                    {
                        Debug.Console(0, this, "Failed to parse birthday: {0}", parts[1]);
                    }
                }
                else
                {
                    Debug.Console(0, this, "Invalid user data format: {0}", userData);
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error processing user data: {0}", ex.Message);
            }
        }
    }
    
    public class UserLoginEventArgs : EventArgs
    {
        public int UserId { get; set; }
        public string UserName { get; set; }
        public DateTime Birthday { get; set; }
        public bool IsBirthday { get; set; }
    }
}