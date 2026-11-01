using System.Runtime.InteropServices;
using DHud.Helpers;
using Xunit;

namespace DHud.Tests.Unit;

/// <summary>Win+D ile küçültülen/gizlenen widget'ların geri getirilmesi için hangi iletilerin "kayıp" sayıldığı.</summary>
[Trait(Cat.Name, Cat.Fast)]
public class ShowDesktopGuardTests
{
    private const int WM_SIZE = 0x0005;
    private const int WM_SHOWWINDOW = 0x0018;
    private const int WM_WINDOWPOSCHANGED = 0x0047;

    [Fact]
    public void Minimize_is_a_loss_but_restore_and_maximize_are_not()
    {
        Assert.True(ShowDesktopGuard.IsLost(WM_SIZE, 1, IntPtr.Zero));
        Assert.False(ShowDesktopGuard.IsLost(WM_SIZE, 0, IntPtr.Zero));   // SIZE_RESTORED
        Assert.False(ShowDesktopGuard.IsLost(WM_SIZE, 2, IntPtr.Zero));   // SIZE_MAXIMIZED
    }

    [Fact]
    public void Hiding_because_of_another_window_is_a_loss_but_an_explicit_hide_or_a_show_is_not()
    {
        Assert.True(ShowDesktopGuard.IsLost(WM_SHOWWINDOW, 0, 1));         // SW_PARENTCLOSING
        Assert.False(ShowDesktopGuard.IsLost(WM_SHOWWINDOW, 0, 0));        // ShowWindow(SW_HIDE): uygulamanın kendi kararı
        Assert.False(ShowDesktopGuard.IsLost(WM_SHOWWINDOW, 1, 0));        // gösterme
    }

    [Fact]
    public void A_position_change_with_the_hide_flag_is_a_loss()
    {
        Assert.True(WindowPosFlags(0x0080));    // SWP_HIDEWINDOW
        Assert.False(WindowPosFlags(0x0040));   // SWP_SHOWWINDOW
        Assert.False(WindowPosFlags(0x0010));   // SWP_NOACTIVATE
    }

    [Fact]
    public void Other_messages_are_ignored()
    {
        Assert.False(ShowDesktopGuard.IsLost(0x000F, 1, 1));
    }

    private static bool WindowPosFlags(uint flags)
    {
        // WINDOWPOS: hwnd, hwndInsertAfter, x, y, cx, cy, flags
        var size = IntPtr.Size * 2 + 5 * 4;
        var memory = Marshal.AllocHGlobal(size);
        try
        {
            for (var i = 0; i < size; i++)
            {
                Marshal.WriteByte(memory, i, 0);
            }

            Marshal.WriteInt32(memory, IntPtr.Size * 2 + 16, unchecked((int)flags));
            return ShowDesktopGuard.IsLost(WM_WINDOWPOSCHANGED, IntPtr.Zero, memory);
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }
}
