package com.example.laptopstatus

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import androidx.core.content.ContextCompat

/** Restores only the monitoring service the user explicitly enabled. */
class MonitorBootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent?) {
        if (intent?.action != Intent.ACTION_BOOT_COMPLETED &&
            intent?.action != Intent.ACTION_MY_PACKAGE_REPLACED) return
        val prefs = context.getSharedPreferences("connection", Context.MODE_PRIVATE)
        if (!prefs.getBoolean("backgroundMonitor", false)) return
        runCatching {
            ContextCompat.startForegroundService(context, Intent(context, MonitorService::class.java))
        }
    }
}
