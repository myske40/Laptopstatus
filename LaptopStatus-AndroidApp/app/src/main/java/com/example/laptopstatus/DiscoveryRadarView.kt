package com.example.laptopstatus

import android.animation.ValueAnimator
import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.util.AttributeSet
import android.view.View
import android.view.animation.AnimationUtils
import android.view.animation.LinearInterpolator
import android.animation.TimeInterpolator
import androidx.core.content.ContextCompat

class DiscoveryRadarView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
    defStyleAttr: Int = 0
) : View(context, attrs, defStyleAttr) {
    private var motionAnimationsEnabled = true
    private val density = resources.displayMetrics.density
    private val accent = ContextCompat.getColor(context, R.color.accent)
    private val ringPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeWidth = 1.2f * density
        color = accent
    }
    private val animator = ValueAnimator.ofFloat(0f, 1f).apply {
        duration = 3000L
        repeatCount = ValueAnimator.INFINITE
        interpolator = LinearInterpolator()
        addUpdateListener { invalidate() }
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val cx = width / 2f
        val cy = height / 2f
        val maxRadius = minOf(width, height) * 0.47f
        val baseRadius = 17f * density
        val phase = animator.animatedValue as? Float ?: 0f
        for (index in 0..2) {
            val progress = (phase + index / 3f) % 1f
            ringPaint.alpha = ((1f - progress) * 44).toInt().coerceIn(0, 44)
            val radius = baseRadius + (maxRadius - baseRadius) * progress
            canvas.drawCircle(cx, cy, radius, ringPaint)
        }
        ringPaint.alpha = 28
        canvas.drawCircle(cx, cy, maxRadius, ringPaint)
    }

    override fun onAttachedToWindow() {
        super.onAttachedToWindow()
        if (motionAnimationsEnabled && systemAnimationsEnabled() && !animator.isStarted) animator.start()
    }

    override fun onDetachedFromWindow() {
        animator.cancel()
        super.onDetachedFromWindow()
    }

    fun setMotionAnimationsEnabled(enabled: Boolean) {
        motionAnimationsEnabled = enabled
        if (enabled && systemAnimationsEnabled() && isAttachedToWindow) {
            if (!animator.isStarted) animator.start()
        } else {
            animator.cancel()
        }
        invalidate()
    }

    private fun systemAnimationsEnabled() = android.os.Build.VERSION.SDK_INT < 26 || ValueAnimator.areAnimatorsEnabled()
}

class MetricRingView(context: Context, private val caption: String) : View(context) {
    private var motionAnimationsEnabled = true
    private val density = resources.displayMetrics.density
    private val track = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeWidth = 5f * density
        strokeCap = Paint.Cap.ROUND
        color = 0x333F4A56
    }
    private val valuePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        textAlign = Paint.Align.CENTER
        typeface = android.graphics.Typeface.create("sans-serif", android.graphics.Typeface.BOLD)
        textSize = 17f * density
        color = 0xFFF3F6FB.toInt()
    }
    private val captionPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        textAlign = Paint.Align.CENTER
        textSize = 11f * density
        color = 0xFFAAB7C6.toInt()
    }
    private val arc = RectF()
    private var displayValue = "—"
    private var targetDisplayValue = "—"
    private var targetPercent = 0f
    private var animatedPercent = 0f
    private var animatedNumber = 0f
    private var metricAnimator: ValueAnimator? = null
    private val metricInterpolator: TimeInterpolator = AnimationUtils.loadInterpolator(context, R.interpolator.ios_spring)
    private val progressPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeWidth = 5f * density
        strokeCap = Paint.Cap.ROUND
        color = 0xFF72E4BF.toInt()
    }

    fun setMetric(value: String, percent: Int?) {
        targetDisplayValue = value
        displayValue = value
        metricAnimator?.cancel()
        targetPercent = percent?.coerceIn(0, 100)?.toFloat() ?: 0f
        if (percent == null) {
            animatedPercent = targetPercent
            animatedNumber = 0f
            invalidate()
            return
        }

        val targetNumber = percent.toFloat()
        val fromPercent = animatedPercent
        val fromNumber = animatedNumber
        val animationsEnabled = motionAnimationsEnabled && (android.os.Build.VERSION.SDK_INT < 26 || ValueAnimator.areAnimatorsEnabled())
        if (!animationsEnabled) {
            animatedPercent = targetPercent
            animatedNumber = targetNumber
            displayValue = "${percent}%"
            invalidate()
            return
        }
        metricAnimator = ValueAnimator.ofFloat(0f, 1f).apply {
            duration = 1100L
            interpolator = metricInterpolator
            addUpdateListener {
                val progress = it.animatedValue as Float
                animatedPercent = fromPercent + (targetPercent - fromPercent) * progress
                animatedNumber = fromNumber + (targetNumber - fromNumber) * progress
                displayValue = "${(animatedNumber + 0.5f).toInt()}%"
                invalidate()
            }
            start()
        }
    }

    fun setMotionAnimationsEnabled(enabled: Boolean) {
        motionAnimationsEnabled = enabled
        if (enabled && (android.os.Build.VERSION.SDK_INT < 26 || ValueAnimator.areAnimatorsEnabled())) return
        metricAnimator?.cancel()
        metricAnimator = null
        animatedPercent = targetPercent
        animatedNumber = targetPercent
        displayValue = targetDisplayValue
        invalidate()
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val radius = minOf(width * 0.31f, height * 0.32f).coerceAtLeast(22f * density)
        val cx = width / 2f
        val cy = radius + 8f * density
        arc.set(cx - radius, cy - radius, cx + radius, cy + radius)
        canvas.drawArc(arc, 0f, 360f, false, track)
        canvas.drawArc(arc, -90f, 3.6f * animatedPercent, false, progressPaint)
        val baseline = cy - (valuePaint.ascent() + valuePaint.descent()) / 2f
        canvas.drawText(displayValue, cx, baseline, valuePaint)
        canvas.drawText(caption, cx, height - 4f * density, captionPaint)
    }

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val desiredHeight = (126f * density).toInt()
        val width = MeasureSpec.getSize(widthMeasureSpec)
        val height = when (MeasureSpec.getMode(heightMeasureSpec)) {
            MeasureSpec.EXACTLY -> MeasureSpec.getSize(heightMeasureSpec)
            MeasureSpec.AT_MOST -> minOf(desiredHeight, MeasureSpec.getSize(heightMeasureSpec))
            else -> desiredHeight
        }
        setMeasuredDimension(width, height)
    }
}

class PowerCountdownRingView(context: Context) : View(context) {
    private val density = resources.displayMetrics.density
    private val track = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeWidth = 5f * density
        color = 0xFF3B4653.toInt()
    }
    private val arcPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeWidth = 5f * density
        strokeCap = Paint.Cap.ROUND
        color = 0xFFE86170.toInt()
    }
    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        textAlign = Paint.Align.CENTER
        textSize = 22f * density
        typeface = android.graphics.Typeface.create("sans-serif", android.graphics.Typeface.BOLD)
        color = 0xFFF3F6FB.toInt()
    }
    private val rect = RectF()
    var remainingSeconds: Int = 10
        set(value) { field = value.coerceIn(0, 10); invalidate() }
    var remainingFraction: Float = 1f
        set(value) { field = value.coerceIn(0f, 1f); invalidate() }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val radius = minOf(width, height) * 0.40f
        val cx = width / 2f
        val cy = height / 2f
        rect.set(cx - radius, cy - radius, cx + radius, cy + radius)
        canvas.drawArc(rect, 0f, 360f, false, track)
        canvas.drawArc(rect, -90f, 360f * remainingFraction, false, arcPaint)
        val baseline = cy - (textPaint.ascent() + textPaint.descent()) / 2f
        canvas.drawText(remainingSeconds.toString(), cx, baseline, textPaint)
    }

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val side = (96f * density).toInt()
        setMeasuredDimension(resolveSize(side, widthMeasureSpec), resolveSize(side, heightMeasureSpec))
    }
}

class SegmentedMemoryView(context: Context, attrs: AttributeSet? = null) : View(context, attrs) {
    private var motionAnimationsEnabled = true
    private val density = resources.displayMetrics.density
    private val colors = intArrayOf(
        ContextCompat.getColor(context, R.color.memory_apps),
        ContextCompat.getColor(context, R.color.memory_cache),
        ContextCompat.getColor(context, R.color.memory_pool)
    )
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private var fractions = floatArrayOf(0f, 0f, 0f)
    private var targetFractions = floatArrayOf(0f, 0f, 0f)
    private var segmentAnimator: ValueAnimator? = null
    private val segmentInterpolator: TimeInterpolator = AnimationUtils.loadInterpolator(context, R.interpolator.ios_spring)
    private val bounds = RectF()
    private val clipShape = Path()

    fun setSegments(usedBytes: Long, totalBytes: Long, appBytes: Long, cacheBytes: Long, poolBytes: Long) {
        val denominator = totalBytes.coerceAtLeast(1L).toDouble()
        val maxUsedFraction = (usedBytes.toDouble() / denominator).coerceIn(0.0, 1.0)
        val categories = doubleArrayOf(
            appBytes.coerceAtLeast(0).toDouble(),
            cacheBytes.coerceAtLeast(0).toDouble(),
            poolBytes.coerceAtLeast(0).toDouble()
        )
        val sum = categories.sum()
        val scale = if (sum <= 0.0) 0.0 else maxUsedFraction / sum
        targetFractions = FloatArray(3) { (categories[it] * scale).toFloat() }
        val startFractions = fractions.copyOf()
        segmentAnimator?.cancel()
        val animationsEnabled = motionAnimationsEnabled && (android.os.Build.VERSION.SDK_INT < 26 || ValueAnimator.areAnimatorsEnabled())
        if (!animationsEnabled) {
                fractions = targetFractions.copyOf()
            invalidate()
            return
        }
        segmentAnimator = ValueAnimator.ofFloat(0f, 1f).apply {
            duration = 1000L
            interpolator = segmentInterpolator
            addUpdateListener {
                val progress = it.animatedValue as Float
                fractions = FloatArray(3) { index -> startFractions[index] + (targetFractions[index] - startFractions[index]) * progress }
                invalidate()
            }
            start()
        }
    }

    fun setMotionAnimationsEnabled(enabled: Boolean) {
        motionAnimationsEnabled = enabled
        if (enabled && (android.os.Build.VERSION.SDK_INT < 26 || ValueAnimator.areAnimatorsEnabled())) return
        segmentAnimator?.cancel()
        segmentAnimator = null
        fractions = targetFractions.copyOf()
        invalidate()
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val radius = height / 2f
        bounds.set(0f, 0f, width.toFloat(), height.toFloat())
        paint.color = ContextCompat.getColor(context, R.color.memory_available)
        canvas.drawRoundRect(bounds, radius, radius, paint)
        clipShape.reset()
        clipShape.addRoundRect(bounds, radius, radius, Path.Direction.CW)
        canvas.save()
        canvas.clipPath(clipShape)
        var left = 0f
        fractions.forEachIndexed { index, fraction ->
            val segmentWidth = (width * fraction).coerceAtLeast(0f)
            if (segmentWidth > 0f) {
                paint.color = colors[index]
                canvas.drawRect(left, 0f, (left + segmentWidth).coerceAtMost(width.toFloat()), height.toFloat(), paint)
                left += segmentWidth
            }
        }
        canvas.restore()
    }

    override fun onDetachedFromWindow() {
        segmentAnimator?.cancel()
        segmentAnimator = null
        super.onDetachedFromWindow()
    }

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val desiredHeight = (8f * density).toInt()
        val width = MeasureSpec.getSize(widthMeasureSpec)
        setMeasuredDimension(width, resolveSize(desiredHeight, heightMeasureSpec))
    }
}
