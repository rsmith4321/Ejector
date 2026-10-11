using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace EasyEject.Modern {
    // Redirect the framework's initial programmatic focus synchronously, including
    // a later popup-loaded pass. Explicit keyboard/pointer navigation stays usable.
    internal sealed class KeepConnectedDialog : ContentDialog {
        FrameworkElement layout;
        Button close;
        bool userChoseFocus;
        internal bool SafeInitialFocus { get; private set; }
#if UI_TESTS
        internal string FocusDiagnostics { get; private set; }
#endif
        internal KeepConnectedDialog() {
            CloseButtonText = "Keep Connected";
            DefaultButton = ContentDialogButton.Close;
            Opened += delegate { ApplyTemplate(); if (close == null || layout == null) Hide(); };
        }
        protected override void OnApplyTemplate() {
            if (layout != null) layout.GettingFocus -= InitialFocus;
            base.OnApplyTemplate();
            layout = GetTemplateChild("LayoutRoot") as FrameworkElement;
            close = GetTemplateChild("CloseButton") as Button;
            if (close != null) close.IsTabStop = true;
            if (layout != null) layout.GettingFocus += InitialFocus;
        }
        void InitialFocus(UIElement sender, GettingFocusEventArgs args) {
            if (args.FocusState == FocusState.Keyboard || args.FocusState == FocusState.Pointer) {
                userChoseFocus = true;
                return;
            }
            if (userChoseFocus || args.FocusState != FocusState.Programmatic || close == null) return;
            bool safe = object.ReferenceEquals(args.NewFocusedElement, close) || args.TrySetNewFocusedElement(close);
            SafeInitialFocus |= safe;
            if (!safe) args.Cancel = true;
#if UI_TESTS
            FocusDiagnostics = "Initial programmatic focus redirected=" + safe;
#endif
        }
    }
}
