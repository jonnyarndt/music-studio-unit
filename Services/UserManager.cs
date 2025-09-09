using System;
using Crestron.SimplSharp;
using core_tools;

namespace musicStudioUnit.Services
{
    /// <summary>
    /// User Management Service for handling user login and ID lookup
    /// </summary>
    public class UserManager : IKeyName, IDisposable
    {
        private readonly string _key;
        private readonly object _userLibrary; // SIMPL# User Library instance
        private UserInfo _currentUser;
        private bool _isUserLoggedIn;

        public string Key => _key;
        public string Name => "User Manager";

        // Properties
        public bool IsUserLoggedIn => _isUserLoggedIn;
        public UserInfo CurrentUser => _currentUser;

        // Events
        public event EventHandler<UserLoginEventArgs> UserLoggedIn;
        public event EventHandler UserLoggedOut;

        public UserManager(string key)
        {
            _key = key;
            DeviceManager.AddDevice(key, this);

            // TODO: Initialize SIMPL# User Library
            // _userLibrary = new USER(); // This would be the actual SIMPL# library

            Debug.Console(1, this, "User Manager initialized");
        }

        /// <summary>
        /// Attempt to login a user with their ID
        /// </summary>
        public bool LoginUser(int userId)
        {
            Debug.Console(1, this, "Attempting to login user ID: {0}", userId);

            try
            {
                // Validate user ID range
                if (userId < 1 || userId > 60000)
                {
                    Debug.Console(0, this, "User ID {0} is out of valid range (1-60000)", userId);
                    return false;
                }

                // Lookup user in database
                var userInfo = LookupUser(userId);
                if (userInfo != null && !string.IsNullOrEmpty(userInfo.Name))
                {
                    _currentUser = userInfo;
                    _isUserLoggedIn = true;

                    Debug.Console(1, this, "User logged in: {0} (ID: {1})", userInfo.Name, userId);

                    // Fire login event
                    UserLoggedIn?.Invoke(this, new UserLoginEventArgs
                    {
                        User = userInfo,
                        IsBirthday = IsTodayUsersBirthday(userInfo)
                    });

                    return true;
                }
                else
                {
                    Debug.Console(0, this, "User ID {0} not found or invalid", userId);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error logging in user {0}: {1}", userId, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Logout the current user
        /// </summary>
        public void LogoutUser()
        {
            if (_isUserLoggedIn)
            {
                Debug.Console(1, this, "Logging out user: {0}", _currentUser?.Name ?? "Unknown");
                
                _currentUser = null;
                _isUserLoggedIn = false;

                // Fire logout event
                UserLoggedOut?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Continue as guest (no login required)
        /// </summary>
        public void ContinueAsGuest()
        {
            Debug.Console(1, this, "User continuing as guest");
            
            _currentUser = null;
            _isUserLoggedIn = false;
            
            // Could fire a guest session event if needed
        }

        /// <summary>
        /// Check if today is the user's birthday
        /// </summary>
        public bool IsTodayUsersBirthday(UserInfo user)
        {
            if (user?.BirthDate == null) return false;

            var today = DateTime.Now;
            var birthDate = user.BirthDate.Value;

            return today.Month == birthDate.Month && today.Day == birthDate.Day;
        }

        /// <summary>
        /// Get formatted birthday display string
        /// </summary>
        public string GetBirthdayDisplayString(UserInfo user)
        {
            if (user?.BirthDate == null) return string.Empty;

            return user.BirthDate.Value.ToString("yyyyMMdd");
        }

        /// <summary>
        /// Lookup user information by ID using SIMPL# library
        /// </summary>
        private UserInfo LookupUser(int userId)
        {
            try
            {
                // TODO: Implement actual SIMPL# library call
                // This is where you would call the USER library LookupUID function
                // and register for the UserName delegate callback
                
                // Placeholder implementation for now
                // In real implementation, this would be async with callback
                
                // Example of what the real implementation might look like:
                // _userLibrary.LookupUID(userId);
                // Wait for callback or use event-based pattern
                
                // For now, return a placeholder
                return new UserInfo
                {
                    Id = userId,
                    Name = "Test User " + userId,
                    BirthDate = DateTime.Now.AddYears(-25) // Placeholder
                };
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error looking up user {0}: {1}", userId, ex.Message);
                return null;
            }
        }

        public void Dispose()
        {
            // Cleanup user library if needed
        }
    }

    /// <summary>
    /// User information data class
    /// </summary>
    public class UserInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public DateTime? BirthDate { get; set; }
    }

    /// <summary>
    /// Event arguments for user login
    /// </summary>
    public class UserLoginEventArgs : EventArgs
    {
        public UserInfo User { get; set; }
        public bool IsBirthday { get; set; }
    }
}
