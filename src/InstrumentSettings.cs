using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace instruments
{
    public class InstrumentSettings
    {
        public bool enabled { get; set; } = true;
        public float playerVolume { get; set; } = 0.7f;
        public float blockVolume { get; set; } = 1.0f;

    }
}