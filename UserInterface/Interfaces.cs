using Crestron.SimplSharpPro.DeviceSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace musicStudioUnit
{
    internal interface IHasBasicTriListWithSmartObject
    {
        BasicTriListWithSmartObject Panel { get; }
    }
}
