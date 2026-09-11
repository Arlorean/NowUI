// Mirrors UnityEngine.RuntimePlatform for the NowUI standalone build.
// Governed by StandaloneCoreDesign.md §3.2 ("Enums/RuntimePlatform.cs" — ship the *complete* enum);
// values verified against UnityEngine.CoreModule.dll from Unity 6000.4.0f1.
//
// NowFilePickerUserFolders.Platform switches on WindowsEditor/WindowsPlayer/OSXEditor/OSXPlayer/
// LinuxEditor/LinuxPlayer. Per design §3.2 the shim's Application.platform defaults to the OS-mapped *desktop*
// value (WindowsPlayer / OSXPlayer / LinuxPlayer), not WebGLPlayer; a browser host sets WebGLPlayer explicitly.

namespace UnityEngine
{
    /// <summary>
    /// The platform the player is running on, with Unity's exact numeric values (design §3.2).
    /// <para>
    /// The numbering has gaps and duplicates for historical reasons and both are Unity-faithful, not typos:
    /// 6 and 14 are unused (retired platforms), <c>WindowsEditor</c> is 7 rather than 3, the Metro/WSA pairs alias
    /// each other (<c>MetroPlayerX86 == WSAPlayerX86 == 18</c>, and likewise for X64/ARM), <c>BB10Player ==
    /// BlackBerryPlayer == 22</c>, and the two obsolete members <c>CloudRendering</c> and <c>GameCoreScarlett</c>
    /// both carry <c>-1</c>. Code must therefore compare platforms by member, never by ordinal position.
    /// </para>
    /// </summary>
    public enum RuntimePlatform
    {
        OSXEditor = 0,
        OSXPlayer = 1,
        WindowsPlayer = 2,
        OSXWebPlayer = 3,
        OSXDashboardPlayer = 4,
        WindowsWebPlayer = 5,
        WindowsEditor = 7,
        IPhonePlayer = 8,
        XBOX360 = 10,
        PS3 = 9,
        Android = 11,
        NaCl = 12,
        FlashPlayer = 15,
        LinuxPlayer = 13,
        LinuxEditor = 16,
        WebGLPlayer = 17,
        MetroPlayerX86 = 18,
        WSAPlayerX86 = 18,
        MetroPlayerX64 = 19,
        WSAPlayerX64 = 19,
        MetroPlayerARM = 20,
        WSAPlayerARM = 20,
        WP8Player = 21,
        BB10Player = 22,
        BlackBerryPlayer = 22,
        TizenPlayer = 23,
        PSP2 = 24,
        PS4 = 25,
        PSM = 26,
        XboxOne = 27,
        SamsungTVPlayer = 28,
        WiiU = 30,
        tvOS = 31,
        Switch = 32,
        Lumin = 33,
        Stadia = 34,
        CloudRendering = -1,
        LinuxHeadlessSimulation = 35,
        GameCoreScarlett = -1,
        GameCoreXboxSeries = 36,
        GameCoreXboxOne = 37,
        PS5 = 38,
        EmbeddedLinuxArm64 = 39,
        EmbeddedLinuxArm32 = 40,
        EmbeddedLinuxX64 = 41,
        EmbeddedLinuxX86 = 42,
        LinuxServer = 43,
        WindowsServer = 44,
        OSXServer = 45,
        QNXArm32 = 46,
        QNXArm64 = 47,
        QNXX64 = 48,
        QNXX86 = 49,
        VisionOS = 50,
        Switch2 = 51,
        KeplerArm64 = 52,
        KeplerX64 = 53,    }
}
