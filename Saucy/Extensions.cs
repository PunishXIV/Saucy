using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Saucy;
public unsafe static class Extensions
{
    public static bool PassFilterBool(this ImGuiTextFilterPtr self, ImU8String text)
    {
        bool ret = false;
        fixed(byte* textPtr = text)
            ret = ImGuiNative.PassFilter(self.Handle, textPtr, textPtr + text.Length) != 0;
        text.Dispose();
        return ret;
    }
}