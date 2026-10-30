package com.example.laptopstatus

import android.animation.AnimatorSet
import android.animation.ObjectAnimator
import android.animation.ValueAnimator
import android.content.Context
import android.graphics.BitmapFactory
import android.graphics.Color
import android.graphics.PixelFormat
import android.graphics.Typeface
import android.graphics.drawable.ColorDrawable
import android.graphics.drawable.GradientDrawable
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import android.view.Gravity
import android.view.View
import android.view.WindowManager
import android.widget.Button
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import org.json.JSONObject

/** A short-lived, user-dismissible overlay for the explicitly enabled Android Joke feature. */
object AndroidJokeOverlay {
    private val main = Handler(Looper.getMainLooper())
    private var windowManager: WindowManager? = null
    private var overlay: View? = null
    private var animator: AnimatorSet? = null
    private val autoDismiss = Runnable { dismiss() }

    fun show(context: Context, data: JSONObject) {
        if (Looper.myLooper() != Looper.getMainLooper()) {
            main.post { show(context, data) }
            return
        }
        val appContext = context.applicationContext
        if (overlay != null || !Settings.canDrawOverlays(appContext)) return

        val color = data.optString("androidJokeBackgroundHex", "#AE1218")
            .takeIf { it.matches(Regex("#[0-9A-Fa-f]{6}")) }
            ?.let { runCatching { Color.parseColor(it) }.getOrNull() }
            ?: Color.rgb(174, 18, 24)
        val message = data.optString("androidJokeNotificationMessage")
            .trim().take(200).ifBlank { "Mode Joke aktif — ini hanya simulasi." }
        val root = FrameLayout(appContext).apply {
            setBackgroundColor(color)
            isClickable = true
            isFocusable = false
        }
        val card = LinearLayout(appContext).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
            setPadding(dp(appContext, 24), dp(appContext, 28), dp(appContext, 24), dp(appContext, 26))
            background = GradientDrawable().apply {
                setColor(Color.argb(48, 0, 0, 0))
                cornerRadius = dp(appContext, 30).toFloat()
                setStroke(dp(appContext, 1), Color.argb(70, 255, 255, 255))
            }
        }
        val icon = makeIcon(appContext, data)
        card.addView(icon, LinearLayout.LayoutParams(dp(appContext, 128), dp(appContext, 128)).apply {
            bottomMargin = dp(appContext, 20)
        })
        card.addView(TextView(appContext).apply {
            text = "MODE JOKE AKTIF"
            setTextColor(Color.WHITE)
            textSize = 26f
            typeface = Typeface.DEFAULT_BOLD
            gravity = Gravity.CENTER
            setPadding(0, 0, 0, dp(appContext, 14))
        })
        card.addView(TextView(appContext).apply {
            text = message
            setTextColor(Color.WHITE)
            textSize = 19f
            gravity = Gravity.CENTER
            setPadding(0, 0, 0, dp(appContext, 14))
        })
        card.addView(TextView(appContext).apply {
            text = "Simulasi sementara · tertutup otomatis dalam 30 detik"
            setTextColor(Color.WHITE)
            alpha = .82f
            textSize = 13f
            gravity = Gravity.CENTER
            setPadding(0, 0, 0, dp(appContext, 22))
        })
        card.addView(Button(appContext).apply {
            text = "Tutup prank"
            setOnClickListener { dismiss() }
        }, LinearLayout.LayoutParams(-1, dp(appContext, 52)))
        root.addView(card, FrameLayout.LayoutParams(-1, -2, Gravity.CENTER).apply {
            leftMargin = dp(appContext, 22)
            rightMargin = dp(appContext, 22)
        })

        val type = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O)
            WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
        else WindowManager.LayoutParams.TYPE_SYSTEM_ALERT
        val params = WindowManager.LayoutParams(
            WindowManager.LayoutParams.MATCH_PARENT,
            WindowManager.LayoutParams.MATCH_PARENT,
            type,
            WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN or
                WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS or
                WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON or
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE,
            PixelFormat.TRANSLUCENT
        ).apply {
            gravity = Gravity.TOP or Gravity.START
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P)
                layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES
        }
        val manager = appContext.getSystemService(Context.WINDOW_SERVICE) as WindowManager
        try {
            manager.addView(root, params)
            windowManager = manager
            overlay = root
            animateIcon(icon, data.optString("androidJokeAnimation", "pulse"))
            main.removeCallbacks(autoDismiss)
            main.postDelayed(autoDismiss, 30_000L)
        } catch (_: Exception) {
            runCatching { manager.removeView(root) }
            overlay = null
            windowManager = null
        }
    }

    fun dismiss() {
        if (Looper.myLooper() != Looper.getMainLooper()) {
            main.post { dismiss() }
            return
        }
        main.removeCallbacks(autoDismiss)
        animator?.cancel()
        animator = null
        overlay?.let { view -> runCatching { windowManager?.removeView(view) } }
        overlay = null
        windowManager = null
    }

    private fun makeIcon(context: Context, data: JSONObject): View {
        val encoded = data.optString("androidJokeIconBase64")
        if (encoded.isNotBlank() && encoded.length < 180_000) {
            val bitmap = runCatching {
                val bytes = android.util.Base64.decode(encoded, android.util.Base64.DEFAULT)
                val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
                BitmapFactory.decodeByteArray(bytes, 0, bytes.size, bounds)
                bounds.inSampleSize = maxOf(1, maxOf(bounds.outWidth, bounds.outHeight) / 384)
                bounds.inJustDecodeBounds = false
                BitmapFactory.decodeByteArray(bytes, 0, bytes.size, bounds)
            }.getOrNull()
            if (bitmap != null) return ImageView(context).apply {
                setImageBitmap(bitmap)
                scaleType = ImageView.ScaleType.FIT_CENTER
                contentDescription = "Ikon Mode Joke"
            }
        }
        return TextView(context).apply {
            text = data.optString("androidJokeIconGlyph").trim().take(8).ifBlank { "😵" }
            textSize = 82f
            gravity = Gravity.CENTER
            setTextColor(Color.WHITE)
            contentDescription = "Ikon Mode Joke"
        }
    }

    private fun animateIcon(icon: View, style: String) {
        val animators = when (style.lowercase()) {
            "spin" -> listOf(ObjectAnimator.ofFloat(icon, View.ROTATION, 0f, 360f).apply { duration = 1500 })
            "shake" -> listOf(ObjectAnimator.ofFloat(icon, View.ROTATION, -12f, 12f).apply { duration = 180 })
            "bounce" -> listOf(ObjectAnimator.ofFloat(icon, View.TRANSLATION_Y, 0f, -22f).apply { duration = 420 })
            "pop" -> listOf(ObjectAnimator.ofFloat(icon, View.SCALE_X, .7f, 1.18f), ObjectAnimator.ofFloat(icon, View.SCALE_Y, .7f, 1.18f))
            else -> listOf(ObjectAnimator.ofFloat(icon, View.SCALE_X, 1f, 1.16f), ObjectAnimator.ofFloat(icon, View.SCALE_Y, 1f, 1.16f))
        }
        animators.forEach { it.repeatMode = ValueAnimator.REVERSE; it.repeatCount = ValueAnimator.INFINITE }
        animator?.cancel()
        animator = AnimatorSet().apply {
            playTogether(animators)
            interpolator = android.view.animation.AccelerateDecelerateInterpolator()
            start()
        }
    }

    private fun dp(context: Context, value: Int): Int = (value * context.resources.displayMetrics.density).toInt()
}
