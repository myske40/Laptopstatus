# AppFunctions discovers generated entry points and annotated methods at runtime.
-keep @androidx.appfunctions.AppFunctionServiceEntryPoint class * { *; }
-keepclassmembers class * {
    @androidx.appfunctions.AppFunction <methods>;
}

# Android WebView invokes these methods reflectively from the trusted bundled UI.
-keepclassmembers class * {
    @android.webkit.JavascriptInterface <methods>;
}
