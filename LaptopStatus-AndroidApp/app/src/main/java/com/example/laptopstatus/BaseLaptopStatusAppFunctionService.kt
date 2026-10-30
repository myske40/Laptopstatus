package com.example.laptopstatus

import android.app.PendingIntent
import android.content.Intent
import androidx.annotation.RequiresApi
import androidx.appfunctions.AppFunction
import androidx.appfunctions.AppFunctionAppUnknownException
import androidx.appfunctions.AppFunctionException
import androidx.appfunctions.AppFunctionInvalidArgumentException
import androidx.appfunctions.AppFunctionService
import androidx.appfunctions.AppFunctionServiceEntryPoint
import androidx.appfunctions.AppFunctionStringValueConstraint
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.util.Locale

/**
 * AppFunctions exposed to Android's on-device intelligence and eligible assistants.
 * Remote operations are read-only. Any Mode Joke request opens the app's confirmation UI.
 */
@RequiresApi(36)
@AppFunctionServiceEntryPoint(
    serviceName = "LaptopStatusAppFunctionService",
    appFunctionXmlFileName = "laptop_status_app_function_service",
)
abstract class BaseLaptopStatusAppFunctionService : AppFunctionService() {
    /**
     * Read the current laptop connection, CPU, memory, battery, and charger status.
     * The laptop must already be paired in Status Perangkat and reachable on the configured network.
     * This function only reads status and never changes Windows settings or sends controls.
     * @return A short Indonesian status summary, without the pairing key or TLS fingerprint.
     */
    @AppFunction(isDescribedByKDoc = true)
    suspend fun getLaptopStatus(): String = withContext(Dispatchers.IO) {
        val status = requestLaptopJson("/api/status")
        val device = status.optString("deviceName", "Laptop")
        val cpu = status.optInt("cpuPercent").coerceIn(0, 100)
        val ram = status.optInt("ramPercent").coerceIn(0, 100)
        val battery = if (status.isNull("batteryPercent")) "baterai tidak tersedia" else "baterai ${status.optInt("batteryPercent").coerceIn(0, 100)}%"
        val power = if (status.optBoolean("onAcPower")) "tersambung ke charger" else "menggunakan baterai"
        val hardware = listOf(status.optString("manufacturer"), status.optString("model"))
            .filter(String::isNotBlank)
            .distinct()
            .joinToString(" ")
        listOf(
            "$device aktif",
            "CPU $cpu%",
            "RAM $ram%",
            battery,
            power,
            hardware,
        ).filter(String::isNotBlank).joinToString(" • ")
    }

    /**
     * Search running Windows processes by application or process name.
     * This returns up to ten matching names, process IDs, and memory use. It cannot stop or hide a process.
     * @param query Name or part of a Windows process name; must contain 1 to 64 characters.
     * @return Matching processes, or a clear message when there are no matches.
     * @throws AppFunctionInvalidArgumentException when query is blank or too long.
     */
    @AppFunction(isDescribedByKDoc = true)
    suspend fun searchWindowsProcesses(query: String): String = withContext(Dispatchers.IO) {
        val term = query.trim()
        if (term.isEmpty() || term.length > 64) {
            throw AppFunctionInvalidArgumentException("Masukkan nama aplikasi 1 sampai 64 karakter.")
        }
        val response = requestLaptopJson("/api/processes")
        val rows = response.optJSONArray("processes") ?: JSONArray()
        val matches = (0 until rows.length()).mapNotNull { index ->
            rows.optJSONObject(index)
        }.filter { process ->
            process.optString("name").contains(term, ignoreCase = true)
        }.take(10)
        if (matches.isEmpty()) return@withContext "Tidak ditemukan proses Windows yang cocok dengan ‘$term’."

        val allMatches = (0 until rows.length()).count { index ->
            rows.optJSONObject(index)?.optString("name")?.contains(term, ignoreCase = true) == true
        }
        val details = matches.joinToString("\n") { process ->
            val name = process.optString("name", "Proses")
            val pid = process.optInt("pid")
            val memoryMiB = process.optLong("workingSetBytes").coerceAtLeast(0L) / (1024.0 * 1024.0)
            String.format(Locale.getDefault(), "%s — PID %d, RAM %.1f MB", name, pid, memoryMiB)
        }
        val suffix = if (allMatches > matches.size) "\nMenampilkan ${matches.size} dari $allMatches hasil." else ""
        "Ditemukan $allMatches proses yang cocok dengan ‘$term’:\n$details$suffix"
    }

    /**
     * Open a Status Perangkat page on request.
     * Use STATUS for laptop status, PROCESSES for Windows processes, HISTORY for connection history,
     * CONNECTION for pairing settings, SCREEN for screen controls, or JOKE to show the in-app confirmation.
     * Choosing JOKE does not activate it; the user must confirm in the app.
     * @param feature The page to open: STATUS, PROCESSES, HISTORY, CONNECTION, SCREEN, or JOKE.
     * @return A PendingIntent that opens the selected page in Status Perangkat.
     */
    @AppFunction(isDescribedByKDoc = true)
    suspend fun openLaptopFeature(
        @AppFunctionStringValueConstraint(enumValues = ["STATUS", "PROCESSES", "HISTORY", "CONNECTION", "SCREEN", "JOKE"])
        feature: String,
    ): PendingIntent = withContext(Dispatchers.IO) {
        val normalized = feature.trim().lowercase(Locale.ROOT)
        if (normalized !in setOf("status", "processes", "history", "connection", "screen", "joke")) {
            throw AppFunctionInvalidArgumentException("Halaman tidak dikenal. Pilih STATUS, PROCESSES, HISTORY, CONNECTION, SCREEN, atau JOKE.")
        }
        if (normalized == "joke") {
            val status = requestLaptopJson("/api/status")
            openControlIntent("joke", 7201, status.optBoolean("onAcPower"), status.optBoolean("jokeActive"))
        } else {
            openFeatureIntent(normalized, 7201)
        }
    }
    /**
     * Ask to activate the laptop's Joke Mode by opening the app's confirmation dialog.
     * This function checks the latest laptop status but never sends a command to Windows.
     * Joke Mode starts only if the user confirms in Status Perangkat.
     * @return A PendingIntent that displays the explicit confirmation in the app.
     */
    @AppFunction(isDescribedByKDoc = true)
    suspend fun requestJokeMode(): PendingIntent = withContext(Dispatchers.IO) {
        val status = requestLaptopJson("/api/status")
        openControlIntent("joke", 7202, status.optBoolean("onAcPower"), status.optBoolean("jokeActive"))
    }


    /**
     * Request a Windows power or Joke Mode action by opening the matching confirmation dialog in Status Perangkat.
     * No command is sent by this function. The user must press the confirmation button in the app before Windows acts.
     * Shutdown and restart show an additional yellow warning when Windows reports that it is charging.
     * @param control The requested action: SHUTDOWN, RESTART, SLEEP, or JOKE.
     * @return A PendingIntent that opens the explicit in-app confirmation.
     */
    @AppFunction(isDescribedByKDoc = true)
    suspend fun requestWindowsControl(
        @AppFunctionStringValueConstraint(enumValues = ["SHUTDOWN", "RESTART", "SLEEP", "JOKE"])
        control: String,
    ): PendingIntent = withContext(Dispatchers.IO) {
        val normalized = control.trim().lowercase(Locale.ROOT)
        if (normalized !in setOf("shutdown", "restart", "sleep", "joke")) {
            throw AppFunctionInvalidArgumentException("Pilih SHUTDOWN, RESTART, SLEEP, atau JOKE.")
        }
        val status = requestLaptopJson("/api/status")
        openControlIntent(normalized, 7203, status.optBoolean("onAcPower"), status.optBoolean("jokeActive"))
    }
    private fun openControlIntent(control: String, requestCode: Int, onAcPower: Boolean?, jokeActive: Boolean?): PendingIntent {
        val intent = Intent(this, MainActivity::class.java)
            .setAction(Intent.ACTION_VIEW)
            .putExtra("assistant_control", control)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP)
        onAcPower?.let { intent.putExtra("assistant_on_ac_power", it) }
        jokeActive?.let {
            intent.putExtra("assistant_laptop_online", true)
            intent.putExtra("assistant_joke_active", it)
        }
        return PendingIntent.getActivity(
            this,
            requestCode,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
    }
    private fun openFeatureIntent(feature: String, requestCode: Int): PendingIntent {
        val intent = Intent(this, MainActivity::class.java)
            .setAction(Intent.ACTION_VIEW)
            .putExtra("assistant_feature", feature)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP)
        return PendingIntent.getActivity(
            this,
            requestCode,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
    }

    private fun requestLaptopJson(path: String): JSONObject {
        val preferences = getSharedPreferences("connection", MODE_PRIVATE)
        val address = preferences.getString("address", "").orEmpty().trim()
        val key = preferences.getString("key", "").orEmpty().trim()
        val fingerprint = preferences.getString("fingerprint", "").orEmpty()
            .filter(Char::isLetterOrDigit)
            .uppercase(Locale.ROOT)
        if (address.isBlank() || key.isBlank() || fingerprint.length != 64) {
            throw AppFunctionInvalidArgumentException(
                "Pasangkan laptop dan simpan alamat, kunci pairing, serta fingerprint TLS di aplikasi Status Perangkat terlebih dahulu.",
            )
        }
        try {
            return PinnedTls.withConnection(address, 8765, path, "GET", key, fingerprint) { connection ->
                when (val code = connection.responseCode) {
                    200 -> JSONObject(connection.inputStream.bufferedReader().use { it.readText() })
                    401 -> throw AppFunctionInvalidArgumentException("Laptop menolak kunci pairing. Periksa pengaturan koneksi di aplikasi.")
                    else -> throw AppFunctionAppUnknownException("Laptop menolak permintaan status (HTTP $code).")
                }
            }
        } catch (error: AppFunctionException) {
            throw error
        } catch (_: Exception) {
            throw AppFunctionAppUnknownException(
                "Laptop tidak dapat dijangkau. Pastikan Windows berjalan dan koneksi sudah dipasangkan di Status Perangkat.",
            )
        }
    }
}