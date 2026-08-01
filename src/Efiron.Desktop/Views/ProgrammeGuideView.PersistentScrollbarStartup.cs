namespace Efiron.Desktop.Views;

public sealed partial class ProgrammeGuideView
{
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // The persistent scrollbar used to be injected only after projection
        // or while collecting evidence. Its layout geometry was valid, but a
        // RenderTargetBitmap could capture the first stable EPG frame before
        // the dynamically added rail/thumb had entered the rendered tree.
        // Create it with the control template so both real users and physical
        // pixel gates see the same element from the first EPG frame.
        EnsurePersistentVerticalScrollBar();
    }
}
