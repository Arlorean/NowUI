#if NOWUI_STANDALONE
// The engine-free half of the NowFilePicker thumbnail pipeline; the counterpart of
// NowFilePicker.Thumbnails.Unity.cs. Unity never defines NOWUI_STANDALONE, so this file
// compiles to nothing there. See Docs/Standalone/StandaloneCoreDesign.md sections 5.2 and 12.8.
//
// Thumbnail loading is inert: the Unity half reads the file through a file:// UnityWebRequest,
// which has no browser equivalent, so every request fails immediately and the picker falls back
// to the icon path that GetThumbnailEntry already draws for a failed entry. A desktop host can
// add an INowThumbnailLoader later without touching the core half.

namespace NowUI
{
    public partial struct NowFilePicker
    {
        static void StartThumbnailRequest(PopupState state, ThumbnailEntry entry)
        {
            if (entry == null || entry.state != ThumbnailState.Pending)
                return;

            entry.state = ThumbnailState.Failed;
        }

        static void PollThumbnailRequests(PopupState state)
        {
        }

        static void AbortThumbnailRequest(ThumbnailEntry entry)
        {
        }
    }
}
#endif
