#if NOWUI_UGUI
using UnityEngine;

namespace NowUI
{
    /// <summary>
    /// UGUI host for NowLayout content. It owns the exact measure/draw cycle, so
    /// root layout code uses ordinary Row/Horizontal or Column/Vertical scopes
    /// and never needs its own RunMeasured wrapper. <see cref="NowGraphic.DrawNowUI"/>
    /// runs once with drawing suppressed and input passive, then once for the real draw.
    /// Reacting to control results is safe for the controls in this package: each
    /// one that hands a value to a later frame guards that commit with
    /// <see cref="NowInput.isPassive"/>, so only the real draw commits it. A CUSTOM
    /// control that carries a value across frames must do the same, or the measure
    /// pass consumes it and the real draw reports no change. Guard unconditional
    /// state changes with <see cref="NowLayout.isMeasurePass"/>.
    /// </summary>
    [AddComponentMenu("NowUI/Now Layout Graphic")]
    public class NowLayoutGraphic : NowGraphic
    {
        internal sealed override bool useLayoutMeasurePass => true;
    }
}
#endif
