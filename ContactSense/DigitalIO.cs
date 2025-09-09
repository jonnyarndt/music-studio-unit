using Crestron.SimplSharpPro;
using core_tools;
using Crestron.SimplSharp.Net;
using System.Windows.Input;
using PepperDash.Core;
using System;

using PepperDash.Core;
namespace musicStudioUnit
{
    internal class DigitalIO : IDisposable
    {
        private bool alreadyDisposed;
        internal bool DigitalInput01State { get; private set; }
        internal bool DigitalInput02State { get; private set; }
        internal DigitalInput digitalInput01;
        internal DigitalInput digitalInput02;
        internal CrestronCollection<DigitalInput> DigitalInputPorts { get; }

        /// <summary>
        /// Constructor for the class
        /// </summary>
        internal DigitalIO()
        {
            // Initialize the DigitalInputPorts collection for ports
            digitalInput01 = Global.ControlSystem.DigitalInputPorts[1];
            digitalInput02 = Global.ControlSystem.DigitalInputPorts[2];

            // Subscribe to the Digital input events to know when the state has changed
            digitalInput01.StateChange += new DigitalInputEventHandler(InputPort_StateChange);
            digitalInput02.StateChange += new DigitalInputEventHandler(InputPort_StateChange);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Part of the implementation of the IDisposable pattern
        /// </summary>
        /// <param name="disposing">indicates whether the method is being called from the Dispose method (with disposing set to true) or from the finalizer (with disposing set to false).</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!alreadyDisposed)
            {
                if (disposing)
                {
                    // Dispose other managed resources here
                    // Example: if you have other IDisposable fields, dispose them here
                }
            }

            // Dispose unmanaged resources here, meaning Release them explicitly (e.g., close file handles, free memory)
            // Unmanaged Resources would include:
            // - COM objects that are not part of the .NET Framework
            // - Pointers to blocks of memory created using local memory allocation
            // - Handles to OS resources such as files, devices, GDI objects, etc.
            // - Subscriptions to external system events
            // - Anything that implements IDisposable
            // - Network connections, database connections
            alreadyDisposed = true;
        }

        /// <summary>
        /// Destructor for FlightTelemetry. Ensures unmanaged resources are released if the object is finalized.
        /// </summary>
        /// <comment>
        /// Method calls the local protected virtual void `Dispose` method
        /// </comment>
        ~DigitalIO() { Dispose(false); }


        /// <summary>
        /// DigitalInput event handler
        /// </summary>
        /// <param name="digitalInput"></param>
        /// <param name="args"></param>
        /// <url cref="https://help.crestron.com/SimplSharp/html/T_Crestron_SimplSharpPro_DigitalInputEventHandler.htm"/url>
        internal void InputPort_StateChange(DigitalInput digitalInput, DigitalInputEventArgs args)
        {
            OnDigitalInputChanged(digitalInput.ID, args.State);
        }

        /// <summary>
        /// Callback when the state of a digital input port changes
        /// </summary>
        /// <param name="port"></param>
        /// <param name="state"></param>
        /// <url cref="https://help.crestron.com/SimplSharp/html/T_Crestron_SimplSharpPro_DigitalInputEventHandler.htm"/url>
        internal void OnDigitalInputChanged(uint port, bool state)
        {
            switch (port)
            {
                case 1:
                    DigitalInput01State = state;
                    Global.Occupied = state;
                    Debug.Console(2, "DigitalIO", "Digital Input-1->{0}", state);
                    break;
                case 2:
                    DigitalInput02State = state;
                    Debug.Console(2, "DigitalIO", "Digital Input-2->{0}", state);
                    // New file is ready to be read in
                    Debug.Console(1, "Occupancy State: {0}", state);
                    break;
                default:
                    break;
            }
        }
    }
}
