package com.example.laptopstatus

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Intent
import android.content.Context
import android.content.pm.ServiceInfo
import android.net.wifi.WifiManager
import android.os.Build
import android.os.IBinder
import android.provider.Settings
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.app.ServiceCompat
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.nio.charset.StandardCharsets
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

class MonitorService : Service() {
    private val scheduler = Executors.newSingleThreadScheduledExecutor()
    @Volatile private var lastMonitorNotificationText = ""

    override fun onCreate() {
        super.onCreate()
        val prefs = getSharedPreferences("connection", MODE_PRIVATE)
        prefs.edit()
            .remove("appJokeMonitorAllowed")
            .remove("appJokeEnabled")
            .remove("pendingJokeEnable")
            .remove("jokeNotificationRequested")
            .remove("jokeApps")
            .remove("remoteJokeEnabled")
            .apply()
        NotificationManagerCompat.from(this).cancel(JOKE_NOTIFICATION_ID)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O)
            getSystemService(NotificationManager::class.java).deleteNotificationChannel("laptop_app_jokes")
        createNotificationChannel()
        val serviceTypes = if (Build.VERSION.SDK_INT >= 34)
            ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE
        else ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE
        ServiceCompat.startForeground(this, MONITOR_NOTIFICATION_ID,
            monitoringNotification("Menghubungkan ke laptop", "Notifikasi status Laptop Status aktif."), serviceTypes)
        scheduler.scheduleWithFixedDelay(::checkLaptop, 0, 15, TimeUnit.SECONDS)
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (!getSharedPreferences("connection", MODE_PRIVATE).getBoolean("backgroundMonitor", false)) {
            stopSelf(startId)
            return START_NOT_STICKY
        }
        NotificationManagerCompat.from(this).notify(MONITOR_NOTIFICATION_ID,
            monitoringNotification("Menghubungkan ke laptop", "Notifikasi status Laptop Status aktif."))
        return START_STICKY
    }

    private fun checkLaptop() {
        val prefs = getSharedPreferences("connection", MODE_PRIVATE)
        if (!prefs.getBoolean("backgroundMonitor", false)) return
        val address = prefs.getString("address", "").orEmpty()
        val key = prefs.getString("key", "").orEmpty()
        if (address.isBlank() || key.isBlank()) return

        var online = false
        var name = "Laptop"
        var status: JSONObject? = null
        try {
            val fingerprint = prefs.getString("fingerprint", "").orEmpty()
            val data = try {
                requestStatus(address, key, fingerprint)
            } catch (_: Exception) {
                val lan = discoverPairedLaptopOnLan(prefs.getString("activeDeviceId", "").orEmpty(), fingerprint)
                    ?: throw IllegalStateException("Laptop belum terjangkau melalui jaringan saat ini.")
                val recovered = requestStatus(lan, key, fingerprint)
                prefs.edit().putString("address", lan).apply()
                recovered
            }
            status = data.second
            online = status.optBoolean("online", true)
            name = status.optString("deviceName", "Laptop")
            updateAndroidJokeOverlay(prefs, status)
            if (online) LaptopBatteryAlerts.checkAndNotify(this, status)
        } catch (_: Exception) { }

        if (prefs.getBoolean("backgroundMonitor", false))
            postMonitoringStatus(name, online, status)

        val hadSample = prefs.getBoolean("monitorHasSample", false)
        val wasOnline = prefs.getBoolean("monitorWasOnline", false)
        prefs.edit().putBoolean("monitorWasOnline", online).putBoolean("monitorHasSample", true).apply()
        if (hadSample && online && !wasOnline) postLaptopOnline(name)
    }

    private fun requestStatus(host: String, token: String, fingerprint: String): Pair<String, JSONObject> {
        val result = PinnedTls.withConnection(host, 8765, "/api/status", "GET", token, fingerprint) { connection ->
            if (connection.responseCode != 200) throw IllegalStateException("Laptop menolak koneksi (${connection.responseCode}).")
            JSONObject(connection.inputStream.bufferedReader().use { it.readText() })
        }
        return host to result
    }

    private fun updateAndroidJokeOverlay(prefs: android.content.SharedPreferences, status: JSONObject) {
        val active = status.optBoolean("jokeActive", false)
        val remoteEnabled = status.optBoolean("androidJokeNotificationEnabled", false)
        if (!active) {
            prefs.edit()
                .putBoolean("androidJokeOverlayHandledForActive", false)
                .putBoolean("androidJokeOverlayPermissionNotified", false)
                .apply()
            AndroidJokeOverlay.dismiss()
            NotificationManagerCompat.from(this).cancel(JOKE_OVERLAY_PERMISSION_NOTIFICATION_ID)
            return
        }
        if (!remoteEnabled || !prefs.getBoolean("androidJokeOverlayEnabled", false)) {
            AndroidJokeOverlay.dismiss()
            return
        }
        if (prefs.getBoolean("mainActivityVisible", false) ||
            prefs.getBoolean("androidJokeOverlayHandledForActive", false)) return

        if (!Settings.canDrawOverlays(this)) {
            if (!prefs.getBoolean("androidJokeOverlayPermissionNotified", false)) {
                postOverlayPermissionNotification()
                prefs.edit().putBoolean("androidJokeOverlayPermissionNotified", true).apply()
            }
            prefs.edit().putBoolean("androidJokeOverlayHandledForActive", true).apply()
            return
        }

        prefs.edit()
            .putBoolean("androidJokeOverlayHandledForActive", true)
            .putBoolean("androidJokeOverlayPermissionNotified", false)
            .apply()
        AndroidJokeOverlay.show(this, status)
    }

    private fun postOverlayPermissionNotification() {
        val manager = getSystemService(NotificationManager::class.java)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O &&
            manager.getNotificationChannel(JOKE_OVERLAY_CHANNEL_ID) == null) {
            manager.createNotificationChannel(NotificationChannel(
                JOKE_OVERLAY_CHANNEL_ID,
                "Izin Mode Joke Android",
                NotificationManager.IMPORTANCE_HIGH
            ).apply { description = "Petunjuk izin untuk menampilkan Mode Joke di atas aplikasi lain." })
        }
        val intent = Intent(this, MainActivity::class.java)
            .putExtra(EXTRA_OPEN_JOKE_OVERLAY_PERMISSION, true)
            .addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP)
        val pendingIntent = PendingIntent.getActivity(
            this,
            JOKE_OVERLAY_PERMISSION_NOTIFICATION_ID,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val notification = NotificationCompat.Builder(this, JOKE_OVERLAY_CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_laptop)
            .setContentTitle("Mode Joke perlu izin overlay")
            .setContentText("Ketuk untuk mengizinkan simulasi tampil di atas aplikasi lain.")
            .setStyle(NotificationCompat.BigTextStyle().bigText(
                "Android meminta izin khusus agar simulasi singkat dapat tampil saat aplikasi lain dibuka."
            ))
            .setContentIntent(pendingIntent)
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .build()
        runCatching { NotificationManagerCompat.from(this).notify(JOKE_OVERLAY_PERMISSION_NOTIFICATION_ID, notification) }
    }

    private fun discoverPairedLaptopOnLan(deviceId: String, fingerprint: String): String? {
        val wifi = applicationContext.getSystemService(Context.WIFI_SERVICE) as? WifiManager ?: return null
        val lock = wifi.createMulticastLock("LaptopStatusBackgroundDiscovery").apply { setReferenceCounted(false); acquire() }
        try {
            DatagramSocket(null).use { socket ->
                socket.reuseAddress = true
                socket.bind(InetSocketAddress(8766))
                socket.soTimeout = 900
                val dataBuffer = ByteArray(2048)
                val deadline = System.nanoTime() + 4_000_000_000L
                while (System.nanoTime() < deadline) {
                    try {
                        val packet = DatagramPacket(dataBuffer, dataBuffer.size)
                        socket.receive(packet)
                        val announcement = JSONObject(String(packet.data, packet.offset, packet.length, StandardCharsets.UTF_8))
                        if (announcement.optString("service") != "LaptopStatus") continue
                        if (deviceId.isNotBlank() && announcement.optString("deviceId") != deviceId) continue
                        val candidateFingerprint = announcement.optString("fingerprint").filter(Char::isLetterOrDigit).uppercase(Locale.ROOT)
                        if (candidateFingerprint != fingerprint.filter(Char::isLetterOrDigit).uppercase(Locale.ROOT)) continue
                        val candidate = packet.address.hostAddress?.substringBefore('%') ?: continue
                        if (candidate != prefsAddress()) return candidate
                    } catch (_: java.net.SocketTimeoutException) { }
                    catch (_: org.json.JSONException) { }
                }
            }
        } catch (_: Exception) { }
        finally { if (lock.isHeld) lock.release() }
        return null
    }

    private fun prefsAddress(): String = getSharedPreferences("connection", MODE_PRIVATE).getString("address", "").orEmpty()

    private fun postLaptopOnline(name: String) {
        val time = SimpleDateFormat("HH:mm", Locale.getDefault()).format(Date())
        val notification = NotificationCompat.Builder(this, EVENT_CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_laptop)
            .setContentTitle("Laptop menyala")
            .setContentText("$name terhubung kembali • $time")
            .setPriority(NotificationCompat.PRIORITY_DEFAULT)
            .setAutoCancel(true)
            .build()
        try { NotificationManagerCompat.from(this).notify(4102, notification) }
        catch (_: SecurityException) { }
    }

    private fun postMonitoringStatus(name: String, online: Boolean, status: JSONObject?) {
        val metrics = buildList {
            status?.optDouble("cpuPercent", Double.NaN)?.takeIf { it.isFinite() && it in 0.0..100.0 }
                ?.let { add("CPU ${it.toInt()}%") }
            status?.optDouble("ramPercent", Double.NaN)?.takeIf { it.isFinite() && it in 0.0..100.0 }
                ?.let { add("RAM ${it.toInt()}%") }
            if (status != null && !status.isNull("batteryPercent")) {
                val battery = status.optInt("batteryPercent", -1)
                if (battery in 0..100) add("Baterai $battery%")
            }
        }
        val title = if (online) "$name terhubung" else "$name tidak terjangkau"
        val details = if (online && metrics.isNotEmpty()) metrics.joinToString("  ·  ")
            else if (online) "Laptop aktif · menunggu data sensor"
            else "Pemeriksaan otomatis berjalan di latar belakang"
        val signature = "$title\n$details"
        if (signature == lastMonitorNotificationText) return
        lastMonitorNotificationText = signature
        runCatching {
            NotificationManagerCompat.from(this).notify(MONITOR_NOTIFICATION_ID, monitoringNotification(title, details))
        }
    }

    private fun monitoringNotification(title: String, details: String): Notification {
        val openAppIntent = Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP)
        val contentIntent = PendingIntent.getActivity(
            this,
            MONITOR_NOTIFICATION_ID,
            openAppIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        return NotificationCompat.Builder(this, MONITOR_CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_laptop)
            .setContentTitle(title)
            .setContentText(details)
            .setStyle(NotificationCompat.BigTextStyle().bigText(details))
            .setContentIntent(contentIntent)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
            .setCategory(NotificationCompat.CATEGORY_SERVICE)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .build()
    }

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val manager = getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(NotificationChannel(MONITOR_CHANNEL_ID, "Pemantauan laptop", NotificationManager.IMPORTANCE_LOW).apply {
                description = "Pemberitahuan layanan pemantauan koneksi laptop"
            })
            manager.createNotificationChannel(NotificationChannel(EVENT_CHANNEL_ID, "Status laptop", NotificationManager.IMPORTANCE_DEFAULT).apply {
                description = "Status koneksi, pemantauan, dan peringatan baterai laptop"
            })
        }
    }

    override fun onDestroy() {
        scheduler.shutdownNow()
        AndroidJokeOverlay.dismiss()
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    companion object {
        private const val MONITOR_NOTIFICATION_ID = 4100
        private const val MONITOR_CHANNEL_ID = "laptop_monitor"
        private const val EVENT_CHANNEL_ID = "laptop_status_events"
        private const val JOKE_NOTIFICATION_ID = 4201
        private const val JOKE_OVERLAY_CHANNEL_ID = "android_joke_overlay_permission"
        private const val JOKE_OVERLAY_PERMISSION_NOTIFICATION_ID = 4204
        const val EXTRA_OPEN_JOKE_OVERLAY_PERMISSION = "open_joke_overlay_permission"
    }
}
