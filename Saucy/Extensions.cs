using Dalamud.Bindings.ImGui;

namespace Saucy;
public static unsafe class Extensions
{
    public static bool PassFilterBool(this ImGuiTextFilterPtr self, ImU8String text)
    {
        var ret = false;
        fixed (byte* textPtr = text)
            ret = ImGuiNative.PassFilter(self.Handle, textPtr, textPtr + text.Length) != 0;
        text.Recycle();
        return ret;
    }
}
