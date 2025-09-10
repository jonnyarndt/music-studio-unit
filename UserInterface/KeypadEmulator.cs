using System.Text;

namespace musicStudioUnit
{
    /// <summary>
    /// Emaulte a keypad with number, enter, clear, and backspace buttons.
    /// </summary>
    internal class KeypadEmulator
    {
        private StringBuilder _inputString;
        private uint _result;

        /// <summary>
        /// Feedback of when the KeypadEmulator's result changes.
        /// </summary>
        internal event EventHandler<uint> KeypadResultChanged;

        /// <summary>
        /// UNIT value of the result of the keypad emulator.
        /// </summary>
        internal uint Result
        {
            get => _result;
            private set
            {
                if (_result != value)
                {
                    _result = value;
                    OnResultChanged(_result);
                }
            }
        }

        /// <summary>
        /// Keypad Emulator's current value as a string.
        /// </summary>
        internal string OutputString { get; private set; }

        /// <summary>
        /// Default contructor for KeypadEmulator
        /// </summary>
        internal KeypadEmulator()
        {
            _inputString = new StringBuilder();
            Result = 0;
            OutputString = string.Empty;
        }

        protected virtual void OnResultChanged(uint newResult)
        {
            KeypadResultChanged?.Invoke(this, newResult);
        }

        internal void Number(int number)
        {
            if (number < 0 || number > 9)
                throw new ArgumentOutOfRangeException(nameof(number), "Number must be between 0 and 9.");

            _inputString.Append(number);
            UpdateResult();
        }

        internal void Enter()
        {
            UpdateResult();
            // 1 second wait, then clear input string
            System.Threading.Thread.Sleep(1000);
            Clear();
        }

        internal void Clear()
        {
            _inputString.Clear();
            UpdateResult();
        }

        internal void Backspace()
        {
            if (_inputString.Length > 0)
            {
                _inputString.Length--;
                UpdateResult();
            }
        }

        private void UpdateResult()
        {
            OutputString = _inputString.ToString();
            if (uint.TryParse(OutputString, out uint result))
            {
                Result = result;
            }
            else
            {
                Result = 0;
            }
        }
    }
}
