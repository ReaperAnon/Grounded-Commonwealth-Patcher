using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.WPF.Reflection.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GroundedCommonwealthPatcher.Settings
{
    public class GCConfig
    {
        [Tooltip("If turned off, looks will not be transferred and the option below won't do anything. In case you have your own visual overhauls or you want to do things by hand.")]
        public bool TransferLooks { get; set; } = true;

        [Tooltip("Transfers all visual changes from Grounded Commonwealth and overrides any other looks mods. If disabled only NPCs whose racial features were changed will be transfered.")]
        public bool FullLooksTransfer { get; set; } = true;
    }
}
