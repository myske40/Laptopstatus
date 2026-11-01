package com.example.laptopstatus

import android.Manifest
import android.animation.ObjectAnimator
import android.animation.ValueAnimator
import android.app.Dialog
import android.app.DatePickerDialog
import android.app.TimePickerDialog
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.usage.UsageEvents
import android.content.Context
import android.content.ClipboardManager
import android.content.ClipData
import android.content.Intent
import android.content.SharedPreferences
import android.content.ContentValues
import android.content.res.ColorStateList
import android.net.Uri
import android.provider.Settings
import android.net.ConnectivityManager
import android.media.MediaPlayer
import android.content.pm.ActivityInfo
import android.graphics.Color
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.drawable.ColorDrawable
import android.graphics.drawable.GradientDrawable
import android.net.wifi.WifiManager
import android.os.Build
import android.graphics.Rect
import android.os.Bundle
import android.os.Handler
import android.os.Environment
import android.os.Looper
import android.text.util.Linkify
import android.text.method.ScrollingMovementMethod
import android.widget.Button
import android.widget.EditText
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ProgressBar
import android.widget.TextView
import android.widget.Toast
import android.view.View
import android.view.ViewGroup
import android.view.MotionEvent
import android.view.DragEvent
import android.view.DragAndDropPermissions
import android.view.Gravity
import android.view.WindowManager
import android.view.animation.AnimationUtils
import android.view.animation.DecelerateInterpolator
import android.view.animation.OvershootInterpolator
import android.webkit.JavascriptInterface
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.appcompat.app.AppCompatActivity
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.widget.SwitchCompat
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import androidx.core.content.FileProvider
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import android.provider.MediaStore
import android.provider.OpenableColumns
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.codescanner.GmsBarcodeScannerOptions
import com.google.mlkit.vision.codescanner.GmsBarcodeScanning
import org.json.JSONArray
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.net.URLEncoder
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.IOException
import java.io.InputStream
import java.nio.charset.StandardCharsets
import java.security.SecureRandom
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.UUID
import java.util.WeakHashMap
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean

private data class DiscoveredLaptop(
    val deviceId: String,
    val address: String,
    val deviceName: String,
    val manufacturer: String,
    val model: String,
    val macAddress: String,
    val fingerprint: String,
    val port: Int,
    val addresses: List<String> = listOf(address)
)

private data class DiscoveryRowRefs(
    val row: LinearLayout,
    val signal: TextView,
    val status: TextView,
    val spinner: ProgressBar,
    val connectButton: TextView,
    val steps: LinearLayout,
    val stepLabels: List<TextView>
)

private data class HardwareCardRefs(
    val root: View,
    val usageValue: TextView,
    val usageBar: ProgressBar,
    val temperatureValue: TextView,
    val temperatureBar: ProgressBar
)

private data class RemoteGpu(
    val model: String,
    val usagePercent: Int?,
    val temperatureC: Int?
)

class MainActivity : AppCompatActivity() {
    private val worker = Executors.newSingleThreadScheduledExecutor()
    private val screenWorker = Executors.newSingleThreadExecutor()
    private val remoteShellWorker = Executors.newSingleThreadExecutor()
    private val discoveryWorker = Executors.newSingleThreadExecutor()
    private val dataWorker = Executors.newFixedThreadPool(2)
    private val main = Handler(Looper.getMainLooper())
    private val screenRefresh = Handler(Looper.getMainLooper())
    private val remoteShellPollHandler = Handler(Looper.getMainLooper())
    private val remoteShellPollInFlight = AtomicBoolean(false)
    @Volatile private var remoteShellSessionId = ""
    @Volatile private var remoteShellCursor = 0L
    private val remoteShellPollRunnable = object : Runnable {
        override fun run() = pollRemoteShell()
    }
    @Volatile private var address = ""
    @Volatile private var key = ""
    @Volatile private var laptopOnline = false
    @Volatile private var androidJokeNotificationShown = false
    @Volatile private var androidJokeActiveOnWindows = false
    private var androidJokeDialog: Dialog? = null
    private var androidJokeAnimator: android.animation.AnimatorSet? = null
    private val androidJokeAutoDismiss = Handler(Looper.getMainLooper())
    private val androidJokeDismissRunnable = Runnable { dismissAndroidJokeOverlay() }
    @Volatile private var laptopOnAcPower = false
    private var jokeEnabled = false
    private var wasOnline: Boolean? = null
    private var lastConnectionChimeAt = 0L
    private var lastConnectionChimeSuccess: Boolean? = null
    private val discoveryRunning = AtomicBoolean(false)
    private val troubleshootingRunning = AtomicBoolean(false)
    private val troubleshootingLogLock = Any()
    private val troubleshootingLogLines = mutableListOf<String>()
    @Volatile private var motionProcessStatus = "Belum dimuat"
    private val pairingRunning = AtomicBoolean(false)
    private val lanRouteSwitchRunning = AtomicBoolean(false)
    private val discoveredDevices = linkedMapOf<String, DiscoveredLaptop>()
    private val discoveryRows = linkedMapOf<String, DiscoveryRowRefs>()
    @Volatile private var latestDiscoveredDevicesPayload = "[]"
    private var lastWindowsProcesses: List<RemoteProcess> = emptyList()
    private var selectedDiscoveryDevice: DiscoveredLaptop? = null
    private var selectedDiscoveryStatus: TextView? = null
    private var failedDiscoveryDeviceId: String? = null
    private var pendingConnectionDeviceId: String? = null
    @Volatile private var lastDiscoveryAttemptAt = 0L
    private lateinit var prefs: SharedPreferences
    private lateinit var addressInput: EditText
    private lateinit var keyInput: EditText
    private lateinit var fingerprintInput: EditText
    private lateinit var screenPreview: ImageView
    private lateinit var screenTextInput: EditText
    private val screenFrameBusy = AtomicBoolean(false)
    private var screenActive = false
    private var screenPending = false
    private var screenPasswordAccessAvailable = false
    private var connectedHostIsLinux = false
    @Volatile private var screenSourceLabel: String? = null
    private var screenFullscreen = false
    private var demoScreenFullscreen = false
    private var selectedNavigationId = R.id.navDevice
    private var navigationInitialized = false
    private var navigationAnimationGeneration = 0
    private var statusPillAnimationGeneration = 0
    private var currentStatusPillState: String? = null
    private val viewAnimationGenerations = WeakHashMap<View, Int>()
    private val progressAnimators = mutableMapOf<Int, ObjectAnimator>()
    private var hardwareCardsSignature = ""
    private var cpuHardwareCard: HardwareCardRefs? = null
    private val gpuHardwareCards = mutableListOf<HardwareCardRefs>()
    private var fanSpecChip: TextView? = null
    private var pendingAssistantIntent: Intent? = null
    private var processSearchFilter = ""
    private var lastPointerSentAt = 0L
    private val events = mutableListOf<HistoryItem>()
    private lateinit var adapter: HistoryAdapter
    private val clock = SimpleDateFormat("HH:mm", Locale.getDefault())
    private lateinit var cpuRing: MetricRingView
    private lateinit var ramRing: MetricRingView
    private lateinit var batteryRing: MetricRingView
    private lateinit var motionDemoWebView: WebView
    @Volatile private var latestStatusPayload = "{\"online\":false}"
    @Volatile private var latestHistoryPayload = "[]"
    @Volatile private var latestMemoryPayload = "{}"
    @Volatile private var motionDemoPageReady = false
    private var currentMotionDemoAsset = ""
    private var temporaryNativeUiForWebAction = false
    private var manualConnectionPageOpen = false
    private var updatingMonitorSwitch = false
    private var updatingJokeOverlaySwitch = false
    private val armedProcessKills = mutableMapOf<Int, Runnable>()
    private val maximumShareFileBytes = 25 * 1024 * 1024
    private var pendingTroubleshootLog = ""
    private var nativeShellDialog: AlertDialog? = null
    private var nativeShellOutput: TextView? = null
    private var nativeShellStatus: TextView? = null
    private var nativeShellInput: EditText? = null
    private var nativeShellScroll: android.widget.ScrollView? = null
    private var nativeShellStartButton: TextView? = null
    private var nativeShellStopButton: TextView? = null
    private var nativeShellSendButton: TextView? = null
    private val shareFilePicker = registerForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) sendShareFile(uri)
    }
    private val troubleshootLogFilePicker = registerForActivityResult(ActivityResultContracts.CreateDocument("text/plain")) { uri ->
        val log = pendingTroubleshootLog
        pendingTroubleshootLog = ""
        if (uri == null) {
            publishTroubleshootActionStatus("Penyimpanan file log dibatalkan.")
            return@registerForActivityResult
        }
        try {
            contentResolver.openOutputStream(uri, "wt")?.use { output -> output.write(log.toByteArray(StandardCharsets.UTF_8)) }
                ?: throw IOException("File log tidak dapat dibuat.")
            publishTroubleshootActionStatus("File .log berhasil disimpan.")
            appendMotionTroubleshootLog("${diagnosticTimestamp()} I/EXPORT log tersimpan melalui pemilih file Android")
        } catch (error: Exception) {
            publishTroubleshootActionStatus(error.message ?: "File log gagal disimpan.")
            appendMotionTroubleshootLog("${diagnosticTimestamp()} E/EXPORT ${error.javaClass.simpleName} message=${error.message ?: "no detail"}")
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        prefs = getSharedPreferences("connection", MODE_PRIVATE)
        prefs.edit().putBoolean("mainActivityVisible", true).apply()
        if (prefs.getBoolean("classicTheme", false)) setTheme(R.style.Theme_LaptopStatus_Classic)
        super.onCreate(savedInstanceState)
        pendingAssistantIntent = intent.takeIf {
            it.hasExtra("assistant_feature") || it.hasExtra("assistant_query") || it.hasExtra("assistant_control") || it.hasExtra("feature") || it.hasExtra("name")
        }
        setContentView(R.layout.activity_main)
        if (intent.getBooleanExtra(MonitorService.EXTRA_OPEN_JOKE_OVERLAY_PERMISSION, false)) {
            findViewById<View>(R.id.appRoot).post { setJokeOverlayEnabled(true) }
        }
        applyClassicTheme()
        setupMetricRings()
        applyMotionAnimationPreference()
        clearRetiredAppJokeData()
        addressInput = findViewById(R.id.laptopAddress)
        keyInput = findViewById(R.id.pairingKey)
        fingerprintInput = findViewById(R.id.screenFingerprint)
        screenPreview = findViewById(R.id.screenPreview)
        screenTextInput = findViewById(R.id.screenTextInput)
        findViewById<SwitchCompat>(R.id.lowBatteryAlertsSwitch).apply {
            isChecked = prefs.getBoolean("lowBatteryAlerts", true)
            setOnCheckedChangeListener { _, enabled ->
                prefs.edit().putBoolean("lowBatteryAlerts", enabled).apply()
            }
        }
        findViewById<SwitchCompat>(R.id.connectionChimeSwitch).apply {
            isChecked = prefs.getBoolean("connectionChimes", true)
            setOnCheckedChangeListener { _, enabled ->
                prefs.edit().putBoolean("connectionChimes", enabled).apply()
            }
        }
        findViewById<SwitchCompat>(R.id.latestPowerPopupSwitch).apply {
            isChecked = prefs.getBoolean("latestPowerPopupOnOldUi", false)
            setOnCheckedChangeListener { _, enabled ->
                prefs.edit().putBoolean("latestPowerPopupOnOldUi", enabled).apply()
            }
        }
        findViewById<SwitchCompat>(R.id.backgroundMonitorSwitch).apply {
            isChecked = prefs.getBoolean("backgroundMonitor", false)
            setOnCheckedChangeListener { _, enabled ->
                if (!updatingMonitorSwitch) setBackgroundMonitoring(enabled)
            }
        }
        findViewById<SwitchCompat>(R.id.androidJokeOverlaySwitch).apply {
            isChecked = prefs.getBoolean("androidJokeOverlayEnabled", false)
            setOnCheckedChangeListener { _, enabled ->
                if (!updatingJokeOverlaySwitch) setJokeOverlayEnabled(enabled)
            }
        }
        findViewById<SwitchCompat>(R.id.motionAnimationsSwitch).apply {
            isChecked = prefs.getBoolean("motionAnimations", true)
            setOnCheckedChangeListener { _, enabled ->
                prefs.edit().putBoolean("motionAnimations", enabled).apply()
                applyMotionAnimationPreference()
            }
        }
        findViewById<SwitchCompat>(R.id.classicThemeSwitch).apply {
            isChecked = prefs.getBoolean("classicTheme", false)
            setOnCheckedChangeListener { _, enabled ->
                prefs.edit().putBoolean("classicTheme", enabled).apply()
                applyClassicTheme()
                if (::motionDemoWebView.isInitialized && prefs.getBoolean("motionDemoUi", true)) loadMotionDemoPage()
            }
        }
        findViewById<SwitchCompat>(R.id.motionDemoUiSwitch).apply {
            isChecked = prefs.getBoolean("motionDemoUi", true)
            setOnCheckedChangeListener { _, enabled ->
                prefs.edit().putBoolean("motionDemoUi", enabled).apply()
                temporaryNativeUiForWebAction = false
                applyMotionDemoMode()
            }
        }
        findViewById<Button>(R.id.restorePreviousUiButton).setOnClickListener {
            if (temporaryNativeUiForWebAction) returnToMotionDemoAfterNativeAction() else restorePreviousNativeUi()
        }
        createNotificationChannel()
        requestNotificationPermission()
        animateEntrance()
        addressInput.setText(prefs.getString("address", ""))
        keyInput.setText(prefs.getString("key", ""))
        fingerprintInput.setText(prefs.getString("fingerprint", ""))
        address = addressInput.text.toString().trim()
        key = keyInput.text.toString().trim()
        logLiveActivity("APP", "session-start version=${BuildConfig.VERSION_NAME} ui=${if (prefs.getBoolean("motionDemoUi", true)) "web" else "native"}")

        adapter = HistoryAdapter(events)
        restoreHistory()
        findViewById<RecyclerView>(R.id.historyList).apply {
            layoutManager = LinearLayoutManager(this@MainActivity)
            adapter = this@MainActivity.adapter
        }
        findViewById<Button>(R.id.clearHistoryButton).setOnClickListener { confirmClearHistory() }
        findViewById<Button>(R.id.connectButton).setOnClickListener {
            address = addressInput.text.toString().trim().trimEnd('/')
            key = keyInput.text.toString().trim()
            val fingerprint = normalizeFingerprint(fingerprintInput.text.toString())
            fingerprintInput.setText(fingerprint)
            if (fingerprint.length != 64 || address.isBlank() || key.isBlank()) {
                findViewById<TextView>(R.id.connectionInfo).text = when {
                    address.isBlank() -> "Masukkan alamat IP laptop."
                    key.isBlank() -> "Masukkan kunci pairing dari aplikasi Windows."
                    else -> "Masukkan fingerprint TLS 64 karakter dari Pengaturan Windows."
                }
                return@setOnClickListener
            }
            prefs.edit().putString("address", address).putString("key", key).putString("fingerprint", fingerprint).apply()
            findViewById<TextView>(R.id.connectionInfo).text = "Mencoba menghubungkan..."
            findViewById<View>(R.id.manualConnectionProgressRow).visibility = View.VISIBLE
            findViewById<Button>(R.id.connectButton).apply { isEnabled = false; text = "Menghubungkan..." }
            worker.execute { poll() }
        }
        findViewById<Button>(R.id.discoverButton).setOnClickListener {
            findViewById<TextView>(R.id.discoveryStatus).text = "Memindai perangkat di jaringan Wi-Fi lokal..."
            requestLaptopDiscovery()
        }
        findViewById<Button>(R.id.scanQrButton).setOnClickListener { scanLaptopQrCode() }
        findViewById<Button>(R.id.tailscaleGuideButton).setOnClickListener { showTailscaleGuide() }
        findViewById<Button>(R.id.manualSetupButton).setOnClickListener {
            val manual = findViewById<View>(R.id.manualConnectionControls)
            manual.visibility = if (manual.visibility == View.VISIBLE) View.GONE else View.VISIBLE
            findViewById<Button>(R.id.manualSetupButton).text = if (manual.visibility == View.VISIBLE) "Sembunyikan pengaturan manual" else "Pengaturan koneksi manual"
            if (manual.visibility == View.VISIBLE) {
                manual.post { findViewById<android.widget.ScrollView>(R.id.contentScroll).smoothScrollTo(0, manual.bottom) }
            }
        }
        findViewById<Button>(R.id.refreshMemoryButton).setOnClickListener { loadMemoryDetails() }
        findViewById<Button>(R.id.refreshProcessesButton).setOnClickListener { loadWindowsProcesses() }
        findViewById<TextView>(R.id.nativeTroubleshootingLog).movementMethod = ScrollingMovementMethod.getInstance()
        findViewById<Button>(R.id.nativeTroubleshootButton).setOnClickListener { runConnectionTroubleshooting() }
        findViewById<Button>(R.id.nativeShellButton).setOnClickListener { showRemoteShellDialog() }
        findViewById<Button>(R.id.nativeCopyLogButton).setOnClickListener { copyTroubleshootLog(currentTroubleshootingLog()) }
        findViewById<Button>(R.id.nativeSaveLogButton).setOnClickListener { saveTroubleshootLog(currentTroubleshootingLog()) }
        findViewById<Button>(R.id.nativeDeveloperLogButton).setOnClickListener { shareTroubleshootLogWithDeveloper(currentTroubleshootingLog()) }
        findViewById<Button>(R.id.nativePcLogButton).setOnClickListener { sendTroubleshootLogToPc(currentTroubleshootingLog()) }
        findViewById<EditText>(R.id.processSearchInput).addTextChangedListener(object : android.text.TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) = Unit
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {
                processSearchFilter = s?.toString().orEmpty().trim()
                renderWindowsProcesses(lastWindowsProcesses, processSearchFilter)
            }
            override fun afterTextChanged(s: android.text.Editable?) = Unit
        })
        findViewById<Button>(R.id.shareFileButton).setOnClickListener { showFileShareOptions() }
        findViewById<Button>(R.id.shareClipboardButton).setOnClickListener { showClipboardShareOptions() }
        findViewById<View>(R.id.shareDropTarget).setOnDragListener { _, event -> handleShareDrop(event) }
        findViewById<Button>(R.id.toggleProcessListButton).setOnClickListener { toggleProcessList() }
        findViewById<Button>(R.id.ramActionButton).setOnClickListener { showRamActionPicker() }
        findViewById<Button>(R.id.schedulePowerButton).setOnClickListener { showSchedulePicker() }
        findViewById<Button>(R.id.cancelScheduleButton).setOnClickListener { cancelScheduledPower() }
        findViewById<Button>(R.id.guideButton).setOnClickListener { safeShowOnboardingGuide() }
        findViewById<Button>(R.id.assistantGuideButton).setOnClickListener { showAssistantGuide() }
        updateMonitorButton()
        findViewById<Button>(R.id.disconnectLaptopButton).setOnClickListener { confirmDisconnectLaptop() }
        findViewById<View>(R.id.navDevice).setOnClickListener { selectNavigation(R.id.navDevice) }
        findViewById<View>(R.id.navHistory).setOnClickListener { selectNavigation(R.id.navHistory) }
        findViewById<View>(R.id.navDetails).setOnClickListener { selectNavigation(R.id.navDetails) }
        findViewById<View>(R.id.navDeviceDetails).setOnClickListener { selectNavigation(R.id.navDeviceDetails) }
        findViewById<View>(R.id.navSchedule).setOnClickListener { selectNavigation(R.id.navSchedule) }
        findViewById<View>(R.id.navAccount).setOnClickListener { selectNavigation(R.id.navAccount) }
        selectNavigation(R.id.navDevice)
        findViewById<Button>(R.id.remoteShutdown).setOnClickListener {
            confirmControl("shutdown", "Matikan Windows?", "Laptop akan dimatikan setelah 10 detik. Pastikan pekerjaan sudah disimpan.")
        }
        findViewById<Button>(R.id.remoteRestart).setOnClickListener {
            confirmControl("restart", "Restart Windows?", "Laptop akan restart setelah 10 detik. Pastikan pekerjaan sudah disimpan.")
        }
        findViewById<Button>(R.id.remoteSleep).setOnClickListener {
            confirmControl("sleep", "Sleep Windows?", "Laptop akan masuk mode sleep sekarang.")
        }
        findViewById<Button>(R.id.jokeButton).setOnClickListener {
            if (connectedHostIsLinux) {
                findViewById<TextView>(R.id.connectionInfo).text = "Mode Joke belum tersedia untuk Linux."
                return@setOnClickListener
            }
            val action = if (jokeEnabled) "joke-off" else "joke-on"
            if (jokeEnabled) worker.execute { sendControl(action) }
            else confirmControl(action, "Aktifkan Mode Joke?", "Layar laptop akan merah selama maksimal 30 detik. Pesan di layar akan menjelaskan bahwa ini hanya simulasi.")
        }
        findViewById<Button>(R.id.screenRequestButton).setOnClickListener {
            if (connectedHostIsLinux) {
                findViewById<TextView>(R.id.connectionInfo).text = "Kendali layar Linux belum tersedia."
                return@setOnClickListener
            }
            if (screenActive || screenPending) {
                findViewById<TextView>(R.id.connectionInfo).text = "Sesi layar sudah aktif atau menunggu persetujuan Windows."
            } else {
                AlertDialog.Builder(this)
                    .setTitle("Mulai kendali layar")
                    .setMessage(if (screenPasswordAccessAvailable)
                        "Pilih persetujuan langsung di laptop, atau masukkan password khusus LaptopStatus yang telah diaktifkan di Windows. Sesi dibatasi 10 menit."
                    else
                        "Windows akan menampilkan permintaan persetujuan. Akses dengan password khusus belum diaktifkan di Windows. Sesi dibatasi 10 menit.")
                    .setNegativeButton("Batal", null)
                    .setPositiveButton("Minta persetujuan") { _, _ -> screenWorker.execute { sendScreenControl("screen-request") } }
                    .setNeutralButton(if (screenPasswordAccessAvailable) "Pakai password" else "Atur di Windows") { _, _ ->
                        if (screenPasswordAccessAvailable) showScreenPasswordDialog()
                        else findViewById<TextView>(R.id.connectionInfo).text = "Aktifkan akses password LaptopStatus di halaman Pengaturan aplikasi Windows terlebih dahulu."
                    }
                    .show()
            }
        }
        findViewById<Button>(R.id.screenStopButton).setOnClickListener {
            if (screenFullscreen) setScreenFullscreen(false)
            screenWorker.execute { sendScreenControl("screen-stop") }
            returnToMotionDemoAfterNativeAction()
        }
        findViewById<Button>(R.id.screenFullscreenButton).setOnClickListener {
            setScreenFullscreen(!screenFullscreen)
        }
        findViewById<Button>(R.id.screenSendTextButton).setOnClickListener {
            val text = screenTextInput.text.toString()
            if (text.isNotEmpty()) screenWorker.execute { sendScreenText(text) }
        }
        screenPreview.setOnTouchListener { _, event -> handleScreenTouch(event) }
        screenRefresh.post(screenRefreshRunnable)
        worker.scheduleWithFixedDelay({ poll() }, 0, 5, java.util.concurrent.TimeUnit.SECONDS)
        queueDiscovery()
        findViewById<View>(R.id.appRoot).post {
            if (!prefs.getBoolean("onboardingCompleted", false) && !prefs.getBoolean("motionDemoUi", true)) safeShowOnboardingGuide()
            else consumePendingAssistantIntent()
        }
        configureMotionDemoWebView()
    }

    private fun animateEntrance() {
        val scroll = findViewById<android.widget.ScrollView>(R.id.contentScroll)
        scroll.animate().cancel()
        if (!areMotionAnimationsEnabled()) {
            scroll.alpha = 1f
            scroll.translationY = 0f
            val content = scroll.getChildAt(0) as? ViewGroup ?: return
            for (index in 0 until content.childCount) {
                content.getChildAt(index).apply {
                    animate().cancel()
                    alpha = 1f
                    translationY = 0f
                }
            }
            return
        }
        scroll.alpha = 0f
        scroll.translationY = 12f
        val spring = AnimationUtils.loadInterpolator(this, R.interpolator.ios_spring)
        scroll.animate().alpha(1f).translationY(0f).setDuration(460).setInterpolator(spring).start()
        val content = scroll.getChildAt(0) as? ViewGroup ?: return
        for (index in 0 until content.childCount) {
            val child = content.getChildAt(index)
            child.animate().cancel()
            if (child.visibility != View.VISIBLE) {
                child.alpha = 1f
                child.translationY = 0f
                continue
            }
            child.alpha = 0f
            child.translationY = dp(18).toFloat()
            child.animate().alpha(1f).translationY(0f)
                .setStartDelay((index * 48L).coerceAtMost(240L))
                .setDuration(520)
                .setInterpolator(spring)
                .start()
        }
    }

    private fun selectNavigation(selectedId: Int) {
        if (screenFullscreen) return
        val generation = ++navigationAnimationGeneration
        val previousNavigationId = selectedNavigationId
        if (navigationInitialized && previousNavigationId != selectedId) {
            val page = when (selectedId) {
                R.id.navHistory -> "history"
                R.id.navDetails -> "statistics"
                R.id.navDeviceDetails -> "device_details"
                R.id.navSchedule -> "schedule"
                R.id.navAccount -> "account"
                else -> "device"
            }
            logLiveActivity("NAV", "open=$page")
        }
        val animatePageChanges = navigationInitialized && previousNavigationId != selectedId && areMotionAnimationsEnabled()
        val direction = if (selectedId in intArrayOf(R.id.navDevice, R.id.navHistory, R.id.navDetails, R.id.navDeviceDetails, R.id.navSchedule, R.id.navAccount)) {
            val index = { id: Int -> when (id) { R.id.navDevice -> 0; R.id.navHistory -> 1; R.id.navDetails -> 2; R.id.navDeviceDetails -> 3; R.id.navSchedule -> 4; else -> 5 } }
            if (index(selectedId) >= index(previousNavigationId)) 1f else -1f
        } else 1f
        selectedNavigationId = selectedId
        val motionEnabled = areMotionAnimationsEnabled()
        val spring = if (motionEnabled) AnimationUtils.loadInterpolator(this, R.interpolator.ios_spring) else null
        val pageTitle = findViewById<TextView>(R.id.pageTitle)
        val pageSubtitle = findViewById<TextView>(R.id.pageSubtitle)
        val headerTitle = when (selectedId) {
            R.id.navHistory -> "Riwayat"
            R.id.navDetails -> "Statistik"
            R.id.navDeviceDetails -> "Rincian perangkat"
            R.id.navSchedule -> "Jadwal"
            R.id.navAccount -> "Akun"
            else -> "Perangkat"
        }
        val headerSubtitle = when (selectedId) {
            R.id.navHistory -> "Aktivitas terbaru"
            R.id.navDetails -> "Statistik sistem dan troubleshooting"
            R.id.navDeviceDetails -> "Spesifikasi, GPU, dan sensor laptop"
            R.id.navSchedule -> "Atur waktu kontrol daya"
            R.id.navAccount -> "Preferensi dan koneksi"
            else -> "Pantau dan kendalikan laptop"
        }
        val headerTextChanged = pageTitle.text != headerTitle || pageSubtitle.text != headerSubtitle
        pageTitle.animate().cancel()
        pageSubtitle.animate().cancel()
        if (headerTextChanged) {
            if (animatePageChanges) {
                val spring = AnimationUtils.loadInterpolator(this, R.interpolator.ios_spring)
                pageTitle.animate().alpha(0f).translationX(-dp(8).toFloat()).setDuration(110L).withEndAction {
                    if (generation != navigationAnimationGeneration) return@withEndAction
                    pageTitle.text = headerTitle
                    pageSubtitle.text = headerSubtitle
                    pageTitle.translationX = dp(8).toFloat()
                    pageSubtitle.alpha = 0f
                    pageSubtitle.translationX = 0f
                    pageTitle.animate().alpha(1f).translationX(0f).setDuration(280L).setInterpolator(spring).start()
                    pageSubtitle.animate().alpha(1f).setDuration(250L).setInterpolator(spring).start()
                }.start()
                pageSubtitle.animate().alpha(0f).setDuration(110L).start()
            } else {
                pageTitle.text = headerTitle
                pageSubtitle.text = headerSubtitle
                pageTitle.alpha = 1f
                pageTitle.translationX = 0f
                pageSubtitle.alpha = 1f
                pageSubtitle.translationX = 0f
            }
        } else {
            pageTitle.alpha = 1f
            pageTitle.translationX = 0f
            pageSubtitle.alpha = 1f
            pageSubtitle.translationX = 0f
        }
        val navIds = intArrayOf(R.id.navDevice, R.id.navHistory, R.id.navDetails, R.id.navDeviceDetails, R.id.navSchedule, R.id.navAccount)
        navIds.forEach { id ->
            val item = findViewById<ViewGroup>(id)
            val selected = id == selectedId
            item.background = if (selected) ContextCompat.getDrawable(this,
                if (prefs.getBoolean("classicTheme", false)) R.drawable.bg_nav_selected_classic else R.drawable.bg_nav_selected) else null
            item.animate().cancel()
            item.scaleX = if (selected) 0.97f else 1f
            item.scaleY = if (selected) 0.97f else 1f
            if (motionEnabled) {
                item.animate().translationY(if (selected) -dp(2).toFloat() else 0f).scaleX(1f).scaleY(1f).setDuration(300)
                    .setInterpolator(spring).start()
            } else {
                item.translationY = 0f
                item.scaleX = 1f
                item.scaleY = 1f
            }
            if (item.childCount > 1) {
                (item.getChildAt(1) as? TextView)?.setTextColor(if (selected) getColor(R.color.accent) else getColor(R.color.text_dim))
            }
            if (item.childCount > 0) item.getChildAt(0).alpha = if (selected) 1f else 0.62f
        }

        val pageSections = mapOf(
            R.id.deviceSummaryCard to (selectedId == R.id.navDevice),
            R.id.screenControlPanel to (selectedId == R.id.navDevice),
            R.id.memoryPanel to (selectedId == R.id.navDetails),
            R.id.processPanel to (selectedId == R.id.navDetails),
            R.id.troubleshootingPanel to (selectedId == R.id.navDetails),
            R.id.telemetryPanel to (selectedId == R.id.navDeviceDetails),
            R.id.sharePanel to (selectedId == R.id.navDetails),
            R.id.ramMapPanel to (selectedId == R.id.navDetails),
            R.id.powerControlPanel to (selectedId == R.id.navDevice),
            R.id.scheduleSectionTitle to (selectedId == R.id.navSchedule),
            R.id.schedulePanel to (selectedId == R.id.navSchedule),
            R.id.historySectionTitle to (selectedId == R.id.navHistory),
            R.id.historyList to (selectedId == R.id.navHistory),
            R.id.accountSettingsPanel to (selectedId == R.id.navAccount),
            R.id.connectionPanel to (selectedId == R.id.navDevice)
        )
        var enteringIndex = 0
        pageSections.forEach { (id, visible) ->
            val section = findViewById<View>(id)
            section.animate().cancel()
            if (visible) {
                section.visibility = View.VISIBLE
                if (animatePageChanges && !isPageSectionVisible(id, previousNavigationId)) {
                    section.alpha = 0f
                    section.translationX = widthForSlide(section) * direction
                    section.translationY = 0f
                    section.animate().alpha(1f).translationX(0f)
                        .setStartDelay((enteringIndex++ * 32L).coerceAtMost(96L))
                        .setDuration(360L).setInterpolator(spring).withEndAction {
                            if (generation != navigationAnimationGeneration) return@withEndAction
                            section.alpha = 1f
                            section.translationX = 0f
                            section.translationY = 0f
                        }.start()
                } else {
                    section.alpha = 1f
                    section.translationY = 0f
                    section.translationX = 0f
                }
            } else {
                if (animatePageChanges && isPageSectionVisible(id, previousNavigationId) && section.visibility == View.VISIBLE) {
                    section.animate().alpha(0f).translationX(-widthForSlide(section) * direction * 0.18f).setDuration(210L)
                        .withEndAction {
                            if (generation == navigationAnimationGeneration && !isPageSectionVisible(id, selectedNavigationId)) {
                                section.visibility = View.GONE
                                section.alpha = 1f
                                section.translationY = 0f
                                section.translationX = 0f
                            }
                        }.start()
                } else {
                    section.visibility = View.GONE
                    section.alpha = 1f
                    section.translationY = 0f
                    section.translationX = 0f
                }
            }
        }
        navigationInitialized = true
        if (selectedId == R.id.navSchedule && laptopOnline) loadSchedule()
        if (selectedId == R.id.navDetails && laptopOnline) loadMemoryDetails()
        val scroll = findViewById<android.widget.ScrollView>(R.id.contentScroll)
        if (motionEnabled) scroll.smoothScrollTo(0, 0) else scroll.scrollTo(0, 0)
    }

    private fun widthForSlide(section: View): Float =
        (section.width.takeIf { it > 0 } ?: findViewById<View>(R.id.contentScroll).width).toFloat().coerceAtLeast(dp(80).toFloat())

    private fun isPageSectionVisible(viewId: Int, navigationId: Int): Boolean = when (viewId) {
        R.id.deviceSummaryCard, R.id.screenControlPanel, R.id.powerControlPanel -> navigationId == R.id.navDevice
        R.id.memoryPanel, R.id.processPanel, R.id.ramMapPanel, R.id.troubleshootingPanel -> navigationId == R.id.navDetails
        R.id.telemetryPanel -> navigationId == R.id.navDeviceDetails
        R.id.sharePanel -> navigationId == R.id.navDetails
        R.id.scheduleSectionTitle, R.id.schedulePanel -> navigationId == R.id.navSchedule
        R.id.historySectionTitle, R.id.historyList -> navigationId == R.id.navHistory
        R.id.accountSettingsPanel -> navigationId == R.id.navAccount
        R.id.connectionPanel -> navigationId == R.id.navDevice
        else -> false
    }

    private fun areMotionAnimationsEnabled(): Boolean =
        prefs.getBoolean("motionAnimations", true) && (Build.VERSION.SDK_INT < Build.VERSION_CODES.O || ValueAnimator.areAnimatorsEnabled())

    private fun applyMotionAnimationPreference() {
        val enabled = areMotionAnimationsEnabled()
        if (!enabled) {
            progressAnimators.values.toList().forEach { it.cancel() }
            progressAnimators.clear()
        }
        (findViewById<android.widget.FrameLayout>(R.id.discoveryRadar).getChildAt(0) as? DiscoveryRadarView)
            ?.setMotionAnimationsEnabled(enabled)
        if (::cpuRing.isInitialized) listOf(cpuRing, ramRing, batteryRing).forEach { it.setMotionAnimationsEnabled(enabled) }
        findViewById<SegmentedMemoryView>(R.id.memoryUsageBar).setMotionAnimationsEnabled(enabled)
        if (::motionDemoWebView.isInitialized && motionDemoPageReady) {
            motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.setMotionAnimations($enabled)", null)
        }
    }

    private fun applyClassicTheme() {
        val classic = prefs.getBoolean("classicTheme", false)
        findViewById<View>(android.R.id.content).setBackgroundResource(if (classic) R.drawable.bg_app_classic else R.drawable.bg_app)
        val cardIds = intArrayOf(
            R.id.deviceSummaryCard, R.id.memoryPanel, R.id.processPanel, R.id.telemetryPanel,
            R.id.sharePanel, R.id.ramMapPanel, R.id.troubleshootingPanel, R.id.schedulePanel, R.id.accountSettingsPanel,
            R.id.connectionPanel, R.id.screenControlPanel, R.id.powerControlPanel
        )
            cardIds.forEach { id -> findViewById<View>(id)?.setBackgroundResource(if (classic) R.drawable.bg_card_classic else R.drawable.bg_card) }
        findViewById<LinearLayout>(R.id.hardwareCardsContainer).setBackgroundResource(android.R.color.transparent)
        val hardwareCardBackground = if (classic) R.drawable.bg_hardware_card_classic else R.drawable.bg_hardware_card
        val hardwareContainer = findViewById<LinearLayout>(R.id.hardwareCardsContainer)
        for (index in 0 until hardwareContainer.childCount) hardwareContainer.getChildAt(index).setBackgroundResource(hardwareCardBackground)
        findViewById<View>(R.id.bottomNavigation).setBackgroundResource(if (classic) R.drawable.bg_glass_bar_classic else R.drawable.bg_glass_bar)
        findViewById<View>(R.id.shareDropTarget).setBackgroundResource(if (classic) R.drawable.bg_input_classic else R.drawable.bg_input)
        findViewById<View>(R.id.statusPill).setBackgroundResource(
            if (classic && laptopOnline) R.drawable.bg_pill_on_classic else if (classic) R.drawable.bg_pill_off_classic
            else if (laptopOnline) R.drawable.bg_pill_on else R.drawable.bg_pill_off
        )
        findViewById<View>(selectedNavigationId).setBackgroundResource(if (classic) R.drawable.bg_nav_selected_classic else R.drawable.bg_nav_selected)
    }

    private fun restorePreviousNativeUi() {
        prefs.edit().putBoolean("motionDemoUi", false).putBoolean("motionAnimations", true).apply()
        temporaryNativeUiForWebAction = false
        findViewById<SwitchCompat>(R.id.motionDemoUiSwitch).isChecked = false
        findViewById<SwitchCompat>(R.id.motionAnimationsSwitch).isChecked = true
        applyMotionAnimationPreference()
        applyMotionDemoMode()
        selectNavigation(R.id.navDevice)
    }

    private fun configureMotionDemoWebView() {
        motionDemoWebView = findViewById(R.id.motionDemoWebView)
        motionDemoWebView.settings.apply {
            javaScriptEnabled = true
            domStorageEnabled = true
            allowFileAccess = true
            allowContentAccess = false
            javaScriptCanOpenWindowsAutomatically = false
            setSupportMultipleWindows(false)
        }
        motionDemoWebView.addJavascriptInterface(MotionDemoBridge(), "Android")
        motionDemoWebView.setBackgroundColor(Color.rgb(9, 15, 24))
        motionDemoWebView.overScrollMode = View.OVER_SCROLL_NEVER
        motionDemoWebView.webViewClient = object : WebViewClient() {
            override fun onPageFinished(view: WebView, url: String) {
                super.onPageFinished(view, url)
                motionDemoPageReady = true
                syncMotionDemoPreferences()
                publishMotionDemoStatus()
                publishMotionDemoHistory()
                publishMotionDemoMemory()
                publishMotionDemoProcesses()
                publishMotionTroubleshootingLog()
                publishMotionDemoDiscoveredDevices()
                publishMotionDemoScreenState()
                installWindowsToolsInLog(view)
                if (prefs.getBoolean("motionDemoUi", true) && !prefs.getBoolean("classicTheme", false)) {
                    loadScheduleForDemoRequest(reportErrors = false)
                }
                view.requestLayout()
                view.postInvalidateOnAnimation()
                view.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.ensureVisible()", null)
            }

            override fun shouldOverrideUrlLoading(view: WebView, url: String): Boolean =
                !url.startsWith("file:///android_asset/", ignoreCase = true)
        }
        loadMotionDemoPage()
        applyMotionDemoMode()
    }

    private fun installWindowsToolsInLog(view: WebView) {
        val script = """
            (() => {
              const card = document.querySelector('#problemLog')?.parentElement;
              if (!card || document.getElementById('windowsToolsActions')) return;
              const title = document.createElement('b');
              title.textContent = 'Kontrol Windows';
              title.style.marginTop = '14px';
              title.style.display = 'block';
              const note = document.createElement('small');
              note.className = 'dim';
              note.textContent = 'Terminal berjalan dengan akun Windows biasa. Password kendali layar diminta sebelum sesi dimulai; perintah administrator tetap memerlukan UAC Windows.';
              note.style.display = 'block';
              note.style.margin = '5px 0 8px';
              const row = document.createElement('div');
              row.id = 'windowsToolsActions';
              row.className = 'log-actions';
              const appButton = document.createElement('button');
              appButton.className = 'act';
              appButton.type = 'button';
              appButton.textContent = 'Buka aplikasi Windows';
              appButton.addEventListener('click', () => window.Android?.showWindowsAppPicker?.());
              const shellButton = document.createElement('button');
              shellButton.className = 'act';
              shellButton.type = 'button';
              shellButton.textContent = 'Terminal CMD / PowerShell';
              shellButton.addEventListener('click', () => window.Android?.showRemoteShellDialog?.());
              row.append(appButton, shellButton);
              const anchor = card.querySelector('.log-actions');
              if (anchor) anchor.insertAdjacentElement('afterend', title);
              else card.append(title);
              title.insertAdjacentElement('afterend', note);
              note.insertAdjacentElement('afterend', row);
            })();
        """.trimIndent()
        view.evaluateJavascript(script, null)
    }

    private fun loadMotionDemoPage() {
        if (!::motionDemoWebView.isInitialized) return
        motionDemoPageReady = false
        val asset = if (prefs.getBoolean("classicTheme", false)) "demo-android-motion-lama.html" else "demo-android-motion.html"
        currentMotionDemoAsset = asset
        motionDemoWebView.loadUrl("file:///android_asset/$asset")
    }

    private fun applyMotionDemoMode() {
        if (!::motionDemoWebView.isInitialized) return
        val enabled = prefs.getBoolean("motionDemoUi", true) && !temporaryNativeUiForWebAction
        findViewById<View>(R.id.appRoot).visibility = if (enabled) View.GONE else View.VISIBLE
        motionDemoWebView.visibility = if (enabled) View.VISIBLE else View.GONE
        findViewById<SwitchCompat>(R.id.motionDemoUiSwitch).isChecked = prefs.getBoolean("motionDemoUi", true)
        findViewById<SwitchCompat>(R.id.motionAnimationsSwitch).isChecked = prefs.getBoolean("motionAnimations", true)
        findViewById<SwitchCompat>(R.id.classicThemeSwitch).isChecked = prefs.getBoolean("classicTheme", false)
        findViewById<Button>(R.id.restorePreviousUiButton).visibility =
            if (temporaryNativeUiForWebAction) View.VISIBLE else View.GONE
        if (temporaryNativeUiForWebAction) {
            findViewById<Button>(R.id.restorePreviousUiButton).text = "Kembali ke UI terbaru"
        }
        if (enabled) {
            val expectedAsset = if (prefs.getBoolean("classicTheme", false)) "demo-android-motion-lama.html" else "demo-android-motion.html"
            if (currentMotionDemoAsset != expectedAsset) loadMotionDemoPage()
            else if (motionDemoPageReady) syncMotionDemoPreferences()
        }
    }

    private fun toggleMotionDemoMode() {
        val enableMotionUi = !prefs.getBoolean("motionDemoUi", true)
        prefs.edit().putBoolean("motionDemoUi", enableMotionUi).putBoolean("motionAnimations", true).apply()
        temporaryNativeUiForWebAction = false
        findViewById<SwitchCompat>(R.id.motionAnimationsSwitch).isChecked = true
        applyMotionAnimationPreference()
        applyMotionDemoMode()
    }

    private fun syncMotionDemoPreferences() {
        if (!::motionDemoWebView.isInitialized || !motionDemoPageReady) return
        val motion = areMotionAnimationsEnabled()
        val classic = prefs.getBoolean("classicTheme", false)
        val background = prefs.getBoolean("backgroundMonitor", false)
        val battery = prefs.getBoolean("lowBatteryAlerts", true)
        val chime = prefs.getBoolean("connectionChimes", true)
        val jokeOverlay = prefs.getBoolean("androidJokeOverlayEnabled", false)
        val toggleStyle = prefs.getString("toggleAnimationStyle", "elastic").orEmpty().takeIf { it in setOf("elastic", "spring", "plain") } ?: "elastic"
        val toggleSlow = prefs.getBoolean("toggleAnimationSlow", false)
        motionDemoWebView.evaluateJavascript(
            "window.LaptopStatus&&window.LaptopStatus.setPreferences($motion,$classic,$background,$battery,$chime);window.LaptopStatus&&window.LaptopStatus.setJokeOverlayEnabled&&window.LaptopStatus.setJokeOverlayEnabled($jokeOverlay);window.applyToggleAnimationPreferences&&window.applyToggleAnimationPreferences(${JSONObject.quote(toggleStyle)},$toggleSlow)", null
        )
        publishMotionDemoStatus()
        publishMotionDemoHistory()
        publishMotionDemoMemory()
        publishMotionDemoProcesses()
    }

    private fun publishMotionDemoStatus() {
        if (!::motionDemoWebView.isInitialized || !motionDemoPageReady) return
        motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.updateStatus(${JSONObject.quote(latestStatusPayload)})", null)
    }

    private fun publishMotionDemoHistory() {
        if (!::motionDemoWebView.isInitialized || !motionDemoPageReady) return
        motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.setHistory(${JSONObject.quote(latestHistoryPayload)})", null)
    }

    private fun appendMotionTroubleshootLog(line: String) {
        val snapshot = synchronized(troubleshootingLogLock) {
            troubleshootingLogLines.add(line)
            while (troubleshootingLogLines.size > 800) troubleshootingLogLines.removeAt(0)
            troubleshootingLogLines.joinToString("\n")
        }
        val quoted = JSONObject.quote(line)
        main.post {
            findViewById<TextView>(R.id.nativeTroubleshootingLog).apply {
                text = snapshot
                post { scrollTo(0, ((layout?.getLineTop(lineCount) ?: 0) - height).coerceAtLeast(0)) }
            }
            val hasLog = snapshot.isNotBlank()
            listOf(R.id.nativeCopyLogButton, R.id.nativeSaveLogButton, R.id.nativeDeveloperLogButton, R.id.nativePcLogButton)
                .forEach { findViewById<Button>(it).isEnabled = hasLog }
            if (::motionDemoWebView.isInitialized) {
                motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.appendTroubleshootLog($quoted)", null)
            }
        }
    }

    private fun currentTroubleshootingLog(): String = synchronized(troubleshootingLogLock) {
        troubleshootingLogLines.joinToString("\n")
    }

    private fun logLiveActivity(component: String, message: String, level: Char = 'I') {
        val safeComponent = component.uppercase(Locale.ROOT)
            .filter { it in 'A'..'Z' || it in '0'..'9' || it == '_' }
            .take(12).ifBlank { "APP" }
        val safeMessage = message
            .replace(Regex("(?i)(token|key|password|fingerprint)=\\S+")) { match -> "${match.groupValues[1]}=<redacted>" }
            .replace(Regex("\\b(?:[0-9]{1,3}\\.){3}[0-9]{1,3}(?::[0-9]{1,5})?\\b"), "<endpoint>")
            .take(240)
        appendMotionTroubleshootLog("${diagnosticTimestamp()} $level/$safeComponent $safeMessage")
    }

    private fun publishMotionTroubleshootingLog() {
        if (!::motionDemoWebView.isInitialized || !motionDemoPageReady) return
        motionDemoWebView.evaluateJavascript(
            "window.LaptopStatus&&window.LaptopStatus.setTroubleshootLog(${JSONObject.quote(currentTroubleshootingLog())})", null
        )
    }

    private fun beginMotionTroubleshootingUi() {
        main.post {
            findViewById<Button>(R.id.nativeTroubleshootButton).apply { isEnabled = false; text = "Pemeriksaan berjalan…" }
            val hasLog = currentTroubleshootingLog().isNotBlank()
            listOf(R.id.nativeCopyLogButton, R.id.nativeSaveLogButton, R.id.nativeDeveloperLogButton, R.id.nativePcLogButton)
                .forEach { findViewById<Button>(it).isEnabled = hasLog }
            findViewById<ProgressBar>(R.id.nativeTroubleshootingProgress).apply { visibility = View.VISIBLE; progress = 8 }
            findViewById<TextView>(R.id.nativeTroubleshootingActionStatus).text = ""
            findViewById<TextView>(R.id.troubleshootingStatus).text = "Menyiapkan pemeriksaan koneksi…"
            if (::motionDemoWebView.isInitialized) {
                motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.beginTroubleshoot()", null)
            }
        }
    }

    private fun publishMotionTroubleshootProgress(percent: Int, message: String) {
        val quoted = JSONObject.quote(message)
        main.post {
            findViewById<ProgressBar>(R.id.nativeTroubleshootingProgress).apply { visibility = View.VISIBLE; progress = percent.coerceIn(0, 100) }
            findViewById<TextView>(R.id.troubleshootingStatus).text = message
            if (::motionDemoWebView.isInitialized) {
                motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.setTroubleshootProgress($percent,$quoted)", null)
            }
        }
    }

    private fun finishMotionTroubleshooting(success: Boolean, message: String) {
        val quoted = JSONObject.quote(message)
        main.post {
            findViewById<ProgressBar>(R.id.nativeTroubleshootingProgress).apply { visibility = View.VISIBLE; progress = 100 }
            findViewById<Button>(R.id.nativeTroubleshootButton).apply { isEnabled = true; text = "Periksa lagi" }
            findViewById<TextView>(R.id.troubleshootingStatus).text = message
            if (::motionDemoWebView.isInitialized) {
                motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.finishTroubleshoot($success,$quoted)", null)
            }
        }
    }

    private fun runConnectionTroubleshooting() {
        if (!troubleshootingRunning.compareAndSet(false, true)) {
            appendMotionTroubleshootLog("${diagnosticTimestamp()} W/DIAG pemeriksaan sebelumnya masih berjalan")
            return
        }
        beginMotionTroubleshootingUi()
        val deviceId = prefs.getString("activeDeviceId", "").orEmpty()
        val rememberedHost = address.ifBlank { prefs.getString("address", "").orEmpty() }.trim()
        val token = key.ifBlank {
            prefs.getString("paired_${deviceId}_token", "").orEmpty().ifBlank { prefs.getString("key", "").orEmpty() }
        }
        val fingerprint = normalizeFingerprint(
            prefs.getString("paired_${deviceId}_fingerprint", "").orEmpty()
                .ifBlank { prefs.getString("fingerprint", "").orEmpty() }
        )

        appendMotionTroubleshootLog("${diagnosticTimestamp()} I/DIAG pemeriksaan koneksi dimulai")
        publishMotionTroubleshootProgress(10, "Memeriksa jaringan Android…")
        discoveryWorker.execute {
            var tlsConnected = false
            var discovered: List<DiscoveredLaptop> = emptyList()
            try {
                val connectivity = getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager
                val activeNetwork = connectivity.activeNetwork
                val capabilities = activeNetwork?.let(connectivity::getNetworkCapabilities)
                val transports = buildList {
                    if (capabilities?.hasTransport(android.net.NetworkCapabilities.TRANSPORT_WIFI) == true) add("WIFI")
                    if (capabilities?.hasTransport(android.net.NetworkCapabilities.TRANSPORT_CELLULAR) == true) add("CELLULAR")
                    if (capabilities?.hasTransport(android.net.NetworkCapabilities.TRANSPORT_VPN) == true) add("VPN")
                    if (capabilities?.hasTransport(android.net.NetworkCapabilities.TRANSPORT_ETHERNET) == true) add("ETHERNET")
                    if (isEmpty()) add("UNKNOWN")
                }
                appendMotionTroubleshootLog("${diagnosticTimestamp()} I/NET activeNetwork=${activeNetwork != null} transports=${transports.joinToString(",")}")

                val interfaces = java.net.NetworkInterface.getNetworkInterfaces()
                if (interfaces == null) {
                    appendMotionTroubleshootLog("${diagnosticTimestamp()} W/NET tidak ada network interface yang dilaporkan Android")
                } else {
                    val interfaceList = java.util.Collections.list(interfaces)
                    if (interfaceList.isEmpty()) appendMotionTroubleshootLog("${diagnosticTimestamp()} W/NET daftar interface kosong")
                    interfaceList.forEach { networkInterface ->
                        val addresses = java.util.Collections.list(networkInterface.inetAddresses)
                            .mapNotNull { it.hostAddress?.substringBefore('%') }
                        appendMotionTroubleshootLog(
                            "${diagnosticTimestamp()} I/NET if=${networkInterface.name} up=${runCatching { networkInterface.isUp }.getOrDefault(false)} addresses=${addresses.ifEmpty { listOf("-") }.joinToString(",")}"
                        )
                    }
                }

                publishMotionTroubleshootProgress(30, "Menguji koneksi aman ke laptop…")
                if (rememberedHost.isBlank()) {
                    appendMotionTroubleshootLog("${diagnosticTimestamp()} W/TLS SKIP host laptop belum tersimpan")
                } else if (token.isBlank() || fingerprint.length != 64) {
                    appendMotionTroubleshootLog("${diagnosticTimestamp()} W/TLS SKIP kredensial pairing atau fingerprint belum tersedia")
                } else {
                    val candidates = (savedRouteCandidates(deviceId) + rememberedHost).map(String::trim).filter(String::isNotBlank).distinct()
                    candidates.forEachIndexed { index, host ->
                        if (tlsConnected) return@forEachIndexed
                        val started = android.os.SystemClock.elapsedRealtime()
                        appendMotionTroubleshootLog("${diagnosticTimestamp()} I/TLS GET https://$host:8765/api/status pin=SHA256 tersimpan attempt=${index + 1}/${candidates.size}")
                        try {
                            val data = requestLaptopStatus(host, token, fingerprint, connectTimeout = 3000, readTimeout = 3000)
                            val elapsed = android.os.SystemClock.elapsedRealtime() - started
                            tlsConnected = true
                            appendMotionTroubleshootLog("${diagnosticTimestamp()} I/TLS HTTP 200 elapsed=${elapsed}ms device=${data.optString("deviceName", "Laptop Windows")} idMatch=${deviceId.isBlank() || data.optString("deviceId") == deviceId}")
                        } catch (error: Exception) {
                            val elapsed = android.os.SystemClock.elapsedRealtime() - started
                            appendMotionTroubleshootLog("${diagnosticTimestamp()} E/TLS ${error.javaClass.simpleName} elapsed=${elapsed}ms message=${error.message ?: "no detail"}")
                        }
                    }
                }

                publishMotionTroubleshootProgress(55, "Memindai pengumuman Laptop Status di LAN…")
                appendMotionTroubleshootLog("${diagnosticTimestamp()} I/UDP bind=0.0.0.0:8766 timeout=1500ms duration=6s")
                discovered = discoverLaptop(onDiagnostics = ::appendMotionTroubleshootLog, allowConnectionChanges = false)
            } catch (error: Exception) {
                appendMotionTroubleshootLog("${diagnosticTimestamp()} E/DIAG ${error.javaClass.simpleName} message=${error.message ?: "no detail"}")
            } finally {
                troubleshootingRunning.set(false)
                val success = tlsConnected || discovered.isNotEmpty()
                val message = when {
                    tlsConnected -> "Laptop merespons pemeriksaan HTTPS. Log mentah tersedia di atas."
                    discovered.isNotEmpty() -> "${discovered.size} laptop terdeteksi lewat LAN. Log mentah tersedia di atas."
                    else -> "Tidak ada respons laptop. Periksa log mentah untuk detail koneksi."
                }
                appendMotionTroubleshootLog("${diagnosticTimestamp()} I/DIAG selesai result=${if (success) "FOUND" else "NO_RESPONSE"} tls=$tlsConnected udpDevices=${discovered.size}")
                finishMotionTroubleshooting(success, message)
            }
        }
    }

    private fun diagnosticTimestamp(): String = SimpleDateFormat("HH:mm:ss.SSS", Locale.US).format(Date())

    private fun publishTroubleshootActionStatus(message: String) {
        val quoted = JSONObject.quote(message)
        main.post {
            findViewById<TextView>(R.id.nativeTroubleshootingActionStatus).text = message
            if (::motionDemoWebView.isInitialized) {
                motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.setTroubleshootActionStatus($quoted)", null)
            }
        }
    }

    private fun cleanTroubleshootLog(log: String): String {
        if (log.isBlank()) throw IllegalArgumentException("Belum ada log untuk diproses. Jalankan pemeriksaan koneksi dahulu.")
        return log.trimEnd() + "\n"
    }

    private fun troubleshootLogFileName(): String = "LaptopStatus-diagnostik-${SimpleDateFormat("yyyyMMdd-HHmmss", Locale.US).format(Date())}.log"

    private fun copyTroubleshootLog(rawLog: String) {
        try {
            val log = cleanTroubleshootLog(rawLog)
            val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            clipboard.setPrimaryClip(ClipData.newPlainText("Laptop Status diagnostic log", log))
            publishTroubleshootActionStatus("Log mentah disalin ke clipboard Android.")
            appendMotionTroubleshootLog("${diagnosticTimestamp()} I/CLIPBOARD raw-log copied bytes=${log.toByteArray(StandardCharsets.UTF_8).size}")
        } catch (error: Exception) {
            publishTroubleshootActionStatus(error.message ?: "Log gagal disalin.")
        }
    }

    private fun saveTroubleshootLog(rawLog: String) {
        try {
            pendingTroubleshootLog = cleanTroubleshootLog(rawLog)
            troubleshootLogFilePicker.launch(troubleshootLogFileName())
        } catch (error: Exception) {
            publishTroubleshootActionStatus(error.message ?: "Log gagal disimpan.")
        }
    }

    private fun shareTroubleshootLogWithDeveloper(rawLog: String) {
        try {
            val log = cleanTroubleshootLog(rawLog)
            val directory = File(cacheDir, "diagnostic-logs")
            if (!directory.exists() && !directory.mkdirs()) throw IOException("Folder sementara untuk log tidak dapat dibuat.")
            val file = File(directory, troubleshootLogFileName())
            file.writeText(log, StandardCharsets.UTF_8)
            val uri = FileProvider.getUriForFile(this, "$packageName.fileprovider", file)
            val send = Intent(Intent.ACTION_SEND).apply {
                type = "text/plain"
                putExtra(Intent.EXTRA_SUBJECT, "Laptop Status - log diagnostik")
                putExtra(Intent.EXTRA_TEXT, "Log diagnostik Laptop Status terlampir.")
                putExtra(Intent.EXTRA_STREAM, uri)
                clipData = ClipData.newUri(contentResolver, file.name, uri)
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            }
            startActivity(Intent.createChooser(send, "Bagikan log"))
            publishTroubleshootActionStatus("Pilih aplikasi tujuan untuk membagikan log.")
            appendMotionTroubleshootLog("${diagnosticTimestamp()} I/SHARE chooser opened file=${file.name}")
        } catch (error: Exception) {
            publishTroubleshootActionStatus(error.message ?: "Menu berbagi log tidak dapat dibuka.")
            appendMotionTroubleshootLog("${diagnosticTimestamp()} E/SHARE ${error.javaClass.simpleName} message=${error.message ?: "no detail"}")
        }
    }

    private fun sendTroubleshootLogToPc(rawLog: String) {
        val log = try { cleanTroubleshootLog(rawLog) } catch (error: Exception) {
            publishTroubleshootActionStatus(error.message ?: "Belum ada log untuk dikirim.")
            return
        }
        val deviceId = prefs.getString("activeDeviceId", "").orEmpty()
        val host = address.ifBlank { prefs.getString("address", "").orEmpty() }.trim()
        val token = key.ifBlank {
            prefs.getString("paired_${deviceId}_token", "").orEmpty().ifBlank { prefs.getString("key", "").orEmpty() }
        }
        val fingerprint = normalizeFingerprint(
            prefs.getString("paired_${deviceId}_fingerprint", "").orEmpty()
                .ifBlank { prefs.getString("fingerprint", "").orEmpty() }
        )
        if (host.isBlank() || token.isBlank() || fingerprint.length != 64) {
            publishTroubleshootActionStatus("Hubungkan atau pasangkan laptop Windows sebelum mengirim log.")
            appendMotionTroubleshootLog("${diagnosticTimestamp()} E/SHARE-PC send skipped; paired endpoint unavailable")
            return
        }
        val bytes = log.toByteArray(StandardCharsets.UTF_8)
        val fileName = troubleshootLogFileName()
        publishTroubleshootActionStatus("Mengirim $fileName ke PC…")
        appendMotionTroubleshootLog("${diagnosticTimestamp()} I/SHARE-PC POST https://$host:8765/api/share/file?name=$fileName bytes=${bytes.size}")
        dataWorker.execute {
            try {
                val encodedName = URLEncoder.encode(fileName, StandardCharsets.UTF_8.name())
                val response = PinnedTls.withConnection(
                    host, 8765, "/api/share/file?name=$encodedName", "POST", token, fingerprint,
                    connectTimeoutMillis = 4000, readTimeoutMillis = 60_000
                ) { connection ->
                    connection.doOutput = true
                    connection.setFixedLengthStreamingMode(bytes.size)
                    connection.setRequestProperty("Content-Type", "text/plain; charset=utf-8")
                    connection.outputStream.use { it.write(bytes) }
                    val code = connection.responseCode
                    val body = (if (code in 200..299) connection.inputStream else connection.errorStream)
                        ?.bufferedReader(StandardCharsets.UTF_8)?.use { it.readText() }.orEmpty()
                    if (code !in 200..299) throw IOException("Windows menolak log (HTTP $code): ${runCatching { JSONObject(body).optString("error") }.getOrDefault(body).take(180)}")
                    JSONObject(body)
                }
                main.post {
                    publishTroubleshootActionStatus("Log .log terkirim ke folder berbagi LaptopStatus di Windows.")
                    appendMotionTroubleshootLog("${diagnosticTimestamp()} I/SHARE-PC HTTP 200 saved=${response.optBoolean("ok")} bytes=${response.optInt("length", bytes.size)} file=$fileName")
                }
            } catch (error: Exception) {
                main.post {
                    publishTroubleshootActionStatus(error.message ?: "Log gagal dikirim ke PC.")
                    appendMotionTroubleshootLog("${diagnosticTimestamp()} E/SHARE-PC ${error.javaClass.simpleName} message=${error.message ?: "no detail"}")
                }
            }
        }
    }

    private fun publishMotionDemoMemory() {
        if (!::motionDemoWebView.isInitialized || !motionDemoPageReady) return
        val payload = JSONObject.quote(latestMemoryPayload)
        motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.updateMemory($payload);window.LaptopStatus&&window.LaptopStatus.setProcessMemory&&window.LaptopStatus.setProcessMemory($payload)", null)
    }

    private fun publishMotionDemoProcesses() {
        if (!::motionDemoWebView.isInitialized || !motionDemoPageReady) return
        val payload = JSONArray().also { rows ->
            lastWindowsProcesses.forEach { process ->
                rows.put(JSONObject().apply {
                    put("pid", process.pid)
                    put("name", process.name)
                    put("memoryBytes", process.memoryBytes)
                    put("canTerminate", process.canTerminate)
                    put("note", process.note)
                    put("iconBase64", process.iconBase64)
                })
            }
        }.toString()
        motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.setProcesses(${JSONObject.quote(payload)},${JSONObject.quote(motionProcessStatus)})", null)
    }

    private fun terminateProcessFromDemo(pid: Int, requireConfirmation: Boolean = true) {
        val process = lastWindowsProcesses.firstOrNull { it.pid == pid }
        if (!laptopOnline || process == null) {
            android.widget.Toast.makeText(this, "Muat ulang daftar proses terlebih dahulu.", android.widget.Toast.LENGTH_SHORT).show()
            loadWindowsProcesses()
            return
        }
        if (!process.canTerminate) {
            android.widget.Toast.makeText(this, process.note.ifBlank { "Windows melindungi proses ini." }, android.widget.Toast.LENGTH_SHORT).show()
            logLiveActivity("PROC", "termination blocked pid=$pid")
            return
        }
        val requestTermination = {
            logLiveActivity("PROC", "POST /api/process/kill pid=$pid")
            dataWorker.execute {
                try {
                    val code = PinnedTls.withConnection(address, 8765, "/api/process/kill?pid=${process.pid}", "POST", key,
                        prefs.getString("fingerprint", "").orEmpty()) { connection ->
                        connection.doOutput = true
                        connection.outputStream.use { }
                        connection.responseCode
                    }
                    logLiveActivity("PROC", "POST /api/process/kill HTTP $code pid=$pid", if (code == 202) 'I' else 'W')
                    main.post {
                        val result = if (code == 202) "Permintaan dikirim ke Windows." else "Windows menolak permintaan (HTTP $code)."
                        android.widget.Toast.makeText(this, result, android.widget.Toast.LENGTH_SHORT).show()
                        loadWindowsProcesses()
                    }
                } catch (error: Exception) {
                    logLiveActivity("PROC", "POST /api/process/kill failed pid=$pid error=${error.javaClass.simpleName} detail=${error.message ?: "no detail"}", 'E')
                    main.post { android.widget.Toast.makeText(this, error.message ?: "Gagal menghentikan proses.", android.widget.Toast.LENGTH_SHORT).show() }
                }
            }
        }
        if (requireConfirmation) {
            AlertDialog.Builder(this)
                .setTitle("Hentikan ${process.name}?")
                .setMessage("Windows mungkin meminta konfirmasi administrator. Simpan pekerjaan di aplikasi tersebut terlebih dahulu.")
                .setNegativeButton("Batal") { _, _ -> logLiveActivity("PROC", "termination cancelled pid=$pid") }
                .setPositiveButton("Hentikan") { _, _ -> requestTermination() }
                .show()
        } else requestTermination()
    }

    private fun openNativePageForDemo(page: String) {
        temporaryNativeUiForWebAction = prefs.getBoolean("motionDemoUi", true)
        applyMotionDemoMode()
        val target = when (page.lowercase(Locale.ROOT)) {
            "history" -> R.id.navHistory
            "detail", "details", "process", "memory" -> R.id.navDetails
            "schedule" -> R.id.navSchedule
            "account" -> R.id.navAccount
            else -> R.id.navDevice
        }
        selectNavigation(target)
        if (page.equals("connection", true)) {
            if (findViewById<View>(R.id.manualConnectionControls).visibility != View.VISIBLE) {
                findViewById<Button>(R.id.manualSetupButton).performClick()
            }
            findViewById<android.widget.ScrollView>(R.id.contentScroll).post {
                findViewById<android.widget.ScrollView>(R.id.contentScroll).smoothScrollTo(0, findViewById<View>(R.id.connectionPanel).top)
            }
        }
    }

    private fun returnToMotionDemoAfterNativeAction() {
        if (!temporaryNativeUiForWebAction || !prefs.getBoolean("motionDemoUi", true)) return
        temporaryNativeUiForWebAction = false
        applyMotionDemoMode()
    }

    private fun hardwareLogoDataUrl(model: String, isGpu: Boolean): String {
        val normalized = model.lowercase(Locale.ROOT)
        val drawableId = if (isGpu) {
            val key = when {
                "rtx" in normalized -> "rtx"
                "gtx" in normalized -> "gtx"
                Regex("\\bmx\\s?\\d").containsMatchIn(normalized) -> "mx"
                Regex("\\bgt\\s?\\d").containsMatchIn(normalized) -> "gt"
                "arc" in normalized -> "arc"
                "uhd" in normalized -> "uhd"
                "hd graphics" in normalized -> "hd"
                "mobility" in normalized || Regex("\\bati\\b").containsMatchIn(normalized) -> "radeon_ati"
                "radeon" in normalized && Regex("\\b(?:6[0-9]0m|7[0-9]0m|8[0-9]0m|rx\\s?[5-9]\\d{3})\\b").containsMatchIn(normalized) -> "rdna"
                "radeon" in normalized -> "radeon"
                "adreno" in normalized || "snapdragon" in normalized -> "snapdragon"
                else -> null
            }
            key?.let { resources.getIdentifier("ic_hardware_$it", "drawable", packageName) } ?: 0
        } else {
            val resource = when {
                "nvidia" in normalized || "geforce" in normalized || "quadro" in normalized -> R.drawable.ic_brand_nvidia
                "amd" in normalized || "ryzen" in normalized || "radeon" in normalized -> R.drawable.ic_brand_amd
                "intel" in normalized || "iris" in normalized -> R.drawable.ic_brand_intel
                else -> 0
            }
            resource
        }
        if (drawableId == 0) return ""
        return runCatching {
            val drawable = ContextCompat.getDrawable(this, drawableId) ?: return ""
            val bitmap = Bitmap.createBitmap(96, 96, Bitmap.Config.ARGB_8888)
            val canvas = Canvas(bitmap)
            val intrinsicWidth = drawable.intrinsicWidth.takeIf { it > 0 } ?: 96
            val intrinsicHeight = drawable.intrinsicHeight.takeIf { it > 0 } ?: 96
            val scale = minOf(canvas.width.toFloat() / intrinsicWidth, canvas.height.toFloat() / intrinsicHeight)
            val width = (intrinsicWidth * scale).toInt().coerceAtLeast(1)
            val height = (intrinsicHeight * scale).toInt().coerceAtLeast(1)
            val left = (canvas.width - width) / 2
            val top = (canvas.height - height) / 2
            drawable.setBounds(left, top, left + width, top + height)
            drawable.draw(canvas)
            val bytes = ByteArrayOutputStream().use { output ->
                bitmap.compress(Bitmap.CompressFormat.PNG, 100, output)
                output.toByteArray()
            }
            bitmap.recycle()
            "data:image/png;base64," + android.util.Base64.encodeToString(bytes, android.util.Base64.NO_WRAP)
        }.getOrDefault("")
    }

    private inner class MotionDemoBridge {
        @JavascriptInterface fun uiReady() { runOnUiThread { syncMotionDemoPreferences() } }
        @JavascriptInterface fun getHistory(): String = latestHistoryPayload
        @JavascriptInterface fun getDiscoveredDevices(): String = latestDiscoveredDevicesPayload
        @JavascriptInterface fun isDiscoveryRunning(): Boolean = discoveryRunning.get()
        @JavascriptInterface fun startDiscovery() { logLiveActivity("DISC", "manual_scan_requested"); requestLaptopDiscovery() }
        @JavascriptInterface fun connectDiscoveredDevice(deviceId: String) { runOnUiThread {
            val laptop = discoveredDevices[deviceId]
            val refs = discoveryRows[deviceId]
            if (laptop == null || refs == null) {
                android.widget.Toast.makeText(this@MainActivity, "Laptop tidak lagi ditemukan. Pindai ulang jaringan.", android.widget.Toast.LENGTH_LONG).show()
                requestLaptopDiscovery()
            } else connectDiscoveredLaptop(laptop, refs.signal, refs.status, refs.spinner)
        } }
        @JavascriptInterface fun clearHistory() { runOnUiThread { confirmClearHistory() } }
        @JavascriptInterface fun getMemory(): String = latestMemoryPayload
        @JavascriptInterface fun getHardwareLogoData(model: String, isGpu: Boolean): String = hardwareLogoDataUrl(model, isGpu)
        @JavascriptInterface fun isOnline(): Boolean = laptopOnline
        @JavascriptInterface fun scanQr() { runOnUiThread { logLiveActivity("DISC", "qr_scanner_opened"); scanLaptopQrCode() } }
        @JavascriptInterface fun openConnection() { runOnUiThread {
            logLiveActivity("CONN", "connection_setup_opened")
            if (prefs.getBoolean("motionDemoUi", true) && ::motionDemoWebView.isInitialized) {
                if (motionDemoPageReady) {
                    motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.showManualConnection()", null)
                } else {
                    motionDemoWebView.postDelayed({
                        if (motionDemoPageReady) motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.showManualConnection()", null)
                        else android.widget.Toast.makeText(this@MainActivity, "UI sambungan masih dimuat. Coba tekan Hubungkan lagi.", android.widget.Toast.LENGTH_SHORT).show()
                    }, 300)
                }
            } else {
                openNativePageForDemo("connection")
            }
        } }
        @JavascriptInterface fun connectManual(host: String, pairingKey: String, tlsFingerprint: String) { runOnUiThread {
            val cleanHost = host.trim().trimEnd('/')
            val cleanKey = pairingKey.trim()
            val cleanFingerprint = normalizeFingerprint(tlsFingerprint)
            val error = when {
                cleanHost.isBlank() -> "Masukkan alamat IP atau alamat Tailscale laptop."
                cleanKey.isBlank() -> "Masukkan kunci pairing dari aplikasi Windows."
                cleanFingerprint.length != 64 -> "Masukkan fingerprint TLS 64 karakter dari Pengaturan Windows."
                else -> ""
            }
            if (error.isNotBlank()) {
                motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.showManualConnectionError(${JSONObject.quote(error)})", null)
                return@runOnUiThread
            }
            addressInput.setText(cleanHost)
            keyInput.setText(cleanKey)
            fingerprintInput.setText(cleanFingerprint)
            manualConnectionPageOpen = false
            motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.closeManualConnection()", null)
            findViewById<Button>(R.id.connectButton).performClick()
        } }
        @JavascriptInterface fun manualConnectionPageVisibility(open: Boolean) { runOnUiThread { manualConnectionPageOpen = open } }
        @JavascriptInterface fun openPage(page: String) { runOnUiThread {
            if (!prefs.getBoolean("motionDemoUi", true)) {
                openNativePageForDemo(page)
                return@runOnUiThread
            }
            val script = if (page.equals("connection", true))
                "window.LaptopStatus&&window.LaptopStatus.showManualConnection()"
            else "window.LaptopStatus&&window.LaptopStatus.navigateToPage(${JSONObject.quote(page)})"
            if (motionDemoPageReady) motionDemoWebView.evaluateJavascript(script, null)
            else motionDemoWebView.postDelayed({
                if (motionDemoPageReady) motionDemoWebView.evaluateJavascript(script, null)
                else android.widget.Toast.makeText(this@MainActivity, "UI masih dimuat. Coba lagi sebentar.", android.widget.Toast.LENGTH_SHORT).show()
            }, 300)
        } }
        @JavascriptInterface fun requestPower(action: String) { runOnUiThread {
            logLiveActivity("POWER", "confirmation_opened action=${action.lowercase(Locale.ROOT)}")
            if (!laptopOnline) {
                android.widget.Toast.makeText(this@MainActivity, "Hubungkan laptop sebelum mengirim perintah daya.", android.widget.Toast.LENGTH_LONG).show()
                return@runOnUiThread
            }
            showDemoPowerConfirmation(action, includeChargingWarning = true)
        } }
        @JavascriptInterface fun powerNeedsChargingWarning(action: String): Boolean =
            laptopOnAcPower && action.lowercase(Locale.ROOT) in setOf("matikan", "shutdown", "mulai ulang", "restart")
        @JavascriptInterface fun confirmPowerAfterWarning(action: String) { runOnUiThread { logLiveActivity("POWER", "charging_warning_accepted action=${action.lowercase(Locale.ROOT)}"); showDemoPowerConfirmation(action, includeChargingWarning = false) } }
        @JavascriptInterface fun confirmPowerFromDemo(action: String, customMessage: String) { runOnUiThread {
            val normalized = action.lowercase(Locale.ROOT)
            if (normalized !in setOf("shutdown", "restart", "sleep")) return@runOnUiThread
            logLiveActivity("POWER", "command_confirmed action=$normalized")
            if (!laptopOnline) {
                android.widget.Toast.makeText(this@MainActivity, "Laptop terputus. Perintah daya tidak dikirim.", android.widget.Toast.LENGTH_LONG).show()
                return@runOnUiThread
            }
            worker.execute { sendControl(normalized, customMessage.take(240)) }
        } }
        @JavascriptInterface fun requestJoke() { runOnUiThread { logLiveActivity("ACTION", "joke_toggle_requested"); findViewById<Button>(R.id.jokeButton).performClick() } }
        @JavascriptInterface fun requestScreen() { runOnUiThread {
            logLiveActivity("SCREEN", "control_requested")
            if (screenActive || screenPending) {
                AlertDialog.Builder(this@MainActivity).setTitle("Kendali layar")
                    .setMessage("Sesi layar sudah aktif atau sedang menunggu persetujuan di laptop.")
                    .setPositiveButton("Mengerti", null).show()
            } else {
                AlertDialog.Builder(this@MainActivity)
                    .setTitle("Mulai kendali layar")
                    .setMessage(if (screenPasswordAccessAvailable)
                        "Pilih persetujuan langsung di laptop, atau masukkan password khusus LaptopStatus yang telah diaktifkan di Windows. Sesi dibatasi 10 menit."
                    else
                        "Windows akan menampilkan permintaan persetujuan. Akses dengan password khusus belum diaktifkan di Windows. Sesi dibatasi 10 menit.")
                    .setNegativeButton("Batal", null)
                    .setPositiveButton("Minta persetujuan") { _, _ ->
                        screenWorker.execute { sendScreenControl("screen-request") }
                    }
                    .setNeutralButton(if (screenPasswordAccessAvailable) "Pakai password" else "Atur di Windows") { _, _ ->
                        if (screenPasswordAccessAvailable) showScreenPasswordDialog()
                        else android.widget.Toast.makeText(this@MainActivity, "Aktifkan akses password LaptopStatus di Pengaturan Windows.", android.widget.Toast.LENGTH_LONG).show()
                    }.show()
            }
        } }
        @JavascriptInterface fun stopScreen() { runOnUiThread {
            logLiveActivity("SCREEN", "control_stop_requested")
            screenWorker.execute { sendScreenControl("screen-stop") }
        } }
        @JavascriptInterface fun toggleFullscreen() { runOnUiThread { motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.toggleScreenFullscreen()", null) } }
        @JavascriptInterface fun setScreenControlLandscape(enabled: Boolean) { runOnUiThread {
            if (enabled) {
                demoScreenFullscreen = true
                requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
                setImmersiveSystemBars(true)
            } else if (demoScreenFullscreen) {
                demoScreenFullscreen = false
                setImmersiveSystemBars(false)
                requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_PORTRAIT
            }
        } }
        @JavascriptInterface fun sendScreenPointer(action: String, xRatio: Float, yRatio: Float) { runOnUiThread { sendMotionDemoScreenPointer(action, xRatio, yRatio) } }
        @JavascriptInterface fun sendScreenText(text: String) { runOnUiThread { screenWorker.execute { this@MainActivity.sendScreenText(text) } } }
        @JavascriptInterface fun shareFile() { runOnUiThread { logLiveActivity("SHARE", "file_picker_opened"); showFileShareOptions() } }
        @JavascriptInterface fun shareClipboard() { runOnUiThread { logLiveActivity("SHARE", "clipboard_share_opened"); showClipboardShareOptions() } }
        @JavascriptInterface fun copyTroubleshootLog(log: String) { runOnUiThread { this@MainActivity.copyTroubleshootLog(log) } }
        @JavascriptInterface fun saveTroubleshootLog(log: String) { runOnUiThread { this@MainActivity.saveTroubleshootLog(log) } }
        @JavascriptInterface fun shareTroubleshootLogWithDeveloper(log: String) { runOnUiThread { this@MainActivity.shareTroubleshootLogWithDeveloper(log) } }
        @JavascriptInterface fun sendTroubleshootLogToPc(log: String) { runOnUiThread { this@MainActivity.sendTroubleshootLogToPc(log) } }
        @JavascriptInterface fun showWindowsAppPicker() { runOnUiThread { this@MainActivity.showWindowsAppPicker() } }
        @JavascriptInterface fun showWindowsAdminShellPicker() { runOnUiThread { this@MainActivity.showWindowsAdminShellPicker() } }
        @JavascriptInterface fun showRemoteShellDialog() { runOnUiThread { this@MainActivity.showRemoteShellDialog() } }
        @JavascriptInterface fun startRemoteShell(shellType: String) { this@MainActivity.startRemoteShell(shellType) }
        @JavascriptInterface fun writeRemoteShell(text: String) { this@MainActivity.writeRemoteShell(text) }
        @JavascriptInterface fun stopRemoteShell() { this@MainActivity.stopRemoteShell() }
        @JavascriptInterface fun runRamAction() { runOnUiThread { showRamActionPicker() } }
        @JavascriptInterface fun loadProcesses(filter: String) { runOnUiThread { loadWindowsProcesses(filter) } }
        @JavascriptInterface fun terminateProcess(pid: Int) { runOnUiThread { terminateProcessFromDemo(pid) } }
        @JavascriptInterface fun confirmTerminateProcess(pid: Int) { runOnUiThread { terminateProcessFromDemo(pid, requireConfirmation = false) } }
        @JavascriptInterface fun logSessionActivity(event: String) {
            val safeEvent = event.trim().take(72)
            if (safeEvent.matches(Regex("[A-Za-z0-9_.:-]{1,72}"))) logLiveActivity("UI", safeEvent)
        }
        @JavascriptInterface fun troubleshoot() { runOnUiThread { logLiveActivity("DIAG", "connection_check_requested"); runConnectionTroubleshooting() } }
        @JavascriptInterface fun openSchedule() { runOnUiThread { showSchedulePicker() } }
        @JavascriptInterface fun createScheduleFromDemo(action: String, atUtc: String) {
            val normalized = action.lowercase(Locale.ROOT)
            val target = atUtc.toLongOrNull()
            if (normalized !in setOf("shutdown", "restart", "sleep") || target == null || target <= System.currentTimeMillis()) {
                publishDemoScheduleError("Waktu atau jenis jadwal tidak valid.")
                return
            }
            if (!laptopOnline) {
                publishDemoScheduleError("Hubungkan laptop sebelum membuat jadwal.")
                return
            }
            logLiveActivity("SCHEDULE", "create_requested action=$normalized")
            saveScheduleFromDemo(normalized, target)
        }
        @JavascriptInterface fun cancelScheduleFromDemo() { logLiveActivity("SCHEDULE", "cancel_requested"); cancelScheduleFromDemoRequest() }
        @JavascriptInterface fun loadScheduleForDemo() { loadScheduleForDemoRequest(reportErrors = true) }
        @JavascriptInterface fun loadScheduleRoutines() { loadScheduleRoutinesForDemo() }
        @JavascriptInterface fun setScheduleRoutine(index: Int, enabled: Boolean) { logLiveActivity("SCHEDULE", "routine_toggle index=$index enabled=$enabled"); setScheduleRoutineForDemo(index, enabled) }
        @JavascriptInterface fun showGuide() { runOnUiThread {
            val motionUiVisible = prefs.getBoolean("motionDemoUi", true) && !temporaryNativeUiForWebAction
            if (motionUiVisible && motionDemoPageReady && !prefs.getBoolean("classicTheme", false)) {
                motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.showGuide()", null)
            } else safeShowOnboardingGuide()
        } }
        @JavascriptInterface fun showTailscaleGuide() { runOnUiThread { this@MainActivity.showTailscaleGuide() } }
        @JavascriptInterface fun disconnect() { runOnUiThread { logLiveActivity("CONN", "disconnect_confirmation_opened"); confirmDisconnectLaptop() } }
        @JavascriptInterface fun setMotionAnimations(enabled: Boolean) { runOnUiThread {
            prefs.edit().putBoolean("motionAnimations", enabled).apply()
            findViewById<SwitchCompat>(R.id.motionAnimationsSwitch).isChecked = enabled
            applyMotionAnimationPreference()
        } }
        @JavascriptInterface fun setToggleAnimationPreferences(style: String, slow: Boolean) { runOnUiThread {
            val normalized = style.lowercase(Locale.ROOT)
            if (normalized !in setOf("elastic", "spring", "plain")) return@runOnUiThread
            prefs.edit().putString("toggleAnimationStyle", normalized).putBoolean("toggleAnimationSlow", slow).apply()
        } }
        @JavascriptInterface fun setClassicTheme(enabled: Boolean) { runOnUiThread {
            prefs.edit().putBoolean("classicTheme", enabled).apply()
            applyClassicTheme()
            loadMotionDemoPage()
        } }
        @JavascriptInterface fun setPreference(name: String, enabled: Boolean) { runOnUiThread {
            when (name) {
                "backgroundMonitor" -> { prefs.edit().putBoolean(name, enabled).apply(); setBackgroundMonitoring(enabled); updateMonitorButton() }
                "androidJokeOverlay" -> setJokeOverlayEnabled(enabled)
                "lowBatteryAlerts", "connectionChimes" -> prefs.edit().putBoolean(name, enabled).apply()
            }
        } }
        @JavascriptInterface fun returnToPreviousUi() { runOnUiThread { restorePreviousNativeUi() } }
        @JavascriptInterface fun showNativeUi() { runOnUiThread { restorePreviousNativeUi() } }
    }

    private fun showDemoPowerConfirmation(action: String, includeChargingWarning: Boolean) {
        val normalized = when (action.lowercase(Locale.ROOT)) {
            "matikan", "shutdown" -> "shutdown"
            "mulai ulang", "restart" -> "restart"
            "tidur", "sleep" -> "sleep"
            else -> return
        }
        val title = when (normalized) { "shutdown" -> "Matikan Windows?"; "restart" -> "Restart Windows?"; else -> "Sleep Windows?" }
        val message = when (normalized) {
            "shutdown" -> "Laptop akan dimatikan setelah 10 detik. Pastikan pekerjaan sudah disimpan."
            "restart" -> "Laptop akan restart setelah 10 detik. Pastikan pekerjaan sudah disimpan."
            else -> "Laptop akan masuk mode sleep sekarang."
        }
        val warning = if (includeChargingWarning && laptopOnAcPower) when (normalized) {
            "shutdown" -> "Anda sedang dalam keadaan mengisi daya. Apakah Anda yakin ingin mematikan perangkat?"
            "restart" -> "Anda sedang dalam keadaan mengisi daya. Apakah Anda yakin ingin memulai ulang perangkat?"
            else -> null
        } else null
        if (::motionDemoWebView.isInitialized && motionDemoPageReady && !prefs.getBoolean("classicTheme", false)) {
            val script = "window.LaptopStatus&&window.LaptopStatus.showPowerConfirmation(${JSONObject.quote(normalized)},${JSONObject.quote(title)},${JSONObject.quote(message)},${warning?.let { JSONObject.quote(it) } ?: "null"})"
            motionDemoWebView.evaluateJavascript(script, null)
        } else {
            showPowerConfirmation(normalized, title, message, warning)
        }
    }
    private fun showFileShareOptions() {
        if (!laptopOnline) {
            findViewById<TextView>(R.id.shareStatus).text = "Hubungkan laptop terlebih dahulu untuk berbagi file."
            return
        }
        AlertDialog.Builder(this)
            .setTitle("Berbagi file")
            .setItems(arrayOf("Pilih file dari Android untuk dikirim", "Ambil file yang disiapkan Windows")) { _, which ->
                if (which == 0) shareFilePicker.launch(arrayOf("*/*")) else receiveSharedFile()
            }
            .setNegativeButton("Batal", null)
            .show()
    }

    private fun showClipboardShareOptions() {
        if (!laptopOnline) {
            findViewById<TextView>(R.id.shareStatus).text = "Hubungkan laptop terlebih dahulu untuk berbagi clipboard."
            return
        }
        AlertDialog.Builder(this)
            .setTitle("Berbagi clipboard")
            .setItems(arrayOf("Kirim teks clipboard Android ke Windows", "Ambil teks clipboard dari Windows")) { _, which ->
                if (which == 0) sendAndroidClipboard() else receiveWindowsClipboard()
            }
            .setNegativeButton("Batal", null)
            .show()
    }

    private fun handleShareDrop(event: DragEvent): Boolean {
        val target = findViewById<View>(R.id.shareDropTarget)
        return when (event.action) {
            DragEvent.ACTION_DRAG_STARTED -> event.clipData != null
            DragEvent.ACTION_DRAG_ENTERED -> {
                target.alpha = 0.75f
                true
            }
            DragEvent.ACTION_DRAG_EXITED, DragEvent.ACTION_DRAG_ENDED -> {
                target.alpha = 1f
                true
            }
            DragEvent.ACTION_DROP -> {
                target.alpha = 1f
                val uri = event.clipData?.getItemAt(0)?.uri
                if (uri != null) sendShareFile(uri, requestDragAndDropPermissions(event)) else findViewById<TextView>(R.id.shareStatus).text = "File yang dilepas tidak dapat dibaca."
                true
            }
            else -> false
        }
    }

    private fun shareFileName(uri: Uri): String {
        var name = "file.bin"
        contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { cursor ->
            if (cursor.moveToFirst()) {
                val index = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                if (index >= 0) name = cursor.getString(index) ?: name
            }
        }
        return File(name).name.take(140).ifBlank { "file.bin" }
    }

    private fun readSharedUri(uri: Uri): ByteArray {
        val output = ByteArrayOutputStream()
        contentResolver.openInputStream(uri)?.use { input ->
            val buffer = ByteArray(16 * 1024)
            while (true) {
                val count = input.read(buffer)
                if (count < 0) break
                if (output.size() + count > maximumShareFileBytes) throw IOException("Ukuran file maksimal 25 MB.")
                output.write(buffer, 0, count)
            }
        } ?: throw IOException("Android tidak dapat membuka file yang dipilih.")
        if (output.size() == 0) throw IOException("File kosong tidak dapat dikirim.")
        return output.toByteArray()
    }

    private fun sendShareFile(uri: Uri, dragPermissions: DragAndDropPermissions? = null) {
        if (!laptopOnline) {
            dragPermissions?.release()
            findViewById<TextView>(R.id.shareStatus).text = "Hubungkan laptop terlebih dahulu untuk mengirim file."
            return
        }
        val status = findViewById<TextView>(R.id.shareStatus)
        val name = shareFileName(uri)
        status.text = "Menyiapkan $name…"
        dataWorker.execute {
            try {
                val bytes = readSharedUri(uri)
                val encodedName = URLEncoder.encode(name, StandardCharsets.UTF_8.name())
                val response = PinnedTls.withConnection(address, 8765, "/api/share/file?name=$encodedName", "POST", key,
                    prefs.getString("fingerprint", "").orEmpty(), readTimeoutMillis = 60_000) { connection ->
                    connection.doOutput = true
                    connection.setFixedLengthStreamingMode(bytes.size)
                    connection.setRequestProperty("Content-Type", "application/octet-stream")
                    connection.outputStream.use { it.write(bytes) }
                    val code = connection.responseCode
                    val body = (if (code in 200..299) connection.inputStream else connection.errorStream)?.bufferedReader()?.use { it.readText() }.orEmpty()
                    if (code !in 200..299) throw IOException(JSONObject(body).optString("error", "Gagal mengirim file."))
                    body
                }
                main.post { status.text = "$name terkirim ke laptop (${formatShareBytes(bytes.size.toLong())})." }
            } catch (error: Exception) {
                main.post { status.text = error.message ?: "File tidak dapat dikirim." }
            } finally {
                dragPermissions?.release()
            }
        }
    }

    private fun sendAndroidClipboard() {
        val status = findViewById<TextView>(R.id.shareStatus)
        val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        val text = clipboard.primaryClip?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.coerceToText(this)?.toString().orEmpty()
        if (text.isBlank()) {
            status.text = "Clipboard Android kosong atau tidak berisi teks."
            return
        }
        if (text.toByteArray(StandardCharsets.UTF_8).size > 128 * 1024) {
            status.text = "Teks clipboard maksimal 128 KB."
            return
        }
        status.text = "Mengirim teks clipboard ke laptop…"
        dataWorker.execute {
            try {
                PinnedTls.withConnection(address, 8765, "/api/share/clipboard", "POST", key,
                    prefs.getString("fingerprint", "").orEmpty()) { connection ->
                    connection.doOutput = true
                    connection.setFixedLengthStreamingMode(text.toByteArray(StandardCharsets.UTF_8).size)
                    connection.setRequestProperty("Content-Type", "text/plain; charset=utf-8")
                    connection.outputStream.use { it.write(text.toByteArray(StandardCharsets.UTF_8)) }
                    if (connection.responseCode !in 200..299) throw IOException("Clipboard gagal dikirim.")
                }
                main.post { status.text = "Clipboard Android sudah disalin ke Windows." }
            } catch (error: Exception) { main.post { status.text = error.message ?: "Clipboard gagal dikirim." } }
        }
    }

    private fun receiveSharedFile() {
        val status = findViewById<TextView>(R.id.shareStatus)
        status.text = "Memeriksa file yang disiapkan Windows…"
        dataWorker.execute {
            try {
                val pending = PinnedTls.withConnection(address, 8765, "/api/share/pending", "GET", key,
                    prefs.getString("fingerprint", "").orEmpty()) { connection ->
                    if (connection.responseCode != 200) throw IOException("Laptop tidak dapat memeriksa file.")
                    JSONObject(connection.inputStream.bufferedReader().use { it.readText() })
                }
                if (!pending.optBoolean("fileAvailable")) {
                    main.post { status.text = "Belum ada file yang dikirim dari Windows." }
                    return@execute
                }
                val id = pending.optString("fileId")
                val name = File(pending.optString("fileName")).name
                val size = pending.optLong("fileLength")
                main.post {
                    AlertDialog.Builder(this)
                        .setTitle("File dari Windows")
                        .setMessage("${name}\n${formatShareBytes(size)}")
                        .setPositiveButton("Unduh") { _, _ -> downloadSharedFile(id, name, status) }
                        .setNegativeButton("Nanti", null)
                        .show()
                }
            } catch (error: Exception) { main.post { status.text = error.message ?: "File dari Windows belum dapat diperiksa." } }
        }
    }

    private fun downloadSharedFile(id: String, name: String, status: TextView) {
        status.text = "Mengunduh $name…"
        dataWorker.execute {
            try {
                val bytes = PinnedTls.withConnection(address, 8765, "/api/share/file?id=${URLEncoder.encode(id, StandardCharsets.UTF_8.name())}", "GET", key,
                    prefs.getString("fingerprint", "").orEmpty(), readTimeoutMillis = 60_000) { connection ->
                    if (connection.responseCode != 200) throw IOException("File tidak lagi tersedia di laptop.")
                    connection.inputStream.use { readBounded(it, maximumShareFileBytes) }
                }
                val destination = saveSharedFile(name, bytes)
                acknowledgeShare("/api/share/ack?fileId=${URLEncoder.encode(id, StandardCharsets.UTF_8.name())}")
                main.post { status.text = "$name tersimpan di $destination" }
            } catch (error: Exception) { main.post { status.text = error.message ?: "File tidak dapat diunduh." } }
        }
    }

    private fun saveSharedFile(name: String, bytes: ByteArray): String {
        val safeName = File(name).name.take(140).ifBlank { "file.bin" }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            val values = ContentValues().apply {
                put(MediaStore.Downloads.DISPLAY_NAME, safeName)
                put(MediaStore.Downloads.MIME_TYPE, "application/octet-stream")
                put(MediaStore.Downloads.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + "/LaptopStatus")
            }
            val uri = contentResolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values) ?: throw IOException("Folder Download tidak dapat ditulis.")
            contentResolver.openOutputStream(uri)?.use { it.write(bytes) } ?: throw IOException("File unduhan tidak dapat disimpan.")
            return "Download/LaptopStatus/$safeName"
        }
        val directory = File(getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS), "LaptopStatus").apply { mkdirs() }
        File(directory, safeName).writeBytes(bytes)
        return directory.absolutePath
    }

    private fun receiveWindowsClipboard() {
        val status = findViewById<TextView>(R.id.shareStatus)
        status.text = "Memeriksa teks clipboard Windows…"
        dataWorker.execute {
            try {
                val response = PinnedTls.withConnection(address, 8765, "/api/share/clipboard", "GET", key,
                    prefs.getString("fingerprint", "").orEmpty()) { connection ->
                    if (connection.responseCode != 200) throw IOException("Belum ada teks clipboard yang disiapkan Windows.")
                    JSONObject(connection.inputStream.bufferedReader().use { it.readText() }).optString("text")
                }
                if (response.isBlank()) throw IOException("Clipboard Windows kosong.")
                main.post {
                    AlertDialog.Builder(this)
                        .setTitle("Salin clipboard Windows ke Android?")
                        .setMessage("Teks dari Windows akan mengganti clipboard Android saat ini.")
                        .setPositiveButton("Salin") { _, _ ->
                            val manager = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                            manager.setPrimaryClip(ClipData.newPlainText("Laptop Status", response))
                            dataWorker.execute { acknowledgeShare("/api/share/ack?clipboard=true") }
                            status.text = "Clipboard Windows sudah disalin ke Android."
                        }
                        .setNegativeButton("Batal", null)
                        .show()
                }
            } catch (error: Exception) { main.post { status.text = error.message ?: "Clipboard Windows belum dapat diambil." } }
        }
    }

    private fun acknowledgeShare(path: String) {
        PinnedTls.withConnection(address, 8765, path, "POST", key, prefs.getString("fingerprint", "").orEmpty()) { connection ->
            if (connection.responseCode !in 200..299) throw IOException("Kiriman tidak dapat ditandai selesai.")
        }
    }

    private fun formatShareBytes(bytes: Long): String {
        val units = arrayOf("B", "KB", "MB", "GB")
        var value = bytes.toDouble().coerceAtLeast(0.0)
        var unit = 0
        while (value >= 1024 && unit < units.lastIndex) { value /= 1024; unit++ }
        return "%.1f %s".format(Locale.getDefault(), value, units[unit])
    }

    private fun readBounded(input: InputStream, maximumBytes: Int): ByteArray {
        val output = ByteArrayOutputStream(minOf(maximumBytes, 64 * 1024))
        val buffer = ByteArray(16 * 1024)
        while (true) {
            val count = input.read(buffer)
            if (count < 0) break
            if (output.size() + count > maximumBytes) throw IOException("Ukuran file lebih besar dari batas 25 MB.")
            output.write(buffer, 0, count)
        }
        return output.toByteArray()
    }

    private fun transitionStatusPill(text: String, state: String) {
        val pill = findViewById<TextView>(R.id.statusPill)
        if (pill.text.toString() == text && currentStatusPillState == state) return
        currentStatusPillState = state
        val generation = ++statusPillAnimationGeneration
        fun applyState() {
            pill.text = text
            when (state) {
                "connected" -> {
                    pill.setTextColor(Color.rgb(79, 209, 165))
                    pill.setBackgroundResource(if (prefs.getBoolean("classicTheme", false)) R.drawable.bg_pill_on_classic else R.drawable.bg_pill_on)
                }
                "connecting" -> {
                    pill.setTextColor(Color.rgb(255, 190, 83))
                    pill.background = GradientDrawable().apply {
                        cornerRadius = dp(24).toFloat()
                        setColor(Color.rgb(55, 45, 28))
                    }
                }
                else -> {
                    pill.setTextColor(getColor(R.color.text_dim))
                    pill.setBackgroundResource(if (prefs.getBoolean("classicTheme", false)) R.drawable.bg_pill_off_classic else R.drawable.bg_pill_off)
                }
            }
        }
        pill.animate().cancel()
        if (!areMotionAnimationsEnabled()) {
            applyState()
            pill.alpha = 1f
            pill.translationY = 0f
            return
        }
        pill.animate().alpha(0f).translationY(-dp(5).toFloat()).setDuration(130L).withEndAction {
            if (generation != statusPillAnimationGeneration) return@withEndAction
            applyState()
            pill.alpha = 0f
            pill.translationY = dp(5).toFloat()
            pill.animate().alpha(1f).translationY(0f).setDuration(340L)
                .setInterpolator(AnimationUtils.loadInterpolator(this, R.interpolator.ios_spring)).start()
        }.start()
    }

    private fun consumePendingAssistantIntent() {
        val launch = pendingAssistantIntent ?: return
        pendingAssistantIntent = null
        handleAssistantLaunch(launch)
    }

    private fun handleAssistantLaunch(launch: Intent) {
        if (launch.hasExtra("assistant_laptop_online")) {
            laptopOnline = launch.getBooleanExtra("assistant_laptop_online", false)
            laptopOnAcPower = launch.getBooleanExtra("assistant_on_ac_power", false)
            jokeEnabled = launch.getBooleanExtra("assistant_joke_active", false)
            setRemoteControlsEnabled(laptopOnline)
            updateJokeButton()
        }
        val control = launch.getStringExtra("assistant_control")
        if (!control.isNullOrBlank()) {
            openAssistantControl(control, launch.getBooleanExtra("assistant_on_ac_power", laptopOnAcPower))
            return
        }
        val feature = launch.getStringExtra("assistant_feature")
            ?: launch.getStringExtra("feature")
            ?: launch.getStringExtra("featureParam")
        val query = launch.getStringExtra("assistant_query")
            ?: launch.getStringExtra("name")
        if (!feature.isNullOrBlank()) {
            openAssistantFeature(feature)
        } else if (!query.isNullOrBlank()) {
            searchFromAssistant(query)
        } else {
            openAssistantFeature("status")
        }
    }

    private fun openAssistantControl(control: String, assistantOnAcPower: Boolean) {
        laptopOnAcPower = assistantOnAcPower
        when (control.trim().lowercase(Locale.ROOT)) {
            "shutdown" -> confirmControl("shutdown", "Matikan Windows?", "Laptop akan dimatikan setelah 10 detik. Pastikan pekerjaan sudah disimpan.")
            "restart" -> confirmControl("restart", "Restart Windows?", "Laptop akan restart setelah 10 detik. Pastikan pekerjaan sudah disimpan.")
            "sleep" -> confirmControl("sleep", "Sleep Windows?", "Laptop akan masuk mode sleep sekarang.")
            "joke" -> openAssistantFeature("joke")
            else -> findViewById<TextView>(R.id.connectionInfo).text = "Perintah Assistant tidak dikenal."
        }
    }
    private fun openAssistantFeature(feature: String) {
        val normalized = feature.lowercase(Locale.ROOT).replace('-', '_').trim()
        when {
            normalized.contains("joke") -> {
                selectNavigation(R.id.navDevice)
                scrollToView(findViewById(R.id.powerControlPanel))
                if (!jokeEnabled) {
                    if (laptopOnline) {
                        confirmControl("joke-on", "Aktifkan Mode Joke?", "Layar laptop akan menampilkan simulasi selama maksimal 30 detik. Konfirmasi untuk melanjutkan.")
                    } else {
                        findViewById<TextView>(R.id.connectionInfo).text = "Hubungkan laptop terlebih dahulu untuk mengaktifkan Mode Joke."
                        selectNavigation(R.id.navAccount)
                    }
                }
            }
            normalized.contains("process") || normalized.contains("app") -> openAssistantProcesses("")
            normalized.contains("history") || normalized.contains("riwayat") -> {
                selectNavigation(R.id.navHistory)
                findViewById<android.widget.ScrollView>(R.id.contentScroll).smoothScrollTo(0, 0)
            }
            normalized.contains("connection") || normalized.contains("setting") || normalized.contains("koneksi") -> {
                selectNavigation(R.id.navAccount)
                findViewById<android.widget.ScrollView>(R.id.contentScroll).smoothScrollTo(0, 0)
            }
            normalized.contains("screen") || normalized.contains("layar") -> {
                selectNavigation(R.id.navDevice)
                scrollToView(findViewById(R.id.screenControlPanel))
            }
            else -> {
                selectNavigation(R.id.navDevice)
                findViewById<android.widget.ScrollView>(R.id.contentScroll).smoothScrollTo(0, 0)
                worker.execute { poll() }
            }
        }
    }

    private fun openAssistantProcesses(filter: String) {
        selectNavigation(R.id.navDevice)
        processSearchFilter = filter.trim()
        prefs.edit().putBoolean(processListCollapsedPreferenceKey(), false).apply()
        findViewById<Button>(R.id.toggleProcessListButton).text = "Sembunyikan semua"
        findViewById<LinearLayout>(R.id.processList).visibility = View.VISIBLE
        scrollToView(findViewById(R.id.processPanel))
        loadWindowsProcesses(processSearchFilter)
    }

    private fun searchFromAssistant(query: String) {
        val normalized = query.lowercase(Locale.ROOT)
        val term = query
            .replace(Regex("(?i)\\b(find|search for|search|show me|show|look up|open|please|in|on|the|laptop|status|windows|processes|process|applications|application|apps|app|device|perangkat|please|check|current|running|level|cari|temukan|tampilkan|lihat|proses|aplikasi|di|pada)\\b"), " ")
            .replace(Regex("[^\\p{L}\\p{N}._ -]"), " ")
            .replace(Regex("\\s+"), " ")
            .trim()
            .lowercase(Locale.ROOT)
        when {
            normalized.contains("joke") -> openAssistantFeature("joke")
            normalized.contains("history") || normalized.contains("riwayat") -> openAssistantFeature("history")
            normalized.contains("connection") || normalized.contains("setting") || normalized.contains("koneksi") -> openAssistantFeature("connection")
            normalized.contains("process") || normalized.contains("application") || normalized.contains("apps") -> openAssistantProcesses("")
            term.isBlank() || term in setOf("online", "cpu", "ram", "battery", "baterai", "status") -> openAssistantFeature("status")
            else -> {
                openAssistantProcesses(term)
            }
        }
    }

    private fun showAssistantGuide() {
        AlertDialog.Builder(this)
            .setTitle("Google Assistant & Gemini")
            .setMessage(
                "Gemini dapat memakai AppFunctions Laptop Status untuk:\n" +
                    "• melihat status online, CPU, RAM, baterai, dan charger laptop\n" +
                    "• mencari nama proses Windows\n" +
                    "• membuka halaman status, proses, riwayat, koneksi, layar, atau Mode Joke\n" +
                    "• meminta shutdown, restart, sleep, atau Mode Joke lewat dialog konfirmasi di aplikasi\n\n" +
                    "Perintah kontrol tidak dikirim oleh Gemini secara langsung. Buka aplikasi dan tekan Lanjutkan untuk mengirimnya. Untuk mencoba, minta Gemini tampilkan status laptop saya atau cari proses Chrome di laptop saya. Android 16 atau lebih baru diperlukan. Integrasi AppFunctions dengan Gemini masih pratinjau tertutup Google; fungsi dapat belum muncul pada akun biasa. Google Assistant App Actions yang tersedia terpisah dari integrasi Gemini."
            )
            .setPositiveButton("Mengerti", null)
            .show()
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        if (intent.getBooleanExtra(MonitorService.EXTRA_OPEN_JOKE_OVERLAY_PERMISSION, false)) {
            findViewById<View>(R.id.appRoot).post { setJokeOverlayEnabled(true) }
        }
        pendingAssistantIntent = intent.takeIf {
            it.hasExtra("assistant_feature") || it.hasExtra("assistant_query") || it.hasExtra("assistant_control") || it.hasExtra("feature") || it.hasExtra("name")
        }
        findViewById<View>(R.id.appRoot).post { consumePendingAssistantIntent() }
    }

    private fun setScreenFullscreen(enabled: Boolean) {
        if (enabled && !screenActive) return
        if (screenFullscreen == enabled) return
        screenFullscreen = enabled

        val header = findViewById<View>(R.id.appHeader)
        val nav = findViewById<View>(R.id.bottomNavigation)
        val scroll = findViewById<android.widget.ScrollView>(R.id.contentScroll)
        val content = findViewById<ViewGroup>(R.id.contentContainer)
        val panel = findViewById<View>(R.id.screenControlPanel)
        val preview = screenPreview
        val fullButton = findViewById<Button>(R.id.screenFullscreenButton)

        if (enabled) {
            selectedNavigationId = R.id.navDevice
            requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
            header.visibility = View.GONE
            nav.visibility = View.GONE
            scroll.isFillViewport = true
            content.setPadding(0, 0, 0, 0)
            content.layoutParams = content.layoutParams.apply { height = ViewGroup.LayoutParams.MATCH_PARENT }

            listOf(R.id.deviceSummaryCard, R.id.memoryPanel, R.id.processPanel, R.id.powerControlPanel, R.id.historySectionTitle, R.id.historyList, R.id.connectionPanel, R.id.accountSettingsPanel)
                .forEach { findViewById<View>(it).visibility = View.GONE }
            panel.visibility = View.VISIBLE
            panel.layoutParams = (panel.layoutParams as android.widget.LinearLayout.LayoutParams).apply {
                height = ViewGroup.LayoutParams.MATCH_PARENT
                topMargin = 0
            }
            (panel as ViewGroup).setPadding(dp(10), dp(8), dp(10), dp(8))
            (preview.layoutParams as android.widget.LinearLayout.LayoutParams).apply {
                height = 0
                weight = 1f
            }
            preview.layoutParams = preview.layoutParams
            fullButton.text = "Keluar dari layar penuh"
            setImmersiveSystemBars(true)
        } else {
            setImmersiveSystemBars(false)
            requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_PORTRAIT
            header.visibility = View.VISIBLE
            nav.visibility = View.VISIBLE
            scroll.isFillViewport = false
            content.setPadding(dp(20), dp(20), dp(20), dp(20))
            content.layoutParams = content.layoutParams.apply { height = ViewGroup.LayoutParams.WRAP_CONTENT }
            panel.layoutParams = (panel.layoutParams as android.widget.LinearLayout.LayoutParams).apply {
                height = ViewGroup.LayoutParams.WRAP_CONTENT
                topMargin = dp(16)
            }
            (panel as ViewGroup).setPadding(dp(16), dp(16), dp(16), dp(16))
            (preview.layoutParams as android.widget.LinearLayout.LayoutParams).apply {
                height = dp(300)
                weight = 0f
            }
            preview.layoutParams = preview.layoutParams
            fullButton.text = "Tampilkan layar penuh · lanskap"
            selectNavigation(selectedNavigationId)
        }
    }

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()
    private fun px(value: Int): Int = dp(value)

    private fun safeShowOnboardingGuide() {
        if (isFinishing || isDestroyed) return
        try {
            showOnboardingGuide()
        } catch (error: Exception) {
            android.util.Log.e("LaptopStatus", "Panduan awal gagal dibuka", error)
            runCatching {
                AlertDialog.Builder(this)
                    .setTitle("Panduan singkat")
                    .setMessage("Pasangkan laptop dengan memindai QR, lalu pantau status dan kirim perintah setelah nama laptop yang benar dipilih. Kontrol daya meminta konfirmasi; kendali layar dapat dihentikan dari tombol Stop.")
                    .setPositiveButton("Mengerti") { _, _ ->
                        prefs.edit().putBoolean("onboardingCompleted", true).apply()
                    }
                    .show()
            }
        }
    }

    private fun showOnboardingGuide() {
        val pages = listOf(
            Triple("01 / 03", "Pasangkan laptop", "Buka aplikasi Windows, tampilkan QR, lalu pindai dari Android. Pada pemasangan pertama, cocokkan kode dan setujui permintaan di Windows."),
            Triple("02 / 03", "Pantau status", "Halaman Perangkat menampilkan status online, CPU, RAM, dan baterai. Pencarian otomatis bekerja ketika kedua perangkat berada pada Wi-Fi yang sama."),
            Triple("03 / 03", "Kendalikan dengan aman", "Pastikan nama laptop yang dipilih benar sebelum mengirim perintah. Kontrol daya memerlukan konfirmasi; sesi kendali layar memiliki indikator yang dapat dihentikan di Windows.")
        )
        val dialog = Dialog(this)
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(22), dp(20), dp(22), dp(18))
            background = GradientDrawable().apply {
                cornerRadius = dp(24).toFloat()
                setColor(Color.rgb(19, 29, 41))
                setStroke(dp(1), Color.rgb(74, 95, 114))
            }
        }
        val top = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
        top.addView(TextView(this).apply {
            text = "PANDUAN SINGKAT"
            textSize = 11f
            setTextColor(getColor(R.color.accent))
            letterSpacing = 0.08f
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        })
        val step = TextView(this).apply { textSize = 11f; setTextColor(getColor(R.color.text_dim)) }
        top.addView(step)
        root.addView(top)

        val progress = ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal).apply {
            max = pages.size
            progress = 1
            progressTintList = android.content.res.ColorStateList.valueOf(getColor(R.color.accent))
            progressBackgroundTintList = android.content.res.ColorStateList.valueOf(Color.rgb(49, 63, 78))
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(5)).apply { topMargin = dp(12); bottomMargin = dp(20) }
        }
        root.addView(progress)

        val icon = TextView(this).apply {
            text = "⌁"
            textSize = 28f
            gravity = Gravity.CENTER
            setTextColor(getColor(R.color.accent))
            background = GradientDrawable().apply {
                cornerRadius = dp(18).toFloat()
                setColor(Color.argb(45, 114, 228, 191))
                setStroke(dp(1), Color.argb(100, 114, 228, 191))
            }
            layoutParams = LinearLayout.LayoutParams(dp(58), dp(58)).apply { bottomMargin = dp(15) }
        }
        root.addView(icon)
        val pageContent = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        val title = TextView(this).apply {
            textSize = 20f
            setTextColor(getColor(R.color.text_main))
            setTypeface(typeface, android.graphics.Typeface.BOLD)
        }
        pageContent.addView(title)
        val description = TextView(this).apply {
            textSize = 13f
            setTextColor(getColor(R.color.text_dim))
            setLineSpacing(dp(3).toFloat(), 1f)
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(8) }
        }
        pageContent.addView(description)
        root.addView(pageContent, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(22) })

        val buttons = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.END or Gravity.CENTER_VERTICAL }
        val skip = Button(this).apply { text = "Lewati"; minHeight = dp(42) }
        val back = Button(this).apply { text = "Kembali"; minHeight = dp(42); isEnabled = false }
        val next = Button(this).apply { text = "Lanjut"; minHeight = dp(42) }
        buttons.addView(skip)
        buttons.addView(back, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { leftMargin = dp(6) })
        buttons.addView(next, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { leftMargin = dp(6) })
        root.addView(buttons)

        var page = 0
        fun updatePage(animate: Boolean) {
            val current = pages[page]
            step.text = current.first
            title.text = current.second
            description.text = current.third
            progress.setProgress(page + 1, animate && areMotionAnimationsEnabled())
            back.isEnabled = page > 0
            next.text = if (page == pages.lastIndex) "Selesai" else "Lanjut"
            if (animate && areMotionAnimationsEnabled()) {
                pageContent.animate().cancel()
                pageContent.translationX = dp(18).toFloat()
                pageContent.alpha = 0f
                pageContent.animate().translationX(0f).alpha(1f).setDuration(250).setInterpolator(DecelerateInterpolator()).start()
                icon.animate().cancel()
                icon.scaleX = 0.8f
                icon.scaleY = 0.8f
                icon.animate().scaleX(1f).scaleY(1f).setDuration(300).setInterpolator(OvershootInterpolator(1.35f)).start()
            }
        }
        fun finish() {
            prefs.edit().putBoolean("onboardingCompleted", true).apply()
            dialog.dismiss()
            consumePendingAssistantIntent()
        }
        skip.setOnClickListener { finish() }
        back.setOnClickListener { if (page > 0) { page--; updatePage(true) } }
        next.setOnClickListener {
            if (page < pages.lastIndex) { page++; updatePage(true) } else finish()
        }
        val guideScroll = android.widget.ScrollView(this).apply {
            isFillViewport = true
            isVerticalScrollBarEnabled = false
            clipToPadding = false
            addView(root, android.widget.FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        }
        dialog.setContentView(guideScroll)
        dialog.setOnShowListener {
            dialog.window?.apply {
                setBackgroundDrawable(android.graphics.drawable.ColorDrawable(Color.TRANSPARENT))
                val width = (resources.displayMetrics.widthPixels - dp(40)).coerceAtMost(dp(520)).coerceAtLeast(dp(280))
                val maxHeight = (resources.displayMetrics.heightPixels - dp(72)).coerceAtLeast(dp(260))
                root.measure(
                    View.MeasureSpec.makeMeasureSpec(width, View.MeasureSpec.AT_MOST),
                    View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED)
                )
                val height = (root.measuredHeight + dp(4)).coerceAtMost(maxHeight)
                guideScroll.layoutParams = android.widget.FrameLayout.LayoutParams(width, height)
                setLayout(width, height)
                addFlags(android.view.WindowManager.LayoutParams.FLAG_DIM_BEHIND)
                setDimAmount(0.62f)
            }
        }
        updatePage(false)
        dialog.show()
    }

    private fun setImmersiveSystemBars(enabled: Boolean) {
        val controller = WindowCompat.getInsetsController(window, window.decorView)
        if (enabled) {
            controller.systemBarsBehavior = androidx.core.view.WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            controller.hide(WindowInsetsCompat.Type.systemBars())
        } else {
            controller.show(WindowInsetsCompat.Type.systemBars())
        }
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus && (screenFullscreen || demoScreenFullscreen)) setImmersiveSystemBars(true)
    }

    @Suppress("DEPRECATION")
    override fun onBackPressed() {
        if (demoScreenFullscreen) {
            if (::motionDemoWebView.isInitialized) motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.toggleScreenFullscreen()", null)
        } else if (screenFullscreen) setScreenFullscreen(false)
        else if (manualConnectionPageOpen && ::motionDemoWebView.isInitialized) {
            manualConnectionPageOpen = false
            motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.closeManualConnection()", null)
        }
        else if (temporaryNativeUiForWebAction && prefs.getBoolean("motionDemoUi", true)) returnToMotionDemoAfterNativeAction()
        else super.onBackPressed()
    }

    private fun queueDiscovery() {
        val now = System.currentTimeMillis()
        if (now - lastDiscoveryAttemptAt < 12_000 || !discoveryRunning.compareAndSet(false, true)) return
        lastDiscoveryAttemptAt = now
        discoveryWorker.execute { discoverLaptop(alreadyClaimed = true) }
    }

    /**
     * Starts at most one discovery scan, including requests arriving from the WebView.
     * Claiming the scan before enqueueing prevents repeated offline status updates from
     * building a queue of six-second scans behind the single discovery worker.
     */
    private fun requestLaptopDiscovery() {
        if (!discoveryRunning.compareAndSet(false, true)) return
        main.post {
            findViewById<TextView>(R.id.discoveryStatus).text = "Memindai perangkat di jaringan Wi-Fi lokal..."
            findViewById<ProgressBar>(R.id.discoveryProgress).visibility = View.VISIBLE
            if (!laptopOnline) {
                discoveredDevices.clear()
                renderDiscoveredDevices(emptyList())
            }
            publishMotionDemoDiscoveredDevices()
        }
        discoveryWorker.execute { discoverLaptop(alreadyClaimed = true) }
    }

    private fun scanLaptopQrCode() {
        findViewById<TextView>(R.id.discoveryStatus).text = "Membuka pemindai QR..."
        findViewById<ProgressBar>(R.id.discoveryProgress).visibility = View.VISIBLE
        val options = GmsBarcodeScannerOptions.Builder()
            .setBarcodeFormats(Barcode.FORMAT_QR_CODE)
            .enableAutoZoom()
            .build()
        GmsBarcodeScanning.getClient(this, options).startScan()
            .addOnSuccessListener { barcode ->
                val rawValue = barcode.rawValue.orEmpty()
                try {
                    val qr = JSONObject(rawValue)
                    if (qr.optString("type") != "LaptopStatusPairing" || qr.optInt("version") != 1)
                        throw IllegalArgumentException("QR ini bukan kode pairing Laptop Status.")
                    val deviceId = qr.optString("deviceId")
                    val fingerprint = normalizeFingerprint(qr.optString("fingerprint"))
                    val port = qr.optInt("port", 8765)
                    val addresses = qr.optJSONArray("addresses") ?: JSONArray()
                    val candidates = (0 until addresses.length()).map { addresses.optString(it) }
                        .filter { address ->
                            address.matches(Regex("^(?:[0-9]{1,3}\\.){3}[0-9]{1,3}$")) ||
                                address.matches(Regex("^https://[A-Za-z0-9.-]+(?::443)?/?$", RegexOption.IGNORE_CASE))
                        }
                    val preferredAddress = qr.optString("preferredAddress").takeIf { preferred ->
                        preferred.isNotBlank() && candidates.any { it.trimEnd('/').equals(preferred.trimEnd('/'), ignoreCase = true) }
                    }
                    if (deviceId.isBlank() || fingerprint.length != 64 || fingerprint.any { !it.isDigit() && it !in 'A'..'F' } ||
                        port !in 1..65535 || candidates.isEmpty())
                        throw IllegalArgumentException("Data QR tidak lengkap. Tampilkan QR baru dari aplikasi Windows.")

                    // A scanned QR may intentionally prefer the Cloudflare hostname when the
                    // phone is on another network. Do not replace that route with a stale LAN IP.
                    val connectionCandidates = (listOfNotNull(preferredAddress) + candidates)
                        .map { it.trim() }.distinctBy { it.trimEnd('/').lowercase(Locale.ROOT) }
                    val address = connectionCandidates.first()
                    val laptop = DiscoveredLaptop(
                        deviceId = deviceId,
                        address = address,
                        deviceName = qr.optString("deviceName", "Laptop").take(48),
                        manufacturer = qr.optString("manufacturer", "Windows PC").take(48),
                        model = qr.optString("model", "").take(64),
                        macAddress = qr.optString("macAddress", "").take(24),
                        fingerprint = fingerprint,
                        port = port,
                        addresses = connectionCandidates
                    )
                    prefs.edit().putString("paired_${deviceId}_addresses", JSONArray(connectionCandidates).toString()).apply()
                    main.post {
                        findViewById<ProgressBar>(R.id.discoveryProgress).visibility = View.GONE
                        findViewById<View>(R.id.navAccount).performClick()
                        findViewById<TextView>(R.id.discoveryStatus).text = "QR laptop terbaca. Memulai pairing aman..."
                        discoveredDevices[deviceId] = laptop
                        renderDiscoveredDevices(discoveredDevices.values.toList())
                        val row = discoveryRows[deviceId]
                        if (row == null) {
                            findViewById<TextView>(R.id.discoveryStatus).text = "QR terbaca, tetapi kartu perangkat gagal ditampilkan. Coba cari laptop di Wi-Fi."
                        } else {
                            connectDiscoveredLaptop(laptop, row.signal, row.status, row.spinner)
                        }
                    }
                } catch (e: Exception) {
                    findViewById<ProgressBar>(R.id.discoveryProgress).visibility = View.GONE
                    findViewById<TextView>(R.id.discoveryStatus).text = e.message ?: "QR tidak dapat dibaca."
                }
            }
            .addOnCanceledListener {
                findViewById<ProgressBar>(R.id.discoveryProgress).visibility = View.GONE
                findViewById<TextView>(R.id.discoveryStatus).text = "Pemindaian QR dibatalkan."
            }
            .addOnFailureListener { error ->
                findViewById<ProgressBar>(R.id.discoveryProgress).visibility = View.GONE
                findViewById<TextView>(R.id.discoveryStatus).text = when {
                    error.message?.contains("module", ignoreCase = true) == true -> "Pemindai QR belum tersedia. Hubungkan internet sekali agar Google Play services memasangnya, lalu coba lagi."
                    else -> error.message ?: "Pemindai QR gagal dibuka."
                }
            }
    }

    private fun showTailscaleGuide() {
        val markdown = try {
            assets.open("README-Tailscale.md").bufferedReader().use { it.readText() }
        } catch (_: Exception) {
            "Panduan Tailscale tidak dapat dibuka. Unduh aplikasi Android dari https://play.google.com/store/apps/details?id=com.tailscale.ipn"
        }
        val readable = markdown.lineSequence().map { line ->
            when {
                line.startsWith("# ") -> "\n${line.drop(2).uppercase(Locale.ROOT)}\n"
                line.startsWith("## ") -> "\n${line.drop(3)}\n"
                line.startsWith("- ") -> "  • ${line.drop(2)}"
                else -> line
            }
        }.joinToString("\n").trim()
        val body = TextView(this).apply {
            text = readable
            textSize = 14f
            setTextColor(Color.rgb(232, 237, 244))
            setPadding(dp(18), dp(12), dp(18), dp(12))
            Linkify.addLinks(this, Linkify.WEB_URLS)
            linksClickable = true
        }
        val scroll = android.widget.ScrollView(this).apply {
            isFillViewport = true
            addView(body, ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT
            ))
        }
        AlertDialog.Builder(this)
            .setTitle("Panduan Tailscale")
            .setView(scroll)
            .setPositiveButton("Buka Play Store") { _, _ -> openTailscaleAndroidDownload() }
            .setNegativeButton("Tutup", null)
            .show()
    }

    private fun openTailscaleAndroidDownload() {
        val uri = android.net.Uri.parse("https://play.google.com/store/apps/details?id=com.tailscale.ipn")
        try {
            startActivity(Intent(Intent.ACTION_VIEW, uri))
        } catch (_: Exception) {
            findViewById<TextView>(R.id.connectionInfo).text = "Unduh Tailscale dari https://play.google.com/store/apps/details?id=com.tailscale.ipn"
        }
    }

    private fun discoverLaptop(
        alreadyClaimed: Boolean = false,
        onDiagnostics: ((String) -> Unit)? = null,
        allowConnectionChanges: Boolean = true
    ): List<DiscoveredLaptop> {
        if (!alreadyClaimed && !discoveryRunning.compareAndSet(false, true)) {
            main.post { findViewById<TextView>(R.id.discoveryStatus).text = "Pencarian laptop sedang berjalan..." }
            onDiagnostics?.invoke("${diagnosticTimestamp()} W/UDP scan dilewati; discovery lain masih berjalan")
            return emptyList()
        }
        main.post { findViewById<ProgressBar>(R.id.discoveryProgress).visibility = View.VISIBLE }
        val wifi = applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
        var lock: WifiManager.MulticastLock? = null
        val found = linkedMapOf<String, DiscoveredLaptop>()
        try {
            lock = wifi.createMulticastLock("LaptopStatusDiscovery").apply {
                setReferenceCounted(false)
                acquire()
            }
            onDiagnostics?.invoke("${diagnosticTimestamp()} I/UDP multicast lock acquired=${lock.isHeld}")
            DatagramSocket(null).use { socket ->
                socket.reuseAddress = true
                socket.bind(InetSocketAddress(8766))
                socket.soTimeout = 1500
                onDiagnostics?.invoke("${diagnosticTimestamp()} I/UDP socket bound=${socket.localSocketAddress} soTimeout=${socket.soTimeout}ms")
                val buffer = ByteArray(2048)
                val deadline = System.nanoTime() + 6_000_000_000L
                while (System.nanoTime() < deadline) {
                    try {
                        val packet = DatagramPacket(buffer, buffer.size)
                        socket.receive(packet)
                        val source = packet.address.hostAddress?.substringBefore('%') ?: "unknown"
                        val raw = String(packet.data, packet.offset, packet.length, StandardCharsets.UTF_8)
                        val data = try {
                            JSONObject(raw)
                        } catch (error: org.json.JSONException) {
                            onDiagnostics?.invoke("${diagnosticTimestamp()} W/UDP RX source=$source bytes=${packet.length} malformed-json=${error.message ?: "parse error"}")
                            continue
                        }
                        val safePayload = JSONObject(data.toString()).put("fingerprint", "<redacted>")
                        onDiagnostics?.invoke("${diagnosticTimestamp()} I/UDP RX source=$source bytes=${packet.length} payload=$safePayload")
                        if (data.optString("service") != "LaptopStatus") {
                            onDiagnostics?.invoke("${diagnosticTimestamp()} D/UDP IGNORE source=$source reason=service-mismatch")
                            continue
                        }
                        val discoveredAddress = packet.address.hostAddress?.substringBefore('%') ?: continue
                        val id = data.optString("deviceId").ifBlank { data.optString("macAddress").replace(":", "").ifBlank { discoveredAddress } }
                        val laptop = DiscoveredLaptop(
                            deviceId = id,
                            address = discoveredAddress,
                            deviceName = data.optString("deviceName", "Laptop").take(48),
                            manufacturer = data.optString("manufacturer", "Windows PC").take(48),
                            model = data.optString("model", "").take(64),
                            macAddress = data.optString("macAddress", "").take(24),
                            fingerprint = normalizeFingerprint(data.optString("fingerprint")),
                            port = data.optInt("port", 8765)
                        )
                        if (laptop.deviceId.isNotBlank() && laptop.fingerprint.length == 64) {
                            found[id] = laptop
                            onDiagnostics?.invoke("${diagnosticTimestamp()} I/UDP ACCEPT id=$id name=${laptop.deviceName} endpoint=${laptop.address}:${laptop.port} fingerprint=SHA256")
                        } else {
                            onDiagnostics?.invoke("${diagnosticTimestamp()} W/UDP REJECT source=$source reason=missing-device-id-or-invalid-fingerprint")
                        }
                    } catch (_: java.net.SocketTimeoutException) {
                        onDiagnostics?.invoke("${diagnosticTimestamp()} D/UDP timeout no-packet-for=${socket.soTimeout}ms")
                        continue
                    }
                }
            }
        } catch (e: Exception) {
            onDiagnostics?.invoke("${diagnosticTimestamp()} E/UDP ${e.javaClass.simpleName} message=${e.message ?: "no detail"}")
            main.post { findViewById<TextView>(R.id.discoveryStatus).text = e.message ?: "Pencarian perangkat gagal." }
        } finally {
            if (lock?.isHeld == true) lock.release()
            discoveryRunning.set(false)
            onDiagnostics?.invoke("${diagnosticTimestamp()} I/UDP scan-complete validDevices=${found.size} endpoints=${found.values.joinToString(",") { it.address }}")
            main.post {
                findViewById<ProgressBar>(R.id.discoveryProgress).visibility = View.GONE
                discoveredDevices.clear()
                discoveredDevices.putAll(found)
                renderDiscoveredDevices(found.values.toList())
                // The modern WebView connection screen has no native discovery rows to trigger
                // a refresh. Publish the completed scan explicitly so its laptop list updates.
                publishMotionDemoDiscoveredDevices()
                val status = findViewById<TextView>(R.id.discoveryStatus)
                val rememberedId = prefs.getString("activeDeviceId", "").orEmpty()
                val connectedLaptop = found.values.firstOrNull { laptopOnline && (it.deviceId == rememberedId || it.address == address) }
                status.text = when {
                    found.isEmpty() && laptopOnline -> "Tidak ditemukan laptop lain. Koneksi aktif ke ${findViewById<TextView>(R.id.deviceName).text} tetap berjalan."
                    found.isEmpty() -> "Belum ada laptop ditemukan. Untuk LAN, pastikan kedua perangkat di Wi-Fi yang sama. Untuk jaringan berbeda, gunakan QR dan aktifkan Tailscale di kedua perangkat."
                    connectedLaptop != null -> "${found.size} laptop ditemukan. ${connectedLaptop.deviceName} sudah terhubung."
                    else -> "${found.size} laptop ditemukan. Pilih perangkat untuk menghubungkan."
                }
                val remembered = found[rememberedId]
                val localActive = found.values.firstOrNull { it.deviceId == rememberedId && it.address != address }
                if (allowConnectionChanges && laptopOnline && localActive != null && rememberedId.isNotBlank()) preferLanRoute(localActive)
                val refs = discoveryRows[rememberedId]
                if (allowConnectionChanges && !laptopOnline && (wasOnline == false || wasOnline == null) && remembered != null && refs != null && !pairingRunning.get() &&
                    !prefs.getString("paired_${remembered.deviceId}_token", "").isNullOrBlank()) {
                    status.text = "Menyambungkan kembali ke ${remembered.deviceName}..."
                    connectDiscoveredLaptop(remembered, refs.signal, refs.status, refs.spinner)
                }
            }
        }
        return found.values.toList()
    }

    private fun preferLanRoute(laptop: DiscoveredLaptop) {
        if (!lanRouteSwitchRunning.compareAndSet(false, true)) return
        val token = prefs.getString("paired_${laptop.deviceId}_token", "").orEmpty()
        val pinned = normalizeFingerprint(prefs.getString("paired_${laptop.deviceId}_fingerprint", "").orEmpty())
        if (token.isBlank() || pinned != laptop.fingerprint) {
            lanRouteSwitchRunning.set(false)
            return
        }
        dataWorker.execute {
            try {
                val statusData = PinnedTls.withConnection(laptop.address, laptop.port, "/api/status", "GET", token, pinned) { connection ->
                    if (connection.responseCode != 200) throw IllegalStateException("LAN route refused")
                    JSONObject(connection.inputStream.bufferedReader().use { it.readText() })
                }
                if (statusData.optString("deviceId") != laptop.deviceId) return@execute
                main.post {
                    if (prefs.getString("activeDeviceId", "").orEmpty() != laptop.deviceId) return@post
                    address = laptop.address
                    key = token
                    prefs.edit().putString("address", laptop.address).putString("key", token).putString("fingerprint", pinned).apply()
                    addressInput.setText(laptop.address)
                    keyInput.setText(token)
                    fingerprintInput.setText(pinned)
                    showOnline(statusData)
                    findViewById<TextView>(R.id.connectionInfo).text = "Koneksi otomatis memakai Wi-Fi/LAN lokal. Tailscale tetap dapat dipakai saat perangkat berada di jaringan berbeda."
                }
            } catch (_: Exception) { }
            finally { lanRouteSwitchRunning.set(false) }
        }
    }

    private fun renderDiscoveredDevices(devices: List<DiscoveredLaptop>) {
        val list = findViewById<LinearLayout>(R.id.discoveryList)
        list.removeAllViews()
        discoveryRows.clear()
        devices.forEach { laptop ->
            val row = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                setPadding(dp(14), dp(12), dp(12), dp(12))
                background = GradientDrawable().apply {
                    cornerRadius = dp(16).toFloat()
                    setColor(Color.rgb(25, 35, 48))
                    setStroke(dp(1), Color.rgb(47, 60, 74))
                }
                isClickable = true
                isFocusable = true
                foreground = ContextCompat.getDrawable(this@MainActivity, android.R.drawable.list_selector_background)
                layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                    bottomMargin = dp(7)
                }
            }
            val header = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
            }
            val signal = TextView(this).apply {
                text = "▱"
                textSize = 24f
                gravity = Gravity.CENTER
                setTextColor(Color.rgb(114, 228, 191))
                layoutParams = LinearLayout.LayoutParams(dp(38), dp(42)).apply { rightMargin = dp(10) }
            }
            val details = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            }
            val title = TextView(this).apply {
                text = laptop.deviceName
                textSize = 15f
                setTextColor(Color.WHITE)
                setTypeface(typeface, android.graphics.Typeface.BOLD)
            }
            val hardware = listOf(laptop.manufacturer, laptop.model).filter { it.isNotBlank() }.distinct().joinToString(" • ")
            val sub = TextView(this).apply {
                text = "${laptop.address} · ${discoveryTransport(laptop)}"
                textSize = 11f
                setTextColor(Color.rgb(170, 185, 200))
                maxLines = 1
                ellipsize = android.text.TextUtils.TruncateAt.END
            }
            val status = TextView(this).apply {
                text = hardware.ifBlank { laptop.macAddress.takeIf(String::isNotBlank) ?: "Laptop Windows" }
                textSize = 11f
                setTextColor(Color.rgb(170, 185, 200))
                setPadding(0, dp(2), 0, 0)
            }
            val connectButton = TextView(this).apply {
                text = "Hubungkan"
                textSize = 11f
                setTypeface(typeface, android.graphics.Typeface.BOLD)
                gravity = Gravity.CENTER
                setTextColor(Color.rgb(8, 31, 34))
                background = GradientDrawable().apply {
                    cornerRadius = dp(18).toFloat()
                    setColor(Color.rgb(114, 228, 191))
                }
                setPadding(dp(12), dp(9), dp(12), dp(9))
                isClickable = true
                isFocusable = true
                layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                    leftMargin = dp(8)
                }
            }
            details.addView(title)
            details.addView(sub)
            details.addView(status)
            header.addView(signal)
            header.addView(details)
            header.addView(connectButton)
            val spinner = ProgressBar(this).apply {
                isIndeterminate = true
                visibility = View.GONE
                layoutParams = LinearLayout.LayoutParams(dp(28), dp(28)).apply { leftMargin = dp(8) }
            }
            val steps = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                visibility = View.GONE
                setPadding(dp(48), dp(8), dp(4), 0)
            }
            val stepLabels = listOf("Mencari alamat laptop", "Memeriksa sertifikat TLS", "Memasangkan perangkat").map { label ->
                TextView(this).apply {
                    text = "○  $label"
                    textSize = 12f
                    setTextColor(Color.rgb(174, 188, 203))
                    setPadding(0, dp(5), 0, dp(5))
                    steps.addView(this)
                }
            }
            row.addView(header)
            row.addView(spinner)
            row.addView(steps)
            val connect = { connectDiscoveredLaptop(laptop, signal, status, spinner) }
            row.setOnClickListener { connect() }
            connectButton.setOnClickListener { connect() }
            spinner.visibility = View.GONE
            list.addView(row)
            if (areMotionAnimationsEnabled()) {
                row.alpha = 0f
                row.translationY = dp(10).toFloat()
                row.animate().alpha(1f).translationY(0f)
                    .setStartDelay((discoveryRows.size * 70L).coerceAtMost(280L))
                    .setDuration(420L)
                    .setInterpolator(AnimationUtils.loadInterpolator(this, R.interpolator.ios_spring))
                    .start()
            }
            discoveryRows[laptop.deviceId] = DiscoveryRowRefs(row, signal, status, spinner, connectButton, steps, stepLabels)
        }
        refreshDiscoveryConnectionState()
    }

    private fun discoveryTransport(laptop: DiscoveredLaptop): String {
        val host = laptop.address.removePrefix("https://").substringBefore('/').substringBefore(':')
        val parts = host.split('.').mapNotNull { it.toIntOrNull() }
        return when {
            laptop.address.startsWith("https://", ignoreCase = true) -> "Internet"
            parts.size == 4 && parts[0] == 100 && parts[1] in 64..127 -> "Tailscale"
            else -> "Wi-Fi lokal"
        }
    }

    private fun updateConnectionSteps(laptop: DiscoveredLaptop, activeStep: Int, failedMessage: String? = null) {
        val refs = discoveryRows[laptop.deviceId] ?: return
        refs.steps.visibility = View.VISIBLE
        refs.connectButton.visibility = View.GONE
        refs.stepLabels.forEachIndexed { index, label ->
            val complete = failedMessage == null && (activeStep > index || activeStep >= refs.stepLabels.size)
            val active = failedMessage == null && activeStep == index
            val nextText = when {
                failedMessage != null && index == activeStep -> "!  ${failedMessage.take(72)}"
                complete -> "✓  ${listOf("Mencari alamat laptop", "Memeriksa sertifikat TLS", "Memasangkan perangkat")[index]}"
                active -> "◌  ${listOf("Mencari alamat laptop", "Memeriksa sertifikat TLS", "Memasangkan perangkat")[index]}"
                else -> "○  ${listOf("Mencari alamat laptop", "Memeriksa sertifikat TLS", "Memasangkan perangkat")[index]}"
            }
            val textChanged = label.text.toString() != nextText
            label.text = nextText
            label.setTextColor(when {
                failedMessage != null && index == activeStep -> Color.rgb(255, 143, 151)
                complete -> Color.rgb(114, 228, 191)
                active -> Color.rgb(224, 236, 246)
                else -> Color.rgb(174, 188, 203)
            })
            if (textChanged && areMotionAnimationsEnabled()) {
                label.animate().cancel()
                label.alpha = 0.35f
                label.translationY = dp(4).toFloat()
                label.animate().alpha(1f).translationY(0f).setDuration(240L)
                    .setInterpolator(AnimationUtils.loadInterpolator(this, R.interpolator.ios_spring))
                    .start()
            } else {
                label.alpha = 1f
                label.translationY = 0f
            }
        }
    }

    private fun refreshDiscoveryConnectionState() {
        if (!::prefs.isInitialized) return
        val activeId = prefs.getString("activeDeviceId", "").orEmpty()
        discoveryRows.forEach { (deviceId, refs) ->
            val laptop = discoveredDevices[deviceId]
            val connected = laptopOnline && (deviceId == activeId || (laptop != null && laptop.address == address))
            val connecting = pairingRunning.get() && selectedDiscoveryDevice?.deviceId == deviceId
            val checking = pendingConnectionDeviceId == deviceId && !connected
            val showingSteps = connecting || failedDiscoveryDeviceId == deviceId || checking
            refs.connectButton.visibility = if (connected || connecting || checking) View.GONE else View.VISIBLE
            refs.connectButton.isEnabled = !connected && !pairingRunning.get()
            refs.status.text = when {
                connected -> "Terhubung"
                connecting -> "Menghubungkan..."
                checking -> "Memeriksa status..."
                !prefs.getString("paired_${deviceId}_token", "").isNullOrBlank() -> laptop?.let(::discoveryTransport) ?: "Sudah dipasangkan"
                else -> laptop?.let(::discoveryTransport) ?: "Siap dipasangkan"
            }
            refs.status.setTextColor(if (connected) Color.rgb(129, 245, 202) else Color.rgb(206, 229, 241))
            refs.steps.visibility = if (showingSteps) View.VISIBLE else View.GONE
            refs.spinner.visibility = if (connecting) View.VISIBLE else View.GONE
            refs.row.alpha = if (pairingRunning.get() && !connecting) 0.35f else 1f
            refs.row.isClickable = !connected && !pairingRunning.get()
            refs.row.isFocusable = refs.row.isClickable
        }
        publishMotionDemoDiscoveredDevices()
    }

    private fun discoveredDevicesJson(): String = JSONArray().also { array ->
        discoveredDevices.values.forEach { laptop ->
            val connected = laptopOnline && (laptop.deviceId == prefs.getString("activeDeviceId", "") || laptop.address == address)
            array.put(JSONObject().apply {
                put("id", laptop.deviceId)
                put("name", laptop.deviceName)
                put("address", laptop.address)
                put("transport", discoveryTransport(laptop))
                put("manufacturer", laptop.manufacturer)
                put("model", laptop.model)
                put("paired", !prefs.getString("paired_${laptop.deviceId}_token", "").isNullOrBlank())
                put("connected", connected)
                put("connecting", pairingRunning.get() && selectedDiscoveryDevice?.deviceId == laptop.deviceId)
            })
        }
    }.toString()

    private fun publishMotionDemoDiscoveredDevices() {
        if (!::prefs.isInitialized) return
        val payload = discoveredDevicesJson()
        latestDiscoveredDevicesPayload = payload
        if (::motionDemoWebView.isInitialized && motionDemoPageReady) {
            motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.setDiscoveredDevices(${JSONObject.quote(payload)},${discoveryRunning.get()})", null)
        }
    }

    private fun androidClientId(): String {
        prefs.getString("clientId", null)?.let { if (it.length == 32) return it }
        val newId = UUID.randomUUID().toString().replace("-", "")
        prefs.edit().putString("clientId", newId).apply()
        return newId
    }

    private fun chooseReachableLaptopAddress(laptop: DiscoveredLaptop): String {
        val candidates = (laptop.addresses + laptop.address)
            .map { it.trim() }
            .filter(String::isNotBlank)
            .distinctBy { it.trimEnd('/').lowercase(Locale.ROOT) }
        if (candidates.size <= 1) return candidates.firstOrNull() ?: laptop.address

        var lastError: Exception? = null
        for (candidate in candidates) {
            try {
                val responseCode = PinnedTls.withUnpairedConnection(
                    candidate,
                    laptop.port,
                    "/api/status",
                    laptop.fingerprint,
                    readTimeoutMillis = 1400,
                    connectTimeoutMillis = 1400
                ) { connection -> connection.responseCode }
                if (responseCode == 200 || responseCode == 401) return candidate
            } catch (error: Exception) {
                lastError = error
            }
        }

        val detail = lastError?.message?.takeIf(String::isNotBlank)?.let { " Detail: $it" }.orEmpty()
        throw IllegalStateException(
            "Alamat koneksi di QR tidak terjangkau. Pastikan Wi-Fi yang sama aktif untuk LAN, atau Tailscale berjalan di kedua perangkat.$detail",
            lastError
        )
    }

    private fun connectDiscoveredLaptop(laptop: DiscoveredLaptop, signal: TextView, status: TextView, spinner: ProgressBar) {
        if (!pairingRunning.compareAndSet(false, true)) {
            findViewById<TextView>(R.id.discoveryStatus).text = "Sedang menghubungkan ke perangkat lain..."
            return
        }
        failedDiscoveryDeviceId = null
        selectedDiscoveryDevice = laptop
        selectedDiscoveryStatus = status
        updateConnectionSteps(laptop, 1)
        refreshDiscoveryConnectionState()
        transitionStatusPill("Menghubungkan", "connecting")
        findViewById<TextView>(R.id.discoveryStatus).text = "Menghubungkan ke ${laptop.deviceName} melalui ${discoveryTransport(laptop)}..."
        val animation = if (areMotionAnimationsEnabled()) ObjectAnimator.ofFloat(signal, View.ALPHA, 0.35f, 1f).apply {
            duration = 650
            repeatMode = ObjectAnimator.REVERSE
            repeatCount = ObjectAnimator.INFINITE
            start()
        } else null
        spinner.visibility = View.VISIBLE
        status.text = "Mengamankan koneksi..."
        dataWorker.execute {
            try {
                val connectionAddress = chooseReachableLaptopAddress(laptop)
                val tokenKey = "paired_${laptop.deviceId}_token"
                val pinKey = "paired_${laptop.deviceId}_fingerprint"
                var savedToken = prefs.getString(tokenKey, "").orEmpty()
                var savedFingerprint = normalizeFingerprint(prefs.getString(pinKey, "").orEmpty())
                val legacyAddress = prefs.getString("address", "").orEmpty()
                val legacyToken = prefs.getString("key", "").orEmpty()
                val legacyFingerprint = normalizeFingerprint(prefs.getString("fingerprint", "").orEmpty())
                if (savedToken.isBlank() && legacyToken.isNotBlank() &&
                    (legacyAddress == laptop.address || laptop.addresses.any { it.equals(legacyAddress, ignoreCase = true) }) &&
                    legacyFingerprint == laptop.fingerprint) {
                    savedToken = legacyToken
                    savedFingerprint = legacyFingerprint
                }

                if (savedToken.isNotBlank()) {
                    if (savedFingerprint.isNotBlank() && savedFingerprint != laptop.fingerprint)
                        throw IllegalStateException("Sertifikat laptop berubah. Hapus pemasangan lama lalu pasangkan kembali melalui persetujuan Windows.")
                    savedFingerprint = laptop.fingerprint
                    val statusData = PinnedTls.withConnection(connectionAddress, laptop.port, "/api/status", "GET", savedToken, savedFingerprint) { connection ->
                        val code = connection.responseCode
                        if (code != 200) throw IllegalStateException(if (code == 401) "Kunci pemasangan ditolak Windows (HTTP 401)." else "Laptop menolak koneksi (HTTP $code).")
                        JSONObject(connection.inputStream.bufferedReader().use { it.readText() })
                    }
                    if (statusData.optString("deviceId") != laptop.deviceId)
                        throw IllegalStateException("Identitas laptop tidak cocok dengan perangkat yang dipilih.")
                    main.post { updateConnectionSteps(laptop, 3) }
                } else {
                    val code = SecureRandom().nextInt(1_000_000).toString().padStart(6, '0')
                    val phoneName = "${android.os.Build.MANUFACTURER} ${android.os.Build.MODEL}".trim().take(48)
                    val encodedName = URLEncoder.encode(phoneName, "UTF-8")
                    val path = "/api/pair?clientId=${androidClientId()}&deviceName=$encodedName&code=$code"
                    main.post {
                        findViewById<TextView>(R.id.connectionInfo).text = "Kode pemasangan $code — cocokkan dengan dialog di laptop, lalu setujui di Windows."
                        findViewById<TextView>(R.id.discoveryStatus).text = "Menunggu persetujuan di ${laptop.deviceName}..."
                    }
                    val paired = PinnedTls.withUnpairedConnection(connectionAddress, laptop.port, path, laptop.fingerprint) { connection ->
                        main.post { updateConnectionSteps(laptop, 2) }
                        val responseCode = connection.responseCode
                        if (responseCode != 200) {
                            val message = when (responseCode) {
                                403 -> "Pemasangan ditolak atau dialog Windows ditutup."
                                429 -> {
                                    val waitSeconds = connection.getHeaderField("Retry-After")?.toIntOrNull()
                                    val waitText = if (waitSeconds != null) "dalam ${(waitSeconds + 59) / 60} menit" else "nanti"
                                    "Terlalu banyak percobaan pemasangan, atau ada pemasangan lain yang sedang menunggu persetujuan. Coba lagi $waitText."
                                }
                                else -> "Pemasangan gagal (HTTP $responseCode)."
                            }
                            throw IllegalStateException(message)
                        }
                        JSONObject(connection.inputStream.bufferedReader().use { it.readText() })
                    }
                    if (paired.optString("deviceId") != laptop.deviceId)
                        throw IllegalStateException("Identitas laptop tidak cocok dengan daftar perangkat.")
                    savedToken = paired.optString("token")
                    savedFingerprint = normalizeFingerprint(paired.optString("fingerprint"))
                    if (savedToken.length < 32 || savedFingerprint != laptop.fingerprint)
                        throw IllegalStateException("Data pemasangan tidak valid. Coba cari laptop dan pasangkan kembali.")
                    prefs.edit().putString(tokenKey, savedToken).putString(pinKey, savedFingerprint).apply()
                    main.post { updateConnectionSteps(laptop, 3) }
                }

                address = connectionAddress
                key = savedToken
                prefs.edit().putString("address", address).putString("key", key).putString("fingerprint", savedFingerprint)
                    .putString("activeDeviceId", laptop.deviceId).apply()
                main.post {
                    addressInput.setText(address)
                    keyInput.setText(key)
                    fingerprintInput.setText(savedFingerprint)
                    pendingConnectionDeviceId = laptop.deviceId
                    status.text = "Terhubung"
                    status.setTextColor(Color.rgb(129, 245, 202))
                    updateConnectionSteps(laptop, 3)
                    refreshDiscoveryConnectionState()
                    findViewById<TextView>(R.id.discoveryStatus).text = "Perangkat terpilih: ${laptop.deviceName}"
                    findViewById<TextView>(R.id.connectionInfo).text = "Koneksi terenkripsi lewat ${discoveryTransport(laptop.copy(address = connectionAddress))}; jalur LAN/Tailscale tersimpan dan dicoba otomatis bila diperlukan."
                }
                worker.execute { poll() }
            } catch (e: Exception) {
                main.post {
                    playConnectionChime(success = false)
                    failedDiscoveryDeviceId = laptop.deviceId
                    status.text = e.message ?: "Koneksi gagal."
                    updateConnectionSteps(laptop, 2, e.message ?: "Koneksi gagal")
                    if (!laptopOnline) transitionStatusPill("Menunggu", "waiting")
                    findViewById<TextView>(R.id.discoveryStatus).text = e.message ?: "Koneksi gagal."
                    findViewById<TextView>(R.id.connectionInfo).text = e.message ?: "Tidak dapat menghubungkan laptop."
                }
            } finally {
                main.post {
                    animation?.cancel()
                    signal.alpha = 1f
                    spinner.visibility = View.GONE
                    pairingRunning.set(false)
                    refreshDiscoveryConnectionState()
                }
            }
        }
    }

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel("laptop_status_events", "Status laptop", NotificationManager.IMPORTANCE_DEFAULT).apply {
                description = "Pemberitahuan koneksi dan baterai laptop"
            }
            val jokeChannel = NotificationChannel("laptop_startup_joke", "Joke laptop saat Android dibuka", NotificationManager.IMPORTANCE_HIGH).apply {
                description = "Joke dan suara pemberitahuan ketika aplikasi Android dibuka"
                enableVibration(true)
                vibrationPattern = longArrayOf(0, 260, 110, 260)
                setSound(
                    android.media.RingtoneManager.getDefaultUri(android.media.RingtoneManager.TYPE_NOTIFICATION),
                    android.media.AudioAttributes.Builder()
                        .setUsage(android.media.AudioAttributes.USAGE_NOTIFICATION)
                        .setContentType(android.media.AudioAttributes.CONTENT_TYPE_SONIFICATION)
                        .build()
                )
            }
            getSystemService(NotificationManager::class.java).apply {
                createNotificationChannel(channel)
                createNotificationChannel(jokeChannel)
            }
        }
    }

    private fun requestNotificationPermission() {
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != android.content.pm.PackageManager.PERMISSION_GRANTED) {
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 4101)
        }
    }

    private fun notifyLaptopOnline(name: String, now: String) {
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != android.content.pm.PackageManager.PERMISSION_GRANTED) return
        val notification = NotificationCompat.Builder(this, "laptop_status_events")
            .setSmallIcon(R.drawable.ic_laptop)
            .setContentTitle("Laptop menyala")
            .setContentText("$name terhubung kembali • $now")
            .setPriority(NotificationCompat.PRIORITY_DEFAULT)
            .setAutoCancel(true)
            .build()
        NotificationManagerCompat.from(this).notify(4102, notification)
    }

    private fun maybePostAndroidJokeNotification(data: JSONObject) {
        val active = data.optBoolean("androidJokeTriggerActive", false)
        val enabled = data.optBoolean("androidJokeNotificationEnabled", false)
        androidJokeActiveOnWindows = active
        if (!active || !enabled) {
            androidJokeNotificationShown = false
            dismissAndroidJokeOverlay()
            return
        }
        if (androidJokeNotificationShown) return
        androidJokeNotificationShown = true

        if (!isFinishing && !isDestroyed) showAndroidJokeOverlay(data)
        val message = data.optString("androidJokeNotificationMessage")
            .trim()
            .take(240)
            .ifBlank { "Laptop Status aktif di Android. Semoga harimu lancar!" }
        val notificationsAllowed = (Build.VERSION.SDK_INT < 33 || checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == android.content.pm.PackageManager.PERMISSION_GRANTED) &&
            NotificationManagerCompat.from(this).areNotificationsEnabled()
        if (notificationsAllowed) {
            val notification = NotificationCompat.Builder(this, "laptop_startup_joke")
                .setSmallIcon(R.drawable.ic_laptop)
                .setContentTitle("Joke Laptop Status")
                .setContentText(message)
                .setStyle(NotificationCompat.BigTextStyle().bigText(message))
                .setPriority(NotificationCompat.PRIORITY_MAX)
                .setCategory(NotificationCompat.CATEGORY_MESSAGE)
                .setAutoCancel(true)
                .setOnlyAlertOnce(true)
                .build()
            NotificationManagerCompat.from(this).notify(4103, notification)
            logLiveActivity("NOTIFY", "android_joke_notification_posted")
        } else {
            logLiveActivity("NOTIFY", "android_joke_notification_skipped reason=notifications_disabled", 'W')
        }
    }

    private fun showAndroidJokeOverlay(data: JSONObject) {
        if (Looper.myLooper() != Looper.getMainLooper()) {
            main.post { showAndroidJokeOverlay(data) }
            return
        }
        if (androidJokeDialog?.isShowing == true || isFinishing || isDestroyed) return
        val color = data.optString("androidJokeBackgroundHex", "#AE1218")
            .takeIf { it.matches(Regex("#[0-9A-Fa-f]{6}")) }
            ?.let { runCatching { Color.parseColor(it) }.getOrNull() } ?: Color.rgb(174, 18, 24)
        val message = data.optString("androidJokeNotificationMessage")
            .trim().take(200).ifBlank { "Kamu telah mengakses laptop tanpa seizin pemilik." }
        val padding = px(26)
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
            setPadding(padding, padding, padding, padding)
            background = GradientDrawable().apply {
                setColor(color)
                cornerRadius = px(28).toFloat()
            }
        }
        val iconBase64 = data.optString("androidJokeIconBase64")
        val icon: View = if (iconBase64.isNotBlank()) {
            val bitmap = runCatching {
                val bytes = android.util.Base64.decode(iconBase64, android.util.Base64.DEFAULT)
                val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
                BitmapFactory.decodeByteArray(bytes, 0, bytes.size, bounds)
                bounds.inSampleSize = maxOf(1, maxOf(bounds.outWidth, bounds.outHeight) / 384)
                bounds.inJustDecodeBounds = false
                BitmapFactory.decodeByteArray(bytes, 0, bytes.size, bounds)
            }.getOrNull()
            if (bitmap != null) ImageView(this).apply {
                setImageBitmap(bitmap)
                scaleType = ImageView.ScaleType.FIT_CENTER
                layoutParams = LinearLayout.LayoutParams(px(128), px(128)).apply { bottomMargin = px(18) }
                contentDescription = "Ikon joke"
            } else jokeGlyphView(data)
        } else jokeGlyphView(data)
        content.addView(icon)
        content.addView(TextView(this).apply {
            text = "MODE JOKE AKTIF"
            setTextColor(Color.WHITE)
            textSize = 28f
            typeface = android.graphics.Typeface.DEFAULT_BOLD
            gravity = Gravity.CENTER
            setPadding(0, 0, 0, px(16))
        })
        content.addView(TextView(this).apply {
            text = message
            setTextColor(Color.WHITE)
            textSize = 20f
            gravity = Gravity.CENTER
            setPadding(0, 0, 0, px(18))
        })
        content.addView(TextView(this).apply {
            text = "SIMULASI JOKE • layar tertutup otomatis dalam 30 detik"
            setTextColor(Color.WHITE)
            alpha = 0.82f
            textSize = 13f
            gravity = Gravity.CENTER
            setPadding(0, 0, 0, px(24))
        })
        val close = Button(this).apply {
            text = "Tutup prank"
            setOnClickListener { dismissAndroidJokeOverlay() }
        }
        content.addView(close, LinearLayout.LayoutParams(-1, px(52)))

        val dialog = Dialog(this)
        dialog.setContentView(content)
        dialog.setCancelable(true)
        dialog.setCanceledOnTouchOutside(false)
        dialog.setOnDismissListener {
            if (androidJokeDialog === dialog) androidJokeDialog = null
            androidJokeAutoDismiss.removeCallbacks(androidJokeDismissRunnable)
        }
        dialog.show()
        dialog.window?.apply {
            setBackgroundDrawable(ColorDrawable(Color.TRANSPARENT))
            setLayout(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
            addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
            addFlags(WindowManager.LayoutParams.FLAG_DIM_BEHIND)
            setDimAmount(0.08f)
        }
        androidJokeDialog = dialog
        animateAndroidJokeIcon(icon, data.optString("androidJokeAnimation", "pulse"))
        androidJokeAutoDismiss.removeCallbacks(androidJokeDismissRunnable)
        androidJokeAutoDismiss.postDelayed(androidJokeDismissRunnable, 30_000)
        logLiveActivity("JOKE", "android_overlay_started animation=${data.optString("androidJokeAnimation", "pulse")}")
    }

    private fun jokeGlyphView(data: JSONObject): TextView = TextView(this).apply {
        text = data.optString("androidJokeIconGlyph").trim().take(8).ifBlank { "😵" }
        textSize = 84f
        gravity = Gravity.CENTER
        setTextColor(Color.WHITE)
        layoutParams = LinearLayout.LayoutParams(-1, px(142)).apply { bottomMargin = px(10) }
        contentDescription = "Ikon joke"
    }

    private fun animateAndroidJokeIcon(icon: View, animation: String) {
        val name = animation.lowercase(Locale.ROOT)
        val animators = when (name) {
            "spin" -> listOf(ObjectAnimator.ofFloat(icon, View.ROTATION, 0f, 360f).apply { duration = 1500; repeatCount = ValueAnimator.INFINITE })
            "bounce" -> listOf(ObjectAnimator.ofFloat(icon, View.TRANSLATION_Y, 0f, -px(26).toFloat()).loop(420))
            "shake" -> listOf(ObjectAnimator.ofFloat(icon, View.TRANSLATION_X, -px(12).toFloat(), px(12).toFloat()).loop(65))
            "swing" -> listOf(ObjectAnimator.ofFloat(icon, View.ROTATION, -14f, 14f).loop(360))
            "zoom" -> listOf(ObjectAnimator.ofFloat(icon, View.SCALE_X, .72f, 1.16f).loop(620), ObjectAnimator.ofFloat(icon, View.SCALE_Y, .72f, 1.16f).loop(620))
            "fade" -> listOf(ObjectAnimator.ofFloat(icon, View.ALPHA, 1f, .28f).loop(520))
            "slide" -> listOf(ObjectAnimator.ofFloat(icon, View.TRANSLATION_X, -px(42).toFloat(), px(42).toFloat()).loop(820))
            "wobble" -> listOf(ObjectAnimator.ofFloat(icon, View.ROTATION, -9f, 9f).loop(150), ObjectAnimator.ofFloat(icon, View.SCALE_X, 1f, 1.12f).loop(230), ObjectAnimator.ofFloat(icon, View.SCALE_Y, 1f, .92f).loop(230))
            "pop" -> listOf(ObjectAnimator.ofFloat(icon, View.SCALE_X, .62f, 1.2f).loop(300), ObjectAnimator.ofFloat(icon, View.SCALE_Y, .62f, 1.2f).loop(300))
            else -> listOf(ObjectAnimator.ofFloat(icon, View.SCALE_X, 1f, 1.18f).loop(720), ObjectAnimator.ofFloat(icon, View.SCALE_Y, 1f, 1.18f).loop(720))
        }
        androidJokeAnimator?.cancel()
        androidJokeAnimator = android.animation.AnimatorSet().apply { playTogether(animators); start() }
    }

    private fun ObjectAnimator.loop(durationMs: Long): ObjectAnimator = apply {
        duration = durationMs
        repeatMode = ValueAnimator.REVERSE
        repeatCount = ValueAnimator.INFINITE
    }

    private fun dismissAndroidJokeOverlay() {
        androidJokeAutoDismiss.removeCallbacks(androidJokeDismissRunnable)
        androidJokeAnimator?.cancel()
        androidJokeAnimator = null
        androidJokeDialog?.dismiss()
        androidJokeDialog = null
    }

    private fun showWindowsAppPicker() {
        if (!laptopOnline) {
            android.widget.Toast.makeText(this, "Hubungkan laptop terlebih dahulu.", android.widget.Toast.LENGTH_SHORT).show()
            return
        }
        val apps = arrayOf("Notepad", "Kalkulator", "File Explorer", "Task Manager", "Pengaturan Windows")
        val commands = arrayOf("open-app-notepad", "open-app-calculator", "open-app-explorer", "open-app-task-manager", "open-app-settings")
        AlertDialog.Builder(this)
            .setTitle("Buka aplikasi di Windows")
            .setItems(apps) { _, index -> worker.execute { sendControl(commands[index]) } }
            .setNegativeButton("Batal", null)
            .show()
    }

    private fun showRemoteShellDialog() {
        if (!laptopOnline || address.isBlank() || key.isBlank()) {
            android.widget.Toast.makeText(this, "Hubungkan dan pasangkan laptop terlebih dahulu.", android.widget.Toast.LENGTH_LONG).show()
            return
        }
        if (nativeShellDialog?.isShowing == true) return

        val density = resources.displayMetrics.density
        fun px(value: Int) = (value * density).toInt()
        fun surface(fill: Int, radius: Float = 18f) = GradientDrawable().apply {
            shape = GradientDrawable.RECTANGLE
            setColor(fill)
            cornerRadius = px(radius.toInt()).toFloat()
            setStroke(px(1), getColor(R.color.line))
        }
        fun label(value: String, size: Float, color: Int, bold: Boolean = false) = TextView(this).apply {
            text = value
            textSize = size
            setTextColor(color)
            if (bold) setTypeface(typeface, android.graphics.Typeface.BOLD)
        }
        fun pill(value: String, fill: Int, color: Int, radius: Float = 15f) = label(value, 14f, color, true).apply {
            gravity = Gravity.CENTER
            setPadding(px(14), px(10), px(14), px(10))
            background = surface(fill, radius)
            isClickable = true
        }

        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(px(16), px(10), px(16), px(18))
            setBackgroundColor(getColor(R.color.bg))
        }
        val header = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
        val closeButton = pill("‹", getColor(R.color.surface_raised), getColor(R.color.text_main), 16f).apply {
            textSize = 30f
            setPadding(0, 0, 0, px(3))
            contentDescription = "Tutup terminal"
        }
        header.addView(closeButton, LinearLayout.LayoutParams(px(48), px(48)))
        val titleBlock = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(px(12), 0, 0, 0) }
        titleBlock.addView(label(if (connectedHostIsLinux) "Terminal Linux" else "Terminal Windows", 20f, getColor(R.color.text_main), true))
        titleBlock.addView(label("Kendali aman ke laptop", 12f, getColor(R.color.text_dim)).apply { setPadding(0, px(2), 0, 0) })
        header.addView(titleBlock, LinearLayout.LayoutParams(0, -2, 1f))
        header.addView(pill("●  TLS", getColor(R.color.accent_bg), getColor(R.color.accent), 18f).apply { textSize = 11f },
            LinearLayout.LayoutParams(-2, px(38)))
        root.addView(header, LinearLayout.LayoutParams(-1, -2))

        root.addView(label("PILIH SHELL", 11f, getColor(R.color.text_dim), true), LinearLayout.LayoutParams(-1, -2).apply {
            topMargin = px(14); bottomMargin = px(7)
        })
        val shellType = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            setPadding(px(4), px(4), px(4), px(4))
            background = surface(getColor(R.color.surface), 17f)
        }
        val powershellButton = pill(if (connectedHostIsLinux) "Bash" else "PowerShell", getColor(R.color.accent), getColor(R.color.accent_on), 14f)
        val cmdButton = pill(if (connectedHostIsLinux) "POSIX Shell" else "Command Prompt", getColor(R.color.surface), getColor(R.color.text_dim), 14f)
        shellType.addView(powershellButton, LinearLayout.LayoutParams(0, px(44), 1f))
        shellType.addView(cmdButton, LinearLayout.LayoutParams(0, px(44), 1f).apply { marginStart = px(4) })
        root.addView(shellType, LinearLayout.LayoutParams(-1, -2))
        var selectedShellType = if (connectedHostIsLinux) "bash" else "powershell"
        fun selectShell(type: String) {
            selectedShellType = type
            val psSelected = type == "powershell"
            powershellButton.background = surface(getColor(if (psSelected) R.color.accent else R.color.surface), 14f)
            powershellButton.setTextColor(getColor(if (psSelected) R.color.accent_on else R.color.text_dim))
            cmdButton.background = surface(getColor(if (psSelected) R.color.surface else R.color.accent), 14f)
            cmdButton.setTextColor(getColor(if (psSelected) R.color.text_dim else R.color.accent_on))
        }
        powershellButton.setOnClickListener { selectShell("powershell") }
        cmdButton.setOnClickListener { selectShell("cmd") }

        val actions = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
        val startButton = pill("Mulai sesi", getColor(R.color.accent), getColor(R.color.accent_on), 16f)
        val stopButton = pill("Hentikan", getColor(R.color.danger_button), getColor(R.color.danger), 16f).apply { isEnabled = false; alpha = .55f }
        actions.addView(startButton, LinearLayout.LayoutParams(0, px(50), 1f))
        actions.addView(stopButton, LinearLayout.LayoutParams(0, px(50), 1f).apply { marginStart = px(9) })
        root.addView(actions, LinearLayout.LayoutParams(-1, -2).apply { topMargin = px(8) })

        val outputHeader = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
        outputHeader.addView(label("OUTPUT TERMINAL", 12f, getColor(R.color.text_dim), true), LinearLayout.LayoutParams(0, -2, 1f))
        val status = label("SIAP", 10f, getColor(R.color.accent), true)
        outputHeader.addView(status)
        root.addView(outputHeader, LinearLayout.LayoutParams(-1, -2).apply { topMargin = px(12); bottomMargin = px(6) })

        val output = TextView(this).apply {
            text = "Mulai sesi terminal untuk melihat hasil perintah di sini."
            typeface = android.graphics.Typeface.MONOSPACE
            textSize = 12f
            setTextColor(getColor(R.color.text_main))
            setPadding(px(14), px(14), px(14), px(14))
            movementMethod = ScrollingMovementMethod.getInstance()
            setTextIsSelectable(true)
        }
        val outputScroll = android.widget.ScrollView(this).apply {
            isFillViewport = true
            addView(output, android.widget.FrameLayout.LayoutParams(-1, -1))
        }
        val outputCard = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = surface(getColor(R.color.surface), 18f)
            setPadding(px(2), px(2), px(2), px(2))
            addView(outputScroll, LinearLayout.LayoutParams(-1, 0, 1f))
        }
        // Keep a visible, scrollable output console while reserving room for every keyboard row.
        val outputHeightDp = (resources.displayMetrics.heightPixels / density * .30f).toInt().coerceIn(170, 280)
        root.addView(outputCard, LinearLayout.LayoutParams(-1, px(outputHeightDp)))

        val note = label("Standar berjalan tanpa UAC. Tindakan administrator tetap meminta persetujuan Windows.", 11f, getColor(R.color.text_dim)).apply {
            setPadding(px(2), px(6), px(2), px(5))
        }
        root.addView(note, LinearLayout.LayoutParams(-1, -2))
        val inputRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(px(5), px(5), px(5), px(5))
            background = surface(getColor(R.color.surface_raised), 18f)
        }
        val input = EditText(this).apply {
            hint = "Ketik perintah..."
            isSingleLine = true
            imeOptions = android.view.inputmethod.EditorInfo.IME_ACTION_SEND
            inputType = android.text.InputType.TYPE_CLASS_TEXT or android.text.InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS
            setTextColor(getColor(R.color.text_main))
            setHintTextColor(getColor(R.color.text_dim))
            setPadding(px(12), 0, px(8), 0)
            background = null
            showSoftInputOnFocus = false
            isFocusableInTouchMode = true
            isEnabled = false
        }
        val commandUndo = mutableListOf<String>()
        val commandRedo = mutableListOf<String>()
        var previousCommandText = ""
        var restoringCommandText = false
        input.addTextChangedListener(object : android.text.TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {
                if (!restoringCommandText) previousCommandText = s?.toString().orEmpty()
            }
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) = Unit
            override fun afterTextChanged(s: android.text.Editable?) {
                val current = s?.toString().orEmpty()
                if (!restoringCommandText && current != previousCommandText) {
                    commandUndo.add(previousCommandText)
                    if (commandUndo.size > 50) commandUndo.removeAt(0)
                    commandRedo.clear()
                }
            }
        })
        val sendButton = pill("Kirim", getColor(R.color.accent), getColor(R.color.accent_on), 14f).apply { isEnabled = false; alpha = .55f }
        inputRow.addView(input, LinearLayout.LayoutParams(0, px(48), 1f))
        inputRow.addView(sendButton, LinearLayout.LayoutParams(px(86), px(46)))
        root.addView(inputRow, LinearLayout.LayoutParams(-1, -2))

        val keyboardPanel = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(px(4), px(4), px(4), px(4))
            background = surface(getColor(R.color.surface), 16f)
        }
        val keyboardTabs = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        val keyboardBody = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        val keyboardPages = linkedMapOf<String, LinearLayout>()
        val keyboardTabButtons = linkedMapOf<String, TextView>()
        var activeKeyboardTab = "ABC"
        var shiftActive = false
        var ctrlActive = false
        var altActive = false
        var fnActive = false
        var overwriteMode = false
        var numLockEnabled = true
        var insertModeButton: TextView? = null
        var numLockButton: TextView? = null
        val modifierViews = mutableMapOf<String, MutableList<TextView>>()

        fun refreshModifiers() {
            modifierViews.forEach { (name, views) ->
                val active = when (name) { "Ctrl" -> ctrlActive; "Alt" -> altActive; "Shift" -> shiftActive; "Fn" -> fnActive; else -> false }
                views.forEach { view ->
                    view.background = surface(getColor(if (active) R.color.accent_bg else R.color.surface_raised), 9f)
                    view.setTextColor(getColor(if (active) R.color.accent else R.color.text_main))
                }
            }
        }
        fun toggleModifier(name: String) {
            when (name) {
                "Ctrl" -> ctrlActive = !ctrlActive
                "Alt" -> altActive = !altActive
                "Shift" -> shiftActive = !shiftActive
                "Fn" -> fnActive = !fnActive
            }
            refreshModifiers()
        }

        fun refreshKeyboardTabs() {
            keyboardTabButtons.forEach { (name, button) ->
                val selected = name == activeKeyboardTab
                button.background = surface(getColor(if (selected) R.color.accent_bg else R.color.surface), 11f)
                button.setTextColor(getColor(if (selected) R.color.accent else R.color.text_dim))
            }
            keyboardPages.forEach { (name, page) -> page.visibility = if (name == activeKeyboardTab) View.VISIBLE else View.GONE }
        }
        listOf("ABC", "123", "Fn", "Nav", "Num").forEach { name ->
            val tab = pill(name, getColor(R.color.surface), getColor(R.color.text_dim), 11f).apply { textSize = 10f }
            keyboardTabButtons[name] = tab
            keyboardTabs.addView(tab, LinearLayout.LayoutParams(0, px(34), 1f).apply { marginEnd = px(3) })
            val page = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; visibility = View.GONE }
            keyboardPages[name] = page
            keyboardBody.addView(page, LinearLayout.LayoutParams(-1, -2))
            tab.setOnClickListener { activeKeyboardTab = name; refreshKeyboardTabs() }
        }

        fun insertCommandText(value: String) {
            if (!input.isEnabled) return
            val editable = input.text ?: return
            val start = input.selectionStart.coerceAtLeast(0).coerceAtMost(editable.length)
            val end = input.selectionEnd.coerceAtLeast(0).coerceAtMost(editable.length)
            val lower = minOf(start, end)
            val upper = maxOf(start, end)
            val shifted = if (shiftActive) value.map { ch ->
                when (ch) {
                    '1' -> '!'; '2' -> '@'; '3' -> '#'; '4' -> '$'; '5' -> '%'; '6' -> '^'; '7' -> '&'; '8' -> '*'; '9' -> '('; '0' -> ')'
                    '-' -> '_'; '=' -> '+'; '[' -> '{'; ']' -> '}'; '\\' -> '|'; ';' -> ':'; '\'' -> '"'; ',' -> '<'; '.' -> '>'; '/' -> '?'; '`' -> '~'
                    else -> if (ch.isLetter()) ch.uppercaseChar() else ch
                }
            }.joinToString("") else value
            val replaceEnd = if (lower == upper && overwriteMode) (lower + shifted.length).coerceAtMost(editable.length) else upper
            editable.replace(lower, replaceEnd, shifted)
            val cursor = (lower + shifted.length).coerceAtMost(editable.length)
            input.setSelection(cursor)
            if (shiftActive) { shiftActive = false; refreshModifiers() }
        }
        fun eraseBeforeCursor() {
            if (!input.isEnabled) return
            val editable = input.text ?: return
            val start = input.selectionStart.coerceAtLeast(0).coerceAtMost(editable.length)
            val end = input.selectionEnd.coerceAtLeast(0).coerceAtMost(editable.length)
            if (start != end) editable.delete(minOf(start, end), maxOf(start, end))
            else if (start > 0) editable.delete(start - 1, start)
            input.setSelection(input.selectionStart.coerceAtLeast(0).coerceAtMost(editable.length))
        }
        fun eraseAtCursor() {
            if (!input.isEnabled) return
            val editable = input.text ?: return
            val start = input.selectionStart.coerceAtLeast(0).coerceAtMost(editable.length)
            val end = input.selectionEnd.coerceAtLeast(0).coerceAtMost(editable.length)
            if (start != end) editable.delete(minOf(start, end), maxOf(start, end))
            else if (start < editable.length) editable.delete(start, start + 1)
            input.setSelection(input.selectionStart.coerceAtLeast(0).coerceAtMost(editable.length))
        }
        fun moveCursor(delta: Int) {
            val length = input.text?.length ?: 0
            input.setSelection((input.selectionStart + delta).coerceIn(0, length))
        }
        fun addKeyRow(pageName: String, keys: List<Pair<String, () -> Unit>>, height: Int = 40) {
            val row = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
            keys.forEach { (caption, action) ->
                val key = pill(caption, getColor(R.color.surface_raised), getColor(R.color.text_main), 9f).apply {
                    textSize = if (caption.length > 4) 11f else 13f
                    setPadding(px(4), px(3), px(4), px(3))
                    setOnClickListener { action() }
                }
                if (caption in setOf("Ctrl", "Alt", "Shift", "Fn")) modifierViews.getOrPut(caption) { mutableListOf() }.add(key)
                if (caption == "Ins") insertModeButton = key
                if (caption == "Num") numLockButton = key
                val weight = when (caption) { "Space" -> 3f; "Shift" -> 1.5f; "Backspace", "Enter", "Ctrl", "Alt", "Tab" -> 1.35f; else -> 1f }
                row.addView(key, LinearLayout.LayoutParams(0, px(height), weight).apply { marginEnd = px(3) })
            }
            keyboardPages[pageName]?.addView(row, LinearLayout.LayoutParams(-1, -2).apply { if ((keyboardPages[pageName]?.childCount ?: 0) > 0) topMargin = px(3) })
        }
        fun sendFunctionShortcut(number: Int) {
            val powershell = selectedShellType == "powershell"
            val snippets = if (powershell) listOf(
                "Get-Help ", "Get-Command ", "Get-History", "Get-Process ", "Get-Service ", "Test-NetConnection ",
                "Get-ChildItem ", "Get-Location", "Get-ComputerInfo", "Get-EventLog -LogName System -Newest 10", "Clear-Host", "Get-CimInstance Win32_OperatingSystem"
            ) else listOf(
                "help ", "doskey /history", "doskey /history", "tasklist ", "ipconfig ", "ping ",
                "dir ", "cd", "systeminfo", "cls", "whoami", "help"
            )
            insertCommandText(snippets[(number - 1).coerceIn(0, snippets.lastIndex)])
        }
        fun alphaKey(key: String) {
            if (ctrlActive) {
                ctrlActive = false
                refreshModifiers()
                when (key.lowercase(Locale.ROOT)) {
                    "a" -> input.selectAll()
                    "c" -> {
                        if (input.hasSelection()) {
                            val start = minOf(input.selectionStart, input.selectionEnd).coerceAtLeast(0)
                            val end = maxOf(input.selectionStart, input.selectionEnd).coerceAtMost(input.text?.length ?: 0)
                            val selected = input.text?.substring(start, end).orEmpty()
                            (getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager)
                                .setPrimaryClip(ClipData.newPlainText("command", selected))
                        } else if (remoteShellSessionId.isNotBlank()) writeRemoteShell("\u0003")
                    }
                    "v" -> (getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager)
                        .primaryClip?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.coerceToText(this)
                        ?.toString()?.let(::insertCommandText)
                    "x" -> if (input.hasSelection()) {
                        val start = minOf(input.selectionStart, input.selectionEnd).coerceAtLeast(0)
                        val end = maxOf(input.selectionStart, input.selectionEnd).coerceAtMost(input.text?.length ?: 0)
                        val selected = input.text?.substring(start, end).orEmpty()
                        (getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager)
                            .setPrimaryClip(ClipData.newPlainText("command", selected))
                        input.text?.delete(start, end)
                    }
                    "z" -> if (commandUndo.isNotEmpty()) {
                        val current = input.text?.toString().orEmpty()
                        val previous = commandUndo.removeAt(commandUndo.lastIndex)
                        commandRedo.add(current)
                        restoringCommandText = true
                        input.setText(previous)
                        input.setSelection(previous.length)
                        restoringCommandText = false
                    }
                    "y" -> if (commandRedo.isNotEmpty()) {
                        val current = input.text?.toString().orEmpty()
                        val next = commandRedo.removeAt(commandRedo.lastIndex)
                        commandUndo.add(current)
                        restoringCommandText = true
                        input.setText(next)
                        input.setSelection(next.length)
                        restoringCommandText = false
                    }
                    "l" -> nativeShellOutput?.text = ""
                    else -> insertCommandText(key)
                }
                return
            }
            if (altActive && key == "Backspace") {
                altActive = false
                val text = input.text?.toString().orEmpty()
                val cursor = input.selectionStart.coerceIn(0, text.length)
                var cut = cursor
                while (cut > 0 && text[cut - 1].isWhitespace()) cut--
                while (cut > 0 && !text[cut - 1].isWhitespace()) cut--
                if (cut < cursor) input.text?.delete(cut, cursor)
                return
            }
            when (key) {
                "Backspace" -> eraseBeforeCursor()
                "Delete" -> eraseAtCursor()
                "Esc" -> input.text?.clear()
                "Space" -> insertCommandText(" ")
                "Tab" -> insertCommandText("    ")
                "Enter" -> sendButton.performClick()
                "Left" -> moveCursor(-1)
                "Right" -> moveCursor(1)
                "Home" -> input.setSelection(0)
                "End" -> input.setSelection(input.text?.length ?: 0)
                in (1..12).map { "F$it" } -> key.removePrefix("F").toIntOrNull()?.let(::sendFunctionShortcut)
                else -> insertCommandText(key)
            }
            altActive = false
            refreshModifiers()
        }

        addKeyRow("ABC", ("1234567890").map { it.toString() to { alphaKey(it.toString()) } })
        addKeyRow("ABC", "qwertyuiop".map { it.toString() to { alphaKey(it.toString()) } })
        addKeyRow("ABC", "asdfghjkl".map { it.toString() to { alphaKey(it.toString()) } })
        addKeyRow("ABC", listOf("Shift" to { toggleModifier("Shift") }, "z" to { alphaKey("z") }, "x" to { alphaKey("x") }, "c" to { alphaKey("c") }, "v" to { alphaKey("v") }, "b" to { alphaKey("b") }, "n" to { alphaKey("n") }, "m" to { alphaKey("m") }, "Backspace" to { alphaKey("Backspace") }))
        addKeyRow("ABC", listOf(
            "Ctrl" to { toggleModifier("Ctrl") }, "Alt" to { toggleModifier("Alt") },
            "Fn" to { toggleModifier("Fn"); activeKeyboardTab = if (fnActive) "Fn" else "ABC"; refreshKeyboardTabs() },
            "Tab" to { alphaKey("Tab") }, "Space" to { alphaKey("Space") }, "Enter" to { alphaKey("Enter") }
            ), 28)

        addKeyRow("123", "1234567890".map { it.toString() to { alphaKey(it.toString()) } })
        addKeyRow("123", listOf("@", "#", "$", "%", "&", "-", "+", "(", ")", "/").map { it to { alphaKey(it) } })
        addKeyRow("123", listOf("*", "\"", "'", ":", ";", "!", "?", "\\", "|", "~").map { it to { alphaKey(it) } })
        addKeyRow("123", listOf("<", ">", "=", "_", "{", "}", "[", "]", ".", ",").map { it to { alphaKey(it) } })
        addKeyRow("123", listOf("Ctrl" to { toggleModifier("Ctrl") }, "Alt" to { toggleModifier("Alt") }, "Space" to { alphaKey("Space") }, "Backspace" to { alphaKey("Backspace") }, "Enter" to { alphaKey("Enter") }))

        addKeyRow("Fn", (1..4).map { number -> "F$number" to { alphaKey("F$number") } })
        addKeyRow("Fn", (5..8).map { number -> "F$number" to { alphaKey("F$number") } })
        addKeyRow("Fn", (9..12).map { number -> "F$number" to { alphaKey("F$number") } })
        addKeyRow("Fn", listOf(
            "Fn" to { toggleModifier("Fn") }, "Ctrl" to { toggleModifier("Ctrl") }, "Alt" to { toggleModifier("Alt") },
            "Shift" to { toggleModifier("Shift") }, "Esc" to { alphaKey("Esc") }, "Tab" to { alphaKey("Tab") }
        ))
        addKeyRow("Fn", listOf("Ins" to {
            overwriteMode = !overwriteMode
            insertModeButton?.text = if (overwriteMode) "OVR" else "Ins"
        }, "Del" to { eraseAtCursor() }, "Home" to { alphaKey("Home") }, "End" to { alphaKey("End") }, "PgUp" to { outputScroll.fullScroll(View.FOCUS_UP) }, "PgDn" to { outputScroll.fullScroll(View.FOCUS_DOWN) }))

        addKeyRow("Nav", listOf("Esc" to { insertCommandText("^") }, "Tab" to { alphaKey("Tab") }, "Home" to { alphaKey("Home") }, "PgUp" to { outputScroll.fullScroll(View.FOCUS_UP) }, "End" to { alphaKey("End") }, "PgDn" to { outputScroll.fullScroll(View.FOCUS_DOWN) }))
        addKeyRow("Nav", listOf("←" to { alphaKey("Left") }, "↓" to { outputScroll.smoothScrollBy(0, px(180)) }, "↑" to { outputScroll.smoothScrollBy(0, -px(180)) }, "→" to { alphaKey("Right") }, "Del" to { eraseAtCursor() }, "Bksp" to { eraseBeforeCursor() }))
        addKeyRow("Nav", listOf("Ctrl" to { toggleModifier("Ctrl") }, "Alt" to { toggleModifier("Alt") }, "Shift" to { toggleModifier("Shift") }, "Space" to { alphaKey("Space") }, "Enter" to { alphaKey("Enter") }))

        addKeyRow("Num", listOf("Num" to {
            numLockEnabled = !numLockEnabled
            numLockButton?.text = if (numLockEnabled) "Num✓" else "Num"
        }, "/" to { alphaKey("/") }, "*" to { alphaKey("*") }, "-" to { alphaKey("-") }))
        addKeyRow("Num", listOf("7" to { if (numLockEnabled) alphaKey("7") else alphaKey("Home") }, "8" to { if (numLockEnabled) alphaKey("8") else outputScroll.smoothScrollBy(0, -px(180)) }, "9" to { if (numLockEnabled) alphaKey("9") else outputScroll.fullScroll(View.FOCUS_UP) }, "+" to { alphaKey("+") }))
        addKeyRow("Num", listOf("4" to { if (numLockEnabled) alphaKey("4") else alphaKey("Left") }, "5" to { if (numLockEnabled) alphaKey("5") }, "6" to { if (numLockEnabled) alphaKey("6") else alphaKey("Right") }, "Enter" to { alphaKey("Enter") }))
        addKeyRow("Num", listOf("1" to { if (numLockEnabled) alphaKey("1") else alphaKey("End") }, "2" to { if (numLockEnabled) alphaKey("2") else outputScroll.smoothScrollBy(0, px(180)) }, "3" to { if (numLockEnabled) alphaKey("3") else outputScroll.fullScroll(View.FOCUS_DOWN) }, "Bksp" to { eraseBeforeCursor() }))
        addKeyRow("Num", listOf("0" to { if (numLockEnabled) alphaKey("0") else { overwriteMode = !overwriteMode; insertModeButton?.text = if (overwriteMode) "OVR" else "Ins" } }, "." to { if (numLockEnabled) alphaKey(".") else eraseAtCursor() }, "←" to { alphaKey("Left") }, "→" to { alphaKey("Right") }))

        keyboardPanel.addView(keyboardTabs, LinearLayout.LayoutParams(-1, -2))
        keyboardPanel.addView(keyboardBody, LinearLayout.LayoutParams(-1, -2).apply { topMargin = px(5) })
        root.addView(keyboardPanel, LinearLayout.LayoutParams(-1, -2).apply { topMargin = px(5) })
        refreshKeyboardTabs()

        val dialogContent = android.widget.ScrollView(this).apply {
            isFillViewport = true
            clipToPadding = false
            addView(root, android.view.ViewGroup.LayoutParams(-1, -2))
        }
        val dialog = AlertDialog.Builder(this).setView(dialogContent).create()
        nativeShellDialog = dialog
        nativeShellOutput = output
        nativeShellInput = input
        nativeShellScroll = outputScroll
        nativeShellStatus = status
        nativeShellStartButton = startButton
        nativeShellStopButton = stopButton
        nativeShellSendButton = sendButton
        closeButton.setOnClickListener { dialog.dismiss() }
        startButton.setOnClickListener { startRemoteShell(selectedShellType) }
        stopButton.setOnClickListener { stopRemoteShell() }
        sendButton.setOnClickListener {
            val line = input.text?.toString().orEmpty()
            if (line.isNotBlank()) {
                appendRemoteShellOutput((if (selectedShellType == "cmd") "> " else "PS> ") + line + "\n")
                writeRemoteShell(line + "\n")
                input.text?.clear()
            }
        }
        input.setOnEditorActionListener { _, actionId, event ->
            if (actionId == android.view.inputmethod.EditorInfo.IME_ACTION_SEND || event?.keyCode == android.view.KeyEvent.KEYCODE_ENTER) {
                sendButton.performClick(); true
            } else false
        }
        dialog.setOnDismissListener {
            nativeShellDialog = null
            nativeShellOutput = null
            nativeShellInput = null
            nativeShellScroll = null
            nativeShellStatus = null
            nativeShellStartButton = null
            nativeShellStopButton = null
            nativeShellSendButton = null
            if (remoteShellSessionId.isNotBlank()) stopRemoteShell()
        }
        dialog.show()
        dialogContent.post { dialogContent.fullScroll(View.FOCUS_DOWN) }
        dialog.window?.apply {
            setBackgroundDrawable(ColorDrawable(Color.TRANSPARENT))
            setLayout(WindowManager.LayoutParams.MATCH_PARENT, WindowManager.LayoutParams.MATCH_PARENT)
            addFlags(WindowManager.LayoutParams.FLAG_DIM_BEHIND)
            attributes = attributes.apply { dimAmount = .42f }
            setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE)
        }
    }

    private fun showRemoteShellDialogLegacy() {
        if (!laptopOnline || address.isBlank() || key.isBlank()) {
            android.widget.Toast.makeText(this, "Hubungkan dan pasangkan laptop terlebih dahulu.", android.widget.Toast.LENGTH_LONG).show()
            return
        }
        if (nativeShellDialog?.isShowing == true) return

        val density = resources.displayMetrics.density
        fun px(value: Int) = (value * density).toInt()
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(px(18), px(8), px(18), px(4))
        }
        val note = TextView(this).apply {
            text = "Terminal berjalan dengan hak akun Windows biasa. Perintah standar tidak meminta UAC; tindakan administrator tetap mengikuti persetujuan Windows."
            setTextColor(getColor(R.color.text_dim))
            textSize = 12f
        }
        val shellType = android.widget.Spinner(this)
        shellType.adapter = android.widget.ArrayAdapter(
            this,
            android.R.layout.simple_spinner_dropdown_item,
            arrayOf("PowerShell", "Command Prompt")
        )
        val actionRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        val startButton = Button(this).apply { text = "Mulai" }
        val stopButton = Button(this).apply { text = "Hentikan"; isEnabled = false }
        actionRow.addView(startButton, LinearLayout.LayoutParams(0, px(48), 1f))
        actionRow.addView(stopButton, LinearLayout.LayoutParams(0, px(48), 1f).apply { marginStart = px(8) })
        val output = TextView(this).apply {
            text = "Terminal belum dimulai."
            typeface = android.graphics.Typeface.MONOSPACE
            textSize = 11f
            setTextColor(getColor(R.color.text_main))
            setPadding(px(12), px(12), px(12), px(12))
            setBackgroundResource(R.drawable.bg_input)
            movementMethod = ScrollingMovementMethod.getInstance()
        }
        val outputScroll = android.widget.ScrollView(this).apply {
            addView(output, android.widget.FrameLayout.LayoutParams(-1, -2))
        }
        root.addView(note)
        root.addView(shellType, LinearLayout.LayoutParams(-1, px(48)).apply { topMargin = px(8) })
        root.addView(actionRow, LinearLayout.LayoutParams(-1, px(48)).apply { topMargin = px(6) })
        root.addView(outputScroll, LinearLayout.LayoutParams(-1, px(250)).apply { topMargin = px(10) })
        val inputRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        val input = EditText(this).apply {
            hint = "Ketik perintah Windows"
            isSingleLine = true
            inputType = android.text.InputType.TYPE_CLASS_TEXT or android.text.InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS
            setTextColor(getColor(R.color.text_main))
            setHintTextColor(getColor(R.color.text_dim))
            isEnabled = false
        }
        val sendButton = Button(this).apply { text = "Jalankan"; isEnabled = false }
        inputRow.addView(input, LinearLayout.LayoutParams(0, px(52), 1f))
        inputRow.addView(sendButton, LinearLayout.LayoutParams(0, px(52), 1f).apply { marginStart = px(8) })
        root.addView(inputRow, LinearLayout.LayoutParams(-1, px(56)).apply { topMargin = px(8) })

        val dialog = AlertDialog.Builder(this)
            .setTitle("Terminal Windows")
            .setView(root)
            .setNegativeButton("Tutup", null)
            .create()
        nativeShellDialog = dialog
        nativeShellOutput = output
        nativeShellInput = input
        nativeShellScroll = outputScroll
        nativeShellStatus = note
        nativeShellStartButton = startButton
        nativeShellStopButton = stopButton
        nativeShellSendButton = sendButton
        startButton.setOnClickListener {
            val type = if (shellType.selectedItemPosition == 1) "cmd" else "powershell"
            startRemoteShell(type)
        }
        stopButton.setOnClickListener { stopRemoteShell() }
        sendButton.setOnClickListener {
            val line = input.text?.toString().orEmpty()
            if (line.isNotBlank()) {
                writeRemoteShell(line + "\n")
                input.text?.clear()
            }
        }
        input.setOnEditorActionListener { _, actionId, event ->
            if (actionId == android.view.inputmethod.EditorInfo.IME_ACTION_DONE || event?.keyCode == android.view.KeyEvent.KEYCODE_ENTER) {
                sendButton.performClick()
                true
            } else false
        }
        dialog.setOnDismissListener {
            nativeShellDialog = null
            nativeShellOutput = null
            nativeShellInput = null
            nativeShellScroll = null
            nativeShellStatus = null
            nativeShellStartButton = null
            nativeShellStopButton = null
            nativeShellSendButton = null
            if (remoteShellSessionId.isNotBlank()) stopRemoteShell()
        }
        dialog.show()
    }

    private fun startRemoteShell(shellType: String) {
        val type = shellType.lowercase(Locale.ROOT)
        if (type !in setOf("cmd", "powershell", "bash", "sh")) return
        if (!laptopOnline || address.isBlank() || key.isBlank()) {
            publishRemoteShellError("Hubungkan dan pasangkan laptop terlebih dahulu.")
            return
        }
        if (!screenPasswordAccessAvailable) {
            publishRemoteShellError(if (connectedHostIsLinux) "Password terminal Linux tidak tersedia." else "Aktifkan Password Kendali Layar di pengaturan Laptop Status Windows terlebih dahulu.")
            return
        }
        main.post { promptRemoteShellPassword(type) }
    }

    private fun promptRemoteShellPassword(type: String) {
        if (isFinishing || isDestroyed || !screenPasswordAccessAvailable) return
        fun shellShape(fill: Int, radius: Int = 18) = GradientDrawable().apply {
            shape = GradientDrawable.RECTANGLE
            setColor(fill)
            cornerRadius = px(radius).toFloat()
            setStroke(px(1), getColor(R.color.line))
        }
        val passwordInput = EditText(this).apply {
            hint = "Password Windows"
            inputType = android.text.InputType.TYPE_CLASS_TEXT or android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD
            setSingleLine(true)
            setTextColor(getColor(R.color.text_main))
            setHintTextColor(getColor(R.color.text_dim))
            setPadding(px(14), px(10), px(14), px(10))
            background = shellShape(getColor(R.color.bg), 14)
        }
        val promptCard = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(px(22), px(22), px(22), px(18))
            background = shellShape(getColor(R.color.surface_raised), 24)
        }
        val lockMark = TextView(this).apply {
            text = "✓"
            textSize = 23f
            gravity = Gravity.CENTER
            setTextColor(getColor(R.color.accent))
            background = shellShape(getColor(R.color.accent_bg), 16)
        }
        promptCard.addView(lockMark, LinearLayout.LayoutParams(px(48), px(48)))
        promptCard.addView(TextView(this).apply {
            text = "Verifikasi akses"
            textSize = 21f
            setTypeface(typeface, android.graphics.Typeface.BOLD)
            setTextColor(getColor(R.color.text_main))
        }, LinearLayout.LayoutParams(-1, -2).apply { topMargin = px(16) })
        promptCard.addView(TextView(this).apply {
            text = if (connectedHostIsLinux) "Masukkan password terminal Linux yang ditampilkan di menu tray. Password tidak disimpan di Android."
                else "Masukkan password kendali layar Windows. Laptop memverifikasinya langsung; password tidak disimpan di Android."
            textSize = 13f
            setTextColor(getColor(R.color.text_dim))
            setLineSpacing(px(2).toFloat(), 1f)
        }, LinearLayout.LayoutParams(-1, -2).apply { topMargin = px(7); bottomMargin = px(16) })
        promptCard.addView(passwordInput, LinearLayout.LayoutParams(-1, px(52)))
        val promptActions = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        fun promptButton(text: String, fill: Int, color: Int) = TextView(this).apply {
            this.text = text
            textSize = 14f
            setTypeface(typeface, android.graphics.Typeface.BOLD)
            setTextColor(color)
            gravity = Gravity.CENTER
            background = shellShape(fill, 15)
            isClickable = true
        }
        val cancelButton = promptButton("Batal", getColor(R.color.surface), getColor(R.color.text_dim))
        val continueButton = promptButton("Lanjutkan", getColor(R.color.accent), getColor(R.color.accent_on))
        promptActions.addView(cancelButton, LinearLayout.LayoutParams(0, px(48), 1f))
        promptActions.addView(continueButton, LinearLayout.LayoutParams(0, px(48), 1f).apply { marginStart = px(9) })
        promptCard.addView(promptActions, LinearLayout.LayoutParams(-1, -2).apply { topMargin = px(17) })

        val prompt = AlertDialog.Builder(this).setView(promptCard).create()
        cancelButton.setOnClickListener {
            prompt.dismiss()
            updateNativeShellStatus("Sesi terminal dibatalkan.")
            callRemoteShellJs("remoteShellEnded('Sesi terminal dibatalkan.')")
            nativeShellStartButton?.isEnabled = true
        }
        continueButton.setOnClickListener {
            val password = passwordInput.text?.toString().orEmpty()
            if (password.isBlank()) {
                passwordInput.error = "Masukkan password kendali layar."
            } else {
                passwordInput.setText("")
                prompt.dismiss()
                beginRemoteShell(type, password)
            }
        }
        prompt.show()
        prompt.window?.apply {
            setBackgroundDrawable(ColorDrawable(Color.TRANSPARENT))
            val maxWidth = (resources.displayMetrics.widthPixels - px(32)).coerceAtMost(px(460))
            setLayout(maxWidth, WindowManager.LayoutParams.WRAP_CONTENT)
        }
    }

    private fun beginRemoteShell(type: String, screenPassword: String) {
        if (remoteShellSessionId.isNotBlank()) stopRemoteShell()
        remoteShellPollHandler.removeCallbacks(remoteShellPollRunnable)
        main.post { nativeShellStartButton?.isEnabled = false }
        publishRemoteShellNotice(if (connectedHostIsLinux) "Memulai shell Linux…" else "Memulai ${if (type == "cmd") "Command Prompt" else "PowerShell"} di akun Windows biasa…")
        callRemoteShellJs("remoteShellStarting()")
        logLiveActivity("SHELL", "terminal_start_requested type=$type")
        remoteShellWorker.execute {
            try {
                val response = remoteShellRequest("/api/shell/start", "POST", JSONObject().put("type", type).put("screenPassword", screenPassword).toString())
                val sessionId = response.optString("sessionId")
                if (sessionId.isBlank()) throw IllegalStateException(if (connectedHostIsLinux) "Linux tidak mengirim ID sesi terminal." else "Windows tidak mengirim ID sesi terminal.")
                val workingDirectory = response.optString("workingDirectory", "")
                main.post {
                    remoteShellSessionId = sessionId
                    remoteShellCursor = 0L
                    remoteShellPollInFlight.set(false)
                    val shellName = if (connectedHostIsLinux) if (type == "cmd") "POSIX Shell" else "Bash" else if (type == "cmd") "Command Prompt" else "PowerShell"
                    val startMessage = "$shellName siap. Direktori: $workingDirectory\n"
                    nativeShellOutput?.text = startMessage
                    nativeShellInput?.isEnabled = true
                    nativeShellStartButton?.isEnabled = false
                    nativeShellStopButton?.isEnabled = true
                    nativeShellSendButton?.isEnabled = true
                    callRemoteShellJs("remoteShellStarted(${JSONObject.quote(sessionId)},${JSONObject.quote(type)},${JSONObject.quote(workingDirectory)})")
                    updateNativeShellStatus(if (connectedHostIsLinux) "Shell berjalan sebagai pengguna Linux saat ini." else "Shell berjalan sebagai pengguna Windows biasa. Perintah admin tetap memerlukan UAC.")
                    addEvent(if (connectedHostIsLinux) "Terminal Linux dimulai" else "Terminal Windows dimulai", if (connectedHostIsLinux) "Shell Linux" else "$shellName • akun Windows biasa", clock.format(Date()), R.drawable.ic_clock)
                    remoteShellPollHandler.post(remoteShellPollRunnable)
                }
            } catch (error: Exception) {
                val message = error.message ?: if (connectedHostIsLinux) "Gagal membuka terminal Linux." else "Gagal membuka terminal Windows."
                main.post { publishRemoteShellError(message) }
                logLiveActivity("SHELL", "terminal_start_failed type=$type error=${error.javaClass.simpleName}", 'E')
            }
        }
    }

    private fun writeRemoteShell(text: String) {
        val sessionId = remoteShellSessionId
        if (sessionId.isBlank() || text.isEmpty()) return
        if (text.length > 4096) {
            publishRemoteShellNotice("Input terlalu panjang; batasnya 4096 karakter.")
            return
        }
        remoteShellWorker.execute {
            try {
                val path = "/api/shell/input?sessionId=${URLEncoder.encode(sessionId, StandardCharsets.UTF_8.name())}"
                remoteShellRequest(path, "POST", JSONObject().put("text", text).toString())
                logLiveActivity("SHELL", "terminal_input_sent chars=${text.length}")
                remoteShellPollHandler.post { pollRemoteShell() }
            } catch (error: Exception) {
                val message = error.message ?: "Gagal mengirim perintah ke terminal."
                main.post { publishRemoteShellNotice(message) }
            }
        }
    }

    private fun pollRemoteShell() {
        val sessionId = remoteShellSessionId
        if (sessionId.isBlank() || !remoteShellPollInFlight.compareAndSet(false, true)) return
        val cursor = remoteShellCursor
        remoteShellWorker.execute {
            var output = ""
            var nextCursor = cursor
            var truncated = false
            var exited = false
            var error: String? = null
            try {
                val encodedId = URLEncoder.encode(sessionId, StandardCharsets.UTF_8.name())
                val response = remoteShellRequest("/api/shell/read?sessionId=$encodedId&after=$cursor", "GET")
                output = response.optString("output", "")
                nextCursor = response.optLong("nextCursor", cursor)
                truncated = response.optBoolean("truncated", false)
                exited = response.optBoolean("exited", false)
            } catch (failure: Exception) {
                error = failure.message ?: "Koneksi terminal terputus sementara."
            }
            main.post {
                remoteShellPollInFlight.set(false)
                if (remoteShellSessionId != sessionId) return@post
                if (error != null) {
                    publishRemoteShellNotice(error!!)
                    remoteShellPollHandler.postDelayed(remoteShellPollRunnable, 1400)
                    return@post
                }
                remoteShellCursor = nextCursor
                if (truncated) appendRemoteShellOutput("[Keluaran lama dipangkas agar memori tetap ringan.]\n")
                if (output.isNotEmpty()) appendRemoteShellOutput(output)
                callRemoteShellJs("remoteShellOutput(${JSONObject.quote(output)},$truncated)")
                if (exited) finishRemoteShell(sessionId)
                else remoteShellPollHandler.postDelayed(remoteShellPollRunnable, 600)
            }
        }
    }

    private fun stopRemoteShell() {
        val sessionId = remoteShellSessionId
        if (sessionId.isBlank()) {
            publishRemoteShellNotice("Belum ada sesi terminal yang berjalan.")
            return
        }
        remoteShellSessionId = ""
        remoteShellPollInFlight.set(false)
        remoteShellPollHandler.removeCallbacks(remoteShellPollRunnable)
        callRemoteShellJs("remoteShellStopped()")
        main.post {
            nativeShellStartButton?.isEnabled = true
            nativeShellStopButton?.isEnabled = false
            nativeShellSendButton?.isEnabled = false
            nativeShellInput?.isEnabled = false
            updateNativeShellStatus("Terminal dihentikan.")
        }
        remoteShellWorker.execute {
            runCatching {
                val encodedId = URLEncoder.encode(sessionId, StandardCharsets.UTF_8.name())
                remoteShellRequest("/api/shell/stop?sessionId=$encodedId", "POST", "{}")
            }
            logLiveActivity("SHELL", "terminal_stopped")
        }
    }

    private fun finishRemoteShell(sessionId: String) {
        if (remoteShellSessionId != sessionId) return
        remoteShellSessionId = ""
        remoteShellPollHandler.removeCallbacks(remoteShellPollRunnable)
        callRemoteShellJs("remoteShellEnded('Proses shell selesai.')")
        nativeShellStartButton?.isEnabled = true
        nativeShellStopButton?.isEnabled = false
        nativeShellSendButton?.isEnabled = false
        nativeShellInput?.isEnabled = false
        updateNativeShellStatus("Proses shell selesai.")
        remoteShellWorker.execute {
            runCatching {
                val encodedId = URLEncoder.encode(sessionId, StandardCharsets.UTF_8.name())
                remoteShellRequest("/api/shell/stop?sessionId=$encodedId", "POST", "{}")
            }
        }
    }

    private fun remoteShellRequest(path: String, method: String, body: String? = null): JSONObject =
        PinnedTls.withConnection(
            address, 8765, path, method, key, prefs.getString("fingerprint", "").orEmpty(),
            connectTimeoutMillis = 4000, readTimeoutMillis = 5000
        ) { connection ->
            if (body != null) {
                connection.doOutput = true
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8")
                connection.outputStream.use { it.write(body.toByteArray(StandardCharsets.UTF_8)) }
            }
            val code = connection.responseCode
            val stream = if (code in 200..299) connection.inputStream else connection.errorStream
            val responseText = stream?.bufferedReader(StandardCharsets.UTF_8)?.use { it.readText() }.orEmpty()
            if (code !in 200..299) {
                val serverMessage = runCatching { JSONObject(responseText).optString("message") }.getOrNull().orEmpty()
                val fallback = if (code == 401) "Kunci pairing ditolak oleh Windows." else "Terminal ditolak oleh Windows (HTTP $code)."
                throw IllegalStateException(serverMessage.ifBlank { fallback })
            }
            if (responseText.isBlank()) JSONObject() else JSONObject(responseText)
        }

    private fun appendRemoteShellOutput(text: String) {
        val view = nativeShellOutput ?: return
        val combined = view.text.toString() + text
        val visibleText = if (combined.length > 96000) combined.takeLast(96000) else combined
        val styled = android.text.SpannableStringBuilder(visibleText)
        Regex("(?im)^.*(?:CategoryInfo|FullyQualifiedErrorId|CommandNotFoundException|ObjectNotFound).*$")
            .findAll(visibleText).forEach { match ->
                styled.setSpan(android.text.style.RelativeSizeSpan(.82f), match.range.first, match.range.last + 1,
                    android.text.Spanned.SPAN_EXCLUSIVE_EXCLUSIVE)
                styled.setSpan(android.text.style.ForegroundColorSpan(getColor(R.color.text_dim)), match.range.first, match.range.last + 1,
                    android.text.Spanned.SPAN_EXCLUSIVE_EXCLUSIVE)
            }
        view.text = styled
        nativeShellScroll?.post { nativeShellScroll?.fullScroll(View.FOCUS_DOWN) }
    }

    private fun updateNativeShellStatus(message: String) {
        nativeShellStatus?.text = message
    }

    private fun publishRemoteShellNotice(message: String) {
        main.post {
            updateNativeShellStatus(message)
            callRemoteShellJs("remoteShellNotice(${JSONObject.quote(message)})")
        }
    }

    private fun publishRemoteShellError(message: String) {
        main.post {
            nativeShellStartButton?.isEnabled = true
            val notification = message.take(140)
            updateNativeShellStatus(notification)
            callRemoteShellJs("remoteShellError(${JSONObject.quote(notification)})")
            android.widget.Toast.makeText(this, notification, android.widget.Toast.LENGTH_LONG).show()
        }
    }

    private fun callRemoteShellJs(script: String) {
        if (::motionDemoWebView.isInitialized && motionDemoPageReady) {
            main.post { motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.$script", null) }
        }
    }

    private fun showWindowsAdminShellPicker() {
        if (!laptopOnline) {
            android.widget.Toast.makeText(this, "Hubungkan laptop terlebih dahulu.", android.widget.Toast.LENGTH_SHORT).show()
            return
        }
        val commands = arrayOf("admin-cmd", "admin-powershell")
        AlertDialog.Builder(this)
            .setTitle("Buka shell Administrator di Windows")
            .setMessage("Jendela shell akan terlihat di laptop. Konfirmasi lokal dan UAC Windows tetap diperlukan. Setelah disetujui, perintah dalam jendela itu berjalan dengan hak Administrator sampai jendela ditutup.")
            .setItems(arrayOf("Command Prompt Administrator", "PowerShell Administrator")) { _, index ->
                logLiveActivity("SHELL", "admin_shell_requested type=${if (index == 0) "cmd" else "powershell"}")
                worker.execute { sendControl(commands[index]) }
            }
            .setNegativeButton("Batal", null)
            .show()
    }

    private fun scrollToView(target: View) {
        val scroll = findViewById<android.widget.ScrollView>(R.id.contentScroll)
        val rect = Rect()
        target.getDrawingRect(rect)
        scroll.offsetDescendantRectToMyCoords(target, rect)
        if (areMotionAnimationsEnabled()) scroll.smoothScrollTo(0, rect.top) else scroll.scrollTo(0, rect.top)
        target.animate().cancel()
        if (areMotionAnimationsEnabled()) {
            target.translationX = 10f
            target.animate().translationX(0f).setDuration(280).setInterpolator(DecelerateInterpolator()).start()
        } else target.translationX = 0f
    }

    private fun setupMetricRings() {
        val row = findViewById<LinearLayout>(R.id.metricRingRow)
        cpuRing = MetricRingView(this, "CPU")
        ramRing = MetricRingView(this, "RAM")
        batteryRing = MetricRingView(this, "Baterai")
        listOf(cpuRing, ramRing, batteryRing).forEach { meter ->
            row.addView(meter, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f))
        }
        cpuRing.setMetric("—", null)
        ramRing.setMetric("—", null)
        batteryRing.setMetric("—", null)
    }

    private fun animateDashboardCards() {
        if (!areMotionAnimationsEnabled()) return
        var visibleIndex = 0
        listOf(R.id.deviceSummaryCard, R.id.screenControlPanel, R.id.memoryPanel, R.id.processPanel, R.id.powerControlPanel)
            .forEach { id ->
                val card = findViewById<View>(id)
                card.animate().cancel()
                if (card.visibility != View.VISIBLE) {
                    card.alpha = 1f
                    card.translationY = 0f
                    card.scaleX = 1f
                    card.scaleY = 1f
                    return@forEach
                }
                card.alpha = 0f
                card.translationY = dp(22).toFloat()
                card.scaleX = 0.98f
                card.scaleY = 0.98f
                card.animate().alpha(1f).translationY(0f).scaleX(1f).scaleY(1f)
                    .setStartDelay(visibleIndex++ * 55L)
                    .setDuration(480L)
                    .setInterpolator(AnimationUtils.loadInterpolator(this, R.interpolator.ios_spring))
                    .start()
            }
    }

    private fun updateMonitorButton() {
        val toggle = findViewById<SwitchCompat>(R.id.backgroundMonitorSwitch)
        val checked = prefs.getBoolean("backgroundMonitor", false)
        if (toggle.isChecked != checked) {
            updatingMonitorSwitch = true
            toggle.isChecked = checked
            updatingMonitorSwitch = false
        }
    }

    private fun setJokeOverlayEnabled(enabled: Boolean) {
        if (!enabled) {
            prefs.edit()
                .putBoolean("androidJokeOverlayEnabled", false)
                .putBoolean("pendingAndroidJokeOverlayPermission", false)
                .putBoolean("androidJokeOverlayHandledForActive", true)
                .apply()
            AndroidJokeOverlay.dismiss()
            dismissAndroidJokeOverlay()
            syncMotionDemoPreferences()
            return
        }
        AlertDialog.Builder(this)
            .setTitle("Mode Joke di aplikasi lain")
            .setMessage("Agar prank bisa tampil saat aplikasi lain dibuka, Android perlu izin Tampil di atas aplikasi lain dan pemantauan latar belakang. Lanjutkan untuk mengaktifkan pemantauan dan membuka pengaturan izin Android.")
            .setNegativeButton("Batal") { _, _ ->
                syncMotionDemoPreferences()
                syncJokeOverlaySwitch()
            }
            .setPositiveButton("Lanjutkan") { _, _ ->
                if (!prefs.getBoolean("backgroundMonitor", false)) setBackgroundMonitoring(true)
                if (!prefs.getBoolean("backgroundMonitor", false)) {
                    syncMotionDemoPreferences()
                    syncJokeOverlaySwitch()
                    return@setPositiveButton
                }
                if (Settings.canDrawOverlays(this)) {
                    finishJokeOverlayPermission(true)
                } else {
                    prefs.edit().putBoolean("pendingAndroidJokeOverlayPermission", true).apply()
                    runCatching {
                        startActivity(Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:$packageName")))
                    }.onFailure {
                        prefs.edit().putBoolean("pendingAndroidJokeOverlayPermission", false).apply()
                        syncMotionDemoPreferences()
                        syncJokeOverlaySwitch()
                    }
                }
            }
            .show()
    }

    private fun finishJokeOverlayPermission(granted: Boolean) {
        prefs.edit()
            .putBoolean("pendingAndroidJokeOverlayPermission", false)
            .putBoolean("androidJokeOverlayEnabled", granted)
            .putBoolean("androidJokeOverlayHandledForActive", false)
            .putBoolean("androidJokeOverlayPermissionNotified", false)
            .apply()
        syncJokeOverlaySwitch()
        if (granted) {
            syncMonitorService()
            Toast.makeText(this, "Mode Joke dapat tampil di atas aplikasi lain.", Toast.LENGTH_LONG).show()
        } else {
            Toast.makeText(this, "Izin overlay belum diberikan; Mode Joke hanya tampil saat aplikasi terbuka.", Toast.LENGTH_LONG).show()
        }
        syncMotionDemoPreferences()
    }

    private fun setBackgroundMonitoring(enabled: Boolean) {
        if (!enabled) {
            prefs.edit()
                .putBoolean("backgroundMonitor", false)
                .putBoolean("androidJokeOverlayEnabled", false)
                .putBoolean("androidJokeOverlayHandledForActive", true)
                .apply()
            AndroidJokeOverlay.dismiss()
            syncMotionDemoPreferences()
            syncJokeOverlaySwitch()
            syncMonitorService()
            findViewById<TextView>(R.id.connectionInfo).text = "Pemantauan latar belakang dimatikan."
            return
        }
        if (address.isBlank() || key.isBlank()) {
            setMonitorSwitch(false)
            findViewById<TextView>(R.id.connectionInfo).text = "Hubungkan ke laptop dan simpan kunci pairing terlebih dahulu."
            return
        }
        if (normalizeFingerprint(prefs.getString("fingerprint", "").orEmpty()).length != 64) {
            setMonitorSwitch(false)
            findViewById<TextView>(R.id.connectionInfo).text = "Simpan alamat, kunci, dan fingerprint TLS terlebih dahulu."
            return
        }
        prefs.edit().putBoolean("backgroundMonitor", true).putBoolean("monitorWasOnline", false).putBoolean("monitorHasSample", false).apply()
        try {
            syncMonitorService()
            findViewById<TextView>(R.id.connectionInfo).text = "Pemantauan aktif. Android akan memberi notifikasi saat laptop kembali online."
        } catch (error: Exception) {
            prefs.edit().putBoolean("backgroundMonitor", false).apply()
            setMonitorSwitch(false)
            findViewById<TextView>(R.id.connectionInfo).text = error.message ?: "Pemantauan tidak dapat dimulai."
        }
    }

    private fun setMonitorSwitch(checked: Boolean) {
        updatingMonitorSwitch = true
        findViewById<SwitchCompat>(R.id.backgroundMonitorSwitch).isChecked = checked
        updatingMonitorSwitch = false
    }

    private fun syncJokeOverlaySwitch() {
        val toggle = findViewById<SwitchCompat>(R.id.androidJokeOverlaySwitch)
        val checked = prefs.getBoolean("androidJokeOverlayEnabled", false)
        if (toggle.isChecked != checked) {
            updatingJokeOverlaySwitch = true
            toggle.isChecked = checked
            updatingJokeOverlaySwitch = false
        }
    }

    private fun confirmDisconnectLaptop() {
        AlertDialog.Builder(this)
            .setTitle("Putuskan dari laptop?")
            .setMessage("Koneksi aktif dan pemantauan latar belakang akan dihentikan. Pemasangan tersimpan, jadi Anda dapat menghubungkan lagi nanti.")
            .setNegativeButton("Batal", null)
            .setPositiveButton("Putuskan") { _, _ -> disconnectLaptop() }
            .show()
    }

    private fun disconnectLaptop() {
        prefs.edit()
            .remove("address")
            .remove("key")
            .remove("fingerprint")
            .remove("activeDeviceId")
            .putBoolean("backgroundMonitor", false)
            .putBoolean("monitorWasOnline", false)
            .putBoolean("monitorHasSample", false)
            .apply()
        address = ""
        key = ""
        laptopOnline = false
        connectedHostIsLinux = false
        wasOnline = false
        syncMonitorService()
        addressInput.setText("")
        keyInput.setText("")
        fingerprintInput.setText("")
        setMonitorSwitch(false)
        setRemoteControlsEnabled(false)
        findViewById<Button>(R.id.jokeButton).visibility = View.VISIBLE
        findViewById<Button>(R.id.screenRequestButton).visibility = View.VISIBLE
        findViewById<View>(R.id.manualConnectionProgressRow).visibility = View.GONE
        transitionStatusPill("Menunggu", "waiting")
        findViewById<TextView>(R.id.deviceSub).text = "Belum terhubung"
        findViewById<TextView>(R.id.connectionInfo).text = "Koneksi laptop diputus. Pemasangan tetap tersimpan di perangkat ini."
        latestStatusPayload = JSONObject().put("online", false).put("message", "Koneksi laptop diputus dari Android.").toString()
        publishMotionDemoStatus()
        publishMotionDemoScreenState()
        if (prefs.getBoolean("motionDemoUi", true)) {
            temporaryNativeUiForWebAction = false
            applyMotionDemoMode()
        } else {
            selectNavigation(R.id.navDevice)
        }
        refreshDiscoveryConnectionState()
    }

    private fun clearRetiredAppJokeData() {
        prefs.edit()
            .remove("appJokeMonitorAllowed")
            .remove("appJokeEnabled")
            .remove("pendingJokeEnable")
            .remove("jokeNotificationRequested")
            .remove("jokeApps")
            .remove("remoteJokeEnabled")
            .apply()
        NotificationManagerCompat.from(this).cancel(4201)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O)
            getSystemService(NotificationManager::class.java).deleteNotificationChannel("laptop_app_jokes")
        if (!prefs.getBoolean("backgroundMonitor", false))
            stopService(Intent(this, MonitorService::class.java))
    }

    private fun syncMonitorService() {
        val shouldRun = prefs.getBoolean("backgroundMonitor", false)
        val serviceIntent = Intent(this, MonitorService::class.java)
        if (shouldRun) ContextCompat.startForegroundService(this, serviceIntent)
        else stopService(serviceIntent)
    }

    override fun onResume() {
        super.onResume()
        if (!::prefs.isInitialized) return
        prefs.edit().putBoolean("mainActivityVisible", true).apply()
        if (prefs.getBoolean("pendingAndroidJokeOverlayPermission", false)) {
            finishJokeOverlayPermission(Settings.canDrawOverlays(this))
        }
        logLiveActivity("APP", "resume")
        updateMonitorButton()
        syncJokeOverlaySwitch()
    }

    override fun onStop() {
        if (::prefs.isInitialized) prefs.edit().putBoolean("mainActivityVisible", false).apply()
        super.onStop()
    }

    override fun onPause() {
        if (::prefs.isInitialized) logLiveActivity("APP", "pause")
        super.onPause()
    }

    private fun confirmControl(action: String, title: String, message: String) {
        if (action in setOf("shutdown", "restart", "sleep")) {
            val warningText = if (!laptopOnAcPower) null else when (action) {
                "shutdown" -> "Anda sedang dalam keadaan mengisi daya. Apakah Anda yakin ingin mematikan perangkat?"
                "restart" -> "Anda sedang dalam keadaan mengisi daya. Apakah Anda yakin ingin memulai ulang perangkat?"
                else -> null
            }
            showPowerConfirmation(action, title, message, warningText)
            return
        }
        AlertDialog.Builder(this)
            .setTitle(title)
            .setMessage(message)
            .setNegativeButton("Batal", null)
            .setPositiveButton("Lanjutkan") { _, _ ->
                worker.execute { sendControl(action) }
            }
            .show()
    }

    private fun showPowerConfirmation(action: String, title: String, message: String, warningText: String?) {
        val useLatestPopupStyle = !prefs.getBoolean("motionDemoUi", true) &&
            prefs.getBoolean("latestPowerPopupOnOldUi", false)
        val dialog = Dialog(this)
        val sheet = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER_HORIZONTAL
            setPadding(dp(22), dp(10), dp(22), dp(20))
            background = GradientDrawable().apply {
                cornerRadius = dp(28).toFloat()
                setColor(Color.rgb(24, 34, 48))
                setStroke(dp(1), Color.rgb(48, 62, 78))
            }
        }
        val handle = View(this).apply {
            background = GradientDrawable().apply {
                cornerRadius = dp(5).toFloat()
                setColor(Color.rgb(83, 96, 110))
            }
            layoutParams = LinearLayout.LayoutParams(dp(38), dp(5)).apply { bottomMargin = dp(18) }
        }
        sheet.addView(handle)
        if (useLatestPopupStyle) {
            val powerBadge = android.widget.FrameLayout(this).apply {
                background = GradientDrawable().apply {
                    shape = GradientDrawable.OVAL
                    setColor(Color.rgb(30, 61, 61))
                }
                layoutParams = LinearLayout.LayoutParams(dp(48), dp(48)).apply { bottomMargin = dp(10) }
            }
            val powerGlyph = TextView(this).apply {
                text = "⏻"
                textSize = 29f
                gravity = Gravity.CENTER
                setTextColor(Color.rgb(111, 225, 190))
                includeFontPadding = false
            }
            powerBadge.addView(powerGlyph, android.widget.FrameLayout.LayoutParams(dp(48), dp(48), Gravity.CENTER))
            sheet.addView(powerBadge)
        }
        sheet.addView(TextView(this).apply {
            text = title
            textSize = 19f
            gravity = Gravity.CENTER
            setTypeface(typeface, android.graphics.Typeface.BOLD)
            setTextColor(Color.rgb(243, 247, 252))
        })
        val timeoutText = TextView(this).apply {
            text = "Konfirmasi ditutup dalam 10 detik. $message"
            textSize = 12f
            gravity = Gravity.CENTER
            setTextColor(Color.rgb(174, 189, 205))
            setLineSpacing(dp(2).toFloat(), 1f)
            setPadding(0, dp(5), 0, dp(4))
        }
        sheet.addView(timeoutText)

        warningText?.let { warningMessage ->
            sheet.addView(TextView(this).apply {
                text = warningMessage
                textSize = 14f
                setLineSpacing(dp(3).toFloat(), 1f)
                setTextColor(Color.rgb(74, 54, 14))
                setPadding(dp(15), dp(13), dp(15), dp(13))
                background = GradientDrawable().apply {
                    cornerRadius = dp(15).toFloat()
                    setColor(Color.rgb(255, 243, 199))
                    setStroke(dp(1), Color.rgb(231, 184, 66))
                }
                layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                    topMargin = dp(12)
                }
            })
        }

        val customMessageInput = if (action == "shutdown" || action == "restart") EditText(this).apply {
            hint = "Pesan khusus yang tampil di laptop (opsional)"
            setSingleLine(true)
            maxLines = 1
            imeOptions = android.view.inputmethod.EditorInfo.IME_ACTION_DONE
            setPadding(dp(14), dp(10), dp(14), dp(10))
            setTextColor(Color.rgb(243, 247, 252))
            setHintTextColor(Color.rgb(150, 164, 180))
            background = GradientDrawable().apply {
                cornerRadius = dp(14).toFloat()
                setColor(Color.rgb(18, 27, 39))
                setStroke(dp(1), Color.rgb(60, 76, 94))
            }
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(12) }
        } else null
        customMessageInput?.let(sheet::addView)

        val countdown = PowerCountdownRingView(this).apply {
            layoutParams = LinearLayout.LayoutParams(dp(104), dp(104)).apply {
                topMargin = dp(12)
                bottomMargin = dp(16)
            }
        }
        sheet.addView(countdown)
        fun sheetButton(label: String, primary: Boolean): Button = Button(this).apply {
            text = label
            isAllCaps = false
            textSize = 14f
            minHeight = dp(50)
            setTextColor(if (primary) Color.WHITE else Color.rgb(237, 242, 248))
            background = GradientDrawable().apply {
                cornerRadius = dp(16).toFloat()
                setColor(if (primary) Color.rgb(226, 91, 106) else Color.rgb(43, 55, 69))
            }
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(50)).apply {
                bottomMargin = dp(8)
            }
        }
        val runNow = sheetButton("Jalankan sekarang", primary = true)
        val cancel = sheetButton("Batal", primary = false)

        var sending = false
        var remainingMillis = 10_000L
        var deadlineElapsed = 0L
        var pausedForTyping = false
        var expired = false
        var timer: android.os.CountDownTimer? = null
        var resumeTypingRunnable: Runnable? = null

        fun renderCountdown(millis: Long, paused: Boolean = false) {
            val bounded = millis.coerceIn(0L, 10_000L)
            val preciseSeconds = bounded / 1000f
            val left = kotlin.math.ceil(preciseSeconds).toInt()
            countdown.remainingSeconds = left
            countdown.remainingFraction = if (areMotionAnimationsEnabled()) preciseSeconds / 10f else left / 10f
            timeoutText.text = if (paused) {
                "Hitung mundur dijeda saat Anda menulis pesan."
            } else {
                "Konfirmasi ditutup dalam $left detik. $message"
            }
        }

        fun startCountdown() {
            if (sending || pausedForTyping || expired || !dialog.isShowing) return
            val duration = remainingMillis.coerceAtLeast(1L)
            timer?.cancel()
            deadlineElapsed = android.os.SystemClock.elapsedRealtime() + duration
            val activeTimer = object : android.os.CountDownTimer(duration, 100L) {
                override fun onTick(millisUntilFinished: Long) {
                    remainingMillis = millisUntilFinished.coerceAtLeast(0L)
                    renderCountdown(remainingMillis)
                }

                override fun onFinish() {
                    remainingMillis = 0L
                    expired = true
                    renderCountdown(0L)
                    timeoutText.text = "Konfirmasi kedaluwarsa. Tidak ada perintah yang dikirim."
                    if (!sending && dialog.isShowing) dialog.dismiss()
                }
            }
            timer = activeTimer
            activeTimer.start()
        }

        fun pauseCountdownForTyping() {
            if (sending || expired || pausedForTyping) return
            if (deadlineElapsed > 0L) {
                remainingMillis = (deadlineElapsed - android.os.SystemClock.elapsedRealtime()).coerceAtLeast(0L)
            }
            timer?.cancel()
            timer = null
            pausedForTyping = true
            renderCountdown(remainingMillis, paused = true)
            if (areMotionAnimationsEnabled()) {
                countdown.animate().scaleX(1.05f).scaleY(1.05f).setDuration(180L)
                    .setInterpolator(DecelerateInterpolator()).start()
            }
        }

        fun resumeCountdownAfterTyping() {
            if (sending || expired || !pausedForTyping || !dialog.isShowing) return
            pausedForTyping = false
            resumeTypingRunnable?.let(main::removeCallbacks)
            resumeTypingRunnable = null
            if (areMotionAnimationsEnabled()) {
                countdown.animate().scaleX(1f).scaleY(1f).setDuration(220L)
                    .setInterpolator(DecelerateInterpolator()).start()
            }
            renderCountdown(remainingMillis)
            startCountdown()
        }

        customMessageInput?.setOnFocusChangeListener { _, hasFocus ->
            if (hasFocus) {
                pauseCountdownForTyping()
            } else {
                resumeTypingRunnable?.let(main::removeCallbacks)
                resumeTypingRunnable = null
                resumeCountdownAfterTyping()
            }
        }
        customMessageInput?.addTextChangedListener(object : android.text.TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) = Unit
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {
                if (sending || expired || !customMessageInput.hasFocus()) return
                pauseCountdownForTyping()
                resumeTypingRunnable?.let(main::removeCallbacks)
                val resume = Runnable {
                    if (customMessageInput.hasFocus()) resumeCountdownAfterTyping()
                }
                resumeTypingRunnable = resume
                main.postDelayed(resume, 1_200L)
            }
            override fun afterTextChanged(s: android.text.Editable?) = Unit
        })
        customMessageInput?.setOnEditorActionListener { view, actionId, _ ->
            if (actionId == android.view.inputmethod.EditorInfo.IME_ACTION_DONE) {
                val inputManager = getSystemService(INPUT_METHOD_SERVICE) as android.view.inputmethod.InputMethodManager
                inputManager.hideSoftInputFromWindow(view.windowToken, 0)
                view.clearFocus()
                true
            } else false
        }
        sheet.isClickable = true
        sheet.setOnClickListener {
            customMessageInput?.let { input ->
                if (input.hasFocus()) {
                    val inputManager = getSystemService(INPUT_METHOD_SERVICE) as android.view.inputmethod.InputMethodManager
                    inputManager.hideSoftInputFromWindow(input.windowToken, 0)
                    input.clearFocus()
                }
            }
        }

        val customRun = if (customMessageInput != null && !useLatestPopupStyle) sheetButton("Pakai pesan khusus", primary = false) else null
        customRun?.setOnClickListener {
            if (sending) return@setOnClickListener
            sending = true
            timer?.cancel()
            resumeTypingRunnable?.let(main::removeCallbacks)
            dialog.dismiss()
            worker.execute { sendControl(action, customMessageInput?.text?.toString().orEmpty()) }
        }
        customRun?.let(sheet::addView)
        sheet.addView(runNow)
        sheet.addView(cancel)
        runNow.setOnClickListener {
            if (sending) return@setOnClickListener
            sending = true
            timer?.cancel()
            resumeTypingRunnable?.let(main::removeCallbacks)
            dialog.dismiss()
            worker.execute { sendControl(action, customMessageInput?.text?.toString().orEmpty()) }
        }
        cancel.setOnClickListener {
            timer?.cancel()
            resumeTypingRunnable?.let(main::removeCallbacks)
            dialog.dismiss()
        }
        val scrollContainer = android.widget.ScrollView(this).apply {
            isFillViewport = true
            isVerticalScrollBarEnabled = false
            clipToPadding = false
            addView(sheet, android.widget.FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        }
        dialog.setContentView(scrollContainer)
        dialog.setOnShowListener {
            dialog.window?.apply {
                setBackgroundDrawable(ColorDrawable(Color.TRANSPARENT))
                addFlags(android.view.WindowManager.LayoutParams.FLAG_DIM_BEHIND)
                setDimAmount(0.58f)
                val params = attributes
                params.gravity = Gravity.BOTTOM
                params.width = (resources.displayMetrics.widthPixels - dp(20)).coerceAtLeast(dp(280))
                params.height = ViewGroup.LayoutParams.WRAP_CONTENT
                attributes = params
            }
            if (areMotionAnimationsEnabled()) {
                sheet.alpha = 0f
                sheet.translationY = dp(50).toFloat()
                sheet.animate().alpha(1f).translationY(0f).setDuration(320)
                    .setInterpolator(DecelerateInterpolator()).start()
            } else {
                sheet.alpha = 1f
                sheet.translationY = 0f
            }
            startCountdown()
        }
        dialog.setOnDismissListener {
            timer?.cancel()
            resumeTypingRunnable?.let(main::removeCallbacks)
            countdown.animate().cancel()
        }
        dialog.setCancelable(true)
        dialog.setCanceledOnTouchOutside(false)
        dialog.show()
        dialog.window?.setBackgroundDrawable(ColorDrawable(Color.TRANSPARENT))
        dialog.window?.setLayout((resources.displayMetrics.widthPixels - dp(20)).coerceAtLeast(dp(280)), ViewGroup.LayoutParams.WRAP_CONTENT)
        @Suppress("DEPRECATION")
        val adjustForKeyboard = android.view.WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE
        dialog.window?.setSoftInputMode(adjustForKeyboard)
    }

    private fun showScreenPasswordDialog() {
        val passwordInput = EditText(this).apply {
            hint = "Password khusus LaptopStatus"
            inputType = android.text.InputType.TYPE_CLASS_TEXT or android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD
            setSingleLine(true)
            setPadding(dp(14), dp(12), dp(14), dp(12))
        }
        val dialog = AlertDialog.Builder(this)
            .setTitle("Akses LaptopStatus")
            .setMessage("Password ini dibuat dan disimpan hanya sebagai hash di Windows. Tidak memakai password akun Windows.")
            .setView(passwordInput)
            .setNegativeButton("Batal", null)
            .setPositiveButton("Mulai sesi", null)
            .create()
        dialog.setOnShowListener {
            dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener {
                val secret = passwordInput.text.toString()
                if (secret.length < 3 || secret.any { it.isISOControl() } || secret != secret.trim()) {
                    passwordInput.error = "Masukkan password khusus minimal 3 karakter."
                    return@setOnClickListener
                }
                screenWorker.execute { sendScreenPassword(secret) }
                passwordInput.text.clear()
                dialog.dismiss()
            }
        }
        dialog.show()
    }

    private fun sendScreenPassword(password: String) {
        try {
            val code = PinnedTls.withConnection(address, 8767, "/api/control?command=screen-auth", "POST", key,
                prefs.getString("fingerprint", "").orEmpty()) { connection ->
                connection.doOutput = true
                connection.setRequestProperty("X-LaptopStatus-Screen-Password", android.util.Base64.encodeToString(password.toByteArray(StandardCharsets.UTF_8), android.util.Base64.NO_WRAP))
                connection.outputStream.use { }
                connection.responseCode
            }
            if (code != 200) throw IllegalStateException(when (code) {
                401 -> "Kunci pairing salah."
                403 -> "Password salah, atau akses password belum aktif di Windows."
                else -> "Akses layar ditolak (HTTP $code)."
            })
            main.post {
                screenActive = true
                screenPending = false
                updateScreenControlUi()
                publishMotionDemoScreenState()
                findViewById<TextView>(R.id.connectionInfo).text = "Kendali layar terhubung dengan password LaptopStatus. Sesi otomatis berhenti dalam 10 menit."
                addEvent("Kendali layar dimulai", "Sesi terautentikasi dengan password LaptopStatus", clock.format(Date()), R.drawable.ic_clock)
            }
        } catch (e: Exception) {
            main.post { findViewById<TextView>(R.id.connectionInfo).text = e.message ?: "Gagal membuka sesi kendali layar." }
        }
    }

    private fun sendControl(action: String, message: String = "") {
        if (address.isBlank() || key.isBlank()) {
            logLiveActivity("POWER", "command_rejected action=$action reason=missing_pairing", 'W')
            main.post { findViewById<TextView>(R.id.connectionInfo).text = "Masukkan IP dan kunci pairing sebelum mengirim perintah." }
            return
        }
        try {
            logLiveActivity("POWER", "POST /api/control action=$action")
            val encodedMessage = URLEncoder.encode(message.take(240), StandardCharsets.UTF_8.name())
            val code = PinnedTls.withConnection(address, 8765, "/api/control?command=$action&message=$encodedMessage", "POST", key,
                prefs.getString("fingerprint", "").orEmpty()) { connection ->
                connection.doOutput = true
                connection.outputStream.use { }
                connection.responseCode
            }
            if (code != 200) throw IllegalStateException(if (code == 401) "Kunci pairing salah." else "Perintah ditolak (HTTP $code).")
            logLiveActivity("POWER", "POST /api/control HTTP $code action=$action")
            main.post {
                jokeEnabled = when (action) { "joke-on" -> true; "joke-off" -> false; else -> jokeEnabled }
                updateJokeButton()
                val message = when (action) {
                    "shutdown" -> "Perintah shutdown terkirim. Windows akan mati dalam 10 detik."
                    "restart" -> "Perintah restart terkirim. Windows akan restart dalam 10 detik."
                    "sleep" -> "Perintah sleep terkirim ke Windows."
                    "joke-on" -> "Mode joke aktif di Windows selama maksimal 30 detik."
                    "admin-cmd" -> "Permintaan CMD Administrator dikirim. Setujui dialog dan UAC di Windows."
                    "admin-powershell" -> "Permintaan PowerShell Administrator dikirim. Setujui dialog dan UAC di Windows."
                    "open-app-notepad" -> "Notepad dibuka di Windows."
                    "open-app-calculator" -> "Kalkulator dibuka di Windows."
                    "open-app-explorer" -> "File Explorer dibuka di Windows."
                    "open-app-task-manager" -> "Task Manager dibuka di Windows."
                    "open-app-settings" -> "Pengaturan Windows dibuka."
                    else -> "Mode joke dimatikan."
                }
                findViewById<TextView>(R.id.connectionInfo).text = message
                android.widget.Toast.makeText(this@MainActivity, message, android.widget.Toast.LENGTH_LONG).show()
                addEvent("Perintah dikirim", message, clock.format(Date()), R.drawable.ic_clock)
            }
        } catch (e: Exception) {
            logLiveActivity("POWER", "POST /api/control failed action=$action error=${e.javaClass.simpleName} detail=${e.message ?: "no detail"}", 'E')
            main.post {
                val errorMessage = e.message ?: "Gagal mengirim perintah."
                findViewById<TextView>(R.id.connectionInfo).text = errorMessage
                android.widget.Toast.makeText(this@MainActivity, errorMessage, android.widget.Toast.LENGTH_LONG).show()
            }
        }
    }

    private fun normalizeFingerprint(value: String): String = value.filter { it.isLetterOrDigit() }.uppercase(Locale.ROOT)

    private fun sendScreenControl(command: String) {
        try {
            val code = PinnedTls.withConnection(address, 8767, "/api/control?command=$command", "POST", key,
                prefs.getString("fingerprint", "").orEmpty()) { connection ->
                connection.doOutput = true
                connection.outputStream.use { }
                connection.responseCode
            }
            if (code != 200) throw IllegalStateException(if (code == 401) "Kunci pairing salah." else "Permintaan layar gagal (HTTP $code).")
            main.post {
                if (command == "screen-request") {
                    screenPending = true
                    findViewById<TextView>(R.id.connectionInfo).text = "Permintaan dikirim. Setujui dialog yang tampil di Windows."
                } else {
                    screenActive = false
                    screenPending = false
                    updateScreenControlUi()
                    publishMotionDemoScreenState("Sesi kendali layar dihentikan.")
                    findViewById<TextView>(R.id.connectionInfo).text = "Sesi kendali layar dihentikan."
                }
                publishMotionDemoScreenState()
            }
        } catch (e: Exception) {
            main.post { findViewById<TextView>(R.id.connectionInfo).text = e.message ?: "Gagal menghubungi layanan kendali layar." }
        }
    }

    private fun sendScreenInput(action: String, x: Int, y: Int) {
        if (!screenActive) return
        try {
            val code = PinnedTls.withConnection(address, 8767, "/api/screen/input?action=$action&x=$x&y=$y", "POST", key,
                prefs.getString("fingerprint", "").orEmpty()) { connection ->
                connection.doOutput = true
                connection.outputStream.use { }
                connection.responseCode
            }
            if (code == 403) main.post { screenActive = false; screenPending = false; updateScreenControlUi(); publishMotionDemoScreenState("Sesi kendali layar sudah berakhir.") }
            else if (code != 200) main.post { findViewById<TextView>(R.id.screenControlStatus).text = "Perintah layar gagal (HTTP $code)." }
        } catch (_: Exception) { }
    }

    private fun sendMotionDemoScreenPointer(action: String, xRatio: Float, yRatio: Float) {
        if (!screenActive) return
        val bitmap = (screenPreview.drawable as? android.graphics.drawable.BitmapDrawable)?.bitmap ?: return
        val x = (xRatio.coerceIn(0f, 1f) * (bitmap.width - 1)).toInt()
        val y = (yRatio.coerceIn(0f, 1f) * (bitmap.height - 1)).toInt()
        screenWorker.execute { sendScreenInput(action, x, y) }
    }

    private fun publishMotionDemoScreenState(message: String = "") {
        if (!::motionDemoWebView.isInitialized || !motionDemoPageReady) return
        val status = message.ifBlank {
            when {
                screenActive -> "Kendali layar aktif · Ketuk atau geser pratinjau untuk mengontrol laptop."
                screenPending -> "Menunggu persetujuan di laptop Windows."
                else -> "Layar belum aktif."
            }
        }
        motionDemoWebView.evaluateJavascript(
            "window.LaptopStatus&&window.LaptopStatus.setScreenState($screenActive,$screenPending,${JSONObject.quote(status)})", null
        )
    }

    private fun publishMotionDemoScreenFrame(bitmap: Bitmap) {
        if (!::motionDemoWebView.isInitialized || !motionDemoPageReady || !screenActive) return
        runCatching {
            val width = bitmap.width.coerceAtMost(1280)
            val height = (bitmap.height * (width.toFloat() / bitmap.width)).toInt().coerceAtLeast(1)
            val scaled = if (width != bitmap.width) Bitmap.createScaledBitmap(bitmap, width, height, true) else bitmap
            val bytes = ByteArrayOutputStream().use { output ->
                scaled.compress(Bitmap.CompressFormat.JPEG, 55, output)
                output.toByteArray()
            }
            if (scaled !== bitmap) scaled.recycle()
            val data = "data:image/jpeg;base64," + android.util.Base64.encodeToString(bytes, android.util.Base64.NO_WRAP)
            motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.setScreenFrame(${JSONObject.quote(data)})", null)
        }
    }

    private fun handleScreenTouch(event: MotionEvent): Boolean {
        if (!screenActive) return false
        val bitmap = (screenPreview.drawable as? android.graphics.drawable.BitmapDrawable)?.bitmap ?: return false
        val scale = minOf(screenPreview.width.toFloat() / bitmap.width, screenPreview.height.toFloat() / bitmap.height)
        val shownWidth = bitmap.width * scale
        val shownHeight = bitmap.height * scale
        val offsetX = (screenPreview.width - shownWidth) / 2f
        val offsetY = (screenPreview.height - shownHeight) / 2f
        val x = (((event.x - offsetX) / scale).toInt()).coerceIn(0, bitmap.width - 1)
        val y = (((event.y - offsetY) / scale).toInt()).coerceIn(0, bitmap.height - 1)
        when (event.action) {
            MotionEvent.ACTION_DOWN -> { screenWorker.execute { sendScreenInput("down", x, y) }; return true }
            MotionEvent.ACTION_MOVE -> {
                val now = System.currentTimeMillis()
                if (now - lastPointerSentAt >= 90) {
                    lastPointerSentAt = now
                    screenWorker.execute { sendScreenInput("move", x, y) }
                }
                return true
            }
            MotionEvent.ACTION_UP -> { screenWorker.execute { sendScreenInput("up", x, y) }; return true }
            MotionEvent.ACTION_CANCEL -> { screenWorker.execute { sendScreenInput("up", x, y) }; return true }
        }
        return true
    }

    private fun sendScreenText(text: String) {
        try {
            val encoded = URLEncoder.encode(text.take(500), "UTF-8")
            val code = PinnedTls.withConnection(address, 8767, "/api/screen/type?text=$encoded", "POST", key,
                prefs.getString("fingerprint", "").orEmpty()) { connection ->
                connection.doOutput = true
                connection.outputStream.use { }
                connection.responseCode
            }
            if (code != 200) throw IllegalStateException(if (code == 403) "Sesi layar sudah berakhir." else "Pengiriman teks gagal (HTTP $code).")
            main.post { screenTextInput.text.clear(); findViewById<TextView>(R.id.connectionInfo).text = "Teks dikirim ke Windows." }
        } catch (e: Exception) { main.post { findViewById<TextView>(R.id.connectionInfo).text = e.message ?: "Gagal mengirim teks." } }
    }

    private val screenRefreshRunnable = object : Runnable {
        override fun run() {
            if (screenActive && screenFrameBusy.compareAndSet(false, true)) screenWorker.execute {
                try {
                    val bitmap = PinnedTls.withConnection(address, 8767, "/api/screen", "GET", key,
                        prefs.getString("fingerprint", "").orEmpty()) { connection ->
                        if (connection.responseCode != 200) throw IllegalStateException("Sesi layar sudah berhenti (HTTP ${connection.responseCode}).")
                        screenSourceLabel = connection.getHeaderField("X-LaptopStatus-Screen-Source")
                        connection.inputStream.use { BitmapFactory.decodeStream(it) }
                    }
            if (bitmap != null) main.post {
                screenPreview.setImageBitmap(bitmap)
                publishMotionDemoScreenFrame(bitmap)
                val source = screenSourceLabel
                if (!source.isNullOrBlank()) {
                    findViewById<TextView>(R.id.screenControlStatus).text = when {
                        source.contains("spacedesk", ignoreCase = true) -> "Layar kendali memakai monitor virtual spacedesk."
                        else -> "Layar kendali memakai desktop virtual Windows."
                    }
                }
            }
                } catch (e: Exception) {
                    main.post {
                        findViewById<TextView>(R.id.screenControlStatus).text = e.message ?: "Sambungan layar terputus."
                        if (e.message?.contains("HTTP 403") == true) {
                            screenActive = false
                            screenPending = false
                            updateScreenControlUi()
                            publishMotionDemoScreenState("Sesi kendali layar sudah berakhir.")
                        }
                    }
                } finally { screenFrameBusy.set(false) }
            }
            screenRefresh.postDelayed(this, 700)
        }
    }

    private fun updateScreenControlUi() {
        val status = findViewById<TextView>(R.id.screenControlStatus)
        val request = findViewById<Button>(R.id.screenRequestButton)
        val sendText = findViewById<Button>(R.id.screenSendTextButton)
        val stop = findViewById<Button>(R.id.screenStopButton)
        val fullscreen = findViewById<Button>(R.id.screenFullscreenButton)
        setGlassVisibility(screenPreview, screenActive)
        setGlassVisibility(screenTextInput, screenActive)
        setGlassVisibility(sendText, screenActive)
        setGlassVisibility(stop, screenActive || screenPending)
        setGlassVisibility(fullscreen, screenActive)
        if (screenFullscreen && !screenActive) setScreenFullscreen(false)
        request.isEnabled = laptopOnline && !screenActive && !screenPending
        sendText.isEnabled = laptopOnline && screenActive
        stop.isEnabled = laptopOnline && (screenActive || screenPending)
        fullscreen.isEnabled = laptopOnline && screenActive
        request.text = if (screenPending) "Menunggu persetujuan Windows..." else "Minta kendali layar"
        status.text = when {
            screenActive -> "Kendali aktif. Ketuk atau seret pratinjau untuk mouse; sesi berhenti otomatis setelah 10 menit."
            screenPending -> "Menunggu persetujuan yang harus diberikan di laptop Windows."
            screenPasswordAccessAvailable -> "Minta persetujuan di laptop atau gunakan password LaptopStatus yang diaktifkan di Windows."
            else -> "Belum ada sesi. Minta persetujuan di Windows; akses password dapat diaktifkan dari Pengaturan Windows."
        }
    }

    private fun setGlassVisibility(view: View, visible: Boolean) {
        val generation = (viewAnimationGenerations[view] ?: 0) + 1
        viewAnimationGenerations[view] = generation
        view.animate().cancel()
        if (!areMotionAnimationsEnabled()) {
            view.visibility = if (visible) View.VISIBLE else View.GONE
            view.alpha = 1f
            view.translationY = 0f
            return
        }
        if (visible) {
            if (view.visibility != View.VISIBLE) {
                view.alpha = 0f
                view.translationY = 12f
                view.visibility = View.VISIBLE
            }
            view.animate().alpha(1f).translationY(0f).setDuration(280)
                .setInterpolator(DecelerateInterpolator()).start()
        } else if (view.visibility == View.VISIBLE) {
            view.animate().alpha(0f).translationY(-6f).setDuration(150)
                .withEndAction {
                    if (viewAnimationGenerations[view] != generation) return@withEndAction
                    view.visibility = View.GONE
                    view.alpha = 1f
                    view.translationY = 0f
                }
                .start()
        } else {
            view.alpha = 1f
            view.translationY = 0f
        }
    }

    private fun slideValue(view: TextView, value: String) {
        if (view.text.toString() == value) return
        view.animate().cancel()
        if (!areMotionAnimationsEnabled()) {
            view.text = value
            view.alpha = 1f
            view.translationY = 0f
            return
        }
        view.translationY = 7f
        view.alpha = 0.35f
        view.text = value
        view.animate().translationY(0f).alpha(1f).setDuration(390)
            .setInterpolator(AnimationUtils.loadInterpolator(this, R.interpolator.ios_spring)).start()
    }

    private fun animateProgress(id: Int, target: Int) {
        val bar = findViewById<ProgressBar>(id)
        val safeTarget = target.coerceIn(0, 100)
        progressAnimators.remove(id)?.cancel()
        if (bar.progress == safeTarget) return
        if (!areMotionAnimationsEnabled()) {
            bar.progress = safeTarget
            return
        }
        progressAnimators[id] = ObjectAnimator.ofInt(bar, "progress", bar.progress, safeTarget).apply {
            duration = 780
            interpolator = AnimationUtils.loadInterpolator(this@MainActivity, R.interpolator.ios_spring)
            start()
        }
    }

    private fun updateJokeButton() {
        findViewById<Button>(R.id.jokeButton).text = if (jokeEnabled) "Matikan Mode Joke" else "Aktifkan Mode Joke"
        findViewById<TextView>(R.id.jokeStatus).apply {
            text = if (jokeEnabled) "Mode joke aktif di Windows" else "Mode joke mati"
            setTextColor(if (jokeEnabled) Color.rgb(255, 110, 110) else Color.LTGRAY)
        }
    }

    private fun formatBytes(bytes: Long): String {
        var value = bytes.coerceAtLeast(0).toDouble()
        val units = arrayOf("B", "KB", "MB", "GB", "TB")
        var unit = 0
        while (value >= 1024 && unit < units.lastIndex) { value /= 1024; unit++ }
        return String.format(Locale.getDefault(), "%.1f %s", value, units[unit])
    }

    private fun loadMemoryDetails() {
        if (!laptopOnline) {
            findViewById<TextView>(R.id.memorySummary).text = "Hubungkan laptop terlebih dahulu."
            return
        }
        findViewById<TextView>(R.id.memorySummary).text = "Memuat data memori..."
        dataWorker.execute {
            try {
                val data = PinnedTls.withConnection(address, 8765, "/api/memory", "GET", key,
                    prefs.getString("fingerprint", "").orEmpty()) { connection ->
                    if (connection.responseCode != 200) throw IllegalStateException("Data memori tidak tersedia (HTTP ${connection.responseCode}).")
                    JSONObject(connection.inputStream.bufferedReader().use { it.readText() })
                }
                latestMemoryPayload = data.toString()
                val total = data.optLong("totalBytes")
                val used = data.optLong("usedBytes")
                val available = data.optLong("availableBytes")
                val percent = if (total <= 0) 0 else ((used.toDouble() / total) * 100).toInt().coerceIn(0, 100)
                main.post {
                    findViewById<TextView>(R.id.memorySummary).text = "${formatBytes(used)} dari ${formatBytes(total)} dipakai ($percent%). Tersedia ${formatBytes(available)}."
                    findViewById<SegmentedMemoryView>(R.id.memoryUsageBar).setSegments(
                        used,
                        total,
                        data.optLong("processWorkingSetBytes"),
                        data.optLong("systemCacheBytes"),
                        data.optLong("pagedPoolBytes") + data.optLong("nonPagedPoolBytes")
                    )
                    findViewById<TextView>(R.id.memoryBreakdown).text = "Commit ${formatBytes(data.optLong("committedBytes"))} / ${formatBytes(data.optLong("commitLimitBytes"))} • Pool kernel paged ${formatBytes(data.optLong("pagedPoolBytes"))} dan non-paged ${formatBytes(data.optLong("nonPagedPoolBytes"))}."
                    publishMotionDemoMemory()
                }
            } catch (e: Exception) {
                latestMemoryPayload = JSONObject().put("error", e.message ?: "Data memori tidak dapat dimuat.").toString()
                main.post {
                    findViewById<TextView>(R.id.memorySummary).text = e.message ?: "Data memori tidak dapat dimuat."
                    publishMotionDemoMemory()
                }
            }
        }
    }

    private fun showRamActionPicker() {
        val labels = arrayOf(
            "Kosongkan working sets",
            "Kosongkan system working set",
            "Kosongkan modified page list",
            "Kosongkan standby list",
            "Kosongkan standby priority 0"
        )
        val actions = arrayOf("working-sets", "system-working-set", "modified-page-list", "standby-list", "priority0-standby-list")
        AlertDialog.Builder(this)
            .setTitle("Aksi memori ala RAMMap")
            .setItems(labels) { _, which -> confirmRamAction(actions[which], labels[which]) }
            .setNegativeButton("Batal", null)
            .show()
    }

    private fun confirmRamAction(action: String, label: String) {
        val caution = if (action == "working-sets" || action == "system-working-set")
            "Aplikasi mungkin memuat ulang data dan terasa lambat sesaat."
        else
            "Windows dapat memuat kembali halaman memori yang dibersihkan; kinerja bisa melambat sementara."
        AlertDialog.Builder(this)
            .setTitle("$label?")
            .setMessage("$caution\n\nPerintah dijalankan di laptop dengan izin administrator.")
            .setNegativeButton("Batal", null)
            .setPositiveButton("Jalankan") { _, _ -> runRamAction(action, label) }
            .show()
    }

    private fun runRamAction(action: String, label: String) {
        if (!laptopOnline) {
            findViewById<TextView>(R.id.ramActionStatus).text = "Hubungkan laptop terlebih dahulu."
            return
        }
        findViewById<TextView>(R.id.ramActionStatus).text = "Menjalankan $label…"
        dataWorker.execute {
            try {
                val result = PinnedTls.withConnection(address, 8765, "/api/memory/action?action=$action", "POST", key,
                    prefs.getString("fingerprint", "").orEmpty()) { connection ->
                    connection.doOutput = true
                    connection.outputStream.use { }
                    val code = connection.responseCode
                    val stream = if (code in 200..299) connection.inputStream else connection.errorStream
                    val body = stream?.bufferedReader()?.use { it.readText() }.orEmpty()
                    if (code != 200) throw IllegalStateException(JSONObject(body).optString("message", "Aksi memori ditolak (HTTP $code)."))
                    JSONObject(body)
                }
                main.post {
                    findViewById<TextView>(R.id.ramActionStatus).text = result.optString("message", "$label selesai.")
                    loadMemoryDetails()
                }
            } catch (error: Exception) {
                main.post { findViewById<TextView>(R.id.ramActionStatus).text = error.message ?: "Aksi RAMMap gagal." }
            }
        }
    }

    private fun showSchedulePicker() {
        if (!laptopOnline) {
            findViewById<TextView>(R.id.scheduleStatus).text = "Hubungkan laptop sebelum membuat jadwal."
            return
        }
        val now = java.util.Calendar.getInstance()
        val chosen = java.util.Calendar.getInstance().apply { add(java.util.Calendar.MINUTE, 10) }
        val actions = arrayOf("Matikan Windows", "Restart Windows", "Sleep Windows")
        val actionValues = arrayOf("shutdown", "restart", "sleep")
        AlertDialog.Builder(this)
            .setTitle("Pilih tindakan")
            .setItems(actions) { _, selected ->
                DatePickerDialog(this, { _, year, month, day ->
                    TimePickerDialog(this, { _, hour, minute ->
                        chosen.set(year, month, day, hour, minute, 0)
                        chosen.set(java.util.Calendar.MILLISECOND, 0)
                        if (chosen.timeInMillis <= System.currentTimeMillis()) {
                            findViewById<TextView>(R.id.scheduleStatus).text = "Pilih waktu di masa mendatang."
                        } else {
                            confirmSchedule(actionValues[selected], actions[selected], chosen.timeInMillis)
                        }
                    }, chosen.get(java.util.Calendar.HOUR_OF_DAY), chosen.get(java.util.Calendar.MINUTE), true).show()
                }, now.get(java.util.Calendar.YEAR), now.get(java.util.Calendar.MONTH), now.get(java.util.Calendar.DAY_OF_MONTH)).show()
            }
            .setNegativeButton("Batal", null)
            .show()
    }

    private fun confirmSchedule(action: String, label: String, atMillis: Long) {
        val messageInput = EditText(this).apply {
            hint = "Pesan yang tampil di laptop (opsional)"
            setSingleLine(true)
            setPadding(dp(14), dp(10), dp(14), dp(10))
        }
        AlertDialog.Builder(this)
            .setTitle("Jadwalkan $label")
            .setMessage("${Date(atMillis).let { java.text.SimpleDateFormat("EEE, d MMM • HH:mm", Locale.getDefault()).format(it) }}\nJadwal disimpan di Windows. Jika laptop mati lalu menyala sebelum waktu ini, hitung mundur melanjutkan dengan sisa waktu. Jika waktu target terlewati saat laptop mati, perintah dijalankan saat Windows aktif kembali.")
            .setView(messageInput)
            .setNegativeButton("Batal", null)
            .setPositiveButton("Simpan jadwal") { _, _ -> saveSchedule(action, atMillis, messageInput.text.toString()) }
            .show()
    }

    private fun publishDemoSchedule(data: JSONObject) {
        if (::motionDemoWebView.isInitialized && motionDemoPageReady) {
            val json = JSONObject.quote(data.toString())
            motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.setScheduledPower($json)", null)
        }
    }

    private fun publishDemoScheduleError(message: String) {
        runOnUiThread {
            if (::motionDemoWebView.isInitialized && motionDemoPageReady) {
                val safeMessage = JSONObject.quote(message)
                motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.setScheduleResult(false,$safeMessage)", null)
            }
        }
    }

    private fun saveScheduleFromDemo(action: String, atMillis: Long) {
        dataWorker.execute {
            try {
                val path = "/api/schedule?action=$action&atUtc=$atMillis&message="
                val result = PinnedTls.withConnection(address, 8765, path, "POST", key,
                    prefs.getString("fingerprint", "").orEmpty()) { connection ->
                    connection.doOutput = true
                    connection.outputStream.use { }
                    val code = connection.responseCode
                    val body = (if (code in 200..299) connection.inputStream else connection.errorStream)
                        ?.bufferedReader()?.use { it.readText() }.orEmpty()
                    if (code != 200) throw IllegalStateException("Windows menolak jadwal (HTTP $code).")
                    JSONObject(body)
                }
                main.post {
                    renderSchedule(result)
                    if (::motionDemoWebView.isInitialized && motionDemoPageReady) {
                        val json = JSONObject.quote(result.toString())
                        motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.setScheduleResult(true,$json,${JSONObject.quote("Jadwal berhasil disimpan.")})", null)
                    }
                }
            } catch (error: Exception) {
                publishDemoScheduleError(error.message ?: "Gagal menyimpan jadwal.")
            }
        }
    }

    private fun loadScheduleForDemoRequest(reportErrors: Boolean = true) {
        if (address.isBlank() || key.isBlank()) {
            if (reportErrors) publishDemoScheduleError("Hubungkan laptop untuk memuat jadwal.")
            return
        }
        dataWorker.execute {
            try {
                val result = PinnedTls.withConnection(address, 8765, "/api/schedule", "GET", key,
                    prefs.getString("fingerprint", "").orEmpty()) { connection ->
                    if (connection.responseCode != 200) throw IllegalStateException("Jadwal tidak dapat dimuat.")
                    JSONObject(connection.inputStream.bufferedReader().use { it.readText() })
                }
                main.post {
                    renderSchedule(result)
                    publishDemoSchedule(result)
                }
            } catch (error: Exception) {
                if (reportErrors) publishDemoScheduleError(error.message ?: "Gagal memuat jadwal.")
                else android.util.Log.w("LaptopStatus", "Jadwal tidak dapat dipulihkan di UI gerak", error)
            }
        }
    }

    private fun cancelScheduleFromDemoRequest() {
        if (!laptopOnline) {
            publishDemoScheduleError("Laptop sedang offline.")
            return
        }
        dataWorker.execute {
            try {
                val result = PinnedTls.withConnection(address, 8765, "/api/schedule?cancel=true", "POST", key,
                    prefs.getString("fingerprint", "").orEmpty()) { connection ->
                    connection.doOutput = true
                    connection.outputStream.use { }
                    val code = connection.responseCode
                    val body = connection.inputStream.bufferedReader().use { it.readText() }
                    if (code != 200) throw IllegalStateException("Jadwal tidak dapat dibatalkan (HTTP $code).")
                    JSONObject(body)
                }
                main.post {
                    renderSchedule(JSONObject().put("atUtc", 0).put("action", ""))
                    if (::motionDemoWebView.isInitialized && motionDemoPageReady) {
                        val json = JSONObject.quote(JSONObject().put("atUtc", 0).put("action", "").toString())
                        motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.setScheduleResult(true,$json,${JSONObject.quote("Jadwal dibatalkan.")})", null)
                    }
                }
            } catch (error: Exception) {
                publishDemoScheduleError(error.message ?: "Gagal membatalkan jadwal.")
            }
        }
    }

    private fun publishDemoRoutines(result: JSONObject) {
        if (::motionDemoWebView.isInitialized && motionDemoPageReady) {
            val json = JSONObject.quote(result.toString())
            motionDemoWebView.evaluateJavascript("window.LaptopStatus&&window.LaptopStatus.setScheduleRoutines($json)", null)
        }
    }

    private fun publishDemoRoutineResult(index: Int, ok: Boolean, enabled: Boolean, message: String) {
        runOnUiThread {
            if (::motionDemoWebView.isInitialized && motionDemoPageReady) {
                motionDemoWebView.evaluateJavascript(
                    "window.LaptopStatus&&window.LaptopStatus.setScheduleRoutineResult($index,$ok,$enabled,${JSONObject.quote(message)})", null
                )
            }
        }
    }

    private fun loadScheduleRoutinesForDemo() {
        if (!laptopOnline) {
            publishDemoRoutineResult(-1, false, false, "Hubungkan laptop untuk memuat rutinitas.")
            return
        }
        dataWorker.execute {
            try {
                val result = PinnedTls.withConnection(address, 8765, "/api/routines", "GET", key,
                    prefs.getString("fingerprint", "").orEmpty()) { connection ->
                    val code = connection.responseCode
                    val body = (if (code in 200..299) connection.inputStream else connection.errorStream)
                        ?.bufferedReader()?.use { it.readText() }.orEmpty()
                    if (code != 200) throw IllegalStateException("Rutinitas tidak dapat dimuat (HTTP $code).")
                    JSONObject(body)
                }
                publishDemoRoutines(result)
            } catch (error: Exception) {
                publishDemoRoutineResult(-1, false, false, error.message ?: "Gagal memuat rutinitas Windows.")
            }
        }
    }

    private fun setScheduleRoutineForDemo(index: Int, enabled: Boolean) {
        if (index !in 0..2) return
        if (!laptopOnline) {
            publishDemoRoutineResult(index, false, false, "Hubungkan laptop sebelum mengubah rutinitas.")
            return
        }
        dataWorker.execute {
            try {
                val path = "/api/routines?index=$index&enabled=$enabled"
                val result = PinnedTls.withConnection(address, 8765, path, "POST", key,
                    prefs.getString("fingerprint", "").orEmpty()) { connection ->
                    connection.doOutput = true
                    connection.outputStream.use { }
                    val code = connection.responseCode
                    val body = (if (code in 200..299) connection.inputStream else connection.errorStream)
                        ?.bufferedReader()?.use { it.readText() }.orEmpty()
                    if (code != 200) throw IllegalStateException("Windows menolak perubahan rutinitas (HTTP $code).")
                    JSONObject(body)
                }
                val routines = result.optJSONObject("routines") ?: JSONObject()
                main.post {
                    publishDemoRoutineResult(index, true, enabled, if (enabled) "Rutinitas aktif di Windows." else "Rutinitas dimatikan.")
                    if (routines.has("routines")) publishDemoRoutines(routines)
                    if (enabled) addEvent("Rutinitas dijadwalkan", "${listOf("Matikan malam", "Tidur siang", "Mulai ulang")[index]}", clock.format(Date()), R.drawable.ic_clock)
                }
            } catch (error: Exception) {
                publishDemoRoutineResult(index, false, false, error.message ?: "Perubahan rutinitas gagal dikirim.")
            }
        }
    }

    private fun saveSchedule(action: String, atMillis: Long, message: String) {
        val status = findViewById<TextView>(R.id.scheduleStatus)
        status.text = "Menyimpan jadwal…"
        dataWorker.execute {
            try {
                val path = "/api/schedule?action=$action&atUtc=$atMillis&message=${URLEncoder.encode(message.take(240), StandardCharsets.UTF_8.name())}"
                val result = PinnedTls.withConnection(address, 8765, path, "POST", key, prefs.getString("fingerprint", "").orEmpty()) { connection ->
                    connection.doOutput = true
                    connection.outputStream.use { }
                    val code = connection.responseCode
                    val body = (if (code in 200..299) connection.inputStream else connection.errorStream)?.bufferedReader()?.use { it.readText() }.orEmpty()
                    if (code != 200) throw IllegalStateException("Jadwal ditolak (HTTP $code): $body")
                    JSONObject(body)
                }
                main.post { renderSchedule(result) }
            } catch (error: Exception) { main.post { status.text = error.message ?: "Gagal menyimpan jadwal." } }
        }
    }

    private fun loadSchedule() {
        if (!laptopOnline) return
        dataWorker.execute {
            try {
                val result = PinnedTls.withConnection(address, 8765, "/api/schedule", "GET", key,
                    prefs.getString("fingerprint", "").orEmpty()) { connection ->
                    if (connection.responseCode != 200) throw IllegalStateException("Jadwal tidak tersedia.")
                    JSONObject(connection.inputStream.bufferedReader().use { it.readText() })
                }
                main.post { renderSchedule(result) }
            } catch (_: Exception) { }
        }
    }

    private fun renderSchedule(data: JSONObject) {
        val status = findViewById<TextView>(R.id.scheduleStatus)
        val at = data.optLong("atUtc", 0L)
        val action = data.optString("action")
        if (at <= System.currentTimeMillis() || action.isBlank()) {
            status.text = "Belum ada jadwal daya."
            findViewById<Button>(R.id.cancelScheduleButton).visibility = View.GONE
        } else {
            val actionLabel = when (action) { "shutdown" -> "Matikan"; "restart" -> "Restart"; else -> "Sleep" }
            status.text = "$actionLabel dijadwalkan ${java.text.SimpleDateFormat("EEE, d MMM yyyy • HH:mm", Locale.getDefault()).format(Date(at))}."
            findViewById<Button>(R.id.cancelScheduleButton).visibility = View.VISIBLE
        }
    }

    private fun cancelScheduledPower() {
        if (!laptopOnline) return
        val status = findViewById<TextView>(R.id.scheduleStatus)
        status.text = "Membatalkan jadwal…"
        dataWorker.execute {
            try {
                PinnedTls.withConnection(address, 8765, "/api/schedule?cancel=true", "POST", key,
                    prefs.getString("fingerprint", "").orEmpty()) { connection ->
                    connection.doOutput = true
                    connection.outputStream.use { }
                    if (connection.responseCode != 200) throw IllegalStateException("Jadwal tidak dapat dibatalkan.")
                    connection.inputStream.bufferedReader().use { it.readText() }
                }
                main.post { renderSchedule(JSONObject().put("atUtc", 0).put("action", "")) }
            } catch (error: Exception) { main.post { status.text = error.message ?: "Gagal membatalkan jadwal." } }
        }
    }

    private data class RemoteProcess(
        val pid: Int,
        val name: String,
        val memoryBytes: Long,
        val canTerminate: Boolean,
        val note: String,
        val iconBase64: String = ""
    )

    private fun processDeviceKey(): String = normalizeFingerprint(prefs.getString("fingerprint", "").orEmpty()).ifBlank { "default" }
    private fun processListCollapsedPreferenceKey() = "processListCollapsed_${processDeviceKey()}"

    private fun toggleProcessList() {
        val key = processListCollapsedPreferenceKey()
        val collapsed = !prefs.getBoolean(key, false)
        prefs.edit().putBoolean(key, collapsed).apply()
        val list = findViewById<LinearLayout>(R.id.processList)
        val button = findViewById<Button>(R.id.toggleProcessListButton)
        val generation = (viewAnimationGenerations[list] ?: 0) + 1
        viewAnimationGenerations[list] = generation
        button.text = if (collapsed) "Tampilkan semua" else "Sembunyikan semua"
        list.animate().cancel()
        if (!areMotionAnimationsEnabled()) {
            list.visibility = if (collapsed) View.GONE else View.VISIBLE
            list.alpha = 1f
            list.translationY = 0f
            return
        }
        if (collapsed) {
            list.animate().alpha(0f).translationY(-dp(8).toFloat()).setDuration(150)
                .withEndAction {
                    if (viewAnimationGenerations[list] != generation) return@withEndAction
                    list.visibility = View.GONE
                    list.alpha = 1f
                    list.translationY = 0f
                }.start()
        } else {
            list.visibility = View.VISIBLE
            list.alpha = 0f
            list.translationY = -dp(8).toFloat()
            list.animate().alpha(1f).translationY(0f).setDuration(220)
                .setInterpolator(DecelerateInterpolator()).start()
        }
    }

    private fun loadWindowsProcesses(filter: String = processSearchFilter) {
        processSearchFilter = filter.trim()
        val requestFilter = processSearchFilter
        if (!laptopOnline) {
            motionProcessStatus = if (requestFilter.isBlank())
                "Hubungkan laptop terlebih dahulu."
            else
                "Hubungkan laptop terlebih dahulu untuk mencari proses."
            findViewById<TextView>(R.id.processStatus).text = motionProcessStatus
            lastWindowsProcesses = emptyList()
            publishMotionDemoProcesses()
            logLiveActivity("PROC", "GET /api/processes skipped reason=offline", 'W')
            return
        }
        loadMemoryDetails()
        motionProcessStatus = if (requestFilter.isBlank()) "Memuat daftar proses..." else "Mencari proses..."
        findViewById<TextView>(R.id.processStatus).text = motionProcessStatus
        publishMotionDemoProcesses()
        val requestStartedAt = android.os.SystemClock.elapsedRealtime()
        logLiveActivity("PROC", "GET /api/processes")
        dataWorker.execute {
            try {
                val result = PinnedTls.withConnection(address, 8765, "/api/processes", "GET", key,
                    prefs.getString("fingerprint", "").orEmpty()) { connection ->
                    if (connection.responseCode != 200) throw IllegalStateException("Daftar proses tidak tersedia (HTTP ${connection.responseCode}).")
                    JSONObject(connection.inputStream.bufferedReader().use { it.readText() })
                }
                val processes = result.optJSONArray("processes") ?: JSONArray()
                val items = (0 until processes.length()).mapNotNull { index ->
                    val item = processes.optJSONObject(index) ?: return@mapNotNull null
                    RemoteProcess(
                        item.optInt("pid"),
                        item.optString("name", "Proses"),
                        item.optLong("workingSetBytes"),
                        item.optBoolean("canTerminate"),
                        item.optString("terminationNote"),
                        item.optString("iconBase64").takeIf { it.length <= 131072 }.orEmpty()
                    )
                }
                logLiveActivity("PROC", "GET /api/processes HTTP 200 count=${items.size} elapsed=${android.os.SystemClock.elapsedRealtime() - requestStartedAt}ms")
                main.post { renderWindowsProcesses(items, requestFilter) }
            } catch (e: Exception) {
                val message = e.message ?: "Daftar proses gagal dimuat."
                motionProcessStatus = message
                logLiveActivity("PROC", "GET /api/processes failed error=${e.javaClass.simpleName} detail=$message", 'E')
                main.post {
                    findViewById<TextView>(R.id.processStatus).text = message
                    publishMotionDemoProcesses()
                }
            }
        }
    }

    private fun renderWindowsProcesses(processes: List<RemoteProcess>, filter: String = processSearchFilter) {
        armedProcessKills.values.forEach(main::removeCallbacks)
        armedProcessKills.clear()
        lastWindowsProcesses = processes
        val list = findViewById<LinearLayout>(R.id.processList)
        list.removeAllViews()
        val filterTerms = filter.split(Regex("\\s+")).filter(String::isNotBlank)
        val visibleProcesses = if (filter.isBlank()) processes else processes.filter { process ->
            process.name.contains(filter, ignoreCase = true) || filterTerms.any { process.name.contains(it, ignoreCase = true) }
        }
        val collapsed = prefs.getBoolean(processListCollapsedPreferenceKey(), false)
        findViewById<Button>(R.id.toggleProcessListButton).text = if (collapsed) "Tampilkan semua" else "Sembunyikan semua"
        list.visibility = if (collapsed) View.GONE else View.VISIBLE
        motionProcessStatus = when {
            processes.isEmpty() -> "Belum ada proses untuk ditampilkan."
            visibleProcesses.isEmpty() -> "Tidak ada proses yang cocok untuk ‘$filter’."
            filter.isNotBlank() -> "${visibleProcesses.size} hasil untuk ‘$filter’ dari ${processes.size} proses."
            else -> "${processes.size} proses dimuat • penggunaan memori terbesar ditampilkan lebih dulu."
        }
        findViewById<TextView>(R.id.processStatus).text = motionProcessStatus
        publishMotionDemoProcesses()
        visibleProcesses.forEach { process ->
            val row = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                setPadding(dp(12), dp(10), dp(12), dp(10))
                background = GradientDrawable().apply {
                    cornerRadius = dp(13).toFloat()
                    setColor(Color.argb(28, 255, 255, 255))
                    setStroke(dp(1), Color.argb(40, 255, 255, 255))
                }
                layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(7) }
            }
            val header = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
            val iconBytes = runCatching {
                if (process.iconBase64.isBlank()) null
                else android.util.Base64.decode(process.iconBase64, android.util.Base64.DEFAULT)
                    .takeIf { it.size <= 98304 }
                    ?.let { BitmapFactory.decodeByteArray(it, 0, it.size) }
            }.getOrNull()
            val appIcon = ImageView(this).apply {
                if (iconBytes != null) setImageBitmap(iconBytes)
                else setImageDrawable(GradientDrawable().apply {
                    shape = GradientDrawable.RECTANGLE
                    cornerRadius = dp(10).toFloat()
                    setColor(Color.rgb(37, 51, 67))
                })
                scaleType = ImageView.ScaleType.FIT_CENTER
                setPadding(dp(4), dp(4), dp(4), dp(4))
                contentDescription = "Ikon ${process.name}"
                layoutParams = LinearLayout.LayoutParams(dp(36), dp(36)).apply { rightMargin = dp(10) }
            }
            val label = TextView(this).apply {
                text = "${process.name}\nPID ${process.pid}  •  RAM ${formatBytes(process.memoryBytes)}"
                textSize = 13f
                setTextColor(getColor(R.color.text_main))
                maxLines = 2
                ellipsize = android.text.TextUtils.TruncateAt.END
                layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            }
            val kill = Button(this).apply {
                text = if (process.canTerminate) "Hentikan" else "Dilindungi"
                isEnabled = process.canTerminate
                isAllCaps = false
                textSize = 12f
                maxLines = 1
                minWidth = dp(92)
                minHeight = dp(42)
                setTextColor(if (process.canTerminate) Color.rgb(222, 235, 245) else Color.rgb(155, 168, 181))
                backgroundTintList = android.content.res.ColorStateList.valueOf(
                    if (process.canTerminate) Color.rgb(43, 57, 73) else Color.rgb(36, 45, 56))
                layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { leftMargin = dp(10) }
                setOnClickListener {
                    armProcessKill(process, row, this)
                }
            }
            header.addView(appIcon)
            header.addView(label)
            header.addView(kill)
            row.addView(header)
            if (!process.canTerminate && process.note.isNotBlank()) {
                row.addView(TextView(this).apply {
                    text = process.note
                    textSize = 10f
                    setTextColor(getColor(R.color.text_dim))
                    setPadding(dp(46), dp(5), 0, 0)
                    maxLines = 3
                    layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                })
            }
            list.addView(row)
        }
    }

    private fun armProcessKill(process: RemoteProcess, row: View, button: Button) {
        armedProcessKills.remove(process.pid)?.let { timeout ->
            main.removeCallbacks(timeout)
            button.isEnabled = false
            button.text = "Mengirim..."
            dataWorker.execute { requestProcessKill(process, row, button) }
            return
        }
        button.text = "Yakin?"
        button.setTextColor(Color.WHITE)
        button.backgroundTintList = android.content.res.ColorStateList.valueOf(Color.rgb(226, 91, 106))
        val timeout = Runnable {
            armedProcessKills.remove(process.pid)
            if (button.isAttachedToWindow) {
                button.text = "Hentikan"
                button.setTextColor(Color.rgb(222, 235, 245))
                button.backgroundTintList = android.content.res.ColorStateList.valueOf(Color.rgb(43, 57, 73))
            }
        }
        armedProcessKills[process.pid] = timeout
        main.postDelayed(timeout, 2200L)
    }

    private fun requestProcessKill(process: RemoteProcess, row: View, button: Button) {
        try {
            val code = PinnedTls.withConnection(address, 8765, "/api/process/kill?pid=${process.pid}", "POST", key,
                prefs.getString("fingerprint", "").orEmpty()) { connection ->
                connection.doOutput = true
                connection.outputStream.use { }
                connection.responseCode
            }
            if (code != 202) throw IllegalStateException(if (code == 403) "Windows melindungi proses ini." else "Windows menolak permintaan (HTTP $code).")
            main.post {
                findViewById<TextView>(R.id.processStatus).text = "Permintaan dikirim untuk ${process.name}. Setujui UAC di laptop jika muncul."
                val startHeight = row.height
                row.animate().cancel()
                if (!areMotionAnimationsEnabled()) {
                    (row.parent as? ViewGroup)?.removeView(row)
                } else ValueAnimator.ofInt(startHeight, 0).apply {
                    duration = 480L
                    addUpdateListener { animator ->
                        row.layoutParams = row.layoutParams.apply { height = animator.animatedValue as Int }
                        row.alpha = if (startHeight == 0) 0f else (animator.animatedValue as Int).toFloat() / startHeight
                    }
                    addListener(object : android.animation.AnimatorListenerAdapter() {
                        override fun onAnimationEnd(animation: android.animation.Animator) {
                            (row.parent as? ViewGroup)?.removeView(row)
                        }
                    })
                    start()
                }
                main.postDelayed({ loadWindowsProcesses() }, 1800L)
            }
        } catch (e: Exception) {
            main.post {
                findViewById<TextView>(R.id.processStatus).text = e.message ?: "Permintaan penghentian gagal."
                button.isEnabled = true
                button.text = "Hentikan"
                button.setTextColor(Color.rgb(222, 235, 245))
                button.backgroundTintList = android.content.res.ColorStateList.valueOf(Color.rgb(43, 57, 73))
            }
        }
    }

    private fun poll() {
        if (address.isBlank() || key.isBlank()) {
            main.post {
                laptopOnline = false
                setRemoteControlsEnabled(false)
                transitionStatusPill("Mencari", "waiting")
                findViewById<TextView>(R.id.deviceSub).text = "Mencari laptop di jaringan lokal"
                findViewById<TextView>(R.id.connectionInfo).text = "Pilih laptop yang ditemukan. Pemasangan pertama perlu disetujui di Windows."
            }
            queueDiscovery()
            return
        }
        try {
            val requestStartedAt = android.os.SystemClock.elapsedRealtime()
            val activeId = prefs.getString("activeDeviceId", "").orEmpty()
            val token = key
            val fingerprint = prefs.getString("fingerprint", "").orEmpty()
            val currentAddress = address
            var usedAddress = currentAddress
            val data = try {
                requestLaptopStatus(currentAddress, token, fingerprint)
            } catch (primaryError: Exception) {
                val alternatives = savedRouteCandidates(activeId).filterNot { it.equals(currentAddress, ignoreCase = true) }
                var recovered: Pair<String, JSONObject>? = null
                var lastError: Exception = primaryError
                for (candidate in alternatives) {
                    try {
                        recovered = candidate to requestLaptopStatus(candidate, token, fingerprint, 1500, 2000)
                        break
                    } catch (error: Exception) { lastError = error }
                }
                val fallback = recovered ?: throw lastError
                usedAddress = fallback.first
                fallback.second
            }
            if (usedAddress != currentAddress) {
                address = usedAddress
                prefs.edit().putString("address", usedAddress).apply()
            }
            logLiveActivity(
                "NET",
                "GET /api/status HTTP 200 elapsed=${android.os.SystemClock.elapsedRealtime() - requestStartedAt}ms route=${if (usedAddress == currentAddress) "primary" else "fallback"}",
                'D'
            )
            main.post {
                showOnline(data)
                if (usedAddress != currentAddress) {
                    addressInput.setText(usedAddress)
                    findViewById<TextView>(R.id.connectionInfo).text = "Jalur koneksi beralih otomatis ke $usedAddress."
                }
            }
        } catch (e: Exception) {
            logLiveActivity("NET", "GET /api/status failed error=${e.javaClass.simpleName} detail=${e.message ?: "no detail"}", 'W')
            main.post { showOffline(e.message ?: "Laptop tidak dapat dijangkau.", true) }
            queueDiscovery()
        }
    }

    private fun requestLaptopStatus(host: String, token: String, fingerprint: String, connectTimeout: Int = 4000, readTimeout: Int = 5000): JSONObject {
        return PinnedTls.withConnection(host, 8765, "/api/status", "GET", token, fingerprint,
            connectTimeoutMillis = connectTimeout, readTimeoutMillis = readTimeout) { connection ->
            val code = connection.responseCode
            if (code != 200) throw IllegalStateException(if (code == 401) "Kunci pairing salah." else "Laptop menolak koneksi (HTTP $code).")
            val payload = JSONObject(connection.inputStream.bufferedReader().use { it.readText() })
            val activeId = prefs.getString("activeDeviceId", "").orEmpty()
            if (activeId.isNotBlank() && payload.optString("deviceId") != activeId)
                throw IllegalStateException("Alamat alternatif mengarah ke perangkat lain.")
            payload
        }
    }

    private fun savedRouteCandidates(deviceId: String): List<String> {
        if (deviceId.isBlank()) return emptyList()
        val json = prefs.getString("paired_${deviceId}_addresses", "").orEmpty()
        val saved = try {
            val array = JSONArray(json)
            (0 until array.length()).map { array.optString(it).trim() }.filter(String::isNotBlank)
        } catch (_: Exception) { emptyList() }
        return saved.distinctBy { it.trimEnd('/').lowercase(Locale.ROOT) }
    }

    private fun playConnectionChime(success: Boolean) {
        if (!prefs.getBoolean("connectionChimes", true)) return
        val now = System.currentTimeMillis()
        if (lastConnectionChimeSuccess == success && now - lastConnectionChimeAt < 20_000) return
        lastConnectionChimeAt = now
        lastConnectionChimeSuccess = success
        val sound = if (success) R.raw.connection_success else R.raw.connection_failed
        val player = try { MediaPlayer.create(applicationContext, sound) } catch (_: Exception) { null } ?: return
        player.setVolume(0.85f, 0.85f)
        player.setOnCompletionListener { completed -> completed.release() }
        player.setOnErrorListener { failed, _, _ -> failed.release(); true }
        try { player.start() } catch (_: Exception) { player.release() }
    }

    private fun showOnline(data: JSONObject) {
        maybePostAndroidJokeNotification(data)
        if (LaptopBatteryAlerts.checkAndNotify(this, data))
            addEvent("Baterai di bawah 20%", "Notifikasi baterai laptop dikirim", clock.format(Date()), R.drawable.ic_laptop)
        val hadConnection = laptopOnline
        if (!hadConnection) playConnectionChime(success = true)
        if (!hadConnection) logLiveActivity("CONN", "status=online")
        laptopOnline = true
        connectedHostIsLinux = data.optString("osVersion").contains("linux", ignoreCase = true)
        latestStatusPayload = JSONObject(data.toString()).put("online", true).toString()
        pendingConnectionDeviceId = null
        laptopOnAcPower = data.optBoolean("onAcPower", false)
        setRemoteControlsEnabled(true)
        findViewById<TextView>(R.id.deviceName).text = data.optString("deviceName", "Laptop")
        findViewById<Button>(R.id.jokeButton).visibility = if (connectedHostIsLinux) View.GONE else View.VISIBLE
        findViewById<Button>(R.id.screenRequestButton).visibility = if (connectedHostIsLinux) View.GONE else View.VISIBLE
        transitionStatusPill("Terhubung", "connected")
        cpuRing.setMetric("${data.optInt("cpuPercent")}%", data.optInt("cpuPercent"))
        ramRing.setMetric("${data.optInt("ramPercent")}%", data.optInt("ramPercent"))
        findViewById<TextView>(R.id.processCpuValue).text = "${data.optInt("cpuPercent")}%"
        findViewById<TextView>(R.id.processRamValue).text = "${data.optInt("ramPercent")}%"
        val batteryPercent = if (data.isNull("batteryPercent")) null else data.optInt("batteryPercent").coerceIn(0, 100)
        batteryRing.setMetric(batteryPercent?.let { "$it%" } ?: "N/A", batteryPercent)
        val now = clock.format(Date())
        val hardware = listOf(data.optString("manufacturer"), data.optString("model")).filter { it.isNotBlank() }.distinct().joinToString(" • ")
        findViewById<TextView>(R.id.deviceSub).text = listOf("Online", if (laptopOnAcPower) "tersambung ke charger" else "baterai", hardware, "diperbarui $now").filter { it.isNotBlank() }.joinToString(" • ")
        slideValue(findViewById(R.id.connectionInfo), "Terhubung ke ${data.optString("deviceName", "laptop")}. Data status diperbarui $now.")
        findViewById<View>(R.id.manualConnectionProgressRow).visibility = View.GONE
        findViewById<Button>(R.id.connectButton).apply { isEnabled = true; text = "Simpan dan hubungkan" }
        selectedDiscoveryStatus?.let { it.text = "Terhubung"; it.setTextColor(Color.rgb(129, 245, 202)) }
        refreshDiscoveryConnectionState()
        if (!hadConnection) animateDashboardCards()
        jokeEnabled = data.optBoolean("jokeActive", false)
        updateJokeButton()
        screenActive = data.optBoolean("screenControlActive", false)
        screenPending = data.optBoolean("screenControlPending", false)
        screenPasswordAccessAvailable = data.optBoolean("screenPasswordAccessEnabled", false)
        updateScreenControlUi()
        publishMotionDemoScreenState()
        val cpuTemp = data.optDouble("cpuTemperatureC", Double.NaN)
        val fanRpm = data.optDouble("fanRpm", Double.NaN)
        val cpuTempText = if (cpuTemp.isFinite() && cpuTemp in 5.0..115.0) "%.0f°C".format(Locale.getDefault(), cpuTemp) else "—"
        val fanText = if (fanRpm.isFinite() && fanRpm in 1.0..100000.0) "%.0f rpm".format(Locale.getDefault(), fanRpm) else "—"
        val cpuUsage = data.optInt("cpuPercent", -1).takeIf { it in 0..100 }
        findViewById<TextView>(R.id.telemetryDetails).text = "Sensor yang tidak didukung perangkat akan ditampilkan sebagai —."
        val cpuModel = data.optString("cpuModel").ifBlank { "Prosesor tidak terdeteksi" }
        val aggregateGpuUsage = data.optDouble("gpuUsagePercent", Double.NaN)
        val aggregateGpuTemperature = data.optDouble("gpuTemperatureC", Double.NaN)
        val gpuArray = data.optJSONArray("gpus")
        val gpus = if (gpuArray != null && gpuArray.length() > 0) {
            buildList {
                for (index in 0 until gpuArray.length()) {
                    val item = gpuArray.optJSONObject(index) ?: continue
                    val model = item.optString("model").trim()
                    if (model.isBlank()) continue
                    val usage = item.optDouble("usagePercent", Double.NaN)
                        .takeIf { it.isFinite() && it in 0.0..100.0 }?.toInt()
                    val temperature = item.optDouble("temperatureC", Double.NaN)
                        .takeIf { it.isFinite() && it in 5.0..130.0 }?.toInt()
                    add(RemoteGpu(model, usage, temperature))
                }
            }
        } else {
            data.optString("gpuModel").split(";").map(String::trim).filter { it.isNotBlank() }
                .filterNot { it.equals("Grafis tidak terdeteksi", ignoreCase = true) }
                .mapIndexed { index, model ->
                    RemoteGpu(
                        model,
                        if (index == 0 && aggregateGpuUsage.isFinite() && aggregateGpuUsage in 0.0..100.0) aggregateGpuUsage.toInt() else null,
                        if (index == 0 && aggregateGpuTemperature.isFinite() && aggregateGpuTemperature in 5.0..130.0) aggregateGpuTemperature.toInt() else null
                    )
                }
        }
        val validGpuUsage = gpus.firstOrNull { isDedicatedGpuModel(it.model) }?.usagePercent
            ?: gpus.firstOrNull()?.usagePercent
        val memoryTotal = data.optLong("totalPhysicalMemoryBytes")
        val processorCount = data.optInt("logicalProcessors")
        val architecture = data.optString("osArchitecture").ifBlank { "Tidak tersedia" }
        val osVersion = data.optString("osVersion").ifBlank { "Windows" }
        val device = listOf(data.optString("manufacturer"), data.optString("model"))
            .filter { it.isNotBlank() }.distinct().joinToString(" ")
        renderHardwareCards(
            cpuModel, cpuUsage, cpuTemp.takeIf { it.isFinite() && it in 5.0..115.0 }, gpus,
            memoryTotal, processorCount, architecture, osVersion, device, fanText
        )
        findViewById<TextView>(R.id.processGpuValue).text = validGpuUsage?.let { "$it%" } ?: "—"
        findViewById<TextView>(R.id.troubleshootingStatus).text = buildString {
            append("Koneksi laptop aktif. ")
            val unavailable = buildList {
                if (cpuTempText == "—") add("suhu CPU")
                if (gpus.isEmpty()) add("GPU")
                else if (gpus.all { it.temperatureC == null }) add("suhu GPU")
                if (fanText == "—") add("sensor kipas")
            }
            if (unavailable.isEmpty()) append("Sensor utama melaporkan data.")
            else append("Data ").append(unavailable.joinToString(", ")).append(" tidak tersedia dari perangkat.")
        }
        if (!hadConnection) loadMemoryDetails()
        if (!hadConnection) {
            loadSchedule()
            if (prefs.getBoolean("motionDemoUi", true) && !prefs.getBoolean("classicTheme", false)) {
                loadScheduleForDemoRequest(reportErrors = false)
            }
        }
        if (wasOnline == false) {
            addEvent("Laptop menyala", "Laptop terhubung dan mengirim status", now, R.drawable.ic_clock)
            if (!prefs.getBoolean("backgroundMonitor", false)) notifyLaptopOnline(data.optString("deviceName", "Laptop"), now)
        } else if (wasOnline == null) {
            addEvent("Terhubung ke laptop", "Status laptop berhasil dimuat", now, R.drawable.ic_clock)
        }
        wasOnline = true
        publishMotionDemoStatus()
        returnToMotionDemoAfterNativeAction()
    }

    private fun showHardwareBrand(image: ImageView, model: String) {
        val normalized = model.lowercase(Locale.ROOT)
        val brand = when {
            "nvidia" in normalized || "geforce" in normalized || "quadro" in normalized -> Triple(R.drawable.ic_brand_nvidia, "NVIDIA", Color.rgb(118, 185, 0))
            "amd" in normalized || "ryzen" in normalized || "radeon" in normalized -> Triple(R.drawable.ic_brand_amd, "AMD", Color.rgb(237, 28, 36))
            "intel" in normalized || "iris" in normalized -> Triple(R.drawable.ic_brand_intel, "Intel", Color.rgb(0, 113, 197))
            else -> null
        }
        if (brand == null) {
            image.visibility = View.INVISIBLE
            return
        }
        image.setImageResource(brand.first)
        image.imageTintList = ColorStateList.valueOf(brand.third)
        image.contentDescription = "Logo ${brand.second}"
        image.visibility = View.VISIBLE
    }

    private fun showGpuHardwareBrand(image: ImageView, model: String) {
        val normalized = model.lowercase(Locale.ROOT)
        val key = when {
            "rtx" in normalized -> "rtx"
            "gtx" in normalized -> "gtx"
            Regex("\\bmx\\s?\\d").containsMatchIn(normalized) -> "mx"
            Regex("\\bgt\\s?\\d").containsMatchIn(normalized) -> "gt"
            "arc" in normalized -> "arc"
            "uhd" in normalized -> "uhd"
            "hd graphics" in normalized -> "hd"
            "mobility" in normalized || Regex("\\bati\\b").containsMatchIn(normalized) -> "radeon_ati"
            "radeon" in normalized && Regex("\\b(?:6[0-9]0m|7[0-9]0m|8[0-9]0m|rx\\s?[5-9]\\d{3})\\b").containsMatchIn(normalized) -> "rdna"
            "radeon" in normalized -> "radeon"
            "adreno" in normalized || "snapdragon" in normalized -> "snapdragon"
            else -> null
        }
        val drawable = key?.let { resources.getIdentifier("ic_hardware_$it", "drawable", packageName) } ?: 0
        if (drawable == 0) {
            showHardwareBrand(image, model)
            return
        }
        image.setImageResource(drawable)
        image.imageTintList = null
        image.contentDescription = "Logo seri grafis ${key.orEmpty().replace('_', ' ')}"
        image.visibility = View.VISIBLE
    }

    private fun cpuFamilyName(model: String): String {
        val match = Regex("(?i)\\bRyzen\\s+(AI\\s+)?[3579]\\b|\\bCore\\s+Ultra\\s+[579]\\b|\\bCore\\s+i[3579]\\b|\\bSnapdragon\\s+X\\s+\\w+\\b").find(model)
        return match?.value?.trim() ?: when {
            "ryzen" in model.lowercase(Locale.ROOT) -> "AMD Ryzen"
            "intel" in model.lowercase(Locale.ROOT) -> "Intel Core"
            "snapdragon" in model.lowercase(Locale.ROOT) -> "Snapdragon"
            else -> "CPU"
        }
    }

    private fun gpuFamilyName(model: String): String {
        val normalized = model.lowercase(Locale.ROOT)
        return when {
            "rtx" in normalized -> "GeForce RTX"
            "gtx" in normalized -> "GeForce GTX"
            Regex("\\bmx\\s?\\d").containsMatchIn(normalized) -> "GeForce MX"
            Regex("\\bgt\\s?\\d").containsMatchIn(normalized) -> "GeForce GT"
            "arc" in normalized -> "Intel Arc"
            "iris" in normalized -> "Intel Iris Xe"
            "uhd" in normalized -> "Intel UHD"
            "hd graphics" in normalized -> "Intel HD"
            "adreno" in normalized || "snapdragon" in normalized -> "Qualcomm Adreno"
            "radeon rx" in normalized -> "AMD Radeon RX"
            "radeon" in normalized -> "AMD Radeon"
            else -> "Grafis terintegrasi"
        }
    }

    private fun renderHardwareCards(
        cpuModel: String,
        cpuUsage: Int?,
        cpuTemperature: Double?,
        gpus: List<RemoteGpu>,
        memoryBytes: Long,
        processorCount: Int,
        architecture: String,
        osVersion: String,
        device: String,
        fanText: String
    ) {
        val container = findViewById<LinearLayout>(R.id.hardwareCardsContainer)
        val specsContainer = findViewById<LinearLayout>(R.id.systemSpecsContainer)
        val signature = listOf(
            cpuModel, gpus.joinToString("|") { it.model }, memoryBytes.toString(), processorCount.toString(),
            architecture, osVersion, device
        ).joinToString("\u0000")
        if (signature != hardwareCardsSignature) {
            hardwareCardsSignature = signature
            container.removeAllViews()
            gpuHardwareCards.clear()
            cpuHardwareCard = createHardwareCard(cpuModel, "Prosesor", cpuFamilyName(cpuModel), "CPU", isGpu = false).also {
                container.addView(it.root)
            }
            if (gpus.isEmpty()) {
                val empty = TextView(this).apply {
                    text = "GPU tidak terdeteksi oleh sistem."
                    setTextColor(getColor(R.color.text_dim))
                    textSize = 11f
                    setPadding(dp(4), dp(3), dp(4), dp(8))
                }
                container.addView(empty)
            } else {
                gpus.forEachIndexed { index, gpu ->
                    val kind = if (isDedicatedGpuModel(gpu.model)) "KHUSUS" else "TERINTEGRASI"
                    val card = createHardwareCard(gpu.model, "Kartu grafis ${index + 1}", gpuFamilyName(gpu.model), kind, isGpu = true)
                    gpuHardwareCards.add(card)
                    container.addView(card.root)
                }
            }
            specsContainer.removeAllViews()
            fanSpecChip = null
            findViewById<TextView>(R.id.systemSpecsDetails).text =
                "Spesifikasi sistem yang dilaporkan Windows. Sensor yang tidak tersedia ditampilkan sebagai —."
            val specs = buildList {
                if (device.isNotBlank()) add("Perangkat" to device)
                add("Memori" to if (memoryBytes > 0) formatShareBytes(memoryBytes) else "Tidak tersedia")
                add("Prosesor logis" to if (processorCount > 0) processorCount.toString() else "Tidak tersedia")
                add("Kipas" to fanText)
                add("Arsitektur" to architecture)
                add("Sistem operasi" to osVersion)
            }
            specs.chunked(2).forEach { rowSpecs ->
                val row = LinearLayout(this).apply {
                    orientation = LinearLayout.HORIZONTAL
                    layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                }
                rowSpecs.forEachIndexed { index, (label, value) ->
                    val chip = TextView(this).apply {
                        text = "$label\n$value"
                        setTextColor(getColor(R.color.text_main))
                        textSize = 11f
                        maxLines = 3
                        setPadding(dp(11), dp(9), dp(11), dp(9))
                        background = hardwareSpecBackground()
                        layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                            if (index > 0) marginStart = dp(8)
                            bottomMargin = dp(8)
                        }
                    }
                    if (label == "Kipas") fanSpecChip = chip
                    row.addView(chip)
                }
                if (rowSpecs.size == 1) row.addView(View(this).apply {
                    layoutParams = LinearLayout.LayoutParams(0, 1, 1f).apply { marginStart = dp(8) }
                })
                specsContainer.addView(row)
            }
        }

        fanSpecChip?.text = "Kipas\n$fanText"
        updateHardwareMeter(cpuHardwareCard, cpuUsage, cpuTemperature)
        gpus.forEachIndexed { index, gpu ->
            updateHardwareMeter(gpuHardwareCards.getOrNull(index), gpu.usagePercent, gpu.temperatureC?.toDouble())
        }
    }

    private fun createHardwareCard(model: String, role: String, family: String, kind: String, isGpu: Boolean): HardwareCardRefs {
        val root = layoutInflater.inflate(R.layout.item_hardware_card, findViewById(R.id.hardwareCardsContainer), false)
        root.setBackgroundResource(if (prefs.getBoolean("classicTheme", false)) R.drawable.bg_hardware_card_classic else R.drawable.bg_hardware_card)
        val logo = root.findViewById<ImageView>(R.id.hardwareLogo)
        if (isGpu) showGpuHardwareBrand(logo, model) else showHardwareBrand(logo, model)
        val badge = root.findViewById<TextView>(R.id.hardwareBadge).apply { text = family }
        val kindView = root.findViewById<TextView>(R.id.hardwareKind).apply { text = kind }
        val roleView = root.findViewById<TextView>(R.id.hardwareRole).apply { text = role }
        val modelView = root.findViewById<TextView>(R.id.hardwareModel).apply {
            text = model
            if (model.length > 38) textSize = 14f
        }
        val accent = when {
            "nvidia" in model.lowercase(Locale.ROOT) || "geforce" in model.lowercase(Locale.ROOT) -> Color.rgb(118, 185, 0)
            "amd" in model.lowercase(Locale.ROOT) || "radeon" in model.lowercase(Locale.ROOT) || "ryzen" in model.lowercase(Locale.ROOT) -> Color.rgb(237, 28, 36)
            "intel" in model.lowercase(Locale.ROOT) || "iris" in model.lowercase(Locale.ROOT) -> Color.rgb(0, 113, 197)
            else -> getColor(R.color.accent)
        }
        listOf(R.id.firstMeterBar, R.id.secondMeterBar).forEach { id ->
            root.findViewById<ProgressBar>(id).progressTintList = ColorStateList.valueOf(accent)
        }
        root.findViewById<TextView>(R.id.firstMeterLabel).text = if (isGpu) "Penggunaan GPU" else "Penggunaan CPU"
        root.findViewById<TextView>(R.id.secondMeterLabel).text = if (isGpu) "Suhu GPU" else "Suhu CPU"
        badge.contentDescription = "Seri $family"
        kindView.contentDescription = "Jenis $kind"
        roleView.contentDescription = role
        modelView.contentDescription = "Model $model"
        return HardwareCardRefs(
            root,
            root.findViewById(R.id.firstMeterValue),
            root.findViewById(R.id.firstMeterBar),
            root.findViewById(R.id.secondMeterValue),
            root.findViewById(R.id.secondMeterBar)
        )
    }

    private fun updateHardwareMeter(card: HardwareCardRefs?, usage: Int?, temperature: Double?) {
        if (card == null) return
        card.usageValue.text = usage?.let { "$it%" } ?: "—"
        card.usageBar.progress = usage ?: 0
        val validTemperature = temperature?.takeIf { it.isFinite() && it in 5.0..130.0 }
        card.temperatureValue.text = validTemperature?.let { "%.0f°C".format(Locale.getDefault(), it) } ?: "—"
        card.temperatureBar.progress = validTemperature?.toInt()?.coerceIn(0, 100) ?: 0
    }

    private fun hardwareSpecBackground(): GradientDrawable = GradientDrawable().apply {
        val classic = prefs.getBoolean("classicTheme", false)
        setColor(if (classic) Color.rgb(29, 41, 54) else Color.rgb(22, 33, 55))
        setStroke(dp(1), if (classic) Color.rgb(67, 81, 95) else Color.rgb(34, 48, 74))
        cornerRadius = dp(12).toFloat()
    }

    private fun isDedicatedGpuModel(model: String): Boolean =
        model.contains("NVIDIA", true) || model.contains("GeForce", true) || model.contains("Quadro", true) ||
            model.contains("Radeon RX", true) || Regex("(?i)\\bArc\\s+A\\d").containsMatchIn(model) ||
            Regex("(?i)\\bRX\\s?\\d").containsMatchIn(model)

    private fun showOffline(message: String, recordEvent: Boolean) {
        val isConnectionFailureTransition = recordEvent && wasOnline != false
        if (isConnectionFailureTransition) playConnectionChime(success = false)
        if (recordEvent && wasOnline == true) logLiveActivity("CONN", "status=offline reason=$message", 'W')
        laptopOnline = false
        laptopOnAcPower = false
        latestStatusPayload = JSONObject().put("online", false).put("message", message).toString()
        pendingConnectionDeviceId = null
        refreshDiscoveryConnectionState()
        screenActive = false
        screenPending = false
        screenPasswordAccessAvailable = false
        setRemoteControlsEnabled(false)
        transitionStatusPill("Offline", "waiting")
        findViewById<TextView>(R.id.deviceSub).text = "Tidak ada pembaruan terbaru"
        slideValue(findViewById(R.id.connectionInfo), message)
        findViewById<TextView>(R.id.troubleshootingStatus).text =
            "Koneksi laptop terputus. Nilai perangkat terakhir yang ditampilkan mungkin sudah tidak terbaru."
        updateHardwareMeter(cpuHardwareCard, null, null)
        gpuHardwareCards.forEach { updateHardwareMeter(it, null, null) }
        findViewById<View>(R.id.manualConnectionProgressRow).visibility = View.GONE
        findViewById<Button>(R.id.connectButton).apply { isEnabled = true; text = "Simpan dan hubungkan" }
        selectedDiscoveryStatus?.let { if (message.isNotBlank()) it.text = "Tidak terhubung" }
        updateScreenControlUi()
        publishMotionDemoStatus()
        if (recordEvent && wasOnline == true) addEvent("Laptop terputus", "Tidak merespons di jaringan", clock.format(Date()), R.drawable.ic_error)
        if (recordEvent) wasOnline = false
    }

    private fun addEvent(title: String, subtitle: String, time: String, icon: Int) {
        events.add(0, HistoryItem(title, subtitle, time, icon))
        if (events.size > 20) events.removeAt(events.lastIndex)
        saveHistory()
        adapter.notifyDataSetChanged()
    }

    private fun confirmClearHistory() {
        if (events.isEmpty()) {
            android.widget.Toast.makeText(this, "Riwayat sudah kosong.", android.widget.Toast.LENGTH_SHORT).show()
            return
        }
        AlertDialog.Builder(this)
            .setTitle("Hapus riwayat?")
            .setMessage("Semua riwayat aktivitas di Android akan dihapus.")
            .setNegativeButton("Batal", null)
            .setPositiveButton("Hapus") { _, _ ->
                events.clear()
                adapter.notifyDataSetChanged()
                saveHistory()
                android.widget.Toast.makeText(this, "Riwayat dihapus.", android.widget.Toast.LENGTH_SHORT).show()
            }
            .show()
    }

    private fun restoreHistory() {
        try {
            val saved = JSONArray(prefs.getString("history", "[]"))
            for (index in 0 until minOf(saved.length(), 20)) {
                val item = saved.optJSONObject(index) ?: continue
                events.add(HistoryItem(
                    item.optString("title"),
                    item.optString("subtitle"),
                    item.optString("time"),
                    item.optInt("iconRes", R.drawable.ic_clock)
                ))
            }
            adapter.notifyDataSetChanged()
        } catch (_: Exception) {
            events.clear()
            prefs.edit().remove("history").apply()
        }
        latestHistoryPayload = historyJson().toString()
        publishMotionDemoHistory()
    }

    private fun saveHistory() {
        val saved = historyJson()
        latestHistoryPayload = saved.toString()
        prefs.edit().putString("history", latestHistoryPayload).apply()
        publishMotionDemoHistory()
    }

    private fun historyJson(): JSONArray = JSONArray().also { saved ->
        events.take(20).forEach { item ->
            saved.put(JSONObject().apply {
                put("title", item.title)
                put("subtitle", item.subtitle)
                put("time", item.time)
                put("iconRes", item.iconRes)
            })
        }
    }

    private fun setRemoteControlsEnabled(enabled: Boolean) {
        intArrayOf(R.id.remoteShutdown, R.id.remoteRestart, R.id.remoteSleep, R.id.jokeButton).forEach { id ->
            findViewById<Button>(id).isEnabled = enabled
        }
        if (::screenPreview.isInitialized) updateScreenControlUi()
    }

    override fun onDestroy() {
        remoteShellPollHandler.removeCallbacksAndMessages(null)
        val shellSessionToStop = remoteShellSessionId
        remoteShellSessionId = ""
        if (shellSessionToStop.isNotBlank()) {
            remoteShellWorker.execute {
                runCatching {
                    val encodedId = URLEncoder.encode(shellSessionToStop, StandardCharsets.UTF_8.name())
                    remoteShellRequest("/api/shell/stop?sessionId=$encodedId", "POST", "{}")
                }
            }
        }
        screenRefresh.removeCallbacksAndMessages(null)
        worker.shutdownNow()
        screenWorker.shutdownNow()
        remoteShellWorker.shutdown()
        discoveryWorker.shutdownNow()
        dataWorker.shutdownNow()
        super.onDestroy()
    }
}
