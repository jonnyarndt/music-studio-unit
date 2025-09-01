namespace flexpod
{
    internal class TouchPanelJoins
    {

        internal enum Digital
        {
            NotificationBanner = 60,
            PanelWidePopUpClose = 130,
            FooterPageNext = 201,
            FooterPagePrevious = 202
        }

        internal enum Analog
        {
            CrewAnnouncementStatus = 201,
            OxygenStatus = 202,
            CabinStatus = 203,
            FlightStatus = 204,
            SeatbeltStatus = 205,
            RestroomLeftStatus = 206,
            RestroomRightStatus = 207
        }

        internal enum Serial
        {
            NotificationBannerMessage = 60,
            PinKeypadEntryText = 111
        }

        internal enum SmartObject
        {
            PassengerGroup = 101,  // Dynamic List
            FlightTelemetry = 102, // Dynamic List
            MediaBrowser = 104,    // Dynamic List
            Keypad = 111           // Keypad
        }

        internal enum Pages
        {
            Startup = 101,
            Telemetry = 102,
            Lighting = 103,
            MediaRouter = 104,
            MediaPlayer = 105,
            Game = 106
        }

        internal enum PanelWidePopUps
        {
            ResetAll = 130,
            Signin = 131,
            ReliefStation = 132
        }

        internal enum TopBarIconMap
        {
            GreyedOutFlightStatusInFlight = 0,
            GreyedOutHeadphones = 1,
            GreyedOutLock = 2,
            GreyedOutBlankInactive = 3,
            GreyedOutRestroom = 4,

            OrangeFlightStatusOnGround = 5,
            OrangeMessage = 6,
            OrangeLock = 7,
            OrangeBlank = 8,

            GreenHeadphones = 9,
            GreenKey = 10,
            GreenPersonWalking = 11,
            GreenBlank = 12,
            GreenRestroom = 13,

            RedBlank = 14,
            RedSeatbeltsRequired = 15,
            RedOxygenRequired = 16,
            RedRestroom = 17,

            BlueFlightStatusInFlight = 18,
            BlueBlank = 19,
            BlueRestroom = 20,

            GrayedOutBlankActive = 21,
            DarkTransparentNoBorder = 22
        }
    }
}
