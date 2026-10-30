package com.example.laptopstatus

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import org.json.JSONObject

/** Sends one notification when the selected laptop drops below 20%; rearms at 23%. */
internal object LaptopBatteryAlerts {
    private const val CHANNEL_ID = "laptop_status_events"
    private const val NOTIFICATION_ID = 4103
    private const val ALERT_SENT_KEY = "lowBatteryAlertSent"
    private val lock = Any()

    fun checkAndNotify(context: Context, status: JSONObject): Boolean = synchronized(lock) {
        val preferences = context.getSharedPreferences("connection", Context.MODE_PRIVATE)
        val percent: Int? = if (status.isNull("batteryPercent")) null
            else status.optInt("batteryPercent", -1).takeIf { it in 0..100 }
        if (percent == null) return@synchronized false

        if (percent >= 23) {
            if (preferences.getBoolean(ALERT_SENT_KEY, false))
                preferences.edit().putBoolean(ALERT_SENT_KEY, false).apply()
            return@synchronized false
        }
        if (percent >= 20 || !preferences.getBoolean("lowBatteryAlerts", true) ||
            !NotificationManagerCompat.from(context).areNotificationsEnabled()) return@synchronized false
        if (Build.VERSION.SDK_INT >= 33 && ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
            return@synchronized false
        if (preferences.getBoolean(ALERT_SENT_KEY, false)) return@synchronized false

        val name = status.optString("deviceName", "Laptop")
        val notification = NotificationCompat.Builder(context, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_laptop)
            .setContentTitle("Baterai laptop hampir habis")
            .setContentText("$name tersisa $percent%. Sambungkan ke charger.")
            .setPriority(NotificationCompat.PRIORITY_DEFAULT)
            .setAutoCancel(true)
            .build()
        try {
            NotificationManagerCompat.from(context).notify(NOTIFICATION_ID, notification)
            preferences.edit().putBoolean(ALERT_SENT_KEY, true).apply()
            true
        } catch (_: SecurityException) {
            false
        }
    }
}
