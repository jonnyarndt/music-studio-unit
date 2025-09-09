using System;
using System.Text;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.DeviceSupport;
using core_tools;
using musicStudioUnit.Passengers;

namespace musicStudioUnit.UserInterface
{
    /// <summary>
    /// User Login Screen UI Handler for MSU
    /// Provides keypad for user ID entry and displays user information per Client-Scope.md
    /// Integrates with User ID Database (Appendix A)
    /// </summary>
    public class UserLoginScreenUI : IDisposable
    {
        private readonly BasicTriList _panel;
        private readonly StringBuilder _userIdBuilder = new StringBuilder(5); // Max 5 digits for user IDs up to 60,000
        private readonly LoyaltyID _userDatabase;
        
        private bool _isLoggedIn = false;
        private int _currentUserId = 0;
        private string _currentUserName = string.Empty;
        private string _currentUserBirthdate = string.Empty;
        private bool _disposed = false;

        // Events
        public event EventHandler<UserLoginEventArgs> UserLoggedIn;
        public event EventHandler<UserLogoutEventArgs> UserLoggedOut;
        public event EventHandler GuestModeActivated;

        public bool IsLoggedIn => _isLoggedIn;
        public int CurrentUserId => _currentUserId;
        public string CurrentUserName => _currentUserName;

        public UserLoginScreenUI(BasicTriList panel, LoyaltyID userDatabase)
        {
            _panel = panel ?? throw new ArgumentNullException(nameof(panel));
            _userDatabase = userDatabase ?? throw new ArgumentNullException(nameof(userDatabase));

            Debug.Console(1, "UserLoginScreenUI", "Initializing User Login screen UI");

            // Setup event handlers
            SetupTouchPanelEvents();

            // Setup user database event handler
            _userDatabase.UserName += OnUserDatabaseResponse;

            // Initialize UI display
            UpdateLoginDisplay();

            Debug.Console(1, "UserLoginScreenUI", "User Login screen UI initialized successfully");
        }

        /// <summary>
        /// Setup touch panel button events
        /// </summary>
        private void SetupTouchPanelEvents()
        {
            _panel.SigChange += (device, args) =>
            {
                if (!args.Sig.BoolValue) return; // Only handle button press (true)

                switch (args.Sig.Number)
                {
                    // Keypad digits (0-9)
                    case (uint)MSUTouchPanelJoins.UserScreen.KeypadDigit0:
                        OnKeypadDigitPressed('0');
                        break;
                    case (uint)MSUTouchPanelJoins.UserScreen.KeypadDigit1:
                        OnKeypadDigitPressed('1');
                        break;
                    case (uint)MSUTouchPanelJoins.UserScreen.KeypadDigit2:
                        OnKeypadDigitPressed('2');
                        break;
                    case (uint)MSUTouchPanelJoins.UserScreen.KeypadDigit3:
                        OnKeypadDigitPressed('3');
                        break;
                    case (uint)MSUTouchPanelJoins.UserScreen.KeypadDigit4:
                        OnKeypadDigitPressed('4');
                        break;
                    case (uint)MSUTouchPanelJoins.UserScreen.KeypadDigit5:
                        OnKeypadDigitPressed('5');
                        break;
                    case (uint)MSUTouchPanelJoins.UserScreen.KeypadDigit6:
                        OnKeypadDigitPressed('6');
                        break;
                    case (uint)MSUTouchPanelJoins.UserScreen.KeypadDigit7:
                        OnKeypadDigitPressed('7');
                        break;
                    case (uint)MSUTouchPanelJoins.UserScreen.KeypadDigit8:
                        OnKeypadDigitPressed('8');
                        break;
                    case (uint)MSUTouchPanelJoins.UserScreen.KeypadDigit9:
                        OnKeypadDigitPressed('9');
                        break;

                    // Control buttons
                    case (uint)MSUTouchPanelJoins.UserScreen.LoginButton:
                        OnLoginButtonPressed();
                        break;
                    case (uint)MSUTouchPanelJoins.UserScreen.LogoutButton:
                        OnLogoutButtonPressed();
                        break;
                    case (uint)MSUTouchPanelJoins.UserScreen.GuestModeButton:
                        OnGuestModeButtonPressed();
                        break;
                    case (uint)MSUTouchPanelJoins.UserScreen.ClearButton:
                        OnClearButtonPressed();
                        break;
                }
            };

            Debug.Console(2, "UserLoginScreenUI", "Touch panel events configured");
        }

        /// <summary>
        /// Handle keypad digit press
        /// </summary>
        private void OnKeypadDigitPressed(char digit)
        {
            if (_isLoggedIn) return; // Don't accept input when logged in

            try
            {
                // Limit to 5 digits (max user ID is 60,000)
                if (_userIdBuilder.Length < 5)
                {
                    _userIdBuilder.Append(digit);
                    UpdateUserIdDisplay();
                    Debug.Console(2, "UserLoginScreenUI", "Digit entered: {0}, Current ID: {1}", 
                        digit, _userIdBuilder.ToString());
                }
                else
                {
                    Debug.Console(1, "UserLoginScreenUI", "Maximum ID length reached");
                    UpdateLoginStatus("Maximum 5 digits allowed");
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, "UserLoginScreenUI", "Error handling keypad digit: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Handle login button press
        /// </summary>
        private void OnLoginButtonPressed()
        {
            if (_isLoggedIn) return; // Already logged in

            try
            {
                string userIdText = _userIdBuilder.ToString();
                if (string.IsNullOrEmpty(userIdText))
                {
                    UpdateLoginStatus("Please enter a User ID");
                    return;
                }

                if (!int.TryParse(userIdText, out int userId))
                {
                    UpdateLoginStatus("Invalid User ID format");
                    return;
                }

                // Validate ID range (1 to 60,000 per Client-Scope.md)
                if (userId < 1 || userId > 60000)
                {
                    UpdateLoginStatus("User ID must be between 1 and 60,000");
                    return;
                }

                Debug.Console(1, "UserLoginScreenUI", "Login requested for User ID: {0}", userId);
                UpdateLoginStatus("Looking up user...");

                // Query user database
                _userDatabase.LookupUID(userId);
            }
            catch (Exception ex)
            {
                Debug.Console(0, "UserLoginScreenUI", "Error handling login: {0}", ex.Message);
                UpdateLoginStatus("Login error occurred");
            }
        }

        /// <summary>
        /// Handle logout button press
        /// </summary>
        private void OnLogoutButtonPressed()
        {
            if (!_isLoggedIn) return; // Not logged in

            try
            {
                Debug.Console(1, "UserLoginScreenUI", "User logout requested");
                
                // Clear user data
                _isLoggedIn = false;
                _currentUserId = 0;
                _currentUserName = string.Empty;
                _currentUserBirthdate = string.Empty;
                _userIdBuilder.Clear();

                // Update UI
                UpdateLoginDisplay();
                UpdateLoginStatus("Logged out successfully");

                // Notify listeners
                UserLoggedOut?.Invoke(this, new UserLogoutEventArgs());

                Debug.Console(1, "UserLoginScreenUI", "User logged out successfully");
            }
            catch (Exception ex)
            {
                Debug.Console(0, "UserLoginScreenUI", "Error handling logout: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Handle guest mode button press
        /// </summary>
        private void OnGuestModeButtonPressed()
        {
            try
            {
                Debug.Console(1, "UserLoginScreenUI", "Guest mode activated");
                
                // Clear any entered data
                _userIdBuilder.Clear();
                UpdateUserIdDisplay();
                UpdateLoginStatus("Continuing as guest");

                // Notify listeners
                GuestModeActivated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.Console(0, "UserLoginScreenUI", "Error handling guest mode: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Handle clear button press
        /// </summary>
        private void OnClearButtonPressed()
        {
            try
            {
                _userIdBuilder.Clear();
                UpdateUserIdDisplay();
                UpdateLoginStatus("ID cleared");
                Debug.Console(2, "UserLoginScreenUI", "User ID entry cleared");
            }
            catch (Exception ex)
            {
                Debug.Console(0, "UserLoginScreenUI", "Error handling clear: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Handle user database response
        /// </summary>
        private void OnUserDatabaseResponse(string response)
        {
            try
            {
                Debug.Console(2, "UserLoginScreenUI", "User database response: {0}", response);

                // Parse response: "Douglas Fitzgerald||19670701"
                if (string.IsNullOrEmpty(response))
                {
                    UpdateLoginStatus("User lookup failed");
                    return;
                }

                string[] parts = response.Split(new string[] { "||" }, StringSplitOptions.None);
                if (parts.Length != 2)
                {
                    Debug.Console(0, "UserLoginScreenUI", "Invalid database response format: {0}", response);
                    UpdateLoginStatus("Invalid user data received");
                    return;
                }

                string userName = parts[0].Trim();
                string birthdate = parts[1].Trim();

                // Validate data
                if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(birthdate))
                {
                    UpdateLoginStatus("Invalid user data");
                    return;
                }

                // Check if this is a valid user (not random characters for out-of-range IDs)
                if (userName.Length < 3 || !IsValidBirthdate(birthdate))
                {
                    UpdateLoginStatus("User ID not found");
                    return;
                }

                // Login successful
                _isLoggedIn = true;
                _currentUserId = int.Parse(_userIdBuilder.ToString());
                _currentUserName = userName;
                _currentUserBirthdate = birthdate;

                // Update UI
                UpdateLoginDisplay();
                UpdateLoginStatus($"Welcome, {userName}!");

                // Check for birthday
                CheckBirthday(birthdate);

                // Notify listeners
                UserLoggedIn?.Invoke(this, new UserLoginEventArgs(_currentUserId, userName, birthdate));

                Debug.Console(1, "UserLoginScreenUI", "User logged in successfully: {0} (ID: {1})", 
                    userName, _currentUserId);
            }
            catch (Exception ex)
            {
                Debug.Console(0, "UserLoginScreenUI", "Error handling user database response: {0}", ex.Message);
                UpdateLoginStatus("Login processing error");
            }
        }

        /// <summary>
        /// Validate birthdate format (yyyymmdd)
        /// </summary>
        private bool IsValidBirthdate(string birthdate)
        {
            if (string.IsNullOrEmpty(birthdate) || birthdate.Length != 8)
                return false;

            return DateTime.TryParseExact(birthdate, "yyyyMMdd", 
                System.Globalization.CultureInfo.InvariantCulture, 
                System.Globalization.DateTimeStyles.None, out _);
        }

        /// <summary>
        /// Check if today is the user's birthday
        /// </summary>
        private void CheckBirthday(string birthdate)
        {
            try
            {
                if (DateTime.TryParseExact(birthdate, "yyyyMMdd", 
                    System.Globalization.CultureInfo.InvariantCulture, 
                    System.Globalization.DateTimeStyles.None, out DateTime userBirthday))
                {
                    var today = DateTime.Today;
                    bool isBirthday = (today.Month == userBirthday.Month && today.Day == userBirthday.Day);
                    
                    _panel.BooleanInput[(uint)MSUTouchPanelJoins.UserScreen.BirthdayIndicator].BoolValue = isBirthday;
                    
                    if (isBirthday)
                    {
                        string birthdayMessage = "HAPPY BIRTHDAY";
                        _panel.StringInput[(uint)MSUTouchPanelJoins.UserScreen.BirthdayMessage].StringValue = birthdayMessage;
                        Debug.Console(1, "UserLoginScreenUI", "Birthday detected for user: {0}", _currentUserName);
                    }
                    else
                    {
                        _panel.StringInput[(uint)MSUTouchPanelJoins.UserScreen.BirthdayMessage].StringValue = string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, "UserLoginScreenUI", "Error checking birthday: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Update user ID entry display
        /// </summary>
        private void UpdateUserIdDisplay()
        {
            string displayText = _userIdBuilder.ToString();
            _panel.StringInput[(uint)MSUTouchPanelJoins.UserScreen.UserIDEntryText].StringValue = displayText;
        }

        /// <summary>
        /// Update login display based on current state
        /// </summary>
        private void UpdateLoginDisplay()
        {
            try
            {
                // Update logged in indicator
                _panel.BooleanInput[(uint)MSUTouchPanelJoins.UserScreen.LoggedInIndicator].BoolValue = _isLoggedIn;

                if (_isLoggedIn)
                {
                    // Show user information
                    _panel.StringInput[(uint)MSUTouchPanelJoins.UserScreen.UserNameText].StringValue = _currentUserName;
                    _panel.StringInput[(uint)MSUTouchPanelJoins.UserScreen.UserBirthdateText].StringValue = _currentUserBirthdate;
                    
                    // Clear ID entry
                    _panel.StringInput[(uint)MSUTouchPanelJoins.UserScreen.UserIDEntryText].StringValue = string.Empty;
                }
                else
                {
                    // Clear user information
                    _panel.StringInput[(uint)MSUTouchPanelJoins.UserScreen.UserNameText].StringValue = string.Empty;
                    _panel.StringInput[(uint)MSUTouchPanelJoins.UserScreen.UserBirthdateText].StringValue = string.Empty;
                    _panel.StringInput[(uint)MSUTouchPanelJoins.UserScreen.BirthdayMessage].StringValue = string.Empty;
                    _panel.BooleanInput[(uint)MSUTouchPanelJoins.UserScreen.BirthdayIndicator].BoolValue = false;
                    
                    // Update ID entry display
                    UpdateUserIdDisplay();
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, "UserLoginScreenUI", "Error updating login display: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Update login status message
        /// </summary>
        private void UpdateLoginStatus(string status)
        {
            _panel.StringInput[(uint)MSUTouchPanelJoins.UserScreen.LoginStatusText].StringValue = status;
            Debug.Console(2, "UserLoginScreenUI", "Login status: {0}", status);
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // Unsubscribe from user database events
                if (_userDatabase != null)
                {
                    _userDatabase.UserName -= OnUserDatabaseResponse;
                }

                Debug.Console(1, "UserLoginScreenUI", "User Login screen UI disposed");
            }
            catch (Exception ex)
            {
                Debug.Console(0, "UserLoginScreenUI", "Error disposing: {0}", ex.Message);
            }
            finally
            {
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Event arguments for user login
    /// </summary>
    public class UserLoginEventArgs : EventArgs
    {
        public int UserId { get; }
        public string UserName { get; }
        public string Birthdate { get; }

        public UserLoginEventArgs(int userId, string userName, string birthdate)
        {
            UserId = userId;
            UserName = userName;
            Birthdate = birthdate;
        }
    }

    /// <summary>
    /// Event arguments for user logout
    /// </summary>
    public class UserLogoutEventArgs : EventArgs
    {
        public DateTime LogoutTime { get; }

        public UserLogoutEventArgs()
        {
            LogoutTime = DateTime.Now;
        }
    }
}
