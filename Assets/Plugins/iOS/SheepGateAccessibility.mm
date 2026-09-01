// The one question Unity cannot ask iOS on its own: has the player turned on Reduce Motion?
//
// UIAccessibilityIsReduceMotionEnabled lives in UIKit and has no Unity API in front of it, so the
// managed side reaches it through this shim. It is declared in AccessibilityPreferences, guarded
// by UNITY_IOS, and every other platform answers false without calling anything.
//
// extern "C" because IL2CPP looks the symbol up by its C name; without it the C++ mangling makes
// the DllImport fail at runtime with an entry point error rather than at build time.

#import <UIKit/UIKit.h>

extern "C" {

bool SheepGateIsReduceMotionEnabled(void)
{
    return UIAccessibilityIsReduceMotionEnabled() ? true : false;
}

}
